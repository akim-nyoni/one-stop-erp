using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKitchenStationPrinters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_printed_on_utc",
                schema: "res",
                table: "kitchen_ticket",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "print_count",
                schema: "res",
                table: "kitchen_ticket",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "station_printer",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    station = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    printer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    port = table.Column<int>(type: "integer", nullable: false),
                    copies = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_station_printer", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_station_printer_company_id_station",
                schema: "res",
                table: "station_printer",
                columns: new[] { "company_id", "station" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "station_printer",
                schema: "res");

            migrationBuilder.DropColumn(
                name: "last_printed_on_utc",
                schema: "res",
                table: "kitchen_ticket");

            migrationBuilder.DropColumn(
                name: "print_count",
                schema: "res",
                table: "kitchen_ticket");
        }
    }
}
