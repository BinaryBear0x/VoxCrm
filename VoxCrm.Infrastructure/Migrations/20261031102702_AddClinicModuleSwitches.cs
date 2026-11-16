using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicModuleSwitches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EnabledModules",
                table: "Clinics",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<long>(
                name: "ModulesRevision",
                table: "Clinics",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnabledModules",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "ModulesRevision",
                table: "Clinics");
        }
    }
}
