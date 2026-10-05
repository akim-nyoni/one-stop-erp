using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "basis",
                schema: "bud",
                table: "budget_line",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "driver_code",
                schema: "bud",
                table: "budget_line",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ceiling",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    fiscal_year_id = table.Column<long>(type: "bigint", nullable: false),
                    department = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ceiling", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "department_unit",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    department = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    division = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    directorate = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    budget_holder = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    budget_holder_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_department_unit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "driver",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    fiscal_year_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ceiling_company_id_fiscal_year_id_department",
                schema: "bud",
                table: "ceiling",
                columns: new[] { "company_id", "fiscal_year_id", "department" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_department_unit_company_id_department",
                schema: "bud",
                table: "department_unit",
                columns: new[] { "company_id", "department" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_driver_company_id_fiscal_year_id_code",
                schema: "bud",
                table: "driver",
                columns: new[] { "company_id", "fiscal_year_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ceiling",
                schema: "bud");

            migrationBuilder.DropTable(
                name: "department_unit",
                schema: "bud");

            migrationBuilder.DropTable(
                name: "driver",
                schema: "bud");

            migrationBuilder.DropColumn(
                name: "basis",
                schema: "bud",
                table: "budget_line");

            migrationBuilder.DropColumn(
                name: "driver_code",
                schema: "bud",
                table: "budget_line");
        }
    }
}
