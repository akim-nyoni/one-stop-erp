using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLandRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "land_disposal",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    disposal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    land_parcel_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    counterparty = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    resolution_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    area_square_metres = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    cost_derecognised = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    consideration = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    exchange_parcel_id = table.Column<long>(type: "bigint", nullable: true),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_land_disposal", x => x.id);
                    table.ForeignKey(
                        name: "fk_land_disposal_land_parcels_exchange_parcel_id",
                        column: x => x.exchange_parcel_id,
                        principalSchema: "hse",
                        principalTable: "land_parcel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_land_disposal_land_parcels_land_parcel_id",
                        column: x => x.land_parcel_id,
                        principalSchema: "hse",
                        principalTable: "land_parcel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "land_dispute",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    dispute_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    land_parcel_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    claimant = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    particulars = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    stage_since = table.Column<DateOnly>(type: "date", nullable: false),
                    outcome = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_land_dispute", x => x.id);
                    table.ForeignKey(
                        name: "fk_land_dispute_land_parcels_land_parcel_id",
                        column: x => x.land_parcel_id,
                        principalSchema: "hse",
                        principalTable: "land_parcel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_land_dispute_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "land_encumbrance",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    land_parcel_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    in_favour_of = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    registered_on = table.Column<DateOnly>(type: "date", nullable: false),
                    lifted_on = table.Column<DateOnly>(type: "date", nullable: true),
                    lift_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_land_encumbrance", x => x.id);
                    table.ForeignKey(
                        name: "fk_land_encumbrance_land_parcels_land_parcel_id",
                        column: x => x.land_parcel_id,
                        principalSchema: "hse",
                        principalTable: "land_parcel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "land_dispute_step",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    land_dispute_id = table.Column<long>(type: "bigint", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_land_dispute_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_land_dispute_step_land_dispute_land_dispute_id",
                        column: x => x.land_dispute_id,
                        principalSchema: "hse",
                        principalTable: "land_dispute",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_land_disposal_company_id_disposal_number",
                schema: "hse",
                table: "land_disposal",
                columns: new[] { "company_id", "disposal_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_land_disposal_exchange_parcel_id",
                schema: "hse",
                table: "land_disposal",
                column: "exchange_parcel_id");

            migrationBuilder.CreateIndex(
                name: "ix_land_disposal_land_parcel_id",
                schema: "hse",
                table: "land_disposal",
                column: "land_parcel_id");

            migrationBuilder.CreateIndex(
                name: "ix_land_dispute_company_id_dispute_number",
                schema: "hse",
                table: "land_dispute",
                columns: new[] { "company_id", "dispute_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_land_dispute_land_parcel_id",
                schema: "hse",
                table: "land_dispute",
                column: "land_parcel_id");

            migrationBuilder.CreateIndex(
                name: "ix_land_dispute_stand_id",
                schema: "hse",
                table: "land_dispute",
                column: "stand_id");

            migrationBuilder.CreateIndex(
                name: "ix_land_dispute_step_land_dispute_id",
                schema: "hse",
                table: "land_dispute_step",
                column: "land_dispute_id");

            migrationBuilder.CreateIndex(
                name: "ix_land_encumbrance_company_id_land_parcel_id",
                schema: "hse",
                table: "land_encumbrance",
                columns: new[] { "company_id", "land_parcel_id" });

            migrationBuilder.CreateIndex(
                name: "ix_land_encumbrance_land_parcel_id",
                schema: "hse",
                table: "land_encumbrance",
                column: "land_parcel_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "land_disposal",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "land_dispute_step",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "land_encumbrance",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "land_dispute",
                schema: "hse");
        }
    }
}
