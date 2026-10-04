using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLandAcquisitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "land_acquisition",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    acquisition_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    parcel_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    area_square_metres = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    counterparty = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    resolution_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    valuer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    valued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    valued_at = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    land_parcel_id = table.Column<long>(type: "bigint", nullable: true),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    abandon_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_land_acquisition", x => x.id);
                    table.ForeignKey(
                        name: "fk_land_acquisition_land_parcels_land_parcel_id",
                        column: x => x.land_parcel_id,
                        principalSchema: "hse",
                        principalTable: "land_parcel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "acquisition_cost",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    land_acquisition_id = table.Column<long>(type: "bigint", nullable: false),
                    component = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    incurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    journal_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acquisition_cost", x => x.id);
                    table.ForeignKey(
                        name: "fk_acquisition_cost_land_acquisition_land_acquisition_id",
                        column: x => x.land_acquisition_id,
                        principalSchema: "hse",
                        principalTable: "land_acquisition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "due_diligence_check",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    land_acquisition_id = table.Column<long>(type: "bigint", nullable: false),
                    item = table.Column<int>(type: "integer", nullable: false),
                    finding = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    acceptable = table.Column<bool>(type: "boolean", nullable: false),
                    signed_off_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    signed_off_on = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_due_diligence_check", x => x.id);
                    table.ForeignKey(
                        name: "fk_due_diligence_check_land_acquisition_land_acquisition_id",
                        column: x => x.land_acquisition_id,
                        principalSchema: "hse",
                        principalTable: "land_acquisition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acquisition_cost_land_acquisition_id",
                schema: "hse",
                table: "acquisition_cost",
                column: "land_acquisition_id");

            migrationBuilder.CreateIndex(
                name: "ix_due_diligence_check_land_acquisition_id_item",
                schema: "hse",
                table: "due_diligence_check",
                columns: new[] { "land_acquisition_id", "item" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_land_acquisition_company_id_acquisition_number",
                schema: "hse",
                table: "land_acquisition",
                columns: new[] { "company_id", "acquisition_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_land_acquisition_land_parcel_id",
                schema: "hse",
                table: "land_acquisition",
                column: "land_parcel_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acquisition_cost",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "due_diligence_check",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "land_acquisition",
                schema: "hse");
        }
    }
}
