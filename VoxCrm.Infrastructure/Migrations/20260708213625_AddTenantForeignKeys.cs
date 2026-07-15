using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppTemplates_ClinicID",
                table: "WhatsAppTemplates",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppNotifications_ClinicID",
                table: "WhatsAppNotifications",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInboundMessages_ClinicID",
                table: "WhatsAppInboundMessages",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_VaccineTypes_ClinicID",
                table: "VaccineTypes",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_VaccinationRecords_ClinicID",
                table: "VaccinationRecords",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceItems_ClinicID",
                table: "ServiceItems",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_PetOwners_ClinicID",
                table: "PetOwners",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ClinicID",
                table: "Payments",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicID",
                table: "Patients",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_PatientOwners_ClinicID",
                table: "PatientOwners",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_Muayeneler_ClinicID",
                table: "Muayeneler",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_Borçlar_ClinicID",
                table: "Borçlar",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicID",
                table: "Appointments",
                column: "ClinicID");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Clinics_ClinicID",
                table: "Appointments",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Borçlar_Clinics_ClinicID",
                table: "Borçlar",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Muayeneler_Clinics_ClinicID",
                table: "Muayeneler",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PatientOwners_Clinics_ClinicID",
                table: "PatientOwners",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Clinics_ClinicID",
                table: "Patients",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Clinics_ClinicID",
                table: "Payments",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PetOwners_Clinics_ClinicID",
                table: "PetOwners",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceItems_Clinics_ClinicID",
                table: "ServiceItems",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VaccinationRecords_Clinics_ClinicID",
                table: "VaccinationRecords",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VaccineTypes_Clinics_ClinicID",
                table: "VaccineTypes",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppInboundMessages_Clinics_ClinicID",
                table: "WhatsAppInboundMessages",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppNotifications_Clinics_ClinicID",
                table: "WhatsAppNotifications",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppTemplates_Clinics_ClinicID",
                table: "WhatsAppTemplates",
                column: "ClinicID",
                principalTable: "Clinics",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Clinics_ClinicID",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Borçlar_Clinics_ClinicID",
                table: "Borçlar");

            migrationBuilder.DropForeignKey(
                name: "FK_Muayeneler_Clinics_ClinicID",
                table: "Muayeneler");

            migrationBuilder.DropForeignKey(
                name: "FK_PatientOwners_Clinics_ClinicID",
                table: "PatientOwners");

            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Clinics_ClinicID",
                table: "Patients");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Clinics_ClinicID",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_PetOwners_Clinics_ClinicID",
                table: "PetOwners");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceItems_Clinics_ClinicID",
                table: "ServiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_VaccinationRecords_Clinics_ClinicID",
                table: "VaccinationRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_VaccineTypes_Clinics_ClinicID",
                table: "VaccineTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppInboundMessages_Clinics_ClinicID",
                table: "WhatsAppInboundMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppNotifications_Clinics_ClinicID",
                table: "WhatsAppNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppTemplates_Clinics_ClinicID",
                table: "WhatsAppTemplates");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppTemplates_ClinicID",
                table: "WhatsAppTemplates");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppNotifications_ClinicID",
                table: "WhatsAppNotifications");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppInboundMessages_ClinicID",
                table: "WhatsAppInboundMessages");

            migrationBuilder.DropIndex(
                name: "IX_VaccineTypes_ClinicID",
                table: "VaccineTypes");

            migrationBuilder.DropIndex(
                name: "IX_VaccinationRecords_ClinicID",
                table: "VaccinationRecords");

            migrationBuilder.DropIndex(
                name: "IX_ServiceItems_ClinicID",
                table: "ServiceItems");

            migrationBuilder.DropIndex(
                name: "IX_PetOwners_ClinicID",
                table: "PetOwners");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ClinicID",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicID",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_PatientOwners_ClinicID",
                table: "PatientOwners");

            migrationBuilder.DropIndex(
                name: "IX_Muayeneler_ClinicID",
                table: "Muayeneler");

            migrationBuilder.DropIndex(
                name: "IX_Borçlar_ClinicID",
                table: "Borçlar");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicID",
                table: "Appointments");
        }
    }
}
