using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Digital_Pharma.Migrations
{
    /// <inheritdoc />
    public partial class SecureResultSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShareExpiresAt",
                table: "Searches",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShareRevokedAt",
                table: "Searches",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ShareTokenHash",
                table: "Searches",
                type: "varbinary(32)",
                maxLength: 32,
                nullable: true);

            // Legacy links had no expiry and stored the bearer token itself. Revoke them on upgrade.
            migrationBuilder.Sql("UPDATE [Searches] SET [ShareToken] = NULL WHERE [ShareToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Searches_ShareTokenHash",
                table: "Searches",
                column: "ShareTokenHash",
                unique: true,
                filter: "[ShareTokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Searches_ShareTokenHash",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "ShareExpiresAt",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "ShareRevokedAt",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "ShareTokenHash",
                table: "Searches");
        }
    }
}
