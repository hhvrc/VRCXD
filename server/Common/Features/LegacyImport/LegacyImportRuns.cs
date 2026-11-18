using System.Text.Json;
using VRCX.Common.Errors;
using VRCX.Common.Options;
using VRCX.Common.Problems;

namespace VRCX.Common.Features.LegacyImport;

/// <summary>Where the uploaded file is in its life: being received, received, checked by a dry run, imported, or failed.</summary>
public enum LegacyImportState
{
    None,
    Uploading,
    Uploaded,
    Checking,
    Checked,
    Importing,
    Imported,
    Failed,
}

/// <summary>What the Settings page shows: the upload, the step running, and the last report.</summary>
public sealed record LegacyImportStatus(
    string? UploadId,
    LegacyImportState State,
    long Size,
    long Received,
    LegacyImportProgress? Progress,
    ImportReport? Report,
    string? Error,
    DateTimeOffset? FinishedAt);

/// <summary>
/// An original VRCX database uploaded from the Settings page, and the dry run and the import run on
/// it. The command line tool reads the file in place on the PC; this takes it in parts instead,
/// because the files are often larger than 700 MB and a proxy in front of the server (Cloudflare's
/// is 100 MB) may refuse a body that large. Each part is appended to a file on disk, never held in
/// memory, and a part that fails can be sent again from where the file ends.
///
/// One upload at a time: a new upload replaces the last one. The dry run and the import run in the
/// background, since the import takes about a minute on a large file. Every change is announced
/// (<see cref="Changed"/>), which <see cref="LegacyImportTopic"/> streams to the page. The file is
/// deleted once it has been imported.
///
/// The upload outlives a restart: its id and size are kept beside the file, so a server that
/// started again knows the file it holds, and the page's upload goes on from where the file ends.
/// What a run reported is not kept; the file is checked again. A file with no record of its upload
/// is deleted.
/// </summary>
public sealed class LegacyImportRuns(
    LegacyImportUploadOptions options,
    DatabaseOptions database,
    TimeProvider time,
    IHostApplicationLifetime lifetime,
    ILogger<LegacyImportRuns> logger)
{
    /// <summary>The largest part taken in one request: under the 100 MB a proxy may allow, with room for the headers.</summary>
    public const long MaxChunk = 64L * 1024 * 1024;

    // The first bytes of every SQLite database file.
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    private readonly Lock _lock = new();
    // One part is written at a time; a second waits rather than interleaving its bytes.
    private readonly SemaphoreSlim _writing = new(1, 1);
    private string? _uploadId;
    private LegacyImportState _state;
    private long _size;
    private long _received;
    private LegacyImportProgress? _progress;
    private ImportReport? _report;
    private string? _error;
    private DateTimeOffset? _finishedAt;
    private Task _running = Task.CompletedTask;

    private string Directory => Path.GetFullPath(options.Dir);

    private string FilePath => Path.Combine(Directory, "VRCX.sqlite3");

    private string RecordPath => FilePath + ".upload.json";

    private sealed record UploadRecord(string UploadId, long Size);

    private bool _restored;

    /// <summary>Takes up the upload a previous process left, once, before anything reads or changes the state.</summary>
    private void Restore()
    {
        lock (_lock)
        {
            if (_restored) return;
            _restored = true;
            try
            {
                var record = File.Exists(RecordPath) ? JsonSerializer.Deserialize<UploadRecord>(File.ReadAllText(RecordPath)) : null;
                var file = new FileInfo(FilePath);
                if (record is { UploadId.Length: > 0, Size: > 0 } && file.Exists && file.Length <= record.Size)
                {
                    _uploadId = record.UploadId;
                    _size = record.Size;
                    _received = file.Length;
                    _state = _received == _size ? LegacyImportState.Uploaded : LegacyImportState.Uploading;
                    logger.LogInformation("Took up the VRCX database upload left at {Received} of {Size} bytes", _received, _size);
                }
                else if (record is not null || file.Exists) DeleteFiles();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "Could not take up the VRCX database upload in {Directory}", Directory);
            }
        }
    }

    /// <summary>
    /// Raised after every change to <see cref="Status"/>, outside the lock: a part received, a step
    /// started, a run finished, the upload started or discarded.
    /// </summary>
    public event Action? Changed;

    private void Announce() => Changed?.Invoke();

    public LegacyImportStatus Status()
    {
        Restore();
        lock (_lock) return new(_uploadId, _state, _size, _received, _progress, _report, _error, _finishedAt);
    }

    /// <summary>Starts a new upload of a file of <paramref name="size"/> bytes, replacing whatever was uploaded before.</summary>
    public async Task<Result<LegacyImportStatus>> StartAsync(long size, CancellationToken ct)
    {
        if (size <= 0) return LegacyImportError.Empty;
        if (size > options.MaxSize) return LegacyImportError.TooLarge(options.MaxSize);
        Restore();
        await _writing.WaitAsync(ct);
        try
        {
            lock (_lock)
                if (IsRunning) return LegacyImportError.Busy;
            System.IO.Directory.CreateDirectory(Directory);
            DeleteFiles();
            lock (_lock)
            {
                _uploadId = null;
                _state = LegacyImportState.None;
            }
            Announce();
            if (FreeSpace() is { } free && free < size) return LegacyImportError.NoSpace(free);
            await using (File.Create(FilePath)) { }
            var uploadId = Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(RecordPath, JsonSerializer.Serialize(new UploadRecord(uploadId, size)), ct);
            lock (_lock)
            {
                _uploadId = uploadId;
                _state = LegacyImportState.Uploading;
                _size = size;
                _received = 0;
                _progress = null;
                _report = null;
                _error = null;
                _finishedAt = null;
            }
            Announce();
            return Status();
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>
    /// Appends one part, which must start where the file ends (<paramref name="offset"/> is the
    /// bytes received so far). A part cut off half way is taken back out, so it can be sent again.
    /// </summary>
    public async Task<Result<LegacyImportStatus>> AppendAsync(string uploadId, long offset, Stream body, CancellationToken ct)
    {
        Restore();
        await _writing.WaitAsync(ct);
        try
        {
            long received, size;
            lock (_lock)
            {
                if (_uploadId != uploadId || _state is LegacyImportState.None) return LegacyImportError.UnknownUpload;
                if (_state is not LegacyImportState.Uploading) return LegacyImportError.WrongOffset(_received);
                if (offset != _received) return LegacyImportError.WrongOffset(_received);
                received = _received;
                size = _size;
            }

            var written = 0L;
            await using (var file = new FileStream(FilePath, FileMode.Open, FileAccess.Write, FileShare.Read, 81920, useAsync: true))
            {
                file.SetLength(received);
                file.Position = received;
                var buffer = new byte[81920];
                try
                {
                    int read;
                    while ((read = await body.ReadAsync(buffer, ct)) > 0)
                    {
                        if (received + written == 0 && !StartsLikeSqlite(buffer.AsSpan(0, read)))
                        {
                            file.SetLength(received);
                            return LegacyImportError.NotSqlite;
                        }
                        if (received + written + read > size)
                        {
                            file.SetLength(received);
                            return LegacyImportError.PastTheEnd;
                        }
                        await file.WriteAsync(buffer.AsMemory(0, read), ct);
                        written += read;
                    }
                }
                catch
                {
                    // The browser went away mid-part: what arrived of it is dropped, and the part is sent again whole.
                    file.SetLength(received);
                    throw;
                }
            }

            lock (_lock)
            {
                _received = received + written;
                if (_received == _size) _state = LegacyImportState.Uploaded;
            }
            Announce();
            return Status();
        }
        finally
        {
            _writing.Release();
        }
    }

    // A part as short as a few bytes still has to agree with as much of the header as it holds.
    private static bool StartsLikeSqlite(ReadOnlySpan<byte> start) =>
        start[..Math.Min(start.Length, SqliteHeader.Length)].SequenceEqual(SqliteHeader.AsSpan(0, Math.Min(start.Length, SqliteHeader.Length)));

    /// <summary>Reads the uploaded file and reports what an import would add (and, replacing history, delete), writing nothing.</summary>
    public Result<LegacyImportStatus> Check(string uploadId, bool replaceHistory = false) => Begin(uploadId, dryRun: true, replaceHistory);

    /// <summary>
    /// Imports the uploaded file. Safe to run again: rows already there are not added twice. With
    /// <paramref name="replaceHistory"/> the server's history of the kinds the file holds goes first
    /// (<see cref="LegacyImportOptions.ReplaceHistory"/>).
    /// </summary>
    public Result<LegacyImportStatus> Import(string uploadId, bool replaceHistory = false) => Begin(uploadId, dryRun: false, replaceHistory);

    /// <summary>Forgets the upload and deletes the file.</summary>
    public async Task<VrcxProblem?> DiscardAsync(CancellationToken ct)
    {
        Restore();
        await _writing.WaitAsync(ct);
        try
        {
            lock (_lock)
            {
                if (IsRunning) return LegacyImportError.Busy;
                _uploadId = null;
                _state = LegacyImportState.None;
                _size = _received = 0;
                _progress = null;
                _report = null;
                _error = null;
                _finishedAt = null;
            }
            DeleteFiles();
            Announce();
            return null;
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>The run under way, if any; tests wait on it.</summary>
    public Task Running
    {
        get
        {
            lock (_lock) return _running;
        }
    }

    private bool IsRunning => _state is LegacyImportState.Checking or LegacyImportState.Importing;

    private Result<LegacyImportStatus> Begin(string uploadId, bool dryRun, bool replaceHistory)
    {
        Restore();
        lock (_lock)
        {
            if (_uploadId != uploadId || _state is LegacyImportState.None) return LegacyImportError.UnknownUpload;
            if (IsRunning) return LegacyImportError.Busy;
            // Once imported the file is gone; an upload never finished has nothing to read.
            if (_state is LegacyImportState.Uploading or LegacyImportState.Imported || _received != _size) return LegacyImportError.NotUploaded;
            _state = dryRun ? LegacyImportState.Checking : LegacyImportState.Importing;
            _progress = null;
            _error = null;
            _finishedAt = null;
            _running = Task.Run(() => RunAsync(uploadId, dryRun, replaceHistory));
        }
        Announce();
        return Status();
    }

    private async Task RunAsync(string uploadId, bool dryRun, bool replaceHistory)
    {
        try
        {
            var report = await LegacyImporter.RunAsync(new LegacyImportOptions
            {
                SqlitePath = FilePath,
                ConnectionString = dryRun ? null : database.Conn,
                DryRun = dryRun,
                ReplaceHistory = replaceHistory,
                Progress = new StepProgress(this, uploadId),
            }, lifetime.ApplicationStopping);
            logger.LogInformation("Legacy import {Kind} finished in {Elapsed}", dryRun ? "dry run" : "import", report.Elapsed);
            if (!dryRun) TryDelete();
            Finish(uploadId, dryRun ? LegacyImportState.Checked : LegacyImportState.Imported, report, null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Legacy import {Kind} failed", dryRun ? "dry run" : "import");
            // The file stays, so the import can be run again: it picks up where it failed.
            Finish(uploadId, LegacyImportState.Failed, null, e.Message);
        }
    }

    private void Finish(string uploadId, LegacyImportState state, ImportReport? report, string? error)
    {
        lock (_lock)
        {
            if (_uploadId != uploadId) return;
            _state = state;
            if (report is not null) _report = report;
            _error = error;
            _finishedAt = time.GetUtcNow();
        }
        Announce();
    }

    private void TryDelete()
    {
        try
        {
            DeleteFiles();
        }
        catch (IOException e)
        {
            logger.LogWarning(e, "Could not delete the imported file {Path}", FilePath);
        }
    }

    // Reading a database in WAL mode leaves its -wal and -shm beside it, and a stale -wal next to the
    // next upload would be read as part of it, so they go with the file.
    private void DeleteFiles()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".upload.json" }) File.Delete(FilePath + suffix);
    }

    private long? FreeSpace()
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Directory)!).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Reports straight into the status, in order. Progress<T> would post each step to the thread pool.
    private sealed class StepProgress(LegacyImportRuns runs, string uploadId) : IProgress<LegacyImportProgress>
    {
        public void Report(LegacyImportProgress value)
        {
            lock (runs._lock)
                if (runs._uploadId == uploadId) runs._progress = value;
            runs.Announce();
        }
    }
}
