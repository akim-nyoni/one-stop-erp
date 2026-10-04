using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLayoutPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "layout_plan",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    plan_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    land_parcel_id = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_layout_plan", x => x.id);
                    table.ForeignKey(
                        name: "fk_layout_plan_land_parcel_land_parcel_id",
                        column: x => x.land_parcel_id,
                        principalSchema: "hse",
                        principalTable: "land_parcel",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "layout_plan_version",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    layout_plan_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    planner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    density = table.Column<int>(type: "integer", nullable: false),
                    submitted_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amendment_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    approval_authority = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    approved_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_layout_plan_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_layout_plan_version_layout_plan_layout_plan_id",
                        column: x => x.layout_plan_id,
                        principalSchema: "hse",
                        principalTable: "layout_plan",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "layout_use_count",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    layout_plan_version_id = table.Column<long>(type: "bigint", nullable: false),
                    use = table.Column<int>(type: "integer", nullable: false),
                    stands = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_layout_use_count", x => x.id);
                    table.ForeignKey(
                        name: "fk_layout_use_count_layout_plan_version_layout_plan_version_id",
                        column: x => x.layout_plan_version_id,
                        principalSchema: "hse",
                        principalTable: "layout_plan_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_layout_plan_company_id_plan_number",
                schema: "hse",
                table: "layout_plan",
                columns: new[] { "company_id", "plan_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_layout_plan_land_parcel_id",
                schema: "hse",
                table: "layout_plan",
                column: "land_parcel_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_layout_plan_version_layout_plan_id_version",
                schema: "hse",
                table: "layout_plan_version",
                columns: new[] { "layout_plan_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_layout_use_count_layout_plan_version_id_use",
                schema: "hse",
                table: "layout_use_count",
                columns: new[] { "layout_plan_version_id", "use" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "layout_use_count",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "layout_plan_version",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "layout_plan",
                schema: "hse");
        }
    }
}
