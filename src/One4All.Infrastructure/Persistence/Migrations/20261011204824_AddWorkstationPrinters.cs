using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkstationPrinters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workstation_printer",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    workstation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    printer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tray = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    copies = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workstation_printer", x => x.id);
                    table.CheckConstraint("ck_workstation_printer_copies", "copies BETWEEN 1 AND 99");
                });

            migrationBuilder.CreateIndex(
                name: "ix_workstation_printer_company_id_workstation_purpose",
                schema: "sys",
                table: "workstation_printer",
                columns: new[] { "company_id", "workstation", "purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workstation_printer",
                schema: "sys");
        }
    }
}
