using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositsAndCancellations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agreement_cancellation",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    agreement_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    paid = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    months_occupied = table.Column<int>(type: "integer", nullable: false),
                    occupation_rental = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    admin_charge = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    arrears = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    refund = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    repossession_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    refund_journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agreement_cancellation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stand_deposit",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    allocation_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    receipt_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    liability_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cleared_on = table.Column<DateOnly>(type: "date", nullable: true),
                    refunded = table.Column<bool>(type: "boolean", nullable: false),
                    clearing_journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stand_deposit", x => x.id);
                    table.ForeignKey(
                        name: "fk_stand_deposit_stand_allocation_allocation_id",
                        column: x => x.allocation_id,
                        principalSchema: "hse",
                        principalTable: "stand_allocation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agreement_cancellation_company_id_agreement_number",
                schema: "hse",
                table: "agreement_cancellation",
                columns: new[] { "company_id", "agreement_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stand_deposit_allocation_id",
                schema: "hse",
                table: "stand_deposit",
                column: "allocation_id",
                unique: true,
                filter: "cleared_on IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_cancellation",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "stand_deposit",
                schema: "hse");
        }
    }
}
