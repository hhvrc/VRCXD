using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261108125120_LegacyImport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "display_name",
            table: "user_trust_changes",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "display_name",
            table: "user_status_changes",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "display_name",
            table: "user_bio_changes",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "display_name",
            table: "user_avatar_changes",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "display_name",
            table: "stored_instance_visits",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "group_name",
            table: "stored_instance_visits",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "world_name",
            table: "stored_instance_visits",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "display_name",
            table: "presence_events",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "group_name",
            table: "presence_events",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "world_name",
            table: "presence_events",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<JsonDocument>(
            name: "v2_details",
            table: "notifications",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "display_name",
            table: "friendships",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ended_display_name",
            table: "friendships",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "legacy_settings",
            columns: table => new
            {
                key = table.Column<string>(type: "text", nullable: false),
                value = table.Column<string>(type: "text", nullable: false),
                imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("legacy_settings_pkey", x => x.key);
            });

        migrationBuilder.CreateTable(
            name: "moderations_against",
            columns: table => new
            {
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false),
                display_name = table.Column<string>(type: "text", nullable: true),
                block = table.Column<bool>(type: "boolean", nullable: false),
                mute = table.Column<bool>(type: "boolean", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("moderations_against_pkey", x => new { x.owner_user_id, x.user_id });
                table.ForeignKey(
                    name: "fk_moderations_against_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_moderations_against_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "unidentified_visits",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                display_name = table.Column<string>(type: "text", nullable: false),
                instance_location = table.Column<string>(type: "text", nullable: false),
                joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("unidentified_visits_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_unidentified_visits_instance_location",
                    column: x => x.instance_location,
                    principalTable: "instances",
                    principalColumn: "location",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_moderations_against_user_id",
            table: "moderations_against",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_unidentified_visits_display_name_joined_at",
            table: "unidentified_visits",
            columns: new[] { "display_name", "joined_at" });

        migrationBuilder.CreateIndex(
            name: "IX_unidentified_visits_instance_location_joined_at",
            table: "unidentified_visits",
            columns: new[] { "instance_location", "joined_at" });

        // The view gains the names the original VRCX stored with imported rows. A pipeline visit
        // takes them from the presence event that opened it.
        migrationBuilder.Sql("""
            CREATE OR REPLACE VIEW instance_visits AS
            SELECT id, user_id, instance_location, joined_at, left_at, source, display_name, world_name, group_name FROM stored_instance_visits
            UNION ALL
            SELECT -c.id, c.user_id, c.instance_location, c.at, c.left_at, 'pipeline'::visit_source, c.display_name, c.world_name, c.group_name
            FROM (
                SELECT e.id, e.user_id, e.at, e.instance_location, e.display_name, e.world_name, e.group_name,
                       lead(e.at) OVER (PARTITION BY e.user_id ORDER BY e.at, e.id) AS left_at
                FROM (
                    SELECT id, user_id, at, instance_location, display_name, world_name, group_name,
                           lag(instance_location) OVER (PARTITION BY user_id ORDER BY at, id) AS previous
                    FROM presence_events
                ) e
                WHERE e.instance_location IS DISTINCT FROM e.previous
            ) c
            WHERE c.instance_location IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP VIEW instance_visits;
            CREATE VIEW instance_visits AS
            SELECT id, user_id, instance_location, joined_at, left_at, source FROM stored_instance_visits
            UNION ALL
            SELECT -c.id, c.user_id, c.instance_location, c.at, c.left_at, 'pipeline'::visit_source
            FROM (
                SELECT e.id, e.user_id, e.at, e.instance_location, lead(e.at) OVER (PARTITION BY e.user_id ORDER BY e.at, e.id) AS left_at
                FROM (
                    SELECT id, user_id, at, instance_location, lag(instance_location) OVER (PARTITION BY user_id ORDER BY at, id) AS previous
                    FROM presence_events
                ) e
                WHERE e.instance_location IS DISTINCT FROM e.previous
            ) c
            WHERE c.instance_location IS NOT NULL;
            """);

        migrationBuilder.DropTable(
            name: "legacy_settings");

        migrationBuilder.DropTable(
            name: "moderations_against");

        migrationBuilder.DropTable(
            name: "unidentified_visits");

        migrationBuilder.DropColumn(
            name: "display_name",
            table: "user_trust_changes");

        migrationBuilder.DropColumn(
            name: "display_name",
            table: "user_status_changes");

        migrationBuilder.DropColumn(
            name: "display_name",
            table: "user_bio_changes");

        migrationBuilder.DropColumn(
            name: "display_name",
            table: "user_avatar_changes");

        migrationBuilder.DropColumn(
            name: "display_name",
            table: "stored_instance_visits");

        migrationBuilder.DropColumn(
            name: "group_name",
            table: "stored_instance_visits");

        migrationBuilder.DropColumn(
            name: "world_name",
            table: "stored_instance_visits");

        migrationBuilder.DropColumn(
            name: "display_name",
            table: "presence_events");

        migrationBuilder.DropColumn(
            name: "group_name",
            table: "presence_events");

        migrationBuilder.DropColumn(
            name: "world_name",
            table: "presence_events");

        migrationBuilder.DropColumn(
            name: "v2_details",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "display_name",
            table: "friendships");

        migrationBuilder.DropColumn(
            name: "ended_display_name",
            table: "friendships");
    }
}
