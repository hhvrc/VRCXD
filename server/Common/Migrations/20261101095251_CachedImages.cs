using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261101095251_CachedImages : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "cached_images",
            columns: table => new
            {
                url = table.Column<string>(type: "text", nullable: false),
                sha256 = table.Column<string>(type: "text", nullable: false),
                content_type = table.Column<string>(type: "text", nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("cached_images_pkey", x => x.url);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "cached_images");
    }
}
