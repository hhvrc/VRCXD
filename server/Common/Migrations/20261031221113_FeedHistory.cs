using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VRCX.Common.VrcxDb;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261031221113_FeedHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "banner_type",
            table: "users",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "banner_url",
            table: "users",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "icon_url",
            table: "users",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "location_before_traveling",
            table: "users",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "location_since",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "traveling_since",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "avatar_name",
            table: "user_avatar_changes",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "avatar_owner_id",
            table: "user_avatar_changes",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "previous_avatar_name",
            table: "user_avatar_changes",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "previous_avatar_owner_id",
            table: "user_avatar_changes",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<TimeSpan>(
            name: "duration_at_previous",
            table: "presence_events",
            type: "interval",
            nullable: true);

        migrationBuilder.AddColumn<UserState>(
            name: "previous_state",
            table: "presence_events",
            type: "user_state",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "banner_type",
            table: "users");

        migrationBuilder.DropColumn(
            name: "banner_url",
            table: "users");

        migrationBuilder.DropColumn(
            name: "icon_url",
            table: "users");

        migrationBuilder.DropColumn(
            name: "location_before_traveling",
            table: "users");

        migrationBuilder.DropColumn(
            name: "location_since",
            table: "users");

        migrationBuilder.DropColumn(
            name: "traveling_since",
            table: "users");

        migrationBuilder.DropColumn(
            name: "avatar_name",
            table: "user_avatar_changes");

        migrationBuilder.DropColumn(
            name: "avatar_owner_id",
            table: "user_avatar_changes");

        migrationBuilder.DropColumn(
            name: "previous_avatar_name",
            table: "user_avatar_changes");

        migrationBuilder.DropColumn(
            name: "previous_avatar_owner_id",
            table: "user_avatar_changes");

        migrationBuilder.DropColumn(
            name: "duration_at_previous",
            table: "presence_events");

        migrationBuilder.DropColumn(
            name: "previous_state",
            table: "presence_events");
    }
}
