using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_batch",
                schema: "rcp",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    file_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    import_date = table.Column<DateOnly>(type: "date", nullable: false),
                    bank_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_batch", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_line",
                schema: "rcp",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    import_batch_id = table.Column<long>(type: "bigint", nullable: false),
                    payer_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    payer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    matched_customer_id = table.Column<long>(type: "bigint", nullable: true),
                    open_item_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_line_import_batches_import_batch_id",
                        column: x => x.import_batch_id,
                        principalSchema: "rcp",
                        principalTable: "import_batch",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_batch_company_id_file_hash",
                schema: "rcp",
                table: "import_batch",
                columns: new[] { "company_id", "file_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_line_import_batch_id_status",
                schema: "rcp",
                table: "import_line",
                columns: new[] { "import_batch_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_line",
                schema: "rcp");

            migrationBuilder.DropTable(
                name: "import_batch",
                schema: "rcp");
        }
    }
}
