using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBinsAndWarehouseLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bin",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    pick_sequence = table.Column<int>(type: "integer", nullable: false),
                    capacity_units = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bin", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bin_stock",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    bin_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bin_stock", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_warehouse_level",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    reorder_level = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reorder_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_warehouse_level", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bin_warehouse_id_code",
                schema: "inv",
                table: "bin",
                columns: new[] { "warehouse_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_bin_id_item_id",
                schema: "inv",
                table: "bin_stock",
                columns: new[] { "bin_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bin_stock_item_id",
                schema: "inv",
                table: "bin_stock",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_warehouse_level_item_id_warehouse_id",
                schema: "inv",
                table: "item_warehouse_level",
                columns: new[] { "item_id", "warehouse_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bin",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "bin_stock",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "item_warehouse_level",
                schema: "inv");
        }
    }
}
