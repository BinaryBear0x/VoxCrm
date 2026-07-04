using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxCrm.Infrastructure.Data;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    [DbContext(typeof(VoxCrmDbContext))]
    [Migration("20260704120000_AddSystemAuditLogs")]
    public partial class AddSystemAuditLogs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemAuditLogs",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: true),
                    EntityId = table.Column<string>(type: "text", nullable: true),
                    DealerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserName = table.Column<string>(type: "text", nullable: true),
                    ActorRole = table.Column<string>(type: "text", nullable: true),
                    HttpMethod = table.Column<string>(type: "text", nullable: true),
                    Path = table.Column<string>(type: "text", nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    ExceptionType = table.Column<string>(type: "text", nullable: true),
                    TraceId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAuditLogs", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditLogs_ClinicId_CreatedAt",
                table: "SystemAuditLogs",
                columns: new[] { "ClinicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditLogs_CreatedAt",
                table: "SystemAuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditLogs_DealerId_CreatedAt",
                table: "SystemAuditLogs",
                columns: new[] { "DealerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAuditLogs_Level_CreatedAt",
                table: "SystemAuditLogs",
                columns: new[] { "Level", "CreatedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemAuditLogs");
        }
    }
}
