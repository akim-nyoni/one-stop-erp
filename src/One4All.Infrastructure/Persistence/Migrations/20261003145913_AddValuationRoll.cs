using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddValuationRoll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "property_valuation",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    property_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_valuation", x => x.id);
                    table.CheckConstraint("ck_property_valuation_value", "value >= 0");
                    table.ForeignKey(
                        name: "fk_property_valuation_property_property_id",
                        column: x => x.property_id,
                        principalSchema: "mun",
                        principalTable: "property",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "valuation_objection",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    objection_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    property_id = table.Column<long>(type: "bigint", nullable: false),
                    objected_valuation_id = table.Column<long>(type: "bigint", nullable: false),
                    value_objected_to = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    claimed_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    grounds = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    lodged_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decided_on = table.Column<DateOnly>(type: "date", nullable: true),
                    decided_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    decision_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_valuation_objection", x => x.id);
                    table.ForeignKey(
                        name: "fk_valuation_objection_property_valuation_objected_valuation_id",
                        column: x => x.objected_valuation_id,
                        principalSchema: "mun",
                        principalTable: "property_valuation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_property_valuation_property_id_effective_from",
                schema: "mun",
                table: "property_valuation",
                columns: new[] { "property_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_valuation_objection_company_id_objection_number",
                schema: "mun",
                table: "valuation_objection",
                columns: new[] { "company_id", "objection_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_valuation_objection_objected_valuation_id",
                schema: "mun",
                table: "valuation_objection",
                column: "objected_valuation_id");

            migrationBuilder.CreateIndex(
                name: "ix_valuation_objection_one_open",
                schema: "mun",
                table: "valuation_objection",
                column: "property_id",
                unique: true,
                filter: "status = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "valuation_objection",
                schema: "mun");

            migrationBuilder.DropTable(
                name: "property_valuation",
                schema: "mun");
        }
    }
}
