using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WeighbridgeInboundGrn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "grn_number",
                schema: "job",
                table: "weighbridge_ticket",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "purchase_order_id",
                schema: "job",
                table: "weighbridge_ticket",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_weighbridge_ticket_purchase_order_id",
                schema: "job",
                table: "weighbridge_ticket",
                column: "purchase_order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_weighbridge_ticket_purchase_order_id",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "grn_number",
                schema: "job",
                table: "weighbridge_ticket");

            migrationBuilder.DropColumn(
                name: "purchase_order_id",
                schema: "job",
                table: "weighbridge_ticket");
        }
    }
}
