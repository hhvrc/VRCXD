using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Tests;

/// <summary>One throwaway Postgres (Docker) per test session; each test gets its own migrated database.</summary>
public sealed class PostgresFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();
    private int _databases;

    public async Task InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public async Task<IDbContextFactory<VrcxContext>> NewDatabaseAsync(IPresenceCache presence)
    {
        var connection = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = $"test_{Interlocked.Increment(ref _databases)}",
        }.ConnectionString;
        await using (var migrator = new MigrationVrcxContext(connection, debug: false))
        {
            await migrator.Database.MigrateAsync();
        }
        var builder = new DbContextOptionsBuilder<VrcxContext>();
        VrcxContext.ConfigureOptionsBuilder(builder, connection, debug: false);
        builder.AddInterceptors(new PresenceFlushInterceptor(new Lazy<IPresenceCache>(presence), NullLogger<PresenceFlushInterceptor>.Instance));
        return new Factory(builder.Options);
    }

    private sealed class Factory(DbContextOptions<VrcxContext> options) : IDbContextFactory<VrcxContext>
    {
        public VrcxContext CreateDbContext() => new(options);
    }
}
