using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportDefinitionVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_definition_version",
                schema: "rpt",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    report_definition_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    definition_json = table.Column<string>(type: "jsonb", maxLength: 256, nullable: false),
                    saved_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_definition_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_definition_version_report_definition_report_definiti",
                        column: x => x.report_definition_id,
                        principalSchema: "rpt",
                        principalTable: "report_definition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_report_definition_version_report_definition_id_version",
                schema: "rpt",
                table: "report_definition_version",
                columns: new[] { "report_definition_id", "version" },
                unique: true);

            // Company and user reports saved before versioning start their history at the version they are on.
            migrationBuilder.Sql("INSERT INTO rpt.report_definition_version (report_definition_id, version, definition_json, saved_by_user_id, saved_at, note) SELECT id, version, definition_json, NULL, now(), 'Version before history was kept' FROM rpt.report_definition WHERE scope <> 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_definition_version",
                schema: "rpt");
        }
    }
}
