using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStandLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stand_lease",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    lease_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    lessee_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    use = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    term_years = table.Column<int>(type: "integer", nullable: false),
                    annual_rent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    review_every_years = table.Column<int>(type: "integer", nullable: false),
                    escalation_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    income_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    accrued_income_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stand_lease", x => x.id);
                    table.CheckConstraint("ck_stand_lease_terms", "term_years BETWEEN 1 AND 99 AND review_every_years >= 1 AND annual_rent > 0");
                    table.ForeignKey(
                        name: "fk_stand_lease_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stand_lease_company_id_lease_number",
                schema: "hse",
                table: "stand_lease",
                columns: new[] { "company_id", "lease_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stand_lease_stand_id",
                schema: "hse",
                table: "stand_lease",
                column: "stand_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stand_lease",
                schema: "hse");
        }
    }
}
