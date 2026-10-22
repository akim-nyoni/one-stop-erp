using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndigentPolicyAndDepositRefundRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_indigent_registration_one_active",
                schema: "mun",
                table: "indigent_registration");

            migrationBuilder.AlterColumn<long>(
                name: "approved_by_user_id",
                schema: "mun",
                table: "indigent_registration",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "rejected_by_user_id",
                schema: "mun",
                table: "indigent_registration",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "mun",
                table: "indigent_registration",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "requested_by_user_id",
                schema: "mun",
                table: "indigent_registration",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Registrations made before the second approval came in were keyed and approved by the same user.
            migrationBuilder.Sql("update mun.indigent_registration set requested_by_user_id = coalesce(approved_by_user_id, 0);");

            migrationBuilder.CreateTable(
                name: "indigent_policy",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    maximum_income = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indigent_policy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stand_deposit_refund_request",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    deposit_id = table.Column<long>(type: "bigint", nullable: false),
                    certificate_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    bank_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stand_deposit_refund_request", x => x.id);
                    table.ForeignKey(
                        name: "fk_stand_deposit_refund_request_stand_deposit_deposit_id",
                        column: x => x.deposit_id,
                        principalSchema: "hse",
                        principalTable: "stand_deposit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_indigent_registration_one_active",
                schema: "mun",
                table: "indigent_registration",
                column: "property_id",
                unique: true,
                filter: "status in (0, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_indigent_policy_company_id",
                schema: "mun",
                table: "indigent_policy",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stand_deposit_refund_request_one_pending",
                schema: "hse",
                table: "stand_deposit_refund_request",
                column: "deposit_id",
                unique: true,
                filter: "status = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indigent_policy",
                schema: "mun");

            migrationBuilder.DropTable(
                name: "stand_deposit_refund_request",
                schema: "hse");

            migrationBuilder.DropIndex(
                name: "ix_indigent_registration_one_active",
                schema: "mun",
                table: "indigent_registration");

            migrationBuilder.DropColumn(
                name: "rejected_by_user_id",
                schema: "mun",
                table: "indigent_registration");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                schema: "mun",
                table: "indigent_registration");

            migrationBuilder.DropColumn(
                name: "requested_by_user_id",
                schema: "mun",
                table: "indigent_registration");

            migrationBuilder.AlterColumn<long>(
                name: "approved_by_user_id",
                schema: "mun",
                table: "indigent_registration",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_indigent_registration_one_active",
                schema: "mun",
                table: "indigent_registration",
                column: "property_id",
                unique: true,
                filter: "status = 0");
        }
    }
}
