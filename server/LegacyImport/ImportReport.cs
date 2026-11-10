using System.Text;

namespace VRCX.LegacyImport;

/// <summary>What happened to one source table (or one derived set of rows).</summary>
public sealed class TableReport(string source, string target)
{
    public string Source { get; } = source;
    public string Target { get; } = target;
    /// <summary>Rows read from the source table.</summary>
    public long Read { get; set; }
    /// <summary>Rows built for the target (after skips; several source rows can make one, a join and its leave make one visit).</summary>
    public long Candidates { get; set; }
    /// <summary>Rows actually added. Null in a dry run, which does not look at Postgres.</summary>
    public long? Inserted { get; set; }
    /// <summary>Existing rows completed or corrected (a stay's missing leave time, a friendship's start).</summary>
    public long Updated { get; set; }
    public Dictionary<string, long> Skipped { get; } = [];

    /// <summary>Candidates that were already there (imported before, or recorded by this server itself).</summary>
    public long? AlreadyPresent => Inserted is { } i ? Candidates - i : null;

    public void Skip(string reason, long count = 1)
    {
        if (count <= 0) return;
        Skipped[reason] = Skipped.GetValueOrDefault(reason) + count;
    }
}

public sealed class ImportReport
{
    public bool DryRun { get; init; }
    public List<TableReport> Tables { get; } = [];
    public List<string> Notes { get; } = [];
    public TimeSpan Elapsed { get; set; }

    public TableReport Table(string source, string target)
    {
        var existing = Tables.FirstOrDefault(t => t.Source == source && t.Target == target);
        if (existing is not null) return existing;
        var report = new TableReport(source, target);
        Tables.Add(report);
        return report;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(DryRun ? "VRCX legacy import: DRY RUN (source read only, Postgres not contacted)" : "VRCX legacy import");
        foreach (var t in Tables)
        {
            sb.Append($"{t.Source} -> {t.Target}: read {t.Read}, ");
            sb.Append(DryRun ? $"would import {t.Candidates}" : $"candidates {t.Candidates}, inserted {t.Inserted}, already present {t.AlreadyPresent}");
            if (t.Updated > 0) sb.Append($", updated {t.Updated}");
            if (t.Skipped.Count > 0)
                sb.Append(", skipped ").Append(string.Join("; ", t.Skipped.OrderByDescending(s => s.Value).Select(s => $"{s.Value} {s.Key}")));
            sb.AppendLine();
        }
        foreach (var note in Notes) sb.AppendLine("note: " + note);
        sb.AppendLine($"took {Elapsed.TotalSeconds:0.0}s");
        return sb.ToString();
    }
}
