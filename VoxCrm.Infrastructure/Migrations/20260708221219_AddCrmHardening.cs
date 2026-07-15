using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Patients_PatientId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Borçlar_PetOwners_PetOwnerId",
                table: "Borçlar");

            migrationBuilder.DropForeignKey(
                name: "FK_Clinics_Dealers_DealerId",
                table: "Clinics");

            migrationBuilder.DropForeignKey(
                name: "FK_Muayeneler_Appointments_AppointmentId",
                table: "Muayeneler");

            migrationBuilder.DropForeignKey(
                name: "FK_Muayeneler_Patients_PatientId",
                table: "Muayeneler");

            migrationBuilder.DropForeignKey(
                name: "FK_PatientOwners_Patients_PatientId",
                table: "PatientOwners");

            migrationBuilder.DropForeignKey(
                name: "FK_PatientOwners_PetOwners_PetOwnerId",
                table: "PatientOwners");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Borçlar_DebtId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_VaccinationRecords_Patients_PatientId",
                table: "VaccinationRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_VaccinationRecords_VaccineTypes_VaccineTypeId",
                table: "VaccinationRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppNotifications_PetOwners_PetOwnerId",
                table: "WhatsAppNotifications");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "WhatsAppTemplates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "WhatsAppTemplates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "WhatsAppNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "WhatsAppNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "WhatsAppInboundMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "WhatsAppInboundMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "VaccineTypes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "VaccineTypes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "VaccinationRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "VaccinationRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "SystemAuditLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "SystemAuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "ServiceItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "ServiceItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "PetOwners",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "PetOwners",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntryType",
                table: "Payments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Payment");

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "Payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversesPaymentId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "Patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "PatientOwners",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "PatientOwners",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Muayeneler",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "Muayeneler",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Dealers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "Dealers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Clinics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "Clinics",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Clinics",
                type: "text",
                nullable: false,
                defaultValue: "Europe/Istanbul");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Borçlar",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "Borçlar",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Borçlar",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Borçlar",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "Borçlar",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedByUserId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReversesPaymentId",
                table: "Payments",
                column: "ReversesPaymentId",
                unique: true,
                filter: "\"ReversesPaymentId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Amount_Positive",
                table: "Payments",
                sql: "\"Amount\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Borçlar_Amount_Positive",
                table: "Borçlar",
                sql: "\"Amount\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicID_ScheduledAt",
                table: "Appointments",
                columns: new[] { "ClinicID", "ScheduledAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Patients_PatientId",
                table: "Appointments",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Borçlar_PetOwners_PetOwnerId",
                table: "Borçlar",
                column: "PetOwnerId",
                principalTable: "PetOwners",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Clinics_Dealers_DealerId",
                table: "Clinics",
                column: "DealerId",
                principalTable: "Dealers",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Muayeneler_Appointments_AppointmentId",
                table: "Muayeneler",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Muayeneler_Patients_PatientId",
                table: "Muayeneler",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PatientOwners_Patients_PatientId",
                table: "PatientOwners",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PatientOwners_PetOwners_PetOwnerId",
                table: "PatientOwners",
                column: "PetOwnerId",
                principalTable: "PetOwners",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Borçlar_DebtId",
                table: "Payments",
                column: "DebtId",
                principalTable: "Borçlar",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Payments_ReversesPaymentId",
                table: "Payments",
                column: "ReversesPaymentId",
                principalTable: "Payments",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VaccinationRecords_Patients_PatientId",
                table: "VaccinationRecords",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VaccinationRecords_VaccineTypes_VaccineTypeId",
                table: "VaccinationRecords",
                column: "VaccineTypeId",
                principalTable: "VaccineTypes",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppNotifications_PetOwners_PetOwnerId",
                table: "WhatsAppNotifications",
                column: "PetOwnerId",
                principalTable: "PetOwners",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Patients_PatientId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Borçlar_PetOwners_PetOwnerId",
                table: "Borçlar");

            migrationBuilder.DropForeignKey(
                name: "FK_Clinics_Dealers_DealerId",
                table: "Clinics");

            migrationBuilder.DropForeignKey(
                name: "FK_Muayeneler_Appointments_AppointmentId",
                table: "Muayeneler");

            migrationBuilder.DropForeignKey(
                name: "FK_Muayeneler_Patients_PatientId",
                table: "Muayeneler");

            migrationBuilder.DropForeignKey(
                name: "FK_PatientOwners_Patients_PatientId",
                table: "PatientOwners");

            migrationBuilder.DropForeignKey(
                name: "FK_PatientOwners_PetOwners_PetOwnerId",
                table: "PatientOwners");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Borçlar_DebtId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Payments_ReversesPaymentId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_VaccinationRecords_Patients_PatientId",
                table: "VaccinationRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_VaccinationRecords_VaccineTypes_VaccineTypeId",
                table: "VaccinationRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppNotifications_PetOwners_PetOwnerId",
                table: "WhatsAppNotifications");

            migrationBuilder.DropIndex(
                name: "IX_Payments_ReversesPaymentId",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Amount_Positive",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Borçlar_Amount_Positive",
                table: "Borçlar");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicID_ScheduledAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "WhatsAppTemplates");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "WhatsAppTemplates");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "WhatsAppInboundMessages");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "WhatsAppInboundMessages");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "VaccineTypes");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "VaccineTypes");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "VaccinationRecords");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "VaccinationRecords");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "SystemAuditLogs");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "SystemAuditLogs");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "ServiceItems");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "ServiceItems");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "PetOwners");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "PetOwners");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "EntryType",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReversesPaymentId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "PatientOwners");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "PatientOwners");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Muayeneler");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "Muayeneler");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Dealers");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "Dealers");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Borçlar");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "Borçlar");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Borçlar");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Borçlar");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Borçlar");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "Appointments");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Patients_PatientId",
                table: "Appointments",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Borçlar_PetOwners_PetOwnerId",
                table: "Borçlar",
                column: "PetOwnerId",
                principalTable: "PetOwners",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Clinics_Dealers_DealerId",
                table: "Clinics",
                column: "DealerId",
                principalTable: "Dealers",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Muayeneler_Appointments_AppointmentId",
                table: "Muayeneler",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "ID");

            migrationBuilder.AddForeignKey(
                name: "FK_Muayeneler_Patients_PatientId",
                table: "Muayeneler",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PatientOwners_Patients_PatientId",
                table: "PatientOwners",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PatientOwners_PetOwners_PetOwnerId",
                table: "PatientOwners",
                column: "PetOwnerId",
                principalTable: "PetOwners",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Borçlar_DebtId",
                table: "Payments",
                column: "DebtId",
                principalTable: "Borçlar",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VaccinationRecords_Patients_PatientId",
                table: "VaccinationRecords",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VaccinationRecords_VaccineTypes_VaccineTypeId",
                table: "VaccinationRecords",
                column: "VaccineTypeId",
                principalTable: "VaccineTypes",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppNotifications_PetOwners_PetOwnerId",
                table: "WhatsAppNotifications",
                column: "PetOwnerId",
                principalTable: "PetOwners",
                principalColumn: "ID",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
