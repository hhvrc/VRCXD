using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261102063503_FriendLogEvents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "friend_log_hidden_entries",
            columns: table => new
            {
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                entry_key = table.Column<string>(type: "text", nullable: false),
                hidden_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("friend_log_hidden_entries_pkey", x => new { x.owner_user_id, x.entry_key });
                table.ForeignKey(
                    name: "fk_friend_log_hidden_entries_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "friend_request_events",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                target_user_id = table.Column<string>(type: "text", nullable: false),
                display_name = table.Column<string>(type: "text", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("friend_request_events_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_friend_request_events_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "idx_friend_request_events_owner_created",
            table: "friend_request_events",
            columns: new[] { "owner_user_id", "created_at" },
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "friend_log_hidden_entries");

        migrationBuilder.DropTable(
            name: "friend_request_events");
    }
}
