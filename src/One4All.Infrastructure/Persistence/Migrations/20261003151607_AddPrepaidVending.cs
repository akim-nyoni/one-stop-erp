using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrepaidVending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prepaid_sale",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    sale_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    property_id = table.Column<long>(type: "bigint", nullable: false),
                    service_type = table.Column<int>(type: "integer", nullable: false),
                    meter_serial = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    sold_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    to_arrears = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    units = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    token = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prepaid_sale", x => x.id);
                    table.CheckConstraint("ck_prepaid_sale_amounts", "amount > 0 AND to_arrears >= 0 AND to_arrears <= amount AND units >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_prepaid_sale_company_id_sale_number",
                schema: "mun",
                table: "prepaid_sale",
                columns: new[] { "company_id", "sale_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prepaid_sale_company_id_sold_on",
                schema: "mun",
                table: "prepaid_sale",
                columns: new[] { "company_id", "sold_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prepaid_sale",
                schema: "mun");
        }
    }
}
