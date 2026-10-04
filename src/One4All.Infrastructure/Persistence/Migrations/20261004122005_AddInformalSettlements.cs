using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInformalSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "informal_settlement",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ward = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    land_parcel_id = table.Column<long>(type: "bigint", nullable: true),
                    cut_off_date = table.Column<DateOnly>(type: "date", nullable: false),
                    upgrading_plan = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    plan_approved_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_informal_settlement", x => x.id);
                    table.ForeignKey(
                        name: "fk_informal_settlement_land_parcels_land_parcel_id",
                        column: x => x.land_parcel_id,
                        principalSchema: "hse",
                        principalTable: "land_parcel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "enumeration_exercise",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    settlement_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    started_on = table.Column<DateOnly>(type: "date", nullable: false),
                    closed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enumeration_exercise", x => x.id);
                    table.ForeignKey(
                        name: "fk_enumeration_exercise_informal_settlement_settlement_id",
                        column: x => x.settlement_id,
                        principalSchema: "hse",
                        principalTable: "informal_settlement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "enumerated_household",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    settlement_id = table.Column<long>(type: "bigint", nullable: false),
                    exercise_id = table.Column<long>(type: "bigint", nullable: false),
                    household_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    head_id_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    head_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    head_date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    household_size = table.Column<int>(type: "integer", nullable: false),
                    monthly_income = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    occupied_since = table.Column<DateOnly>(type: "date", nullable: false),
                    structure_type = table.Column<int>(type: "integer", nullable: false),
                    structure_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    enumerated_on = table.Column<DateOnly>(type: "date", nullable: false),
                    is_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    ineligible_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    waiting_list_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    stand_id = table.Column<long>(type: "bigint", nullable: true),
                    outcome_date = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enumerated_household", x => x.id);
                    table.ForeignKey(
                        name: "fk_enumerated_household_enumeration_exercise_exercise_id",
                        column: x => x.exercise_id,
                        principalSchema: "hse",
                        principalTable: "enumeration_exercise",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_enumerated_household_housing_stands_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_enumerated_household_informal_settlement_settlement_id",
                        column: x => x.settlement_id,
                        principalSchema: "hse",
                        principalTable: "informal_settlement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_enumerated_household_company_id_head_id_number",
                schema: "hse",
                table: "enumerated_household",
                columns: new[] { "company_id", "head_id_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_enumerated_household_company_id_household_number",
                schema: "hse",
                table: "enumerated_household",
                columns: new[] { "company_id", "household_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_enumerated_household_exercise_id",
                schema: "hse",
                table: "enumerated_household",
                column: "exercise_id");

            migrationBuilder.CreateIndex(
                name: "ix_enumerated_household_settlement_id",
                schema: "hse",
                table: "enumerated_household",
                column: "settlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_enumerated_household_stand_id",
                schema: "hse",
                table: "enumerated_household",
                column: "stand_id",
                unique: true,
                filter: "stand_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_enumeration_exercise_settlement_id",
                schema: "hse",
                table: "enumeration_exercise",
                column: "settlement_id",
                unique: true,
                filter: "closed_on IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_informal_settlement_company_id_code",
                schema: "hse",
                table: "informal_settlement",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_informal_settlement_land_parcel_id",
                schema: "hse",
                table: "informal_settlement",
                column: "land_parcel_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enumerated_household",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "enumeration_exercise",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "informal_settlement",
                schema: "hse");
        }
    }
}
