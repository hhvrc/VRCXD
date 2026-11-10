using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VRCX.Common.VrcxDb;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261101132227_LocalFavorites : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "local_favorites",
            columns: table => new
            {
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                type = table.Column<FavoriteType>(type: "favorite_type", nullable: false),
                group_name = table.Column<string>(type: "text", nullable: false),
                target_id = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("local_favorites_pkey", x => new { x.owner_user_id, x.type, x.group_name, x.target_id });
                table.ForeignKey(
                    name: "fk_local_favorites_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_local_favorites_owner_user_id_target_id",
            table: "local_favorites",
            columns: new[] { "owner_user_id", "target_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "local_favorites");
    }
}
