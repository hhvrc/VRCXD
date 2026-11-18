using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261118233240_DropNameIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_worlds_name",
            table: "worlds");

        migrationBuilder.DropIndex(
            name: "IX_groups_name",
            table: "groups");

        migrationBuilder.DropIndex(
            name: "IX_avatars_name",
            table: "avatars");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_worlds_name",
            table: "worlds",
            column: "name");

        migrationBuilder.CreateIndex(
            name: "IX_groups_name",
            table: "groups",
            column: "name");

        migrationBuilder.CreateIndex(
            name: "IX_avatars_name",
            table: "avatars",
            column: "name");
    }
}
