using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using VRCX.Common.VrcxDb;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261031191809_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:Enum:favorite_type", "friend,world,avatar")
            .Annotation("Npgsql:Enum:instance_access_type", "public,hidden,friends,private,group")
            .Annotation("Npgsql:Enum:presence_kind", "online,active,offline,location")
            .Annotation("Npgsql:Enum:user_state", "offline,active,online")
            .Annotation("Npgsql:Enum:visit_source", "pipeline,game_log,api");

        migrationBuilder.CreateTable(
            name: "accounts",
            columns: table => new
            {
                user_id = table.Column<string>(type: "text", nullable: false),
                username = table.Column<string>(type: "text", nullable: true),
                cookies = table.Column<string>(type: "jsonb", nullable: false),
                home_location = table.Column<string>(type: "text", nullable: true),
                current_avatar_id = table.Column<string>(type: "text", nullable: true),
                friend_group_names = table.Column<string[]>(type: "text[]", nullable: false),
                two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                session_valid = table.Column<bool>(type: "boolean", nullable: false),
                logged_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_friend_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_full_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("accounts_pkey", x => x.user_id);
            });

        migrationBuilder.CreateTable(
            name: "favorite_groups",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                display_name = table.Column<string>(type: "text", nullable: false),
                visibility = table.Column<string>(type: "text", nullable: false),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("favorite_groups_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_favorite_groups_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "favorites",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                type = table.Column<FavoriteType>(type: "favorite_type", nullable: false),
                target_id = table.Column<string>(type: "text", nullable: false),
                group_name = table.Column<string>(type: "text", nullable: true),
                first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("favorites_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_favorites_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "avatar_memos",
            columns: table => new
            {
                avatar_id = table.Column<string>(type: "text", nullable: false),
                memo = table.Column<string>(type: "text", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("avatar_memos_pkey", x => x.avatar_id);
            });

        migrationBuilder.CreateTable(
            name: "avatar_tags",
            columns: table => new
            {
                avatar_id = table.Column<string>(type: "text", nullable: false),
                tag = table.Column<string>(type: "text", nullable: false),
                color = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("avatar_tags_pkey", x => new { x.avatar_id, x.tag });
            });

        migrationBuilder.CreateTable(
            name: "avatar_usages",
            columns: table => new
            {
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                avatar_id = table.Column<string>(type: "text", nullable: false),
                last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                time_spent = table.Column<TimeSpan>(type: "interval", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("avatar_usages_pkey", x => new { x.owner_user_id, x.avatar_id });
                table.ForeignKey(
                    name: "fk_avatar_usages_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "avatars",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: true),
                author_id = table.Column<string>(type: "text", nullable: true),
                author_name = table.Column<string>(type: "text", nullable: true),
                image_url = table.Column<string>(type: "text", nullable: true),
                thumbnail_image_url = table.Column<string>(type: "text", nullable: true),
                release_status = table.Column<string>(type: "text", nullable: true),
                tags = table.Column<string[]>(type: "text[]", nullable: false),
                version = table.Column<int>(type: "integer", nullable: true),
                featured = table.Column<bool>(type: "boolean", nullable: false),
                performance_pc = table.Column<string>(type: "text", nullable: true),
                performance_android = table.Column<string>(type: "text", nullable: true),
                performance_ios = table.Column<string>(type: "text", nullable: true),
                style_primary = table.Column<string>(type: "text", nullable: true),
                style_secondary = table.Column<string>(type: "text", nullable: true),
                platforms = table.Column<string[]>(type: "text[]", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                is_stub = table.Column<bool>(type: "boolean", nullable: false),
                first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("avatars_pkey", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "friendships",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                friend_user_id = table.Column<string>(type: "text", nullable: false),
                friend_number = table.Column<int>(type: "integer", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("friendships_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_friendships_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "game_log_clients",
            columns: table => new
            {
                client_id = table.Column<string>(type: "text", nullable: false),
                client_name = table.Column<string>(type: "text", nullable: true),
                account_id = table.Column<string>(type: "text", nullable: true),
                current_instance_location = table.Column<string>(type: "text", nullable: true),
                last_entry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("game_log_clients_pkey", x => x.client_id);
            });

        migrationBuilder.CreateTable(
            name: "group_member_roles",
            columns: table => new
            {
                group_id = table.Column<string>(type: "text", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false),
                role_id = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("group_member_roles_pkey", x => new { x.group_id, x.user_id, x.role_id });
            });

        migrationBuilder.CreateTable(
            name: "group_members",
            columns: table => new
            {
                group_id = table.Column<string>(type: "text", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false),
                membership_id = table.Column<string>(type: "text", nullable: true),
                membership_status = table.Column<string>(type: "text", nullable: false),
                visibility = table.Column<string>(type: "text", nullable: true),
                is_representing = table.Column<bool>(type: "boolean", nullable: false),
                manager_notes = table.Column<string>(type: "text", nullable: true),
                joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                banned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("group_members_pkey", x => new { x.group_id, x.user_id });
            });

        migrationBuilder.CreateTable(
            name: "group_roles",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                group_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: true),
                is_self_assignable = table.Column<bool>(type: "boolean", nullable: false),
                is_management_role = table.Column<bool>(type: "boolean", nullable: false),
                is_added_on_join = table.Column<bool>(type: "boolean", nullable: false),
                default_role = table.Column<bool>(type: "boolean", nullable: false),
                requires_two_factor = table.Column<bool>(type: "boolean", nullable: false),
                requires_purchase = table.Column<bool>(type: "boolean", nullable: false),
                permissions = table.Column<string[]>(type: "text[]", nullable: false),
                order = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                is_stub = table.Column<bool>(type: "boolean", nullable: false),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("group_roles_pkey", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "groups",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                short_code = table.Column<string>(type: "text", nullable: true),
                discriminator = table.Column<string>(type: "text", nullable: true),
                description = table.Column<string>(type: "text", nullable: true),
                icon_url = table.Column<string>(type: "text", nullable: true),
                banner_url = table.Column<string>(type: "text", nullable: true),
                owner_user_id = table.Column<string>(type: "text", nullable: true),
                privacy = table.Column<string>(type: "text", nullable: true),
                join_state = table.Column<string>(type: "text", nullable: true),
                rules = table.Column<string>(type: "text", nullable: true),
                links = table.Column<string[]>(type: "text[]", nullable: false),
                languages = table.Column<string[]>(type: "text[]", nullable: false),
                tags = table.Column<string[]>(type: "text[]", nullable: false),
                badges = table.Column<string[]>(type: "text[]", nullable: false),
                member_count = table.Column<int>(type: "integer", nullable: true),
                is_verified = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                is_stub = table.Column<bool>(type: "boolean", nullable: false),
                first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("groups_pkey", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "instance_visits",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<string>(type: "text", nullable: false),
                instance_location = table.Column<string>(type: "text", nullable: false),
                joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                source = table.Column<VisitSource>(type: "visit_source", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("instance_visits_pkey", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "instances",
            columns: table => new
            {
                location = table.Column<string>(type: "text", nullable: false),
                world_id = table.Column<string>(type: "text", nullable: false),
                instance_id = table.Column<string>(type: "text", nullable: true),
                instance_name = table.Column<string>(type: "text", nullable: false),
                type = table.Column<InstanceAccessType>(type: "instance_access_type", nullable: false),
                region = table.Column<string>(type: "text", nullable: true),
                owner_user_id = table.Column<string>(type: "text", nullable: true),
                group_id = table.Column<string>(type: "text", nullable: true),
                group_access_type = table.Column<string>(type: "text", nullable: true),
                can_request_invite = table.Column<bool>(type: "boolean", nullable: false),
                age_gate = table.Column<bool>(type: "boolean", nullable: false),
                name = table.Column<string>(type: "text", nullable: true),
                display_name = table.Column<string>(type: "text", nullable: true),
                capacity = table.Column<int>(type: "integer", nullable: true),
                queue_enabled = table.Column<bool>(type: "boolean", nullable: true),
                tags = table.Column<string[]>(type: "text[]", nullable: false),
                permanent = table.Column<bool>(type: "boolean", nullable: true),
                client_number = table.Column<string>(type: "text", nullable: true),
                closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("instances_pkey", x => x.location);
                table.ForeignKey(
                    name: "fk_instances_group_id",
                    column: x => x.group_id,
                    principalTable: "groups",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "resource_loads",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                instance_location = table.Column<string>(type: "text", nullable: true),
                url = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("resource_loads_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_resource_loads_instance_location",
                    column: x => x.instance_location,
                    principalTable: "instances",
                    principalColumn: "location",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                display_name = table.Column<string>(type: "text", nullable: false),
                bio = table.Column<string>(type: "text", nullable: true),
                bio_links = table.Column<string[]>(type: "text[]", nullable: false),
                pronouns = table.Column<string>(type: "text", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                status_description = table.Column<string>(type: "text", nullable: true),
                state = table.Column<UserState>(type: "user_state", nullable: false),
                tags = table.Column<string[]>(type: "text[]", nullable: false),
                trust_level = table.Column<string>(type: "text", nullable: false),
                developer_type = table.Column<string>(type: "text", nullable: false),
                current_avatar_image_url = table.Column<string>(type: "text", nullable: true),
                current_avatar_thumbnail_image_url = table.Column<string>(type: "text", nullable: true),
                current_avatar_tags = table.Column<string[]>(type: "text[]", nullable: false),
                profile_pic_override = table.Column<string>(type: "text", nullable: true),
                user_icon = table.Column<string>(type: "text", nullable: true),
                platform = table.Column<string>(type: "text", nullable: true),
                last_platform = table.Column<string>(type: "text", nullable: true),
                last_login = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_activity = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_mobile = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                date_joined = table.Column<DateOnly>(type: "date", nullable: true),
                age_verification_status = table.Column<string>(type: "text", nullable: true),
                age_verified = table.Column<bool>(type: "boolean", nullable: false),
                allow_avatar_copying = table.Column<bool>(type: "boolean", nullable: false),
                location = table.Column<string>(type: "text", nullable: true),
                traveling_to_location = table.Column<string>(type: "text", nullable: true),
                current_instance_location = table.Column<string>(type: "text", nullable: true),
                presence_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                is_stub = table.Column<bool>(type: "boolean", nullable: false),
                first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_full_fetch_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("users_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_users_current_instance_location",
                    column: x => x.current_instance_location,
                    principalTable: "instances",
                    principalColumn: "location",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "notifications",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                version = table.Column<int>(type: "integer", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                sender_user_id = table.Column<string>(type: "text", nullable: true),
                sender_username = table.Column<string>(type: "text", nullable: true),
                receiver_user_id = table.Column<string>(type: "text", nullable: true),
                title = table.Column<string>(type: "text", nullable: true),
                message = table.Column<string>(type: "text", nullable: false),
                image_url = table.Column<string>(type: "text", nullable: true),
                link = table.Column<string>(type: "text", nullable: true),
                link_text = table.Column<string>(type: "text", nullable: true),
                details = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                responses = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                seen = table.Column<bool>(type: "boolean", nullable: false),
                hidden = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("notifications_pkey", x => new { x.owner_user_id, x.id });
                table.ForeignKey(
                    name: "fk_notifications_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_notifications_sender_user_id",
                    column: x => x.sender_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "player_moderations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                target_user_id = table.Column<string>(type: "text", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("player_moderations_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_player_moderations_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_player_moderations_target_user_id",
                    column: x => x.target_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "portal_spawns",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                instance_location = table.Column<string>(type: "text", nullable: true),
                user_id = table.Column<string>(type: "text", nullable: true),
                display_name = table.Column<string>(type: "text", nullable: true),
                target_instance_location = table.Column<string>(type: "text", nullable: true),
                target_world_name = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("portal_spawns_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_portal_spawns_instance_location",
                    column: x => x.instance_location,
                    principalTable: "instances",
                    principalColumn: "location",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_portal_spawns_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "presence_events",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<string>(type: "text", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                kind = table.Column<PresenceKind>(type: "presence_kind", nullable: false),
                location = table.Column<string>(type: "text", nullable: true),
                previous_location = table.Column<string>(type: "text", nullable: true),
                traveling_to_location = table.Column<string>(type: "text", nullable: true),
                instance_location = table.Column<string>(type: "text", nullable: true),
                platform = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("presence_events_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_presence_events_instance_location",
                    column: x => x.instance_location,
                    principalTable: "instances",
                    principalColumn: "location",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_presence_events_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sticker_spawns",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                instance_location = table.Column<string>(type: "text", nullable: true),
                user_id = table.Column<string>(type: "text", nullable: true),
                display_name = table.Column<string>(type: "text", nullable: true),
                inventory_id = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("sticker_spawns_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_sticker_spawns_instance_location",
                    column: x => x.instance_location,
                    principalTable: "instances",
                    principalColumn: "location",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_sticker_spawns_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "user_avatar_changes",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<string>(type: "text", nullable: false),
                changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                image_url = table.Column<string>(type: "text", nullable: true),
                thumbnail_image_url = table.Column<string>(type: "text", nullable: true),
                previous_image_url = table.Column<string>(type: "text", nullable: true),
                previous_thumbnail_image_url = table.Column<string>(type: "text", nullable: true),
                avatar_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("user_avatar_changes_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_user_avatar_changes_avatar_id",
                    column: x => x.avatar_id,
                    principalTable: "avatars",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_user_avatar_changes_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_bio_changes",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<string>(type: "text", nullable: false),
                changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                bio = table.Column<string>(type: "text", nullable: true),
                bio_links = table.Column<string[]>(type: "text[]", nullable: false),
                previous_bio = table.Column<string>(type: "text", nullable: true),
                previous_bio_links = table.Column<string[]>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("user_bio_changes_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_user_bio_changes_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_display_name_changes",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<string>(type: "text", nullable: false),
                changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                previous_display_name = table.Column<string>(type: "text", nullable: false),
                display_name = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("user_display_name_changes_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_user_display_name_changes_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_memos",
            columns: table => new
            {
                user_id = table.Column<string>(type: "text", nullable: false),
                memo = table.Column<string>(type: "text", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("user_memos_pkey", x => x.user_id);
                table.ForeignKey(
                    name: "fk_user_memos_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_notes",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                owner_user_id = table.Column<string>(type: "text", nullable: false),
                target_user_id = table.Column<string>(type: "text", nullable: false),
                note = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("user_notes_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_user_notes_owner_user_id",
                    column: x => x.owner_user_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_user_notes_target_user_id",
                    column: x => x.target_user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_status_changes",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<string>(type: "text", nullable: false),
                changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                status_description = table.Column<string>(type: "text", nullable: true),
                previous_status = table.Column<string>(type: "text", nullable: false),
                previous_status_description = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("user_status_changes_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_user_status_changes_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_trust_changes",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<string>(type: "text", nullable: false),
                changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                previous_trust_level = table.Column<string>(type: "text", nullable: false),
                trust_level = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("user_trust_changes_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_user_trust_changes_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "video_plays",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                instance_location = table.Column<string>(type: "text", nullable: true),
                user_id = table.Column<string>(type: "text", nullable: true),
                display_name = table.Column<string>(type: "text", nullable: true),
                video_url = table.Column<string>(type: "text", nullable: false),
                video_name = table.Column<string>(type: "text", nullable: true),
                video_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("video_plays_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_video_plays_instance_location",
                    column: x => x.instance_location,
                    principalTable: "instances",
                    principalColumn: "location",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_video_plays_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "worlds",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                description = table.Column<string>(type: "text", nullable: true),
                author_id = table.Column<string>(type: "text", nullable: true),
                author_name = table.Column<string>(type: "text", nullable: true),
                capacity = table.Column<int>(type: "integer", nullable: true),
                recommended_capacity = table.Column<int>(type: "integer", nullable: true),
                image_url = table.Column<string>(type: "text", nullable: true),
                thumbnail_image_url = table.Column<string>(type: "text", nullable: true),
                release_status = table.Column<string>(type: "text", nullable: true),
                organization = table.Column<string>(type: "text", nullable: true),
                tags = table.Column<string[]>(type: "text[]", nullable: false),
                version = table.Column<int>(type: "integer", nullable: true),
                visits = table.Column<int>(type: "integer", nullable: true),
                favorites = table.Column<int>(type: "integer", nullable: true),
                featured = table.Column<bool>(type: "boolean", nullable: false),
                preview_youtube_id = table.Column<string>(type: "text", nullable: true),
                publication_date = table.Column<string>(type: "text", nullable: true),
                labs_publication_date = table.Column<string>(type: "text", nullable: true),
                platforms = table.Column<string[]>(type: "text[]", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                is_stub = table.Column<bool>(type: "boolean", nullable: false),
                first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("worlds_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_worlds_author_id",
                    column: x => x.author_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "world_memos",
            columns: table => new
            {
                world_id = table.Column<string>(type: "text", nullable: false),
                memo = table.Column<string>(type: "text", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("world_memos_pkey", x => x.world_id);
                table.ForeignKey(
                    name: "fk_world_memos_world_id",
                    column: x => x.world_id,
                    principalTable: "worlds",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_avatar_usages_avatar_id",
            table: "avatar_usages",
            column: "avatar_id");

        migrationBuilder.CreateIndex(
            name: "IX_avatar_usages_owner_user_id_last_used_at",
            table: "avatar_usages",
            columns: new[] { "owner_user_id", "last_used_at" });

        migrationBuilder.CreateIndex(
            name: "IX_avatars_author_id",
            table: "avatars",
            column: "author_id");

        migrationBuilder.CreateIndex(
            name: "IX_avatars_image_url",
            table: "avatars",
            column: "image_url");

        migrationBuilder.CreateIndex(
            name: "IX_avatars_name",
            table: "avatars",
            column: "name");

        migrationBuilder.CreateIndex(
            name: "IX_favorite_groups_owner_user_id_type_name",
            table: "favorite_groups",
            columns: new[] { "owner_user_id", "type", "name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_favorites_owner_user_id_type_target_id",
            table: "favorites",
            columns: new[] { "owner_user_id", "type", "target_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_friendships_friend_user_id",
            table: "friendships",
            column: "friend_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_friendships_owner_user_id_friend_number",
            table: "friendships",
            columns: new[] { "owner_user_id", "friend_number" });

        migrationBuilder.CreateIndex(
            name: "IX_friendships_owner_user_id_friend_user_id",
            table: "friendships",
            columns: new[] { "owner_user_id", "friend_user_id" },
            unique: true,
            filter: "ended_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_friendships_owner_user_id_started_at",
            table: "friendships",
            columns: new[] { "owner_user_id", "started_at" });

        migrationBuilder.CreateIndex(
            name: "IX_game_log_clients_account_id",
            table: "game_log_clients",
            column: "account_id");

        migrationBuilder.CreateIndex(
            name: "IX_game_log_clients_current_instance_location",
            table: "game_log_clients",
            column: "current_instance_location");

        migrationBuilder.CreateIndex(
            name: "IX_group_member_roles_role_id",
            table: "group_member_roles",
            column: "role_id");

        migrationBuilder.CreateIndex(
            name: "IX_group_members_membership_id",
            table: "group_members",
            column: "membership_id");

        migrationBuilder.CreateIndex(
            name: "IX_group_members_user_id",
            table: "group_members",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_group_roles_group_id",
            table: "group_roles",
            column: "group_id");

        migrationBuilder.CreateIndex(
            name: "IX_groups_name",
            table: "groups",
            column: "name");

        migrationBuilder.CreateIndex(
            name: "IX_groups_owner_user_id",
            table: "groups",
            column: "owner_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_groups_short_code_discriminator",
            table: "groups",
            columns: new[] { "short_code", "discriminator" });

        migrationBuilder.CreateIndex(
            name: "IX_instance_visits_instance_location_joined_at",
            table: "instance_visits",
            columns: new[] { "instance_location", "joined_at" });

        migrationBuilder.CreateIndex(
            name: "IX_instance_visits_user_id_joined_at",
            table: "instance_visits",
            columns: new[] { "user_id", "joined_at" });

        migrationBuilder.CreateIndex(
            name: "IX_instance_visits_user_id_source",
            table: "instance_visits",
            columns: new[] { "user_id", "source" },
            unique: true,
            filter: "left_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_instances_group_id",
            table: "instances",
            column: "group_id");

        migrationBuilder.CreateIndex(
            name: "IX_instances_owner_user_id",
            table: "instances",
            column: "owner_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_instances_world_id_first_seen_at",
            table: "instances",
            columns: new[] { "world_id", "first_seen_at" });

        migrationBuilder.CreateIndex(
            name: "IX_notifications_owner_user_id_created_at",
            table: "notifications",
            columns: new[] { "owner_user_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_notifications_owner_user_id_type_created_at",
            table: "notifications",
            columns: new[] { "owner_user_id", "type", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_notifications_sender_user_id",
            table: "notifications",
            column: "sender_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_player_moderations_owner_user_id_target_user_id_type",
            table: "player_moderations",
            columns: new[] { "owner_user_id", "target_user_id", "type" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_player_moderations_target_user_id",
            table: "player_moderations",
            column: "target_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_portal_spawns_at",
            table: "portal_spawns",
            column: "at");

        migrationBuilder.CreateIndex(
            name: "IX_portal_spawns_instance_location",
            table: "portal_spawns",
            column: "instance_location");

        migrationBuilder.CreateIndex(
            name: "IX_portal_spawns_user_id",
            table: "portal_spawns",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_presence_events_at",
            table: "presence_events",
            column: "at");

        migrationBuilder.CreateIndex(
            name: "IX_presence_events_instance_location",
            table: "presence_events",
            column: "instance_location");

        migrationBuilder.CreateIndex(
            name: "IX_presence_events_user_id_at",
            table: "presence_events",
            columns: new[] { "user_id", "at" });

        migrationBuilder.CreateIndex(
            name: "IX_resource_loads_at",
            table: "resource_loads",
            column: "at");

        migrationBuilder.CreateIndex(
            name: "IX_resource_loads_at_url",
            table: "resource_loads",
            columns: new[] { "at", "url" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_resource_loads_instance_location",
            table: "resource_loads",
            column: "instance_location");

        migrationBuilder.CreateIndex(
            name: "IX_sticker_spawns_at",
            table: "sticker_spawns",
            column: "at");

        migrationBuilder.CreateIndex(
            name: "IX_sticker_spawns_instance_location",
            table: "sticker_spawns",
            column: "instance_location");

        migrationBuilder.CreateIndex(
            name: "IX_sticker_spawns_user_id",
            table: "sticker_spawns",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_user_avatar_changes_avatar_id",
            table: "user_avatar_changes",
            column: "avatar_id");

        migrationBuilder.CreateIndex(
            name: "IX_user_avatar_changes_changed_at",
            table: "user_avatar_changes",
            column: "changed_at");

        migrationBuilder.CreateIndex(
            name: "IX_user_avatar_changes_user_id_changed_at",
            table: "user_avatar_changes",
            columns: new[] { "user_id", "changed_at" });

        migrationBuilder.CreateIndex(
            name: "IX_user_bio_changes_changed_at",
            table: "user_bio_changes",
            column: "changed_at");

        migrationBuilder.CreateIndex(
            name: "IX_user_bio_changes_user_id_changed_at",
            table: "user_bio_changes",
            columns: new[] { "user_id", "changed_at" });

        migrationBuilder.CreateIndex(
            name: "IX_user_display_name_changes_changed_at",
            table: "user_display_name_changes",
            column: "changed_at");

        migrationBuilder.CreateIndex(
            name: "IX_user_display_name_changes_user_id_changed_at",
            table: "user_display_name_changes",
            columns: new[] { "user_id", "changed_at" });

        migrationBuilder.CreateIndex(
            name: "IX_user_notes_owner_user_id_target_user_id",
            table: "user_notes",
            columns: new[] { "owner_user_id", "target_user_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_user_notes_target_user_id",
            table: "user_notes",
            column: "target_user_id");

        migrationBuilder.CreateIndex(
            name: "IX_user_status_changes_changed_at",
            table: "user_status_changes",
            column: "changed_at");

        migrationBuilder.CreateIndex(
            name: "IX_user_status_changes_user_id_changed_at",
            table: "user_status_changes",
            columns: new[] { "user_id", "changed_at" });

        migrationBuilder.CreateIndex(
            name: "IX_user_trust_changes_changed_at",
            table: "user_trust_changes",
            column: "changed_at");

        migrationBuilder.CreateIndex(
            name: "IX_user_trust_changes_user_id_changed_at",
            table: "user_trust_changes",
            columns: new[] { "user_id", "changed_at" });

        migrationBuilder.CreateIndex(
            name: "IX_users_current_instance_location",
            table: "users",
            column: "current_instance_location");

        migrationBuilder.CreateIndex(
            name: "IX_users_display_name",
            table: "users",
            column: "display_name");

        migrationBuilder.CreateIndex(
            name: "IX_users_trust_level",
            table: "users",
            column: "trust_level");

        migrationBuilder.CreateIndex(
            name: "IX_video_plays_at",
            table: "video_plays",
            column: "at");

        migrationBuilder.CreateIndex(
            name: "IX_video_plays_at_video_url",
            table: "video_plays",
            columns: new[] { "at", "video_url" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_video_plays_instance_location",
            table: "video_plays",
            column: "instance_location");

        migrationBuilder.CreateIndex(
            name: "IX_video_plays_user_id",
            table: "video_plays",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "IX_worlds_author_id",
            table: "worlds",
            column: "author_id");

        migrationBuilder.CreateIndex(
            name: "IX_worlds_name",
            table: "worlds",
            column: "name");

        migrationBuilder.AddForeignKey(
            name: "fk_accounts_user_id",
            table: "accounts",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_avatar_memos_avatar_id",
            table: "avatar_memos",
            column: "avatar_id",
            principalTable: "avatars",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_avatar_tags_avatar_id",
            table: "avatar_tags",
            column: "avatar_id",
            principalTable: "avatars",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_avatar_usages_avatar_id",
            table: "avatar_usages",
            column: "avatar_id",
            principalTable: "avatars",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_avatars_author_id",
            table: "avatars",
            column: "author_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_friendships_friend_user_id",
            table: "friendships",
            column: "friend_user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_game_log_clients_account_id",
            table: "game_log_clients",
            column: "account_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_game_log_clients_current_instance_location",
            table: "game_log_clients",
            column: "current_instance_location",
            principalTable: "instances",
            principalColumn: "location",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_group_member_roles_group_id_user_id",
            table: "group_member_roles",
            columns: new[] { "group_id", "user_id" },
            principalTable: "group_members",
            principalColumns: new[] { "group_id", "user_id" },
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_group_member_roles_role_id",
            table: "group_member_roles",
            column: "role_id",
            principalTable: "group_roles",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_group_members_group_id",
            table: "group_members",
            column: "group_id",
            principalTable: "groups",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_group_members_user_id",
            table: "group_members",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_group_roles_group_id",
            table: "group_roles",
            column: "group_id",
            principalTable: "groups",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_groups_owner_user_id",
            table: "groups",
            column: "owner_user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_instance_visits_instance_location",
            table: "instance_visits",
            column: "instance_location",
            principalTable: "instances",
            principalColumn: "location",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_instance_visits_user_id",
            table: "instance_visits",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_instances_owner_user_id",
            table: "instances",
            column: "owner_user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_instances_world_id",
            table: "instances",
            column: "world_id",
            principalTable: "worlds",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_groups_owner_user_id",
            table: "groups");

        migrationBuilder.DropForeignKey(
            name: "fk_instances_owner_user_id",
            table: "instances");

        migrationBuilder.DropForeignKey(
            name: "fk_worlds_author_id",
            table: "worlds");

        migrationBuilder.DropTable(
            name: "avatar_memos");

        migrationBuilder.DropTable(
            name: "avatar_tags");

        migrationBuilder.DropTable(
            name: "avatar_usages");

        migrationBuilder.DropTable(
            name: "favorite_groups");

        migrationBuilder.DropTable(
            name: "favorites");

        migrationBuilder.DropTable(
            name: "friendships");

        migrationBuilder.DropTable(
            name: "game_log_clients");

        migrationBuilder.DropTable(
            name: "group_member_roles");

        migrationBuilder.DropTable(
            name: "instance_visits");

        migrationBuilder.DropTable(
            name: "notifications");

        migrationBuilder.DropTable(
            name: "player_moderations");

        migrationBuilder.DropTable(
            name: "portal_spawns");

        migrationBuilder.DropTable(
            name: "presence_events");

        migrationBuilder.DropTable(
            name: "resource_loads");

        migrationBuilder.DropTable(
            name: "sticker_spawns");

        migrationBuilder.DropTable(
            name: "user_avatar_changes");

        migrationBuilder.DropTable(
            name: "user_bio_changes");

        migrationBuilder.DropTable(
            name: "user_display_name_changes");

        migrationBuilder.DropTable(
            name: "user_memos");

        migrationBuilder.DropTable(
            name: "user_notes");

        migrationBuilder.DropTable(
            name: "user_status_changes");

        migrationBuilder.DropTable(
            name: "user_trust_changes");

        migrationBuilder.DropTable(
            name: "video_plays");

        migrationBuilder.DropTable(
            name: "world_memos");

        migrationBuilder.DropTable(
            name: "group_members");

        migrationBuilder.DropTable(
            name: "group_roles");

        migrationBuilder.DropTable(
            name: "avatars");

        migrationBuilder.DropTable(
            name: "accounts");

        migrationBuilder.DropTable(
            name: "users");

        migrationBuilder.DropTable(
            name: "instances");

        migrationBuilder.DropTable(
            name: "groups");

        migrationBuilder.DropTable(
            name: "worlds");

        migrationBuilder.AlterDatabase()
            .OldAnnotation("Npgsql:Enum:favorite_type", "friend,world,avatar")
            .OldAnnotation("Npgsql:Enum:instance_access_type", "public,hidden,friends,private,group")
            .OldAnnotation("Npgsql:Enum:presence_kind", "online,active,offline,location")
            .OldAnnotation("Npgsql:Enum:user_state", "offline,active,online")
            .OldAnnotation("Npgsql:Enum:visit_source", "pipeline,game_log,api");
    }
}
