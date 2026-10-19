using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receipt_layout",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: true),
                    paper_width_mm = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    header_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    footer_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    show_cashier = table.Column<bool>(type: "boolean", nullable: false),
                    show_terminal = table.Column<bool>(type: "boolean", nullable: false),
                    show_unit_price = table.Column<bool>(type: "boolean", nullable: false),
                    show_tax_summary = table.Column<bool>(type: "boolean", nullable: false),
                    show_tax_number = table.Column<bool>(type: "boolean", nullable: false),
                    show_fiscal = table.Column<bool>(type: "boolean", nullable: false),
                    copies = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_layout", x => x.id);
                    table.CheckConstraint("ck_receipt_layout_copies", "copies BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_layout_company_id_terminal_id",
                schema: "pos",
                table: "receipt_layout",
                columns: new[] { "company_id", "terminal_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_layout",
                schema: "pos");
        }
    }
}
