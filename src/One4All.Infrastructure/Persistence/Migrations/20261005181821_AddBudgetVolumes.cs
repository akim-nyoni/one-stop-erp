using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetVolumes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "bud",
                table: "budget_line",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "planned_rate",
                schema: "bud",
                table: "budget_line",
                type: "numeric(19,8)",
                precision: 19,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "quantity",
                schema: "bud",
                table: "budget_line",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "actual_volume",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    department = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actual_volume", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_actual_volume_company_id_account_code_date",
                schema: "bud",
                table: "actual_volume",
                columns: new[] { "company_id", "account_code", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "actual_volume",
                schema: "bud");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "bud",
                table: "budget_line");

            migrationBuilder.DropColumn(
                name: "planned_rate",
                schema: "bud",
                table: "budget_line");

            migrationBuilder.DropColumn(
                name: "quantity",
                schema: "bud",
                table: "budget_line");
        }
    }
}
