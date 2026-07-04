using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxCrm.Infrastructure.Data;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(VoxCrmDbContext))]
    [Migration("20260702090000_AddWhatsAppGatewayIntegration")]
    public partial class AddWhatsAppGatewayIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ErrorMessage",
                table: "WhatsAppNotifications",
                newName: "LastError");

            migrationBuilder.AddColumn<string>(
                name: "GatewayMessageId",
                table: "WhatsAppNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "WhatsAppNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                table: "WhatsAppNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockExpiresAt",
                table: "WhatsAppNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "WhatsAppNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "WhatsAppNotifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "WhatsAppInboundMessages",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    FromPhone = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GatewaySessionId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppInboundMessages", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppTemplates",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationType = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppTemplates", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppNotifications_ClinicID_NotificationType_Status_NextAttemptAt",
                table: "WhatsAppNotifications",
                columns: new[] { "ClinicID", "NotificationType", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppNotifications_GatewayMessageId",
                table: "WhatsAppNotifications",
                column: "GatewayMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInboundMessages_ClinicID_ReceivedAt",
                table: "WhatsAppInboundMessages",
                columns: new[] { "ClinicID", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppTemplates_ClinicID_NotificationType_IsActive",
                table: "WhatsAppTemplates",
                columns: new[] { "ClinicID", "NotificationType", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppInboundMessages");

            migrationBuilder.DropTable(
                name: "WhatsAppTemplates");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppNotifications_ClinicID_NotificationType_Status_NextAttemptAt",
                table: "WhatsAppNotifications");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppNotifications_GatewayMessageId",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "GatewayMessageId",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "LockedBy",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "LockExpiresAt",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "WhatsAppNotifications");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "WhatsAppNotifications");

            migrationBuilder.RenameColumn(
                name: "LastError",
                table: "WhatsAppNotifications",
                newName: "ErrorMessage");
        }
    }
}
