using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHousingFees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "applicant_fee",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    applicant_id = table.Column<long>(type: "bigint", nullable: false),
                    fee_type = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    receipt_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applicant_fee", x => x.id);
                    table.ForeignKey(
                        name: "fk_applicant_fee_housing_applicants_applicant_id",
                        column: x => x.applicant_id,
                        principalSchema: "hse",
                        principalTable: "applicant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_duty",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    transfer_application_id = table.Column<long>(type: "bigint", nullable: false),
                    consideration = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    rate_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    withheld_on = table.Column<DateOnly>(type: "date", nullable: false),
                    receipt_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    liability_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    remitted_on = table.Column<DateOnly>(type: "date", nullable: true),
                    remittance_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    remittance_journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_duty", x => x.id);
                    table.ForeignKey(
                        name: "fk_transfer_duty_transfer_application_transfer_application_id",
                        column: x => x.transfer_application_id,
                        principalSchema: "hse",
                        principalTable: "transfer_application",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_applicant_fee_applicant_id_fee_type",
                schema: "hse",
                table: "applicant_fee",
                columns: new[] { "applicant_id", "fee_type" },
                unique: true,
                filter: "fee_type = 1");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_duty_transfer_application_id",
                schema: "hse",
                table: "transfer_duty",
                column: "transfer_application_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applicant_fee",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "transfer_duty",
                schema: "hse");
        }
    }
}
