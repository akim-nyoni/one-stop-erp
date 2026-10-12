using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItemAndPropertyRegisterDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "latitude",
                schema: "mun",
                table: "property",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "longitude",
                schema: "mun",
                table: "property",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suburb",
                schema: "mun",
                table: "property",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location",
                schema: "mun",
                table: "meter",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meter_type",
                schema: "mun",
                table: "meter",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "size_mm",
                schema: "mun",
                table: "meter",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "height_cm",
                schema: "inv",
                table: "item",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "length_cm",
                schema: "inv",
                table: "item",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "long_description",
                schema: "inv",
                table: "item",
                type: "text",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "width_cm",
                schema: "inv",
                table: "item",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "item_image",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_image", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_image_company_id_item_id",
                schema: "inv",
                table: "item_image",
                columns: new[] { "company_id", "item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_image",
                schema: "inv");

            migrationBuilder.DropColumn(
                name: "latitude",
                schema: "mun",
                table: "property");

            migrationBuilder.DropColumn(
                name: "longitude",
                schema: "mun",
                table: "property");

            migrationBuilder.DropColumn(
                name: "suburb",
                schema: "mun",
                table: "property");

            migrationBuilder.DropColumn(
                name: "location",
                schema: "mun",
                table: "meter");

            migrationBuilder.DropColumn(
                name: "meter_type",
                schema: "mun",
                table: "meter");

            migrationBuilder.DropColumn(
                name: "size_mm",
                schema: "mun",
                table: "meter");

            migrationBuilder.DropColumn(
                name: "height_cm",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "length_cm",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "long_description",
                schema: "inv",
                table: "item");

            migrationBuilder.DropColumn(
                name: "width_cm",
                schema: "inv",
                table: "item");
        }
    }
}
