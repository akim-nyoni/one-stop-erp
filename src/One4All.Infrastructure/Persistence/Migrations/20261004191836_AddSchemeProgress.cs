using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchemeProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "land_valuation",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    land_parcel_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    valuer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_land_valuation", x => x.id);
                    table.ForeignKey(
                        name: "fk_land_valuation_land_parcel_land_parcel_id",
                        column: x => x.land_parcel_id,
                        principalSchema: "hse",
                        principalTable: "land_parcel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scheme_servicing_progress",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    scheme_id = table.Column<long>(type: "bigint", nullable: false),
                    service = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    percent_complete = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    stands_reached = table.Column<int>(type: "integer", nullable: false),
                    certificate_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheme_servicing_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_scheme_servicing_progress_scheme_scheme_id",
                        column: x => x.scheme_id,
                        principalSchema: "hse",
                        principalTable: "scheme",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stand_survey_step",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stand_survey_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_stand_survey_step_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_land_valuation_land_parcel_id_date",
                schema: "hse",
                table: "land_valuation",
                columns: new[] { "land_parcel_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_scheme_servicing_progress_scheme_id_service_date",
                schema: "hse",
                table: "scheme_servicing_progress",
                columns: new[] { "scheme_id", "service", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_stand_survey_step_stand_id_stage",
                schema: "hse",
                table: "stand_survey_step",
                columns: new[] { "stand_id", "stage" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "land_valuation",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "scheme_servicing_progress",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "stand_survey_step",
                schema: "hse");
        }
    }
}
