using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppRecipientSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAutomatic",
                table: "WhatsAppNotifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PatientId",
                table: "WhatsAppNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientSnapshot",
                table: "WhatsAppNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutomaticVaccinationRemindersEnabled",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ManualWhatsAppEnabled",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAutomatic",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "PatientId",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "RecipientSnapshot",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "AutomaticVaccinationRemindersEnabled",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "ManualWhatsAppEnabled",
                table: "Clinics");
        }
    }
}
