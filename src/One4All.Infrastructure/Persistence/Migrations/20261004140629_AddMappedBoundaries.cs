using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMappedBoundaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "boundary_wkt",
                schema: "hse",
                table: "stand",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "centroid_latitude",
                schema: "hse",
                table: "stand",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "centroid_longitude",
                schema: "hse",
                table: "stand",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "mapped_area_m2",
                schema: "hse",
                table: "stand",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "boundary_wkt",
                schema: "hse",
                table: "land_parcel",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "centroid_latitude",
                schema: "hse",
                table: "land_parcel",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "centroid_longitude",
                schema: "hse",
                table: "land_parcel",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "mapped_area_m2",
                schema: "hse",
                table: "land_parcel",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "boundary_wkt",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "centroid_latitude",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "centroid_longitude",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "mapped_area_m2",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "boundary_wkt",
                schema: "hse",
                table: "land_parcel");

            migrationBuilder.DropColumn(
                name: "centroid_latitude",
                schema: "hse",
                table: "land_parcel");

            migrationBuilder.DropColumn(
                name: "centroid_longitude",
                schema: "hse",
                table: "land_parcel");

            migrationBuilder.DropColumn(
                name: "mapped_area_m2",
                schema: "hse",
                table: "land_parcel");
        }
    }
}
