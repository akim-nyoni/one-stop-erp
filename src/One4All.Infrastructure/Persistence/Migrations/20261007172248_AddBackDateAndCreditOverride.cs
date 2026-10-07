using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackDateAndCreditOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "back_date_allowance_days",
                schema: "sys",
                table: "company",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "credit_limit_override",
                schema: "ar",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    document_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    credit_limit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    authorised_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    entered_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_limit_override", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_limit_override_company_id_customer_id",
                schema: "ar",
                table: "credit_limit_override",
                columns: new[] { "company_id", "customer_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_limit_override",
                schema: "ar");

            migrationBuilder.DropColumn(
                name: "back_date_allowance_days",
                schema: "sys",
                table: "company");
        }
    }
}
