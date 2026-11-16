using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreateSubmissionIdempotencyV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "VaccinationRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "PetOwners",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "Patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VaccinationRecords_ClinicID_SubmissionId",
                table: "VaccinationRecords",
                columns: new[] { "ClinicID", "SubmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PetOwners_ClinicID_SubmissionId",
                table: "PetOwners",
                columns: new[] { "ClinicID", "SubmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicID_SubmissionId",
                table: "Patients",
                columns: new[] { "ClinicID", "SubmissionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VaccinationRecords_ClinicID_SubmissionId",
                table: "VaccinationRecords");

            migrationBuilder.DropIndex(
                name: "IX_PetOwners_ClinicID_SubmissionId",
                table: "PetOwners");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicID_SubmissionId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "VaccinationRecords");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "PetOwners");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "Patients");
        }
    }
}
