using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRunApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "payment_run_approval_required",
                schema: "sys",
                table: "company",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "payment_run",
                schema: "ap",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_on_or_before = table.Column<DateOnly>(type: "date", nullable: false),
                    bank_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    withholding_tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    eft_layout_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
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
                    table.PrimaryKey("pk_payment_run", x => x.id);
                    table.CheckConstraint("ck_payment_run_approver", "approved_by_user_id IS NULL OR approved_by_user_id <> proposed_by_user_id");
                });

            migrationBuilder.CreateTable(
                name: "payment_run_line",
                schema: "ap",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    payment_run_batch_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    due = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    included = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payment_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_run_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_run_line_payment_run_payment_run_batch_id",
                        column: x => x.payment_run_batch_id,
                        principalSchema: "ap",
                        principalTable: "payment_run",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_run_company_id_number",
                schema: "ap",
                table: "payment_run",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_run_line_payment_run_batch_id",
                schema: "ap",
                table: "payment_run_line",
                column: "payment_run_batch_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_run_line",
                schema: "ap");

            migrationBuilder.DropTable(
                name: "payment_run",
                schema: "ap");

            migrationBuilder.DropColumn(
                name: "payment_run_approval_required",
                schema: "sys",
                table: "company");
        }
    }
}
