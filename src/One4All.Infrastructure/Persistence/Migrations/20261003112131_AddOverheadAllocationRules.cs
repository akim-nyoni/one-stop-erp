using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOverheadAllocationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "allocation_rule",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    source_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    source_cost_centre = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    target_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_rule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "allocation_rule_target",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    allocation_rule_id = table.Column<long>(type: "bigint", nullable: false),
                    cost_centre = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    department = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    driver = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_rule_target", x => x.id);
                    table.ForeignKey(
                        name: "fk_allocation_rule_target_allocation_rule_allocation_rule_id",
                        column: x => x.allocation_rule_id,
                        principalSchema: "gl",
                        principalTable: "allocation_rule",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_allocation_rule_company_id_code",
                schema: "gl",
                table: "allocation_rule",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_allocation_rule_target_allocation_rule_id",
                schema: "gl",
                table: "allocation_rule_target",
                column: "allocation_rule_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allocation_rule_target",
                schema: "gl");

            migrationBuilder.DropTable(
                name: "allocation_rule",
                schema: "gl");
        }
    }
}
