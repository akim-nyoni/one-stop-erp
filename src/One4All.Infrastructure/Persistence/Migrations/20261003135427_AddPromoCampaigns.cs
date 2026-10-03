using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromoCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "promo_campaign",
                schema: "vou",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    discount_type = table.Column<int>(type: "integer", nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                    minimum_spend = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    uses_per_code = table.Column<int>(type: "integer", nullable: false),
                    max_redemptions = table.Column<int>(type: "integer", nullable: true),
                    redemptions = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_campaign", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "promo_redemption",
                schema: "vou",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    promo_campaign_id = table.Column<long>(type: "bigint", nullable: false),
                    promo_code_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    spend = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_redemption", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "promo_code",
                schema: "vou",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    promo_campaign_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    times_used = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_code", x => x.id);
                    table.ForeignKey(
                        name: "fk_promo_code_promo_campaign_promo_campaign_id",
                        column: x => x.promo_campaign_id,
                        principalSchema: "vou",
                        principalTable: "promo_campaign",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_promo_campaign_company_id_code",
                schema: "vou",
                table: "promo_campaign",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_promo_code_code",
                schema: "vou",
                table: "promo_code",
                column: "code");

            migrationBuilder.CreateIndex(
                name: "ix_promo_code_promo_campaign_id",
                schema: "vou",
                table: "promo_code",
                column: "promo_campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_promo_redemption_promo_campaign_id_date",
                schema: "vou",
                table: "promo_redemption",
                columns: new[] { "promo_campaign_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "promo_code",
                schema: "vou");

            migrationBuilder.DropTable(
                name: "promo_redemption",
                schema: "vou");

            migrationBuilder.DropTable(
                name: "promo_campaign",
                schema: "vou");
        }
    }
}
