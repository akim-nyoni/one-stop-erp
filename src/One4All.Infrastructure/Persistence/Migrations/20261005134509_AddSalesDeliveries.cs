using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "requested_delivery_date",
                schema: "sop",
                table: "sales_order",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "delivery",
                schema: "sop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    sales_order_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    despatched_on = table.Column<DateOnly>(type: "date", nullable: false),
                    planned_for = table.Column<DateOnly>(type: "date", nullable: true),
                    vehicle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    route = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    driver = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    delivered_on = table.Column<DateOnly>(type: "date", nullable: true),
                    received_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    pod_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_line",
                schema: "sop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sales_delivery_id = table.Column<long>(type: "bigint", nullable: false),
                    sales_order_line_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_line_delivery_sales_delivery_id",
                        column: x => x.sales_delivery_id,
                        principalSchema: "sop",
                        principalTable: "delivery",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_company_id_reference",
                schema: "sop",
                table: "delivery",
                columns: new[] { "company_id", "reference" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_line_sales_delivery_id",
                schema: "sop",
                table: "delivery_line",
                column: "sales_delivery_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_line",
                schema: "sop");

            migrationBuilder.DropTable(
                name: "delivery",
                schema: "sop");

            migrationBuilder.DropColumn(
                name: "requested_delivery_date",
                schema: "sop",
                table: "sales_order");
        }
    }
}
