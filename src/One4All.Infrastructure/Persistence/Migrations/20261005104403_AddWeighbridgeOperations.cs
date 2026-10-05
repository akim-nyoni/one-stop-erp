using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWeighbridgeOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "batch_run_number",
                schema: "job",
                table: "weighbridge_ticket",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "customer_id",
                schema: "job",
                table: "weighbridge_ticket",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "direction",
                schema: "job",
                table: "weighbridge_ticket",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "distance_km",
                schema: "job",
                table: "weighbridge_ticket",
                type: "numeric(19,2)",
                precision: 19,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "driver_name",
                schema: "job",
                table: "weighbridge_ticket",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "haulage_charge",
                schema: "job",
                table: "weighbridge_ticket",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "haulage_zone_code",
                schema: "job",
                table: "weighbridge_ticket",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_manual_weight",
                schema: "job",
                table: "weighbridge_ticket",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "item_id",
                schema: "job",
                table: "weighbridge_ticket",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "manual_authorised_by_user_id",
                schema: "job",
                table: "weighbridge_ticket",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manual_reason",
                schema: "job",
                table: "weighbridge_ticket",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "operator_user_id",
                schema: "job",
                table: "weighbridge_ticket",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "site_name",
                schema: "job",
                table: "weighbridge_ticket",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "supplier_id",
                schema: "job",
                table: "weighbridge_ticket",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "time_in_utc",
                schema: "job",
                table: "weighbridge_ticket",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "time_out_utc",
                schema: "job",
                table: "weighbridge_ticket",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "warehouse_id",
                schema: "job",
                table: "weighbridge_ticket",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "haulage_zone",
                schema: "job",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    from_km = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    to_km = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    rate_per_tonne = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    minimum_charge = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_haulage_zone", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "weighbridge_vehicle",
                schema: "job",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    registration = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    haulier = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    stored_tare_kg = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    max_gross_kg = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weighbridge_vehicle", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_haulage_zone_company_id_code",
                schema: "job",
                table: "haulage_zone",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_weighbridge_vehicle_company_id_registration",
                schema: "job",
                table: "weighbridge_vehicle",
                columns: new[] { "company_id", "registration" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "haulage_zone",
                schema: "job");

            migrationBuilder.DropTable(
                name: "weighbridge_vehicle",
                schema: "job");

            migrationBuilder.DropColumn(
                name: "batch_run_number",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "customer_id",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "direction",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "distance_km",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "driver_name",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "haulage_charge",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "haulage_zone_code",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "is_manual_weight",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "item_id",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "manual_authorised_by_user_id",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "manual_reason",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "operator_user_id",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "site_name",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "time_in_utc",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "time_out_utc",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "warehouse_id",
                schema: "job",
                table: "weighbridge_ticket");
        }
    }
}
