using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using VRCX.Common.VrcxDb;

namespace VRCX.LegacyImport;

/// <summary>A column of a staging table: its name and how COPY writes it.</summary>
public sealed record StageColumn(string Name, NpgsqlDbType Type)
{
    public string SqlType => Type switch
    {
        NpgsqlDbType.Text => "text",
        NpgsqlDbType.TimestampTz => "timestamptz",
        NpgsqlDbType.Interval => "interval",
        NpgsqlDbType.Bigint => "bigint",
        NpgsqlDbType.Integer => "integer",
        NpgsqlDbType.Boolean => "boolean",
        NpgsqlDbType.Jsonb => "jsonb",
        NpgsqlDbType.Array | NpgsqlDbType.Text => "text[]",
        _ => throw new NotSupportedException(Type.ToString()),
    };
}

/// <summary>
/// The Postgres side: one connection, one transaction per imported table. Rows go in with binary
/// COPY into a temporary staging table, then one INSERT ... SELECT adds those that are not there
/// yet. No row is a round trip of its own, and a failure rolls back only the table at hand, which
/// the next run (idempotent) simply does again.
/// </summary>
public sealed class PgTarget : IAsyncDisposable
{
    private IDbContextTransaction? _transaction;

    private PgTarget(VrcxContext db)
    {
        Db = db;
    }

    public VrcxContext Db { get; }

    public NpgsqlConnection Connection => (NpgsqlConnection)Db.Database.GetDbConnection();

    public static async Task<PgTarget> OpenAsync(string connectionString, CancellationToken ct)
    {
        var builder = new DbContextOptionsBuilder<VrcxContext>();
        VrcxContext.ConfigureOptionsBuilder(builder, connectionString, debug: false);
        var db = new VrcxContext(builder.Options);
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));
        await db.Database.OpenConnectionAsync(ct);
        return new PgTarget(db);
    }

    public async Task BeginAsync(CancellationToken ct) => _transaction = await Db.Database.BeginTransactionAsync(ct);

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is null) return;
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
        Db.ChangeTracker.Clear();
    }

    public async Task RollbackAsync()
    {
        if (_transaction is null) return;
        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        _transaction = null;
        Db.ChangeTracker.Clear();
    }

    /// <summary>Creates the temporary table and copies the rows into it. Returns how many were copied.</summary>
    public async Task<long> StageAsync(string stage, IReadOnlyList<StageColumn> columns, IEnumerable<object?[]> rows, CancellationToken ct)
    {
        await ExecuteAsync($"DROP TABLE IF EXISTS {stage}; CREATE TEMP TABLE {stage} ({string.Join(", ", columns.Select(c => $"{c.Name} {c.SqlType}"))}) ON COMMIT DROP", ct);
        long count = 0;
        await using (var writer = await Connection.BeginBinaryImportAsync($"COPY {stage} ({string.Join(", ", columns.Select(c => c.Name))}) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var row in rows)
            {
                await writer.StartRowAsync(ct);
                for (var i = 0; i < columns.Count; i++)
                {
                    var value = row[i];
                    if (value is null) await writer.WriteNullAsync(ct);
                    else if (value is string s) await writer.WriteAsync(Clean(s), columns[i].Type, ct);
                    else if (value is string[] a) await writer.WriteAsync(a.Select(Clean).ToArray(), columns[i].Type, ct);
                    else await writer.WriteAsync(value, columns[i].Type, ct);
                }
                count++;
            }
            await writer.CompleteAsync(ct);
        }
        await ExecuteAsync($"ANALYZE {stage}", ct);
        return count;
    }

    public async Task<int> ExecuteAsync(string sql, CancellationToken ct, params NpgsqlParameter[] parameters)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 1800;
        command.Transaction = _transaction?.GetDbTransaction() as NpgsqlTransaction;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> map, CancellationToken ct, params NpgsqlParameter[] parameters)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 1800;
        command.Transaction = _transaction?.GetDbTransaction() as NpgsqlTransaction;
        command.Parameters.AddRange(parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var list = new List<T>();
        while (await reader.ReadAsync(ct)) list.Add(map(reader));
        return list;
    }

    /// <summary>Postgres text cannot hold NUL; the model strips it on every string column, and so does the import.</summary>
    private static string Clean(string value) => value.Contains('\0') ? value.Replace("\0", "") : value;

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null) await RollbackAsync();
        if (Db.Database.GetDbConnection().State == ConnectionState.Open) await Db.Database.CloseConnectionAsync();
        await Db.DisposeAsync();
    }
}
