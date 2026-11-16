using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerFollowUpSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OwnerFollowUpShares",
                columns: table => new
                {
                    ID = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicID = table.Column<Guid>(type: "uuid", nullable: false),
                    PetOwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientIdsJson = table.Column<string>(type: "text", nullable: false),
                    RecipientSnapshot = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ShowVaccinations = table.Column<bool>(type: "boolean", nullable: false),
                    ShowHistory = table.Column<bool>(type: "boolean", nullable: false),
                    ShowAppointments = table.Column<bool>(type: "boolean", nullable: false),
                    ShowCare = table.Column<bool>(type: "boolean", nullable: false),
                    PublicNote = table.Column<string>(type: "text", nullable: true),
                    NoteApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastIssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerFollowUpShares", x => x.ID);
                    table.ForeignKey(
                        name: "FK_OwnerFollowUpShares_Clinics_ClinicID",
                        column: x => x.ClinicID,
                        principalTable: "Clinics",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerFollowUpShares_PetOwners_PetOwnerId",
                        column: x => x.PetOwnerId,
                        principalTable: "PetOwners",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUpShares_ClinicID",
                table: "OwnerFollowUpShares",
                column: "ClinicID");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUpShares_ClinicID_PetOwnerId",
                table: "OwnerFollowUpShares",
                columns: new[] { "ClinicID", "PetOwnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUpShares_PetOwnerId",
                table: "OwnerFollowUpShares",
                column: "PetOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFollowUpShares_TokenHash",
                table: "OwnerFollowUpShares",
                column: "TokenHash",
                unique: true,
                filter: "\"TokenHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerFollowUpShares");
        }
    }
}
