using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersistExcelImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExcelImportBatches",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    PatientCount = table.Column<int>(type: "integer", nullable: false),
                    UndoneAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UndoneBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcelImportBatches", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ExcelImportBatches_Clinics_ClinicID",
                        column: x => x.ClinicID,
                        principalTable: "Clinics",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExcelImportBatches_ClinicID",
                table: "ExcelImportBatches",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_ExcelImportBatches_ClinicID_SubmissionId",
                table: "ExcelImportBatches",
                columns: new[] { "ClinicID", "SubmissionId" },
                unique: true);
            // Preserve existing import manifests before generic audit retention can remove them.
            migrationBuilder.Sql("""
                INSERT INTO "ExcelImportBatches" ("ID","ClinicID","SubmissionId","ActorUserId","ManifestJson","PatientCount","CreatedAt","IsActive")
                SELECT DISTINCT ON (l."ClinicId", l."EntityId") l."ID",l."ClinicId",l."EntityId"::uuid,COALESCE(l."ActorUserId",'00000000-0000-0000-0000-000000000000'::uuid),l."MetadataJson",
                    CASE WHEN l."Message" ~ '^[0-9]+' THEN substring(l."Message" from '^[0-9]+')::int ELSE 0 END,l."CreatedAt",true
                FROM "SystemAuditLogs" l JOIN "Clinics" c ON c."ID"=l."ClinicId"
                WHERE l."Action"='Excel.Imported' AND l."MetadataJson" IS NOT NULL
                  AND l."EntityId" ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                ORDER BY l."ClinicId",l."EntityId",l."CreatedAt";
                UPDATE "ExcelImportBatches" b SET "UndoneAt"=u."CreatedAt","UndoneBy"=u."ActorUserId"
                FROM "SystemAuditLogs" u WHERE u."Action"='Excel.Undone' AND u."ClinicId"=b."ClinicID" AND u."EntityId"=b."SubmissionId"::text;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExcelImportBatches");
        }
    }
}
