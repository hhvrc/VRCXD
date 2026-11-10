using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VRCX.Common.Migrations;

/// <inheritdoc />
public partial class _20261110173358_SavedCredentials : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "saved_credentials",
            columns: table => new
            {
                account_id = table.Column<string>(type: "text", nullable: false),
                username = table.Column<string>(type: "text", nullable: false),
                protection = table.Column<string>(type: "text", nullable: false),
                secret = table.Column<byte[]>(type: "bytea", nullable: false),
                salt = table.Column<byte[]>(type: "bytea", nullable: true),
                nonce = table.Column<byte[]>(type: "bytea", nullable: true),
                iterations = table.Column<int>(type: "integer", nullable: true),
                saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("saved_credentials_pkey", x => x.account_id);
                table.CheckConstraint("ck_saved_credentials_protection", "protection IN ('data_protection', 'primary_password')");
                table.ForeignKey(
                    name: "fk_saved_credentials_account_id",
                    column: x => x.account_id,
                    principalTable: "accounts",
                    principalColumn: "user_id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "saved_credentials");
    }
}
