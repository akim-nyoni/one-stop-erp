using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTariffSeasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tariff_season",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tariff_id = table.Column<long>(type: "bigint", nullable: false),
                    from_month = table.Column<int>(type: "integer", nullable: false),
                    to_month = table.Column<int>(type: "integer", nullable: false),
                    per_unit_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tariff_season", x => x.id);
                    table.CheckConstraint("ck_tariff_season_months", "from_month BETWEEN 1 AND 12 AND to_month BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "fk_tariff_season_tariffs_tariff_id",
                        column: x => x.tariff_id,
                        principalSchema: "mun",
                        principalTable: "tariff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tariff_season_tariff_id",
                schema: "mun",
                table: "tariff_season",
                column: "tariff_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tariff_season",
                schema: "mun");
        }
    }
}
