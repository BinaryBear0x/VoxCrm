using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestAppointmentsAndVaccineCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VaccineCode",
                table: "VaccinationRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PatientId",
                table: "Appointments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "GuestName",
                table: "Appointments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestNotes",
                table: "Appointments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestPhone",
                table: "Appointments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestPhoneLookupHash",
                table: "Appointments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicID_GuestPhoneLookupHash",
                table: "Appointments",
                columns: new[] { "ClinicID", "GuestPhoneLookupHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicID_GuestPhoneLookupHash",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "VaccineCode",
                table: "VaccinationRecords");

            migrationBuilder.DropColumn(
                name: "GuestName",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "GuestNotes",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "GuestPhone",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "GuestPhoneLookupHash",
                table: "Appointments");

            migrationBuilder.AlterColumn<Guid>(
                name: "PatientId",
                table: "Appointments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
