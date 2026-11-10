using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261108135704_GameSessionTimes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "GameStartedAt",
            table: "game_log_clients",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastGameStartedAt",
            table: "game_log_clients",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastGameStoppedAt",
            table: "game_log_clients",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "GameStartedAt",
            table: "game_log_clients");

        migrationBuilder.DropColumn(
            name: "LastGameStartedAt",
            table: "game_log_clients");

        migrationBuilder.DropColumn(
            name: "LastGameStoppedAt",
            table: "game_log_clients");
    }
}
