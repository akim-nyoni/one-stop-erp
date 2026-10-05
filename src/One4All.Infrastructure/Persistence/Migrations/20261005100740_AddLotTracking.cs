using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLotTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                schema: "inv",
                table: "stock_movement",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "shelf_life_days",
                schema: "inv",
                table: "item",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tracking",
                schema: "inv",
                table: "item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "lot_movement",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    stock_movement_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lot_movement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reason_code",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reason_code", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lot_movement_company_id_item_id_lot_number",
                schema: "inv",
                table: "lot_movement",
                columns: new[] { "company_id", "item_id", "lot_number" });

            migrationBuilder.CreateIndex(
                name: "ix_lot_movement_stock_movement_id",
                schema: "inv",
                table: "lot_movement",
                column: "stock_movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_reason_code_company_id_code",
                schema: "inv",
                table: "reason_code",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lot_movement",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "reason_code",
                schema: "inv");

            migrationBuilder.DropColumn(
                name: "reason_code",
                schema: "inv",
                table: "stock_movement");

            migrationBuilder.DropColumn(
                name: "shelf_life_days",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "tracking",
                schema: "inv",
                table: "item");
        }
    }
}
