using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetEnforcementRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "budget_outcome",
                schema: "pop",
                table: "requisition",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "enforcement_rule",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    department = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    account_prefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    minimum_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enforcement_rule", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_enforcement_rule_company_id_department_account_prefix_minim",
                schema: "bud",
                table: "enforcement_rule",
                columns: new[] { "company_id", "department", "account_prefix", "minimum_value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enforcement_rule",
                schema: "bud");

            migrationBuilder.DropColumn(
                name: "budget_outcome",
                schema: "pop",
                table: "requisition");
        }
    }
}
