using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentsAndStationery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent",
                schema: "rcp",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    commission_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    collected_to_date = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    remitted_to_date = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stationery_book",
                schema: "rcp",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    book_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_serial = table.Column<long>(type: "bigint", nullable: false),
                    to_serial = table.Column<long>(type: "bigint", nullable: false),
                    issued_to_user_id = table.Column<long>(type: "bigint", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stationery_book", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "manual_receipt_capture",
                schema: "rcp",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stationery_book_id = table.Column<long>(type: "bigint", nullable: false),
                    serial = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    receipt_date = table.Column<DateOnly>(type: "date", nullable: false),
                    captured_on = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_receipt_capture", x => x.id);
                    table.ForeignKey(
                        name: "fk_manual_receipt_capture_stationery_books_stationery_book_id",
                        column: x => x.stationery_book_id,
                        principalSchema: "rcp",
                        principalTable: "stationery_book",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_company_id_code",
                schema: "rcp",
                table: "agent",
                columns: new[] { "company_id", "code" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_manual_receipt_capture_stationery_book_id_serial",
                schema: "rcp",
                table: "manual_receipt_capture",
                columns: new[] { "stationery_book_id", "serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stationery_book_company_id_book_number",
                schema: "rcp",
                table: "stationery_book",
                columns: new[] { "company_id", "book_number" },
                unique: true,
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent",
                schema: "rcp");

            migrationBuilder.DropTable(
                name: "manual_receipt_capture",
                schema: "rcp");

            migrationBuilder.DropTable(
                name: "stationery_book",
                schema: "rcp");
        }
    }
}
