using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261118132513_StubRefreshAttempts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "stub_attempted_at",
            table: "worlds",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "stub_retry_at",
            table: "worlds",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "stub_attempted_at",
            table: "groups",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "stub_retry_at",
            table: "groups",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_worlds_stub_attempted_at_first_seen_at",
            table: "worlds",
            columns: new[] { "stub_attempted_at", "first_seen_at" },
            filter: "is_stub");

        migrationBuilder.CreateIndex(
            name: "IX_groups_stub_attempted_at_first_seen_at",
            table: "groups",
            columns: new[] { "stub_attempted_at", "first_seen_at" },
            filter: "is_stub");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_worlds_stub_attempted_at_first_seen_at",
            table: "worlds");

        migrationBuilder.DropIndex(
            name: "IX_groups_stub_attempted_at_first_seen_at",
            table: "groups");

        migrationBuilder.DropColumn(
            name: "stub_attempted_at",
            table: "worlds");

        migrationBuilder.DropColumn(
            name: "stub_retry_at",
            table: "worlds");

        migrationBuilder.DropColumn(
            name: "stub_attempted_at",
            table: "groups");

        migrationBuilder.DropColumn(
            name: "stub_retry_at",
            table: "groups");
    }
}
