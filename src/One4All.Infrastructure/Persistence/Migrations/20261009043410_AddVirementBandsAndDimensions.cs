using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVirementBandsAndDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "from_cost_centre",
                schema: "bud",
                table: "budget_virement",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "to_budget_id",
                schema: "bud",
                table: "budget_virement",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "to_cost_centre",
                schema: "bud",
                table: "budget_virement",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "virement_band",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    up_to_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    level = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_virement_band", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_virement_band_company_id",
                schema: "bud",
                table: "virement_band",
                column: "company_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "virement_band",
                schema: "bud");

            migrationBuilder.DropColumn(
                name: "from_cost_centre",
                schema: "bud",
                table: "budget_virement");

            migrationBuilder.DropColumn(
                name: "to_budget_id",
                schema: "bud",
                table: "budget_virement");

            migrationBuilder.DropColumn(
                name: "to_cost_centre",
                schema: "bud",
                table: "budget_virement");
        }
    }
}
