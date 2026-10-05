using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBankFileLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_file_layout",
                schema: "cb",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    header_template = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    detail_template = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    trailer_template = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    line_ending = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_file_layout", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_file_layout_company_id_code",
                schema: "cb",
                table: "bank_file_layout",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_file_layout",
                schema: "cb");
        }
    }
}
