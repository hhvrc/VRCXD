using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261109162506_GameLogClientNoVr : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "GameNoVr",
            table: "game_log_clients",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "GameNoVr",
            table: "game_log_clients");
    }
}
