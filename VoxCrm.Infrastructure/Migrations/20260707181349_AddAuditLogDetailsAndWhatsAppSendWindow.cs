using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogDetailsAndWhatsAppSendWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "SystemAuditLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMs",
                table: "SystemAuditLogs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "SystemAuditLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "SystemAuditLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "SystemAuditLogs",
                type: "text",
                nullable: false,
                defaultValue: "Success");

            migrationBuilder.AddColumn<string>(
                name: "RequestId",
                table: "SystemAuditLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "SystemAuditLogs",
                type: "text",
                nullable: false,
                defaultValue: "Web");

            migrationBuilder.AddColumn<bool>(
                name: "WhatsAppSendWindowEnabled",
                table: "Clinics",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WhatsAppSendWindowEnd",
                table: "Clinics",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(19, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WhatsAppSendWindowStart",
                table: "Clinics",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(9, 0, 0));

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppTimeZoneId",
                table: "Clinics",
                type: "text",
                nullable: false,
                defaultValue: "Europe/Istanbul");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditLogs_Source_Category_CreatedAt",
                table: "SystemAuditLogs",
                columns: new[] { "Source", "Category", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SystemAuditLogs_Source_Category_CreatedAt",
                table: "SystemAuditLogs");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "SystemAuditLogs");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "SystemAuditLogs");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "SystemAuditLogs");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "SystemAuditLogs");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "SystemAuditLogs");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "SystemAuditLogs");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "SystemAuditLogs");

            migrationBuilder.DropColumn(
                name: "WhatsAppSendWindowEnabled",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "WhatsAppSendWindowEnd",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "WhatsAppSendWindowStart",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "WhatsAppTimeZoneId",
                table: "Clinics");
        }
    }
}
