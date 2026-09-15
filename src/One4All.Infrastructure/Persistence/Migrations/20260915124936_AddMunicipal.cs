using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMunicipal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "mun");

            migrationBuilder.CreateTable(
                name: "meter",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    serial = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    multiplier = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    last_reading = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meter", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "meter_reading",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    meter_id = table.Column<long>(type: "bigint", nullable: false),
                    reading_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reading = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    previous_reading = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    consumption = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meter_reading", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "property",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ward = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    zone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    size_square_metres = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    land_use = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    valuation = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    ratepayer_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tariff",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    service_type = table.Column<int>(type: "integer", nullable: false),
                    tariff_type = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    fixed_charge = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    per_unit_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    rate_per_valuation_cents = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    minimum_charge = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    revenue_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tariff", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "property_service",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    property_id = table.Column<long>(type: "bigint", nullable: false),
                    service_type = table.Column<int>(type: "integer", nullable: false),
                    tariff_id = table.Column<long>(type: "bigint", nullable: false),
                    meter_id = table.Column<long>(type: "bigint", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_service", x => x.id);
                    table.ForeignKey(
                        name: "fk_property_service_property_property_id",
                        column: x => x.property_id,
                        principalSchema: "mun",
                        principalTable: "property",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tariff_block",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tariff_id = table.Column<long>(type: "bigint", nullable: false),
                    from_unit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    to_unit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tariff_block", x => x.id);
                    table.ForeignKey(
                        name: "fk_tariff_block_tariffs_tariff_id",
                        column: x => x.tariff_id,
                        principalSchema: "mun",
                        principalTable: "tariff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_meter_company_id_serial",
                schema: "mun",
                table: "meter",
                columns: new[] { "company_id", "serial" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_meter_reading_company_id_meter_id_reading_date",
                schema: "mun",
                table: "meter_reading",
                columns: new[] { "company_id", "meter_id", "reading_date" });

            migrationBuilder.CreateIndex(
                name: "ix_property_company_id_stand_number",
                schema: "mun",
                table: "property",
                columns: new[] { "company_id", "stand_number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_property_service_property_id_service_type",
                schema: "mun",
                table: "property_service",
                columns: new[] { "property_id", "service_type" });

            migrationBuilder.CreateIndex(
                name: "ix_tariff_company_id_code",
                schema: "mun",
                table: "tariff",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_tariff_block_tariff_id",
                schema: "mun",
                table: "tariff_block",
                column: "tariff_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meter",
                schema: "mun");

            migrationBuilder.DropTable(
                name: "meter_reading",
                schema: "mun");

            migrationBuilder.DropTable(
                name: "property_service",
                schema: "mun");

            migrationBuilder.DropTable(
                name: "tariff_block",
                schema: "mun");

            migrationBuilder.DropTable(
                name: "property",
                schema: "mun");

            migrationBuilder.DropTable(
                name: "tariff",
                schema: "mun");
        }
    }
}
