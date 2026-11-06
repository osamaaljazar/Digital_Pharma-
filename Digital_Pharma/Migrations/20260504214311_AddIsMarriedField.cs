using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Digital_Pharma.Migrations
{
    /// <inheritdoc />
    public partial class AddIsMarriedField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMarried",
                table: "Users",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafeAlternatives",
                table: "Searches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShareToken",
                table: "Searches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserMedications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DrugName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMedications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMedications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserMedications_UserId",
                table: "UserMedications",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserMedications");

            migrationBuilder.DropColumn(
                name: "IsMarried",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SafeAlternatives",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "ShareToken",
                table: "Searches");
        }
    }
}
