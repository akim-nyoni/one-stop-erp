using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDropShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "drop_shipment",
                schema: "sop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    sales_order_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ordered_on = table.Column<DateOnly>(type: "date", nullable: false),
                    delivered_on = table.Column<DateOnly>(type: "date", nullable: true),
                    supplier_invoice_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drop_shipment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "drop_shipment_line",
                schema: "sop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    drop_shipment_id = table.Column<long>(type: "bigint", nullable: false),
                    sales_order_line_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drop_shipment_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_drop_shipment_line_drop_shipments_drop_shipment_id",
                        column: x => x.drop_shipment_id,
                        principalSchema: "sop",
                        principalTable: "drop_shipment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_drop_shipment_company_id_reference",
                schema: "sop",
                table: "drop_shipment",
                columns: new[] { "company_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_drop_shipment_sales_order_id",
                schema: "sop",
                table: "drop_shipment",
                column: "sales_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_drop_shipment_line_drop_shipment_id",
                schema: "sop",
                table: "drop_shipment_line",
                column: "drop_shipment_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drop_shipment_line",
                schema: "sop");

            migrationBuilder.DropTable(
                name: "drop_shipment",
                schema: "sop");
        }
    }
}
