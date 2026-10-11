using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRentTariffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "income_category",
                schema: "hse",
                table: "tenancy",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suburb",
                schema: "hse",
                table: "rental_unit",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "rent_tariff",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    unit_type = table.Column<int>(type: "integer", nullable: false),
                    suburb = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    income_category = table.Column<int>(type: "integer", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    monthly_rent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rent_tariff", x => x.id);
                    table.CheckConstraint("ck_rent_tariff_rent", "monthly_rent >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_rent_tariff_company_id_unit_type_suburb_income_category_eff",
                schema: "hse",
                table: "rent_tariff",
                columns: new[] { "company_id", "unit_type", "suburb", "income_category", "effective_from" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rent_tariff",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "income_category",
                schema: "hse",
                table: "tenancy");

            migrationBuilder.DropColumn(
                name: "suburb",
                schema: "hse",
                table: "rental_unit");
        }
    }
}
