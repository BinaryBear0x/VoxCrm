using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkdayModuleSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "EnabledModules",
                table: "Clinics",
                type: "integer",
                nullable: false,
                defaultValue: 7,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 3);

            // Tasks and the closing assistant used to be part of the always-on base package. Existing clinics
            // keep them (bit 4); only clinics the dealer switches off afterwards lose them.
            migrationBuilder.Sql("UPDATE \"Clinics\" SET \"EnabledModules\" = \"EnabledModules\" | 4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Clinics\" SET \"EnabledModules\" = \"EnabledModules\" & ~4;");

            migrationBuilder.AlterColumn<int>(
                name: "EnabledModules",
                table: "Clinics",
                type: "integer",
                nullable: false,
                defaultValue: 3,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 7);
        }
    }
}
