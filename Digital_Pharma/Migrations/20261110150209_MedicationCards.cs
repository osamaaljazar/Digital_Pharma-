using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Digital_Pharma.Migrations
{
    /// <inheritdoc />
    public partial class MedicationCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DailyTimes",
                table: "UserMedications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DoseAmount",
                table: "UserMedications",
                type: "decimal(12,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoseUnit",
                table: "UserMedications",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "UserMedications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instructions",
                table: "UserMedications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsStopped",
                table: "UserMedications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "UserMedications",
                type: "decimal(12,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuantityUnit",
                table: "UserMedications",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "UserMedications",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleKind",
                table: "UserMedications",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "unspecified");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "UserMedications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MedicationChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserMedicationId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicationChanges_UserMedications_UserMedicationId",
                        column: x => x.UserMedicationId,
                        principalTable: "UserMedications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicationChanges_UserMedicationId",
                table: "MedicationChanges",
                column: "UserMedicationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicationChanges");

            migrationBuilder.DropColumn(
                name: "DailyTimes",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "DoseAmount",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "DoseUnit",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "Instructions",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "IsStopped",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "QuantityUnit",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "ScheduleKind",
                table: "UserMedications");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "UserMedications");
        }
    }
}
