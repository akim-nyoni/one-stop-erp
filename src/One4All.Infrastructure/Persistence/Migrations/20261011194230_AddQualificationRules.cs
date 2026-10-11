using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQualificationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "first_time_buyers_only",
                schema: "hse",
                table: "waiting_list_policy",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "refuse_forfeited",
                schema: "hse",
                table: "waiting_list_policy",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "require_rates_clearance",
                schema: "hse",
                table: "waiting_list_policy",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "waiting_list_income_ceiling",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    maximum_monthly_income = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waiting_list_income_ceiling", x => x.id);
                    table.CheckConstraint("ck_waiting_list_income_ceiling", "maximum_monthly_income >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_waiting_list_income_ceiling_company_id_category",
                schema: "hse",
                table: "waiting_list_income_ceiling",
                columns: new[] { "company_id", "category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waiting_list_income_ceiling",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "first_time_buyers_only",
                schema: "hse",
                table: "waiting_list_policy");

            migrationBuilder.DropColumn(
                name: "refuse_forfeited",
                schema: "hse",
                table: "waiting_list_policy");

            migrationBuilder.DropColumn(
                name: "require_rates_clearance",
                schema: "hse",
                table: "waiting_list_policy");
        }
    }
}
