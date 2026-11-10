using Serilog.Core;
using Serilog.Events;

namespace VRCX.Testing.Infrastructure;

/// <summary>One line the server logged.</summary>
public sealed record CapturedLog(DateTimeOffset At, LogEventLevel Level, string Source, string Message, Exception? Exception)
{
    public override string ToString() => $"[{Level}] {Source}: {Message}{(Exception is null ? "" : "\n" + Exception)}";
}

/// <summary>
/// Everything the scenario's host logs at Information and above, kept in memory so a test can
/// assert on it and a failure can show what the server was doing. Set VRCX_TEST_LOG=1 to also
/// echo it to the console.
/// </summary>
public sealed class CapturedLogs : ILogEventSink
{
    private readonly Lock _gate = new();
    private readonly List<CapturedLog> _entries = [];
    private static readonly bool Echo = Environment.GetEnvironmentVariable("VRCX_TEST_LOG") == "1";

    public void Emit(LogEvent logEvent)
    {
        var source = logEvent.Properties.TryGetValue("SourceContext", out var value) ? value.ToString().Trim('"') : "";
        var entry = new CapturedLog(logEvent.Timestamp, logEvent.Level, source, logEvent.RenderMessage(), logEvent.Exception);
        lock (_gate) _entries.Add(entry);
        if (Echo) Console.WriteLine(entry);
    }

    public IReadOnlyList<CapturedLog> All
    {
        get { lock (_gate) return _entries.ToList(); }
    }

    public IReadOnlyList<CapturedLog> Errors => All.Where(e => e.Level >= LogEventLevel.Error).ToList();

    public IReadOnlyList<CapturedLog> Warnings => All.Where(e => e.Level == LogEventLevel.Warning).ToList();

    public override string ToString() => string.Join("\n", All);
}
