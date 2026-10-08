using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMunicipalServiceTypesAndConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "deposit_amount",
                schema: "mun",
                table: "property_service",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "quantity",
                schema: "mun",
                table: "property_service",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "service_code",
                schema: "mun",
                table: "property_service",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "status",
                schema: "mun",
                table: "property_service",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "status_date",
                schema: "mun",
                table: "property_service",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "service_code",
                schema: "mun",
                table: "billing_run",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "service_type",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    is_metered = table.Column<bool>(type: "boolean", nullable: false),
                    default_deposit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    deposit_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_type", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_type_company_id_code",
                schema: "mun",
                table: "service_type",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_type",
                schema: "mun");

            migrationBuilder.DropColumn(
                name: "deposit_amount",
                schema: "mun",
                table: "property_service");

            migrationBuilder.DropColumn(
                name: "quantity",
                schema: "mun",
                table: "property_service");

            migrationBuilder.DropColumn(
                name: "service_code",
                schema: "mun",
                table: "property_service");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "mun",
                table: "property_service");

            migrationBuilder.DropColumn(
                name: "status_date",
                schema: "mun",
                table: "property_service");

            migrationBuilder.DropColumn(
                name: "service_code",
                schema: "mun",
                table: "billing_run");
        }
    }
}
