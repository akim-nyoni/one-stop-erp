using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkstationPrinterConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workstation_printer_company_id_workstation_purpose",
                schema: "sys",
                table: "workstation_printer");

            migrationBuilder.AddColumn<int>(
                name: "connection",
                schema: "sys",
                table: "workstation_printer",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "host",
                schema: "sys",
                table: "workstation_printer",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "port",
                schema: "sys",
                table: "workstation_printer",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_workstation_printer_company_id_workstation_purpose_printer_",
                schema: "sys",
                table: "workstation_printer",
                columns: new[] { "company_id", "workstation", "purpose", "printer_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workstation_printer_company_id_workstation_purpose_printer_",
                schema: "sys",
                table: "workstation_printer");

            migrationBuilder.DropColumn(
                name: "connection",
                schema: "sys",
                table: "workstation_printer");

            migrationBuilder.DropColumn(
                name: "host",
                schema: "sys",
                table: "workstation_printer");

            migrationBuilder.DropColumn(
                name: "port",
                schema: "sys",
                table: "workstation_printer");

            migrationBuilder.CreateIndex(
                name: "ix_workstation_printer_company_id_workstation_purpose",
                schema: "sys",
                table: "workstation_printer",
                columns: new[] { "company_id", "workstation", "purpose" },
                unique: true);
        }
    }
}
