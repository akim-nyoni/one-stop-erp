using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSectionalTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sectional_scheme",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sectional_scheme", x => x.id);
                    table.ForeignKey(
                        name: "fk_sectional_scheme_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sectional_levy_run",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    sectional_scheme_id = table.Column<long>(type: "bigint", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    levy = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    billed = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    run_on = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sectional_levy_run", x => x.id);
                    table.ForeignKey(
                        name: "fk_sectional_levy_run_sectional_scheme_sectional_scheme_id",
                        column: x => x.sectional_scheme_id,
                        principalSchema: "hse",
                        principalTable: "sectional_scheme",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sectional_unit",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sectional_scheme_id = table.Column<long>(type: "bigint", nullable: false),
                    unit_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    floor_area = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    participation_quota = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sectional_unit", x => x.id);
                    table.ForeignKey(
                        name: "fk_sectional_unit_sectional_scheme_sectional_scheme_id",
                        column: x => x.sectional_scheme_id,
                        principalSchema: "hse",
                        principalTable: "sectional_scheme",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "unit_ownership",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sectional_unit_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    owned_from = table.Column<DateOnly>(type: "date", nullable: false),
                    owned_to = table.Column<DateOnly>(type: "date", nullable: true),
                    consideration = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_unit_ownership", x => x.id);
                    table.ForeignKey(
                        name: "fk_unit_ownership_sectional_unit_sectional_unit_id",
                        column: x => x.sectional_unit_id,
                        principalSchema: "hse",
                        principalTable: "sectional_unit",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sectional_levy_run_sectional_scheme_id_period",
                schema: "hse",
                table: "sectional_levy_run",
                columns: new[] { "sectional_scheme_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sectional_scheme_company_id_code",
                schema: "hse",
                table: "sectional_scheme",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sectional_scheme_stand_id",
                schema: "hse",
                table: "sectional_scheme",
                column: "stand_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sectional_unit_sectional_scheme_id_unit_number",
                schema: "hse",
                table: "sectional_unit",
                columns: new[] { "sectional_scheme_id", "unit_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_unit_ownership_sectional_unit_id",
                schema: "hse",
                table: "unit_ownership",
                column: "sectional_unit_id",
                unique: true,
                filter: "owned_to IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sectional_levy_run",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "unit_ownership",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "sectional_unit",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "sectional_scheme",
                schema: "hse");
        }
    }
}
