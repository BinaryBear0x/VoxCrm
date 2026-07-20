using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmDataConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedPhone",
                table: "PetOwners",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "PetOwners"
                SET "NormalizedPhone" = NULLIF(
                    regexp_replace(COALESCE("Phone", ''), '[^0-9]', '', 'g'),
                    '')
                """);

            migrationBuilder.AlterColumn<string>(
                name: "TimeZoneId",
                table: "Clinics",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_VaccineTypes_ClinicID_Name",
                table: "VaccineTypes",
                columns: new[] { "ClinicID", "Name" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VaccineTypes_ReminderDaysBefore",
                table: "VaccineTypes",
                sql: "\"ReminderDaysBefore\" >= 0 AND \"ReminderDaysBefore\" <= \"ValidityDays\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VaccineTypes_ValidityDays",
                table: "VaccineTypes",
                sql: "\"ValidityDays\" BETWEEN 1 AND 3650");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceItems_ClinicID_Name",
                table: "ServiceItems",
                columns: new[] { "ClinicID", "Name" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServiceItems_Price_NonNegative",
                table: "ServiceItems",
                sql: "\"Price\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_PetOwners_ClinicID_NormalizedPhone",
                table: "PetOwners",
                columns: new[] { "ClinicID", "NormalizedPhone" },
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"NormalizedPhone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicID_MicrochipNumber",
                table: "Patients",
                columns: new[] { "ClinicID", "MicrochipNumber" },
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"MicrochipNumber\" IS NOT NULL AND \"MicrochipNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_PatientOwners_ClinicID_PatientId",
                table: "PatientOwners",
                columns: new[] { "ClinicID", "PatientId" },
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"IsPrimaryOwner\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_PatientOwners_ClinicID_PatientId_PetOwnerId",
                table: "PatientOwners",
                columns: new[] { "ClinicID", "PatientId", "PetOwnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_Slug",
                table: "Clinics",
                column: "Slug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_DurationMinutes",
                table: "Appointments",
                sql: "\"DurationMinutes\" BETWEEN 10 AND 240");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VaccineTypes_ClinicID_Name",
                table: "VaccineTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VaccineTypes_ReminderDaysBefore",
                table: "VaccineTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VaccineTypes_ValidityDays",
                table: "VaccineTypes");

            migrationBuilder.DropIndex(
                name: "IX_ServiceItems_ClinicID_Name",
                table: "ServiceItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceItems_Price_NonNegative",
                table: "ServiceItems");

            migrationBuilder.DropIndex(
                name: "IX_PetOwners_ClinicID_NormalizedPhone",
                table: "PetOwners");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicID_MicrochipNumber",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_PatientOwners_ClinicID_PatientId",
                table: "PatientOwners");

            migrationBuilder.DropIndex(
                name: "IX_PatientOwners_ClinicID_PatientId_PetOwnerId",
                table: "PatientOwners");

            migrationBuilder.DropIndex(
                name: "IX_Clinics_Slug",
                table: "Clinics");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_DurationMinutes",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "NormalizedPhone",
                table: "PetOwners");

            migrationBuilder.AlterColumn<string>(
                name: "TimeZoneId",
                table: "Clinics",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
