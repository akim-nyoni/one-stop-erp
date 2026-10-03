using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCouncilLicensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "council_licence",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    licence_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    holder_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    property_id = table.Column<long>(type: "bigint", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    cycle_months = table.Column<int>(type: "integer", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    revenue_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    renewals = table.Column<int>(type: "integer", nullable: false),
                    revoked_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_council_licence", x => x.id);
                    table.CheckConstraint("ck_council_licence_cycle", "cycle_months >= 1 AND fee >= 0");
                });

            migrationBuilder.CreateTable(
                name: "council_inspection",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    licence_id = table.Column<long>(type: "bigint", nullable: false),
                    inspected_on = table.Column<DateOnly>(type: "date", nullable: false),
                    inspector = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    passed = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_council_inspection", x => x.id);
                    table.ForeignKey(
                        name: "fk_council_inspection_council_licence_licence_id",
                        column: x => x.licence_id,
                        principalSchema: "mun",
                        principalTable: "council_licence",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_council_inspection_licence_id",
                schema: "mun",
                table: "council_inspection",
                column: "licence_id");

            migrationBuilder.CreateIndex(
                name: "ix_council_licence_company_id_expires_on",
                schema: "mun",
                table: "council_licence",
                columns: new[] { "company_id", "expires_on" });

            migrationBuilder.CreateIndex(
                name: "ix_council_licence_company_id_licence_number",
                schema: "mun",
                table: "council_licence",
                columns: new[] { "company_id", "licence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_council_licence_holder_customer_id",
                schema: "mun",
                table: "council_licence",
                column: "holder_customer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "council_inspection",
                schema: "mun");

            migrationBuilder.DropTable(
                name: "council_licence",
                schema: "mun");
        }
    }
}
