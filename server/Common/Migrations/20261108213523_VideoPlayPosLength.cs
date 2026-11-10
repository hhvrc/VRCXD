using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261108213523_VideoPlayPosLength : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "VideoLength",
            table: "video_plays",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "VideoPos",
            table: "video_plays",
            type: "double precision",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "VideoLength",
            table: "video_plays");

        migrationBuilder.DropColumn(
            name: "VideoPos",
            table: "video_plays");
    }
}
