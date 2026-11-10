using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261102065203_GameLogEvents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "game_log_events",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                instance_location = table.Column<string>(type: "text", nullable: true),
                kind = table.Column<string>(type: "text", nullable: false),
                data = table.Column<string>(type: "text", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: true),
                display_name = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("game_log_events_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_game_log_events_instance_location",
                    column: x => x.instance_location,
                    principalTable: "instances",
                    principalColumn: "location",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "game_log_hidden_entries",
            columns: table => new
            {
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                entry_key = table.Column<string>(type: "text", nullable: false),
                hidden_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("game_log_hidden_entries_pkey", x => new { x.owner_user_id, x.entry_key });
                table.ForeignKey(
                    name: "fk_game_log_hidden_entries_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_game_log_events_instance_location",
            table: "game_log_events",
            column: "instance_location");

        migrationBuilder.CreateIndex(
            name: "idx_game_log_events_at",
            table: "game_log_events",
            column: "at",
            descending: new bool[0]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "game_log_events");

        migrationBuilder.DropTable(
            name: "game_log_hidden_entries");
    }
}
