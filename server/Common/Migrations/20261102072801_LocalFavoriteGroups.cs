using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VRCX.Common.VrcxDb;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261102072801_LocalFavoriteGroups : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "local_favorite_groups",
            columns: table => new
            {
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                type = table.Column<FavoriteType>(type: "favorite_type", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("local_favorite_groups_pkey", x => new { x.owner_user_id, x.type, x.name });
                table.ForeignKey(
                    name: "fk_local_favorite_groups_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "local_favorite_groups");
    }
}
