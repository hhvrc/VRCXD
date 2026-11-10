using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261110173034_CustomUserTags : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "custom_user_tags",
            columns: table => new
            {
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false),
                tag = table.Column<string>(type: "text", nullable: false),
                colour = table.Column<string>(type: "text", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("custom_user_tags_pkey", x => new { x.owner_user_id, x.user_id });
                table.ForeignKey(
                    name: "fk_custom_user_tags_owner_user_id",
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
            name: "custom_user_tags");
    }
}
