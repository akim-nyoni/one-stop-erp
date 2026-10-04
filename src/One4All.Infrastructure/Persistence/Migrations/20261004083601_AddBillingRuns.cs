using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_run",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    run_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    service_type = table.Column<int>(type: "integer", nullable: false),
                    bill_date = table.Column<DateOnly>(type: "date", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    ward = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    invoice_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    proposed_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    approved_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_run", x => x.id);
                    table.CheckConstraint("ck_billing_run_approver", "approved_by_user_id IS NULL OR approved_by_user_id <> proposed_by_user_id");
                });

            migrationBuilder.CreateTable(
                name: "billing_run_line",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    billing_run_id = table.Column<long>(type: "bigint", nullable: false),
                    property_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ratepayer_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    charge = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    previous_charge = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    revenue_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    narrative = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    exceptions = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    included = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_run_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_billing_run_line_billing_run_billing_run_id",
                        column: x => x.billing_run_id,
                        principalSchema: "mun",
                        principalTable: "billing_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_billing_run_company_id_run_number",
                schema: "mun",
                table: "billing_run",
                columns: new[] { "company_id", "run_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_billing_run_one_proposed",
                schema: "mun",
                table: "billing_run",
                columns: new[] { "company_id", "service_type", "period" },
                unique: true,
                filter: "status = 0");

            migrationBuilder.CreateIndex(
                name: "ix_billing_run_line_billing_run_id_property_id",
                schema: "mun",
                table: "billing_run_line",
                columns: new[] { "billing_run_id", "property_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_run_line",
                schema: "mun");

            migrationBuilder.DropTable(
                name: "billing_run",
                schema: "mun");
        }
    }
}
