using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261101085110_ProfileFetchedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "profile_fetched_at",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        // Blanks came from user payloads, which never carry a real bio. Null means "never read".
        migrationBuilder.Sql("UPDATE users SET bio = NULL WHERE bio = '';");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "profile_fetched_at",
            table: "users");
    }
}
