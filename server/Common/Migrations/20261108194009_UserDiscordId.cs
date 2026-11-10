using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261108194009_UserDiscordId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DiscordId",
            table: "users",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DiscordId",
            table: "users");
    }
}
