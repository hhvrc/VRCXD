using VRCX.LegacyImport;

// LegacyImport --sqlite <VRCX.sqlite3> [--dry-run] [--connection <npgsql>] [--game-log-account usr_...] [--match-window-seconds N]
// The connection string can also come from VRCX__DB__CONN, as for the server and dotnet ef.
string? sqlite = null, connection = Environment.GetEnvironmentVariable("VRCX__DB__CONN"), gameLogAccount = null;
var dryRun = false;
var window = TimeSpan.FromMinutes(2);
for (var i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
    switch (args[i])
    {
        case "--sqlite": sqlite = Next(); break;
        case "--connection": connection = Next(); break;
        case "--dry-run": dryRun = true; break;
        case "--game-log-account": gameLogAccount = Next(); break;
        case "--match-window-seconds": window = TimeSpan.FromSeconds(int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture)); break;
        case "-h" or "--help":
            Console.WriteLine("""
                Imports an original VRCX database (VRCX.sqlite3, opened read-only) into this server's Postgres.

                  --sqlite PATH               the VRCX.sqlite3 file (default %APPDATA%\VRCX\VRCX.sqlite3)
                  --dry-run                   read the file only and report what would be imported; Postgres is not contacted
                  --connection CONN           Npgsql connection string (or set VRCX__DB__CONN)
                  --game-log-account USR_ID   owner of game log stays the log cannot attribute (default: the account with most stays)
                  --match-window-seconds N    how close a feed row and one this server recorded must be to be the same event (default 120)

                Safe to run again: rows already there are not added twice.
                """);
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument {args[i]} (see --help)");
            return 2;
    }
}
sqlite ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "VRCX.sqlite3");

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

try
{
    var report = await LegacyImporter.RunAsync(new LegacyImportOptions
    {
        SqlitePath = sqlite,
        ConnectionString = connection,
        DryRun = dryRun,
        GameLogAccountId = gameLogAccount,
        MatchWindow = window,
    }, cancel.Token);
    Console.Write(report.ToText());
    return 0;
}
catch (Exception e) when (e is InvalidOperationException or FileNotFoundException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
