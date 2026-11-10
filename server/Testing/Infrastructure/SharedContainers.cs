using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using VRCX.Common.VrcxDb;

namespace VRCX.Testing.Infrastructure;

/// <summary>
/// One Postgres and one Redis container per test process, started on first use. Every test gets
/// its own database, cloned from a template that was migrated once (CREATE DATABASE ... TEMPLATE
/// takes milliseconds, a migration takes seconds), and its own Redis logical database, flushed
/// and handed back when the test ends. Testcontainers' reaper removes the containers when the
/// process exits, so nothing needs disposing explicitly.
/// </summary>
public static class SharedContainers
{
    private const string TemplateDatabase = "vrcx_template";
    /// <summary>Redis logical databases available to concurrent tests.</summary>
    private const int RedisDatabases = 256;

    private static readonly SemaphoreSlim StartLock = new(1, 1);
    // CREATE DATABASE refuses to copy a template another session is copying at the same moment.
    private static readonly SemaphoreSlim CloneLock = new(1, 1);
    private static readonly ConcurrentQueue<int> FreeRedisDatabases = new(Enumerable.Range(1, RedisDatabases - 1));
    private static PostgreSqlContainer? _postgres;
    private static RedisContainer? _redis;
    private static int _databaseCounter;

    /// <summary>The server's connection string for the whole Postgres instance (the default database).</summary>
    public static string PostgresConnectionString => _postgres?.GetConnectionString() ?? throw new InvalidOperationException("Containers are not started");

    public static async Task EnsureStartedAsync()
    {
        if (_postgres is not null && _redis is not null) return;
        await StartLock.WaitAsync();
        try
        {
            if (_postgres is not null && _redis is not null) return;
            var postgres = new PostgreSqlBuilder("postgres:18")
                .WithName($"vrcx-test-pg-{Guid.CreateVersion7():N}")
                .WithDatabase("postgres_admin")
                .WithUsername("vrcx")
                .WithPassword(Guid.NewGuid().ToString("N"))
                // Throwaway data: durability only slows the tests down.
                .WithCommand("-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off", "-c", "max_connections=500")
                .Build();
            var redis = new RedisBuilder("redis:8-alpine")
                .WithName($"vrcx-test-redis-{Guid.CreateVersion7():N}")
                .WithCommand("--databases", RedisDatabases.ToString(System.Globalization.CultureInfo.InvariantCulture), "--save", "", "--appendonly", "no")
                .Build();
            await Task.WhenAll(postgres.StartAsync(), redis.StartAsync());
            await CreateTemplateAsync(postgres.GetConnectionString());
            _postgres = postgres;
            _redis = redis;
        }
        finally
        {
            StartLock.Release();
        }
    }

    private static async Task CreateTemplateAsync(string adminConnection)
    {
        await using (var admin = new NpgsqlConnection(adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {TemplateDatabase}", admin);
            await create.ExecuteNonQueryAsync();
        }
        // No pooling: a pooled connection left open on the template would block every clone.
        var template = new NpgsqlConnectionStringBuilder(adminConnection) { Database = TemplateDatabase, Pooling = false }.ConnectionString;
        await using (var migrator = new MigrationVrcxContext(template, debug: false))
        {
            await migrator.Database.MigrateAsync();
        }
        NpgsqlConnection.ClearAllPools();
        await using (var admin = new NpgsqlConnection(adminConnection))
        {
            await admin.OpenAsync();
            await using var mark = new NpgsqlCommand($"ALTER DATABASE {TemplateDatabase} WITH IS_TEMPLATE true ALLOW_CONNECTIONS false", admin);
            await mark.ExecuteNonQueryAsync();
        }
    }

    /// <summary>A new, migrated, empty database. Dispose drops it.</summary>
    public static async Task<TestDatabase> CreateDatabaseAsync()
    {
        await EnsureStartedAsync();
        var name = $"test_{Environment.ProcessId}_{Interlocked.Increment(ref _databaseCounter)}";
        await CloneLock.WaitAsync();
        try
        {
            await using var admin = new NpgsqlConnection(PostgresConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name} TEMPLATE {TemplateDatabase}", admin);
            await create.ExecuteNonQueryAsync();
        }
        finally
        {
            CloneLock.Release();
        }
        return new TestDatabase(name, new NpgsqlConnectionStringBuilder(PostgresConnectionString) { Database = name }.ConnectionString);
    }

    /// <summary>A Redis logical database of its own. Dispose flushes it and hands it back.</summary>
    public static async Task<TestRedis> RentRedisAsync()
    {
        await EnsureStartedAsync();
        if (!FreeRedisDatabases.TryDequeue(out var index))
            throw new InvalidOperationException($"More than {RedisDatabases - 1} scenarios are alive at once; dispose them (await using).");
        var connection = $"{_redis!.GetConnectionString()},defaultDatabase={index},allowAdmin=true";
        return new TestRedis(index, connection, () => FreeRedisDatabases.Enqueue(index));
    }

    internal static async Task DropDatabaseAsync(string name)
    {
        await using var admin = new NpgsqlConnection(PostgresConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

public sealed class TestDatabase(string name, string connectionString) : IAsyncDisposable
{
    public string Name { get; } = name;
    public string ConnectionString { get; } = connectionString;

    /// <summary>A context on this database for arranging or asserting rows directly.</summary>
    public VrcxContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<VrcxContext>();
        VrcxContext.ConfigureOptionsBuilder(builder, ConnectionString, debug: false);
        return new VrcxContext(builder.Options);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using (var connection = new NpgsqlConnection(ConnectionString)) NpgsqlConnection.ClearPool(connection);
            await SharedContainers.DropDatabaseAsync(Name);
        }
        catch (NpgsqlException)
        {
            // The container is going away with the process anyway.
        }
    }
}

public sealed class TestRedis(int index, string connectionString, Action release) : IAsyncDisposable
{
    public int Index { get; } = index;
    public string ConnectionString { get; } = connectionString;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var redis = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
            foreach (var server in redis.GetServers()) await server.FlushDatabaseAsync(Index);
        }
        catch (RedisException)
        {
        }
        release();
    }
}
