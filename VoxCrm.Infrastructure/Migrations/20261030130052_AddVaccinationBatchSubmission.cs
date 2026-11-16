using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVaccinationBatchSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BatchFingerprint",
                table: "VaccinationRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchSubmissionId",
                table: "VaccinationRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VaccinationRecords_ClinicID_BatchSubmissionId",
                table: "VaccinationRecords",
                columns: new[] { "ClinicID", "BatchSubmissionId" },
                filter: "\"BatchSubmissionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VaccinationRecords_ClinicID_BatchSubmissionId",
                table: "VaccinationRecords");

            migrationBuilder.DropColumn(
                name: "BatchFingerprint",
                table: "VaccinationRecords");

            migrationBuilder.DropColumn(
                name: "BatchSubmissionId",
                table: "VaccinationRecords");
        }
    }
}
