using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VRCX.Common.VrcxDb;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261101094835_PresenceToRedis : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Nothing is thrown away: what leaves these columns goes to archive tables first, and any
        // current state the log does not already end on gets a final presence_events row.
        migrationBuilder.Sql("""
            CREATE TABLE user_presence_archive AS
            SELECT id AS user_id, state::text AS state, platform, location, traveling_to_location, current_instance_location,
                   presence_changed_at, location_since, location_before_traveling, traveling_since, now() AS archived_at
            FROM users
            WHERE presence_changed_at IS NOT NULL OR platform IS NOT NULL OR state <> 'offline';

            CREATE TABLE game_log_client_instance_archive AS
            SELECT client_id, current_instance_location, now() AS archived_at
            FROM game_log_clients WHERE current_instance_location IS NOT NULL;

            INSERT INTO presence_events (user_id, at, kind, location, previous_location, traveling_to_location, instance_location, platform, previous_state)
            SELECT u.id, u.presence_changed_at, u.state::text::presence_kind, u.location, l.location, u.traveling_to_location, u.current_instance_location, u.platform, (CASE l.kind::text WHEN 'location' THEN 'online' ELSE l.kind::text END)::user_state
            FROM users u
            LEFT JOIN LATERAL (SELECT e.location, e.kind, e.at FROM presence_events e WHERE e.user_id = u.id ORDER BY e.at DESC, e.id DESC LIMIT 1) l ON true
            WHERE u.presence_changed_at IS NOT NULL
              AND u.location IS DISTINCT FROM 'traveling'
              AND (l.at IS NULL OR l.location IS DISTINCT FROM u.location OR u.state::text IS DISTINCT FROM CASE l.kind::text WHEN 'location' THEN 'online' ELSE l.kind::text END);
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_game_log_clients_current_instance_location",
            table: "game_log_clients");

        migrationBuilder.DropForeignKey(
            name: "fk_users_current_instance_location",
            table: "users");

        migrationBuilder.DropIndex(
            name: "IX_users_current_instance_location",
            table: "users");

        migrationBuilder.DropIndex(
            name: "IX_game_log_clients_current_instance_location",
            table: "game_log_clients");

        migrationBuilder.DropColumn(
            name: "current_instance_location",
            table: "users");

        migrationBuilder.DropColumn(
            name: "location",
            table: "users");

        migrationBuilder.DropColumn(
            name: "location_before_traveling",
            table: "users");

        migrationBuilder.DropColumn(
            name: "location_since",
            table: "users");

        migrationBuilder.DropColumn(
            name: "platform",
            table: "users");

        migrationBuilder.DropColumn(
            name: "presence_changed_at",
            table: "users");

        migrationBuilder.DropColumn(
            name: "state",
            table: "users");

        migrationBuilder.DropColumn(
            name: "traveling_since",
            table: "users");

        migrationBuilder.DropColumn(
            name: "traveling_to_location",
            table: "users");

        migrationBuilder.DropColumn(
            name: "current_instance_location",
            table: "game_log_clients");

        migrationBuilder.RenameTable(
            name: "instance_visits",
            newName: "stored_instance_visits");

        migrationBuilder.RenameIndex(
            name: "IX_instance_visits_user_id_source",
            table: "stored_instance_visits",
            newName: "IX_stored_instance_visits_user_id_source");

        migrationBuilder.RenameIndex(
            name: "IX_instance_visits_user_id_joined_at",
            table: "stored_instance_visits",
            newName: "IX_stored_instance_visits_user_id_joined_at");

        migrationBuilder.RenameIndex(
            name: "IX_instance_visits_instance_location_joined_at",
            table: "stored_instance_visits",
            newName: "IX_stored_instance_visits_instance_location_joined_at");

        // presence_events becomes partitioned by month. Rows are copied and counted before the old table goes.
        migrationBuilder.Sql("""
            ALTER TABLE presence_events RENAME TO presence_events_unpartitioned;
            CREATE TABLE presence_events (LIKE presence_events_unpartitioned INCLUDING DEFAULTS) PARTITION BY RANGE (at);
            CREATE TABLE presence_events_default PARTITION OF presence_events DEFAULT;

            DO $$
            DECLARE
                month timestamptz;
                last timestamptz;
                name text;
            BEGIN
                SELECT date_trunc('month', coalesce(min(at), now()), 'UTC') INTO month FROM presence_events_unpartitioned;
                last := date_trunc('month', now() + interval '2 months', 'UTC');
                WHILE month <= last LOOP
                    name := 'presence_events_y' || to_char(month AT TIME ZONE 'UTC', 'YYYY') || 'm' || to_char(month AT TIME ZONE 'UTC', 'MM');
                    EXECUTE format('CREATE TABLE %I PARTITION OF presence_events FOR VALUES FROM (%L) TO (%L)', name,
                        to_char(month AT TIME ZONE 'UTC', 'YYYY-MM-DD') || '+00',
                        to_char((month AT TIME ZONE 'UTC') + interval '1 month', 'YYYY-MM-DD') || '+00');
                    month := ((month AT TIME ZONE 'UTC') + interval '1 month') AT TIME ZONE 'UTC';
                END LOOP;
            END $$;

            INSERT INTO presence_events SELECT * FROM presence_events_unpartitioned;

            DO $$
            BEGIN
                IF (SELECT count(*) FROM presence_events) <> (SELECT count(*) FROM presence_events_unpartitioned) THEN
                    RAISE EXCEPTION 'presence_events copy is incomplete';
                END IF;
            END $$;

            CREATE SEQUENCE presence_events_id_sequence AS bigint OWNED BY presence_events.id;
            SELECT setval('presence_events_id_sequence', coalesce((SELECT max(id) FROM presence_events), 0) + 1, false);
            ALTER TABLE presence_events ALTER COLUMN id SET DEFAULT nextval('presence_events_id_sequence');
            DROP TABLE presence_events_unpartitioned;

            ALTER TABLE presence_events ADD CONSTRAINT presence_events_pkey PRIMARY KEY (id, at);
            CREATE INDEX "IX_presence_events_at" ON presence_events (at);
            CREATE INDEX "IX_presence_events_user_id_at" ON presence_events (user_id, at);
            CREATE INDEX "IX_presence_events_instance_location" ON presence_events (instance_location);
            ALTER TABLE presence_events ADD CONSTRAINT fk_presence_events_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE;
            ALTER TABLE presence_events ADD CONSTRAINT fk_presence_events_instance_location FOREIGN KEY (instance_location) REFERENCES instances (location) ON DELETE SET NULL;
            """);

        // Pipeline visits are worked out from presence_events from now on. A stored row leaves the
        // table only when the log produces that exact visit, and it is archived. Anything else stays.
        migrationBuilder.Sql("""
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

            CREATE TABLE pipeline_visits_archive AS
            SELECT s.*, now() AS archived_at FROM stored_instance_visits s
            WHERE s.source = 'pipeline' AND EXISTS (
                SELECT 1 FROM instance_visits v
                WHERE v.id < 0 AND v.user_id = s.user_id AND v.instance_location = s.instance_location
                  AND v.joined_at = s.joined_at AND v.left_at IS NOT DISTINCT FROM s.left_at);

            DELETE FROM stored_instance_visits s USING pipeline_visits_archive a WHERE s.id = a.id;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // presence_events stays partitioned: its rows are unaffected either way.
        migrationBuilder.Sql("""
            DROP VIEW instance_visits;
            INSERT INTO stored_instance_visits (id, user_id, instance_location, joined_at, left_at, source)
            SELECT id, user_id, instance_location, joined_at, left_at, source FROM pipeline_visits_archive;
            DROP TABLE pipeline_visits_archive;
            """);

        migrationBuilder.RenameTable(
            name: "stored_instance_visits",
            newName: "instance_visits");

        migrationBuilder.RenameIndex(
            name: "IX_stored_instance_visits_user_id_source",
            table: "instance_visits",
            newName: "IX_instance_visits_user_id_source");

        migrationBuilder.RenameIndex(
            name: "IX_stored_instance_visits_user_id_joined_at",
            table: "instance_visits",
            newName: "IX_instance_visits_user_id_joined_at");

        migrationBuilder.RenameIndex(
            name: "IX_stored_instance_visits_instance_location_joined_at",
            table: "instance_visits",
            newName: "IX_instance_visits_instance_location_joined_at");

        migrationBuilder.AddColumn<string>(
            name: "current_instance_location",
            table: "users",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "location",
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

        migrationBuilder.AddColumn<string>(
            name: "platform",
            table: "users",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "presence_changed_at",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<UserState>(
            name: "state",
            table: "users",
            type: "user_state",
            nullable: false,
            defaultValue: UserState.Offline);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "traveling_since",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "traveling_to_location",
            table: "users",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "current_instance_location",
            table: "game_log_clients",
            type: "text",
            nullable: true);


        migrationBuilder.CreateIndex(
            name: "IX_users_current_instance_location",
            table: "users",
            column: "current_instance_location");

        migrationBuilder.CreateIndex(
            name: "IX_game_log_clients_current_instance_location",
            table: "game_log_clients",
            column: "current_instance_location");

        migrationBuilder.AddForeignKey(
            name: "fk_game_log_clients_current_instance_location",
            table: "game_log_clients",
            column: "current_instance_location",
            principalTable: "instances",
            principalColumn: "location",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_users_current_instance_location",
            table: "users",
            column: "current_instance_location",
            principalTable: "instances",
            principalColumn: "location",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.Sql("""
            UPDATE users u SET state = a.state::user_state, platform = a.platform, location = a.location,
                traveling_to_location = a.traveling_to_location, current_instance_location = a.current_instance_location,
                presence_changed_at = a.presence_changed_at, location_since = a.location_since,
                location_before_traveling = a.location_before_traveling, traveling_since = a.traveling_since
            FROM user_presence_archive a WHERE a.user_id = u.id;
            UPDATE game_log_clients c SET current_instance_location = a.current_instance_location
            FROM game_log_client_instance_archive a WHERE a.client_id = c.client_id;
            DROP TABLE user_presence_archive;
            DROP TABLE game_log_client_instance_archive;
            """);
    }
}
