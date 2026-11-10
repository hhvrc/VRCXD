using Microsoft.EntityFrameworkCore.Design;
using VRCX.Common.VrcxDb;

namespace VRCX.MigrationHelper;

// Adding a migration never connects, so the connection string only has to parse.
// Set VRCX__DB__CONN to point `dotnet ef database update` at a real database.
public sealed class MigrationVrcxContextFactory : IDesignTimeDbContextFactory<MigrationVrcxContext>
{
    public MigrationVrcxContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("VRCX__DB__CONN")
            ?? "Host=localhost;Database=vrcx;Username=vrcx;Password=vrcx";
        return new MigrationVrcxContext(connectionString, debug: false);
    }
}
