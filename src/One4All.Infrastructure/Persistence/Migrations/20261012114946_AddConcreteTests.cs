using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConcreteTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "concrete_test",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    run_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sample_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    tested_on = table.Column<DateOnly>(type: "date", nullable: false),
                    result = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    specified = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    tolerance = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    passed = table.Column<bool>(type: "boolean", nullable: false),
                    tested_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_concrete_test", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_concrete_test_company_id_run_number",
                schema: "inv",
                table: "concrete_test",
                columns: new[] { "company_id", "run_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "concrete_test",
                schema: "inv");
        }
    }
}
