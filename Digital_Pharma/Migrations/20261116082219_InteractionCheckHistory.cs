using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Digital_Pharma.Migrations
{
    /// <inheritdoc />
    public partial class InteractionCheckHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Searches_UserId",
                table: "Searches");

            migrationBuilder.AddColumn<Guid>(
                name: "CheckGroupId",
                table: "Searches",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckKind",
                table: "Searches",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "pair");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EvaluatedAt",
                table: "Searches",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceNote",
                table: "Searches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceStatus",
                table: "Searches",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<string>(
                name: "SourceSummary",
                table: "Searches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Searches_UserId_CheckGroupId",
                table: "Searches",
                columns: new[] { "UserId", "CheckGroupId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Searches_UserId_CheckGroupId",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "CheckGroupId",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "CheckKind",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "EvaluatedAt",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "EvidenceNote",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "EvidenceStatus",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "SourceSummary",
                table: "Searches");

            migrationBuilder.CreateIndex(
                name: "IX_Searches_UserId",
                table: "Searches",
                column: "UserId");
        }
    }
}
