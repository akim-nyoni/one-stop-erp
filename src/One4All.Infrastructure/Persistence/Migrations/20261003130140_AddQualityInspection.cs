using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityInspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_quarantine",
                schema: "inv",
                table: "warehouse",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "grn_inspection",
                schema: "pop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    goods_received_note_id = table.Column<long>(type: "bigint", nullable: false),
                    inspected_on = table.Column<DateOnly>(type: "date", nullable: false),
                    inspector_user_id = table.Column<long>(type: "bigint", nullable: false),
                    release_warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grn_inspection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "grn_inspection_line",
                schema: "pop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    grn_inspection_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    passed = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    rejected = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grn_inspection_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_grn_inspection_line_grn_inspection_grn_inspection_id",
                        column: x => x.grn_inspection_id,
                        principalSchema: "pop",
                        principalTable: "grn_inspection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_grn_inspection_goods_received_note_id",
                schema: "pop",
                table: "grn_inspection",
                column: "goods_received_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_grn_inspection_line_grn_inspection_id",
                schema: "pop",
                table: "grn_inspection_line",
                column: "grn_inspection_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "grn_inspection_line",
                schema: "pop");

            migrationBuilder.DropTable(
                name: "grn_inspection",
                schema: "pop");

            migrationBuilder.DropColumn(
                name: "is_quarantine",
                schema: "inv",
                table: "warehouse");
        }
    }
}
