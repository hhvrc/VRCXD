using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261109165636_AccountEndpoint : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ApiEndpoint",
            table: "accounts",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PipelineEndpoint",
            table: "accounts",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ApiEndpoint",
            table: "accounts");

        migrationBuilder.DropColumn(
            name: "PipelineEndpoint",
            table: "accounts");
    }
}
