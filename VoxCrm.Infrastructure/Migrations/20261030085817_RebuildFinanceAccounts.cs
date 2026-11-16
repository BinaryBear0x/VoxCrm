using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RebuildFinanceAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinanceAccounts",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    PetOwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceAccounts", x => x.ID);
                    table.ForeignKey(
                        name: "FK_FinanceAccounts_Clinics_ClinicID",
                        column: x => x.ClinicID,
                        principalTable: "Clinics",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanceAccounts_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanceAccounts_PetOwners_PetOwnerId",
                        column: x => x.PetOwnerId,
                        principalTable: "PetOwners",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinanceChanges",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    FinanceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorName = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeforeJson = table.Column<string>(type: "text", nullable: true),
                    AfterJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceChanges", x => x.ID);
                    table.ForeignKey(
                        name: "FK_FinanceChanges_Clinics_ClinicID",
                        column: x => x.ClinicID,
                        principalTable: "Clinics",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanceChanges_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinanceCharges",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    FinanceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ChargeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceCharges", x => x.ID);
                    table.CheckConstraint("CK_FinanceCharges_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_FinanceCharges_Clinics_ClinicID",
                        column: x => x.ClinicID,
                        principalTable: "Clinics",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanceCharges_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinanceInstallments",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    FinanceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceInstallments", x => x.ID);
                    table.CheckConstraint("CK_FinanceInstallments_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_FinanceInstallments_Clinics_ClinicID",
                        column: x => x.ClinicID,
                        principalTable: "Clinics",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanceInstallments_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinanceReceipts",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    FinanceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinanceInstallmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaymentMethod = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceReceipts", x => x.ID);
                    table.CheckConstraint("CK_FinanceReceipts_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_FinanceReceipts_Clinics_ClinicID",
                        column: x => x.ClinicID,
                        principalTable: "Clinics",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanceReceipts_FinanceAccounts_FinanceAccountId",
                        column: x => x.FinanceAccountId,
                        principalTable: "FinanceAccounts",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanceReceipts_FinanceInstallments_FinanceInstallmentId",
                        column: x => x.FinanceInstallmentId,
                        principalTable: "FinanceInstallments",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAccounts_ClinicID",
                table: "FinanceAccounts",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAccounts_ClinicID_SubmissionId",
                table: "FinanceAccounts",
                columns: new[] { "ClinicID", "SubmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAccounts_PatientId",
                table: "FinanceAccounts",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAccounts_PetOwnerId",
                table: "FinanceAccounts",
                column: "PetOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceChanges_ClinicID",
                table: "FinanceChanges",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceChanges_FinanceAccountId",
                table: "FinanceChanges",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCharges_ClinicID",
                table: "FinanceCharges",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCharges_FinanceAccountId",
                table: "FinanceCharges",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceInstallments_ClinicID",
                table: "FinanceInstallments",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceInstallments_FinanceAccountId",
                table: "FinanceInstallments",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceReceipts_ClinicID",
                table: "FinanceReceipts",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceReceipts_FinanceAccountId",
                table: "FinanceReceipts",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceReceipts_FinanceInstallmentId",
                table: "FinanceReceipts",
                column: "FinanceInstallmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceChanges");

            migrationBuilder.DropTable(
                name: "FinanceCharges");

            migrationBuilder.DropTable(
                name: "FinanceReceipts");

            migrationBuilder.DropTable(
                name: "FinanceInstallments");

            migrationBuilder.DropTable(
                name: "FinanceAccounts");
        }
    }
}
