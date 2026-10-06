using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItemCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "abc_class",
                schema: "inv",
                table: "item",
                type: "character varying(1)",
                maxLength: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "brand",
                schema: "inv",
                table: "item",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "colour",
                schema: "inv",
                table: "item",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country_of_origin",
                schema: "inv",
                table: "item",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hs_code",
                schema: "inv",
                table: "item",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "parent_item_id",
                schema: "inv",
                table: "item",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plu",
                schema: "inv",
                table: "item",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "size",
                schema: "inv",
                table: "item",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "item_alias",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    alias = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_alias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_supplier",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_item_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    lead_time_days = table.Column<int>(type: "integer", nullable: false),
                    minimum_order_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    last_cost = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: true),
                    is_preferred = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_supplier", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_unit",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    unit_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    factor = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    barcode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_unit", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_company_id_plu",
                schema: "inv",
                table: "item",
                columns: new[] { "company_id", "plu" });

            migrationBuilder.CreateIndex(
                name: "ix_item_parent_item_id",
                schema: "inv",
                table: "item",
                column: "parent_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_alias_company_id_alias",
                schema: "inv",
                table: "item_alias",
                columns: new[] { "company_id", "alias" });

            migrationBuilder.CreateIndex(
                name: "ix_item_alias_item_id",
                schema: "inv",
                table: "item_alias",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_supplier_item_id_supplier_id",
                schema: "inv",
                table: "item_supplier",
                columns: new[] { "item_id", "supplier_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_unit_company_id_barcode",
                schema: "inv",
                table: "item_unit",
                columns: new[] { "company_id", "barcode" });

            migrationBuilder.CreateIndex(
                name: "ix_item_unit_item_id_unit_code",
                schema: "inv",
                table: "item_unit",
                columns: new[] { "item_id", "unit_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_alias",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "item_supplier",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "item_unit",
                schema: "inv");

            migrationBuilder.DropIndex(
                name: "ix_item_company_id_plu",
                schema: "inv",
                table: "item");

            migrationBuilder.DropIndex(
                name: "ix_item_parent_item_id",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "abc_class",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "brand",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "colour",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "country_of_origin",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "hs_code",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "parent_item_id",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "plu",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "size",
                schema: "inv",
                table: "item");
        }
    }
}
