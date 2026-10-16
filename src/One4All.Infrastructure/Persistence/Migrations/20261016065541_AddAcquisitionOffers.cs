using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "agreed_on",
                schema: "hse",
                table: "land_acquisition",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "agreed_price",
                schema: "hse",
                table: "land_acquisition",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "break_off_reason",
                schema: "hse",
                table: "land_acquisition",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "negotiation",
                schema: "hse",
                table: "land_acquisition",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "acquisition_offer",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    land_acquisition_id = table.Column<long>(type: "bigint", nullable: false),
                    party = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    offered_on = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acquisition_offer", x => x.id);
                    table.ForeignKey(
                        name: "fk_acquisition_offer_land_acquisition_land_acquisition_id",
                        column: x => x.land_acquisition_id,
                        principalSchema: "hse",
                        principalTable: "land_acquisition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acquisition_offer_land_acquisition_id",
                schema: "hse",
                table: "acquisition_offer",
                column: "land_acquisition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acquisition_offer",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "agreed_on",
                schema: "hse",
                table: "land_acquisition");

            migrationBuilder.DropColumn(
                name: "agreed_price",
                schema: "hse",
                table: "land_acquisition");

            migrationBuilder.DropColumn(
                name: "break_off_reason",
                schema: "hse",
                table: "land_acquisition");

            migrationBuilder.DropColumn(
                name: "negotiation",
                schema: "hse",
                table: "land_acquisition");
        }
    }
}
