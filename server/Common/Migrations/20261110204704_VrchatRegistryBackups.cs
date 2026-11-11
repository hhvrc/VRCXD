using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261110204704_VrchatRegistryBackups : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "vrchat_registry_backups",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                account_id = table.Column<string>(type: "text", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                data = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                restore_offered = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("vrchat_registry_backups_pkey", x => x.id);
                table.ForeignKey(
                    name: "fk_vrchat_registry_backups_account_id",
                    column: x => x.account_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_vrchat_registry_backups_account_id_created_at",
            table: "vrchat_registry_backups",
            columns: new[] { "account_id", "created_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "vrchat_registry_backups");
    }
}
