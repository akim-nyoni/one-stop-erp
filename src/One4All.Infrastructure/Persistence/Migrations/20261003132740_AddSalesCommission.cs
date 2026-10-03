using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesCommission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "sales_rep_id",
                schema: "ar",
                table: "customer",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "commission_accrual",
                schema: "ar",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    sales_rep_id = table.Column<long>(type: "bigint", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    accrued = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commission_accrual", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_rep",
                schema: "ar",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    rate_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_rep", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commission_accrual_company_id_sales_rep_id_month",
                schema: "ar",
                table: "commission_accrual",
                columns: new[] { "company_id", "sales_rep_id", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_rep_company_id_code",
                schema: "ar",
                table: "sales_rep",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commission_accrual",
                schema: "ar");

            migrationBuilder.DropTable(
                name: "sales_rep",
                schema: "ar");

            migrationBuilder.DropColumn(
                name: "sales_rep_id",
                schema: "ar",
                table: "customer");
        }
    }
}
