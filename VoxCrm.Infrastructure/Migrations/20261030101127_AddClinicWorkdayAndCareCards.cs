using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicWorkdayAndCareCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingReviewNote",
                table: "Muayeneler",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BillingReviewedAt",
                table: "Muayeneler",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BillingReviewedBy",
                table: "Muayeneler",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedFinanceAccountId",
                table: "Muayeneler",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanApproveCareCards",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CareCards",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    ExaminationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PetOwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TodayActions = table.Column<string>(type: "text", nullable: false),
                    HomeSteps = table.Column<string>(type: "text", nullable: false),
                    CallInstructions = table.Column<string>(type: "text", nullable: false),
                    FollowUpDate = table.Column<DateOnly>(type: "date", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecipientSnapshot = table.Column<string>(type: "text", nullable: true),
                    TokenHash = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OwnerResponse = table.Column<string>(type: "text", nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareCards", x => x.ID);
                    table.ForeignKey(
                        name: "FK_CareCards_Clinics_ClinicID",
                        column: x => x.ClinicID,
                        principalTable: "Clinics",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareCards_Muayeneler_ExaminationId",
                        column: x => x.ExaminationId,
                        principalTable: "Muayeneler",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareCards_PetOwners_PetOwnerId",
                        column: x => x.PetOwnerId,
                        principalTable: "PetOwners",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClosingAcknowledgements",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IssueKey = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosingAcknowledgements", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ClosingAcknowledgements_Clinics_ClinicID",
                        column: x => x.ClinicID,
                        principalTable: "Clinics",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicTasks",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CareCardId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Important = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicTasks", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ClinicTasks_CareCards_CareCardId",
                        column: x => x.CareCardId,
                        principalTable: "CareCards",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicTasks_Clinics_ClinicID",
                        column: x => x.ClinicID,
                        principalTable: "Clinics",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicTasks_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareCards_ClinicID",
                table: "CareCards",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_CareCards_ClinicID_ExaminationId",
                table: "CareCards",
                columns: new[] { "ClinicID", "ExaminationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareCards_ExaminationId",
                table: "CareCards",
                column: "ExaminationId");

            migrationBuilder.CreateIndex(
                name: "IX_CareCards_PetOwnerId",
                table: "CareCards",
                column: "PetOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CareCards_TokenHash",
                table: "CareCards",
                column: "TokenHash",
                unique: true,
                filter: "\"TokenHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicTasks_CareCardId_Kind",
                table: "ClinicTasks",
                columns: new[] { "CareCardId", "Kind" },
                unique: true,
                filter: "\"CareCardId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicTasks_ClinicID",
                table: "ClinicTasks",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicTasks_ClinicID_SubmissionId",
                table: "ClinicTasks",
                columns: new[] { "ClinicID", "SubmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicTasks_PatientId",
                table: "ClinicTasks",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClosingAcknowledgements_ClinicID",
                table: "ClosingAcknowledgements",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_ClosingAcknowledgements_ClinicID_Date_IssueKey",
                table: "ClosingAcknowledgements",
                columns: new[] { "ClinicID", "Date", "IssueKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicTasks");

            migrationBuilder.DropTable(
                name: "ClosingAcknowledgements");

            migrationBuilder.DropTable(
                name: "CareCards");

            migrationBuilder.DropColumn(
                name: "BillingReviewNote",
                table: "Muayeneler");

            migrationBuilder.DropColumn(
                name: "BillingReviewedAt",
                table: "Muayeneler");

            migrationBuilder.DropColumn(
                name: "BillingReviewedBy",
                table: "Muayeneler");

            migrationBuilder.DropColumn(
                name: "ReviewedFinanceAccountId",
                table: "Muayeneler");

            migrationBuilder.DropColumn(
                name: "CanApproveCareCards",
                table: "AspNetUsers");
        }
    }
}
