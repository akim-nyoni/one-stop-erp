using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLaybyPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "layby_plan",
                schema: "ar",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    plan_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    final_date = table.Column<DateOnly>(type: "date", nullable: false),
                    forfeit_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    deposit_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    revenue_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    forfeiture_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    paid = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    forfeited = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_layby_plan", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "layby_line",
                schema: "ar",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    layby_plan_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_layby_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_layby_line_layby_plan_layby_plan_id",
                        column: x => x.layby_plan_id,
                        principalSchema: "ar",
                        principalTable: "layby_plan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_layby_line_item_id",
                schema: "ar",
                table: "layby_line",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_layby_line_layby_plan_id",
                schema: "ar",
                table: "layby_line",
                column: "layby_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_layby_plan_company_id_plan_number",
                schema: "ar",
                table: "layby_plan",
                columns: new[] { "company_id", "plan_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_layby_plan_company_id_status_final_date",
                schema: "ar",
                table: "layby_plan",
                columns: new[] { "company_id", "status", "final_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "layby_line",
                schema: "ar");

            migrationBuilder.DropTable(
                name: "layby_plan",
                schema: "ar");
        }
    }
}
