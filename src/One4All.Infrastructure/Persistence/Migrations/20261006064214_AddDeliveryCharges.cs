using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "charge_invoice_number",
                schema: "sop",
                table: "delivery",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "delivery_charge",
                schema: "sop",
                table: "delivery",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_zone",
                schema: "ar",
                table: "customer",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "delivery_zone_rate",
                schema: "sop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    zone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    base_charge = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    per_kg = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    per_cubic_metre = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    free_above_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_zone_rate", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_zone_rate_company_id_zone",
                schema: "sop",
                table: "delivery_zone_rate",
                columns: new[] { "company_id", "zone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_zone_rate",
                schema: "sop");

            migrationBuilder.DropColumn(
                name: "charge_invoice_number",
                schema: "sop",
                table: "delivery");

            migrationBuilder.DropColumn(
                name: "delivery_charge",
                schema: "sop",
                table: "delivery");

            migrationBuilder.DropColumn(
                name: "delivery_zone",
                schema: "ar",
                table: "customer");
        }
    }
}
