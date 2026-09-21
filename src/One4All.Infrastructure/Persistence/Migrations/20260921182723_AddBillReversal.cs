using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_reversed",
                schema: "res",
                table: "bill",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "reversal_journal_number",
                schema: "res",
                table: "bill",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "reversed_on",
                schema: "res",
                table: "bill",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "tender_reversible",
                schema: "res",
                table: "bill",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "bill_issue",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    restaurant_bill_id = table.Column<long>(type: "bigint", nullable: false),
                    stock_movement_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bill_issue", x => x.id);
                    table.ForeignKey(
                        name: "fk_bill_issue_bill_restaurant_bill_id",
                        column: x => x.restaurant_bill_id,
                        principalSchema: "res",
                        principalTable: "bill",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bill_posting",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    restaurant_bill_id = table.Column<long>(type: "bigint", nullable: false),
                    account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    is_debit = table.Column<bool>(type: "boolean", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bill_posting", x => x.id);
                    table.ForeignKey(
                        name: "fk_bill_posting_bill_restaurant_bill_id",
                        column: x => x.restaurant_bill_id,
                        principalSchema: "res",
                        principalTable: "bill",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bill_issue_restaurant_bill_id",
                schema: "res",
                table: "bill_issue",
                column: "restaurant_bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_bill_posting_restaurant_bill_id",
                schema: "res",
                table: "bill_posting",
                column: "restaurant_bill_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bill_issue",
                schema: "res");

            migrationBuilder.DropTable(
                name: "bill_posting",
                schema: "res");

            migrationBuilder.DropColumn(
                name: "is_reversed",
                schema: "res",
                table: "bill");

            migrationBuilder.DropColumn(
                name: "reversal_journal_number",
                schema: "res",
                table: "bill");

            migrationBuilder.DropColumn(
                name: "reversed_on",
                schema: "res",
                table: "bill");

            migrationBuilder.DropColumn(
                name: "tender_reversible",
                schema: "res",
                table: "bill");
        }
    }
}
