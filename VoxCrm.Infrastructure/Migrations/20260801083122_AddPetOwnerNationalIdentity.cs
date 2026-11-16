using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPetOwnerNationalIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NationalIdentityLookupHash",
                table: "PetOwners",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalIdentityNumber",
                table: "PetOwners",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PetOwners_ClinicID_NationalIdentityLookupHash",
                table: "PetOwners",
                columns: new[] { "ClinicID", "NationalIdentityLookupHash" },
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"NationalIdentityLookupHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PetOwners_ClinicID_NationalIdentityLookupHash",
                table: "PetOwners");

            migrationBuilder.DropColumn(
                name: "NationalIdentityLookupHash",
                table: "PetOwners");

            migrationBuilder.DropColumn(
                name: "NationalIdentityNumber",
                table: "PetOwners");
        }
    }
}
