using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHousingRegisterDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_electricity",
                schema: "hse",
                table: "stand",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_road",
                schema: "hse",
                table: "stand",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_sewer",
                schema: "hse",
                table: "stand",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_storm_water",
                schema: "hse",
                table: "stand",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_water",
                schema: "hse",
                table: "stand",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "intended_use",
                schema: "hse",
                table: "stand",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "servicing_phase",
                schema: "hse",
                table: "stand",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "street_address",
                schema: "hse",
                table: "stand",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suburb",
                schema: "hse",
                table: "stand",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ward",
                schema: "hse",
                table: "stand",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "zone",
                schema: "hse",
                table: "stand",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "zoning",
                schema: "hse",
                table: "stand",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "completion_date",
                schema: "hse",
                table: "scheme",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "funding_source",
                schema: "hse",
                table: "scheme",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                schema: "hse",
                table: "scheme",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "improvements",
                schema: "hse",
                table: "land_parcel",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "land_use",
                schema: "hse",
                table: "land_parcel",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "locality",
                schema: "hse",
                table: "land_parcel",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "occupation",
                schema: "hse",
                table: "land_parcel",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tenure",
                schema: "hse",
                table: "land_parcel",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ward",
                schema: "hse",
                table: "land_parcel",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stand_remark",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    recorded_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stand_remark", x => x.id);
                    table.ForeignKey(
                        name: "fk_stand_remark_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stand_remark_stand_id",
                schema: "hse",
                table: "stand_remark",
                column: "stand_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stand_remark",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "has_electricity",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "has_road",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "has_sewer",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "has_storm_water",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "has_water",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "intended_use",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "servicing_phase",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "street_address",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "suburb",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "ward",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "zone",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "zoning",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "completion_date",
                schema: "hse",
                table: "scheme");

            migrationBuilder.DropColumn(
                name: "funding_source",
                schema: "hse",
                table: "scheme");

            migrationBuilder.DropColumn(
                name: "start_date",
                schema: "hse",
                table: "scheme");

            migrationBuilder.DropColumn(
                name: "improvements",
                schema: "hse",
                table: "land_parcel");

            migrationBuilder.DropColumn(
                name: "land_use",
                schema: "hse",
                table: "land_parcel");

            migrationBuilder.DropColumn(
                name: "locality",
                schema: "hse",
                table: "land_parcel");

            migrationBuilder.DropColumn(
                name: "occupation",
                schema: "hse",
                table: "land_parcel");

            migrationBuilder.DropColumn(
                name: "tenure",
                schema: "hse",
                table: "land_parcel");

            migrationBuilder.DropColumn(
                name: "ward",
                schema: "hse",
                table: "land_parcel");
        }
    }
}
