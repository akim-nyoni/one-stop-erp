using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompensationClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "compensation_claim",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    claim_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    land_parcel_id = table.Column<long>(type: "bigint", nullable: false),
                    claimant = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    lodged_on = table.Column<DateOnly>(type: "date", nullable: false),
                    land_cost_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provision_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    provision = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    settled_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compensation_claim", x => x.id);
                    table.CheckConstraint("ck_compensation_claim_provision", "provision >= 0");
                    table.ForeignKey(
                        name: "fk_compensation_claim_land_parcels_land_parcel_id",
                        column: x => x.land_parcel_id,
                        principalSchema: "hse",
                        principalTable: "land_parcel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_compensation_claim_company_id_claim_number",
                schema: "hse",
                table: "compensation_claim",
                columns: new[] { "company_id", "claim_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_compensation_claim_land_parcel_id",
                schema: "hse",
                table: "compensation_claim",
                column: "land_parcel_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compensation_claim",
                schema: "hse");
        }
    }
}
