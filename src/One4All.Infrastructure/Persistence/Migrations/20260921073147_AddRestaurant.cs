using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "res");

            migrationBuilder.CreateTable(
                name: "menu_item",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    revenue_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    cost_of_sales_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    table_id = table.Column<long>(type: "bigint", nullable: false),
                    order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    waiter_user_id = table.Column<long>(type: "bigint", nullable: false),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    journal_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "table",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    table_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recipe_line",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    menu_item_id = table.Column<long>(type: "bigint", nullable: false),
                    ingredient_item_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipe_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipe_line_menu_items_menu_item_id",
                        column: x => x.menu_item_id,
                        principalSchema: "res",
                        principalTable: "menu_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_line",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    table_order_id = table.Column<long>(type: "bigint", nullable: false),
                    menu_item_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    revenue_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    cost_of_sales_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_line_order_table_order_id",
                        column: x => x.table_order_id,
                        principalSchema: "res",
                        principalTable: "order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_menu_item_company_id_code",
                schema: "res",
                table: "menu_item",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_order_company_id_order_number",
                schema: "res",
                table: "order",
                columns: new[] { "company_id", "order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_line_table_order_id",
                schema: "res",
                table: "order_line",
                column: "table_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_recipe_line_menu_item_id",
                schema: "res",
                table: "recipe_line",
                column: "menu_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_table_company_id_table_number",
                schema: "res",
                table: "table",
                columns: new[] { "company_id", "table_number" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_line",
                schema: "res");

            migrationBuilder.DropTable(
                name: "recipe_line",
                schema: "res");

            migrationBuilder.DropTable(
                name: "table",
                schema: "res");

            migrationBuilder.DropTable(
                name: "order",
                schema: "res");

            migrationBuilder.DropTable(
                name: "menu_item",
                schema: "res");
        }
    }
}
