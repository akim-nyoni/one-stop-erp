using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "stop_sequence",
                schema: "sop",
                table: "delivery",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "delivery_vehicle",
                schema: "sop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    capacity_kg = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    capacity_m3 = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    max_stops = table.Column<int>(type: "integer", nullable: true),
                    default_driver = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_vehicle", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_vehicle_company_id_registration",
                schema: "sop",
                table: "delivery_vehicle",
                columns: new[] { "company_id", "registration" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_vehicle",
                schema: "sop");

            migrationBuilder.DropColumn(
                name: "stop_sequence",
                schema: "sop",
                table: "delivery");
        }
    }
}
