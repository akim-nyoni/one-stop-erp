using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStandPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "offered_price",
                schema: "hse",
                table: "stand_allocation",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stand_pricing_rule",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    scope = table.Column<int>(type: "integer", nullable: false),
                    scheme_id = table.Column<long>(type: "bigint", nullable: true),
                    min_size = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    max_size = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    stand_id = table.Column<long>(type: "bigint", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    overhead_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    levy_per_stand = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    levy_per_square_metre = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    margin_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    round_to = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    proposed_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    approved_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stand_pricing_rule", x => x.id);
                    table.ForeignKey(
                        name: "fk_stand_pricing_rule_scheme_scheme_id",
                        column: x => x.scheme_id,
                        principalSchema: "hse",
                        principalTable: "scheme",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stand_pricing_rule_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stand_pricing_rule_company_id_effective_from",
                schema: "hse",
                table: "stand_pricing_rule",
                columns: new[] { "company_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_stand_pricing_rule_scheme_id",
                schema: "hse",
                table: "stand_pricing_rule",
                column: "scheme_id");

            migrationBuilder.CreateIndex(
                name: "ix_stand_pricing_rule_stand_id",
                schema: "hse",
                table: "stand_pricing_rule",
                column: "stand_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stand_pricing_rule",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "offered_price",
                schema: "hse",
                table: "stand_allocation");
        }
    }
}
