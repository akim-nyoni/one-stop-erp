using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseOrderDeliverySchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "po_delivery",
                schema: "pop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    purchase_order_line_id = table.Column<long>(type: "bigint", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_po_delivery", x => x.id);
                    table.ForeignKey(
                        name: "fk_po_delivery_purchase_order_line_purchase_order_line_id",
                        column: x => x.purchase_order_line_id,
                        principalSchema: "pop",
                        principalTable: "purchase_order_line",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_po_delivery_purchase_order_line_id_due_date",
                schema: "pop",
                table: "po_delivery",
                columns: new[] { "purchase_order_line_id", "due_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "po_delivery",
                schema: "pop");
        }
    }
}
