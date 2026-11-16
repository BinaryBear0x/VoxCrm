using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkVaccinationReminderNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VaccinationRecordId",
                table: "WhatsAppNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppNotifications_ClinicID_VaccinationRecordId_Notifica~",
                table: "WhatsAppNotifications",
                columns: new[] { "ClinicID", "VaccinationRecordId", "NotificationType" },
                unique: true,
                filter: "\"VaccinationRecordId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppNotifications_VaccinationRecordId",
                table: "WhatsAppNotifications",
                column: "VaccinationRecordId");

            migrationBuilder.AddForeignKey(
                name: "FK_WhatsAppNotifications_VaccinationRecords_VaccinationRecordId",
                table: "WhatsAppNotifications",
                column: "VaccinationRecordId",
                principalTable: "VaccinationRecords",
                principalColumn: "ID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WhatsAppNotifications_VaccinationRecords_VaccinationRecordId",
                table: "WhatsAppNotifications");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppNotifications_ClinicID_VaccinationRecordId_Notifica~",
                table: "WhatsAppNotifications");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppNotifications_VaccinationRecordId",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "VaccinationRecordId",
                table: "WhatsAppNotifications");
        }
    }
}
