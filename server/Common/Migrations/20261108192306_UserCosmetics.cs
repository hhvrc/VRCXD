using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261108192306_UserCosmetics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "IconFrame",
            table: "users",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NameplateEffect",
            table: "users",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProfileEffect",
            table: "users",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IconFrame",
            table: "users");

        migrationBuilder.DropColumn(
            name: "NameplateEffect",
            table: "users");

        migrationBuilder.DropColumn(
            name: "ProfileEffect",
            table: "users");
    }
}
