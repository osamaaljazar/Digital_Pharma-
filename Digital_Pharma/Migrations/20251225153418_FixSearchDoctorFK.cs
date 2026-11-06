using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Digital_Pharma.Migrations
{
    /// <inheritdoc />
    public partial class FixSearchDoctorFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Doctors_Users_UserId",
                table: "Doctors");

            migrationBuilder.DropForeignKey(
                name: "FK_Searches_Users_UserId",
                table: "Searches");

            migrationBuilder.AddColumn<int>(
                name: "DoctorId",
                table: "Searches",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Searches_DoctorId",
                table: "Searches",
                column: "DoctorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Doctors_Users_UserId",
                table: "Doctors",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Searches_Doctors_DoctorId",
                table: "Searches",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Searches_Users_UserId",
                table: "Searches",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Doctors_Users_UserId",
                table: "Doctors");

            migrationBuilder.DropForeignKey(
                name: "FK_Searches_Doctors_DoctorId",
                table: "Searches");

            migrationBuilder.DropForeignKey(
                name: "FK_Searches_Users_UserId",
                table: "Searches");

            migrationBuilder.DropIndex(
                name: "IX_Searches_DoctorId",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "DoctorId",
                table: "Searches");

            migrationBuilder.AddForeignKey(
                name: "FK_Doctors_Users_UserId",
                table: "Doctors",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Searches_Users_UserId",
                table: "Searches",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
