using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesCreditNoteOriginalInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "original_invoice_number",
                schema: "ar",
                table: "pending_credit_note",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_code",
                schema: "ar",
                table: "pending_credit_note",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_document_number",
                schema: "trd",
                table: "open_item",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "original_invoice_number",
                schema: "ar",
                table: "pending_credit_note");

            migrationBuilder.DropColumn(
                name: "tax_code",
                schema: "ar",
                table: "pending_credit_note");

            migrationBuilder.DropColumn(
                name: "reference_document_number",
                schema: "trd",
                table: "open_item");
        }
    }
}
