using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "insurance_expiry",
                schema: "fa",
                table: "asset",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "insured_value",
                schema: "fa",
                table: "asset",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "insurer",
                schema: "fa",
                table: "asset",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_number",
                schema: "fa",
                table: "asset",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "warranty_expiry",
                schema: "fa",
                table: "asset",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "asset_count",
                schema: "fa",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    count_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    location = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_count", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_plan",
                schema: "fa",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    asset_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    interval_months = table.Column<int>(type: "integer", nullable: false),
                    next_due = table.Column<DateOnly>(type: "date", nullable: false),
                    last_done = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_plan", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "asset_count_line",
                schema: "fa",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    asset_count_id = table.Column<long>(type: "bigint", nullable: false),
                    asset_id = table.Column<long>(type: "bigint", nullable: true),
                    tag = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    expected_location = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    found_location = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    condition = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    result = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_count_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_count_line_asset_counts_asset_count_id",
                        column: x => x.asset_count_id,
                        principalSchema: "fa",
                        principalTable: "asset_count",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_count_company_id_count_number",
                schema: "fa",
                table: "asset_count",
                columns: new[] { "company_id", "count_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asset_count_line_asset_count_id",
                schema: "fa",
                table: "asset_count_line",
                column: "asset_count_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plan_company_id_asset_id",
                schema: "fa",
                table: "maintenance_plan",
                columns: new[] { "company_id", "asset_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_count_line",
                schema: "fa");

            migrationBuilder.DropTable(
                name: "maintenance_plan",
                schema: "fa");

            migrationBuilder.DropTable(
                name: "asset_count",
                schema: "fa");

            migrationBuilder.DropColumn(
                name: "insurance_expiry",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "insured_value",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "insurer",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "policy_number",
                schema: "fa",
                table: "asset");

            migrationBuilder.DropColumn(
                name: "warranty_expiry",
                schema: "fa",
                table: "asset");
        }
    }
}
