using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCouncilRentals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rental_unit",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit_type = table.Column<int>(type: "integer", nullable: false),
                    rooms = table.Column<int>(type: "integer", nullable: false),
                    monthly_rent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    rent_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vacant_since = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rental_unit", x => x.id);
                    table.CheckConstraint("ck_rental_unit_rent", "monthly_rent >= 0 AND rooms >= 0");
                });

            migrationBuilder.CreateTable(
                name: "tenancy",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    tenancy_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    unit_id = table.Column<long>(type: "bigint", nullable: false),
                    tenant_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    monthly_rent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    deposit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    deposit_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenancy", x => x.id);
                    table.CheckConstraint("ck_tenancy_terms", "deposit >= 0 AND monthly_rent >= 0 AND (end_date IS NULL OR end_date >= start_date)");
                    table.ForeignKey(
                        name: "fk_tenancy_rental_unit_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "hse",
                        principalTable: "rental_unit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rental_unit_company_id_code",
                schema: "hse",
                table: "rental_unit",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenancy_company_id_tenancy_number",
                schema: "hse",
                table: "tenancy",
                columns: new[] { "company_id", "tenancy_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenancy_one_active_per_unit",
                schema: "hse",
                table: "tenancy",
                column: "unit_id",
                unique: true,
                filter: "status = 0");

            migrationBuilder.CreateIndex(
                name: "ix_tenancy_tenant_customer_id",
                schema: "hse",
                table: "tenancy",
                column: "tenant_customer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenancy",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "rental_unit",
                schema: "hse");
        }
    }
}
