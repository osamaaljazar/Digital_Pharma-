using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Digital_Pharma.Migrations
{
    /// <inheritdoc />
    public partial class LinkedHealthProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserMedications_UserId",
                table: "UserMedications");

            migrationBuilder.AddColumn<string>(
                name: "HealthNotes",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.Sql("UPDATE [Users] SET [HealthNotes] = [MedicalConditions] WHERE [MedicalConditions] IS NOT NULL");

            migrationBuilder.AddColumn<byte[]>(
                name: "HealthRowVersion",
                table: "Users",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HealthUpdatedAt",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelId",
                table: "UserMedications",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelJson",
                table: "UserMedications",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LabelRetrievedAt",
                table: "UserMedications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PatientAllergies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SubstanceKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Reaction = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReportedSeverity = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientAllergies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientAllergies_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatientConditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientConditions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserMedications_UserId_LabelId",
                table: "UserMedications",
                columns: new[] { "UserId", "LabelId" },
                unique: true,
                filter: "[LabelId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PatientAllergies_UserId",
                table: "PatientAllergies",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientConditions_UserId_Code",
                table: "PatientConditions",
                columns: new[] { "UserId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatientAllergies");

            migrationBuilder.DropTable(
                name: "PatientConditions");

            migrationBuilder.DropIndex(
                name: "IX_UserMedications_UserId_LabelId",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "HealthNotes",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HealthRowVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HealthUpdatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LabelId",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "LabelJson",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "LabelRetrievedAt",
                table: "UserMedications");

            migrationBuilder.CreateIndex(
                name: "IX_UserMedications_UserId",
                table: "UserMedications",
                column: "UserId");
        }
    }
}
