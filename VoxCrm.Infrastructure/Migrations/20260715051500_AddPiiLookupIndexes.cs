using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxCrm.Infrastructure.Data;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations;

[DbContext(typeof(VoxCrmDbContext))]
[Migration("20260715051500_AddPiiLookupIndexes")]
public partial class AddPiiLookupIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(name: "NormalizedPhone", table: "PetOwners", type: "character varying(64)", maxLength: 64, nullable: true, oldClrType: typeof(string), oldType: "character varying(20)", oldMaxLength: 20, oldNullable: true);
        migrationBuilder.AddColumn<string>(name: "EmailLookupHash", table: "PetOwners", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.CreateIndex(name: "IX_PetOwners_ClinicID_EmailLookupHash", table: "PetOwners", columns: new[] { "ClinicID", "EmailLookupHash" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_PetOwners_ClinicID_EmailLookupHash", table: "PetOwners");
        migrationBuilder.DropColumn(name: "EmailLookupHash", table: "PetOwners");
        migrationBuilder.AlterColumn<string>(name: "NormalizedPhone", table: "PetOwners", type: "character varying(20)", maxLength: 20, nullable: true, oldClrType: typeof(string), oldType: "character varying(64)", oldMaxLength: 64, oldNullable: true);
    }
}
