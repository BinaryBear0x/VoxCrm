using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppInboundDedupe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add column as nullable so existing rows can be backfilled
            migrationBuilder.AddColumn<string>(
                name: "ProviderMessageId",
                table: "WhatsAppInboundMessages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // Step 2: Backfill existing rows with a unique legacy value based on primary key
            migrationBuilder.Sql(
                "UPDATE \"WhatsAppInboundMessages\" SET \"ProviderMessageId\" = 'legacy-' || \"ID\"::text WHERE \"ProviderMessageId\" IS NULL");

            // Step 3: Make column not null now that all rows have a value
            migrationBuilder.AlterColumn<string>(
                name: "ProviderMessageId",
                table: "WhatsAppInboundMessages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            // Step 4: Create unique index — safe now that all values are distinct
            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInboundMessages_ClinicID_ProviderMessageId",
                table: "WhatsAppInboundMessages",
                columns: new[] { "ClinicID", "ProviderMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppInboundMessages_ClinicID_ProviderMessageId",
                table: "WhatsAppInboundMessages");

            migrationBuilder.DropColumn(
                name: "ProviderMessageId",
                table: "WhatsAppInboundMessages");
        }
    }
}
