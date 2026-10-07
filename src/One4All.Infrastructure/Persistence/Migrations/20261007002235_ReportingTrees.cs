using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReportingTrees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reporting_tree",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporting_tree", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reporting_node",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reporting_tree_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    parent_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    reverse_sign = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporting_node", x => x.id);
                    table.ForeignKey(
                        name: "fk_reporting_node_reporting_trees_reporting_tree_id",
                        column: x => x.reporting_tree_id,
                        principalSchema: "gl",
                        principalTable: "reporting_tree",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reporting_account_range",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reporting_node_id = table.Column<long>(type: "bigint", nullable: false),
                    from_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    to_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporting_account_range", x => x.id);
                    table.ForeignKey(
                        name: "fk_reporting_account_range_reporting_node_reporting_node_id",
                        column: x => x.reporting_node_id,
                        principalSchema: "gl",
                        principalTable: "reporting_node",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reporting_account_range_reporting_node_id",
                schema: "gl",
                table: "reporting_account_range",
                column: "reporting_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_reporting_node_reporting_tree_id_code",
                schema: "gl",
                table: "reporting_node",
                columns: new[] { "reporting_tree_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reporting_tree_company_id_code",
                schema: "gl",
                table: "reporting_tree",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reporting_account_range",
                schema: "gl");

            migrationBuilder.DropTable(
                name: "reporting_node",
                schema: "gl");

            migrationBuilder.DropTable(
                name: "reporting_tree",
                schema: "gl");
        }
    }
}
