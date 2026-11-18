using Google.Protobuf.WellKnownTypes;
using VRCX.Common.Live;
using VRCX.Common.Live.Topics;

namespace VRCX.Common.Features.LegacyImport;

/// <summary>
/// The "legacy_import" topic: the uploaded VRCX database's status as GET /1/legacy-import reports it
/// (the same <see cref="LegacyImportRuns.Status"/>), so the Settings page shows an upload, a check
/// or an import going on, from this tab or another. Not an account's: there is one upload per
/// server. The state is small, so every delta is the whole status again, sent after each change
/// <see cref="LegacyImportRuns"/> announces; one that would send what was sent last is left out.
/// </summary>
public sealed class LegacyImportTopic : ILiveTopic
{
    public const string TopicName = "legacy_import";

    private readonly LegacyImportRuns _runs;
    // Held while a status is read and sent, so every sink gets snapshot and deltas in order, and a
    // delta never carries an older status than the snapshot before it.
    private readonly Lock _gate = new();
    private readonly List<ITopicSink> _sinks = [];
    private LegacyImportStatus? _sent;

    public LegacyImportTopic(LegacyImportRuns runs)
    {
        _runs = runs;
        runs.Changed += Publish;
    }

    public string Name => TopicName;
    public bool AccountScoped => false;

    public Task<IDisposable> SubscribeAsync(string accountId, ITopicSink sink, CancellationToken ct)
    {
        lock (_gate)
        {
            var status = _runs.Status();
            sink.Snapshot(new TopicSnapshot { LegacyImport = Message(status) });
            _sinks.Add(sink);
            _sent ??= status;
        }
        return Task.FromResult<IDisposable>(new Handle(this, sink));
    }

    private void Publish()
    {
        lock (_gate)
        {
            _sinks.RemoveAll(s => !s.IsOpen);
            var status = _runs.Status();
            if (_sinks.Count == 0 || status == _sent)
            {
                _sent = status;
                return;
            }
            _sent = status;
            var message = Message(status);
            foreach (var sink in _sinks) sink.Delta(new TopicDelta { LegacyImport = message.Clone() });
        }
    }

    /// <summary>The wire shape of the status GET /1/legacy-import returns.</summary>
    public static LegacyImportUpdate Message(LegacyImportStatus status)
    {
        var message = new LegacyImportUpdate
        {
            UploadId = status.UploadId ?? "",
            State = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(status.State.ToString()),
            Size = status.Size,
            Received = status.Received,
            Step = status.Progress?.Step ?? 0,
            Steps = status.Progress?.Steps ?? 0,
            StepName = status.Progress?.Name ?? "",
            Error = status.Error ?? "",
        };
        if (status.FinishedAt is { } finished) message.FinishedAt = Timestamp.FromDateTimeOffset(finished);
        if (status.Report is { } report)
        {
            var wire = new LegacyImportReport { DryRun = report.DryRun, ElapsedSeconds = report.Elapsed.TotalSeconds };
            wire.Notes.AddRange(report.Notes);
            foreach (var table in report.Tables)
            {
                var row = new LegacyImportTable { Source = table.Source, Target = table.Target, Read = table.Read, Candidates = table.Candidates, Updated = table.Updated };
                if (table.Inserted is { } inserted) row.Inserted = inserted;
                row.Skipped.Add(table.Skipped);
                wire.Tables.Add(row);
            }
            message.Report = wire;
        }
        return message;
    }

    private sealed class Handle(LegacyImportTopic topic, ITopicSink sink) : IDisposable
    {
        public void Dispose()
        {
            lock (topic._gate) topic._sinks.Remove(sink);
        }
    }
}
