using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "rolled_from_budget_id",
                schema: "bud",
                table: "budget",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "budget_outer_year",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    budget_id = table.Column<long>(type: "bigint", nullable: false),
                    year_offset = table.Column<int>(type: "integer", nullable: false),
                    account_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cost_centre = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_outer_year", x => x.id);
                    table.ForeignKey(
                        name: "fk_budget_outer_year_budget_budget_id",
                        column: x => x.budget_id,
                        principalSchema: "bud",
                        principalTable: "budget",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budget_outer_year_budget_id",
                schema: "bud",
                table: "budget_outer_year",
                column: "budget_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_outer_year",
                schema: "bud");

            migrationBuilder.DropColumn(
                name: "rolled_from_budget_id",
                schema: "bud",
                table: "budget");
        }
    }
}
