using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHeldStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "held_stock",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    owner = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_held_stock", x => x.id);
                    table.CheckConstraint("ck_held_stock_quantity", "quantity >= 0");
                });

            migrationBuilder.CreateTable(
                name: "held_stock_movement",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    held_stock_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_held_stock_movement", x => x.id);
                    table.ForeignKey(
                        name: "fk_held_stock_movement_held_stock_held_stock_id",
                        column: x => x.held_stock_id,
                        principalSchema: "inv",
                        principalTable: "held_stock",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_held_stock_company_id_owner_owner_id_item_id_warehouse_id",
                schema: "inv",
                table: "held_stock",
                columns: new[] { "company_id", "owner", "owner_id", "item_id", "warehouse_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_held_stock_movement_held_stock_id",
                schema: "inv",
                table: "held_stock_movement",
                column: "held_stock_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "held_stock_movement",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "held_stock",
                schema: "inv");
        }
    }
}
