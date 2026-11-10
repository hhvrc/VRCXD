using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261031223345_Charts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "shared_connections_opt_out",
            table: "accounts",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "mutual_friend_links",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                friend_user_id = table.Column<string>(type: "text", nullable: false),
                mutual_user_id = table.Column<string>(type: "text", nullable: false),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("mutual_friend_links_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_mutual_friend_links_friend_user_id",
                    column: x => x.friend_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_mutual_friend_links_mutual_user_id",
                    column: x => x.mutual_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_mutual_friend_links_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "mutual_graph_meta",
            columns: table => new
            {
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                friend_user_id = table.Column<string>(type: "text", nullable: false),
                last_fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                opted_out = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("mutual_graph_meta_pkey", x => new { x.owner_user_id, x.friend_user_id });
                table.ForeignKey(
                    name: "fk_mutual_graph_meta_friend_user_id",
                    column: x => x.friend_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_mutual_graph_meta_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_mutual_friend_links_friend_user_id",
            table: "mutual_friend_links",
            column: "friend_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_mutual_friend_links_mutual_user_id",
            table: "mutual_friend_links",
            column: "mutual_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_mutual_friend_links_owner_user_id_friend_user_id_mutual_use~",
            table: "mutual_friend_links",
            columns: new[] { "owner_user_id", "friend_user_id", "mutual_user_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_mutual_friend_links_owner_user_id_mutual_user_id",
            table: "mutual_friend_links",
            columns: new[] { "owner_user_id", "mutual_user_id" });

        migrationBuilder.CreateIndex(
            name: "IX_mutual_graph_meta_friend_user_id",
            table: "mutual_graph_meta",
            column: "friend_user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "mutual_friend_links");

        migrationBuilder.DropTable(
            name: "mutual_graph_meta");

        migrationBuilder.DropColumn(
            name: "shared_connections_opt_out",
            table: "accounts");
    }
}
