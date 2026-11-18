using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Digital_Pharma.Migrations
{
    /// <inheritdoc />
    public partial class ClinicalAlertDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicalAlertDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CareLinkId = table.Column<int>(type: "int", nullable: false),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    DoctorUserId = table.Column<int>(type: "int", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", maxLength: 500, nullable: false),
                    AlertSnapshot = table.Column<string>(type: "nvarchar(max)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalAlertDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalAlertDecisions_CareLinks_CareLinkId",
                        column: x => x.CareLinkId,
                        principalTable: "CareLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClinicalAlertDecisions_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAlertDecisions_CareLinkId_AlertKey_CreatedAt",
                table: "ClinicalAlertDecisions",
                columns: new[] { "CareLinkId", "AlertKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAlertDecisions_OperationId",
                table: "ClinicalAlertDecisions",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalAlertDecisions_PatientId",
                table: "ClinicalAlertDecisions",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicalAlertDecisions");
        }
    }
}
