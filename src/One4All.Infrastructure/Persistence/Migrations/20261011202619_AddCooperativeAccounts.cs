using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCooperativeAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "account_customer_id",
                schema: "hse",
                table: "cooperative",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cooperative_contribution",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    cooperative_id = table.Column<long>(type: "bigint", nullable: false),
                    member_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cooperative_contribution", x => x.id);
                    table.CheckConstraint("ck_cooperative_contribution_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_cooperative_contribution_cooperative_cooperative_id",
                        column: x => x.cooperative_id,
                        principalSchema: "hse",
                        principalTable: "cooperative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cooperative_contribution_cooperative_member_member_id",
                        column: x => x.member_id,
                        principalSchema: "hse",
                        principalTable: "cooperative_member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cooperative_company_id_account_customer_id",
                schema: "hse",
                table: "cooperative",
                columns: new[] { "company_id", "account_customer_id" },
                unique: true,
                filter: "account_customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cooperative_contribution_cooperative_id_date",
                schema: "hse",
                table: "cooperative_contribution",
                columns: new[] { "cooperative_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_cooperative_contribution_member_id",
                schema: "hse",
                table: "cooperative_contribution",
                column: "member_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cooperative_contribution",
                schema: "hse");

            migrationBuilder.DropIndex(
                name: "ix_cooperative_company_id_account_customer_id",
                schema: "hse",
                table: "cooperative");

            migrationBuilder.DropColumn(
                name: "account_customer_id",
                schema: "hse",
                table: "cooperative");
        }
    }
}
