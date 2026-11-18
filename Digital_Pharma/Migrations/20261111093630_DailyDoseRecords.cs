using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Digital_Pharma.Migrations
{
    /// <inheritdoc />
    public partial class DailyDoseRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DoseRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    MedicationId = table.Column<int>(type: "int", nullable: false),
                    Day = table.Column<DateTime>(type: "date", nullable: false),
                    Time = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DrugName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DoseAmount = table.Column<decimal>(type: "decimal(12,3)", nullable: false),
                    DoseUnit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoseRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoseRecords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DoseChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DoseRecordId = table.Column<int>(type: "int", nullable: false),
                    BeforeStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AfterStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoseChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoseChanges_DoseRecords_DoseRecordId",
                        column: x => x.DoseRecordId,
                        principalTable: "DoseRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoseChanges_DoseRecordId",
                table: "DoseChanges",
                column: "DoseRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_DoseRecords_UserId_MedicationId_Day_Time",
                table: "DoseRecords",
                columns: new[] { "UserId", "MedicationId", "Day", "Time" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoseChanges");

            migrationBuilder.DropTable(
                name: "DoseRecords");
        }
    }
}
