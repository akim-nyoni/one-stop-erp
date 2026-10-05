using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "consignment_customer_id",
                schema: "inv",
                table: "warehouse",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_transit",
                schema: "inv",
                table: "warehouse",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "stock_transfer",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    transfer_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    from_warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    transit_warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    to_warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    dispatched_on = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: true),
                    received_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_company_id_transfer_number",
                schema: "inv",
                table: "stock_transfer",
                columns: new[] { "company_id", "transfer_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_transfer",
                schema: "inv");

            migrationBuilder.DropColumn(
                name: "consignment_customer_id",
                schema: "inv",
                table: "warehouse");

            migrationBuilder.DropColumn(
                name: "is_transit",
                schema: "inv",
                table: "warehouse");
        }
    }
}
