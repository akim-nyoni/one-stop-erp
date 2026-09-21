using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKitchenTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "fired",
                schema: "res",
                table: "order_line",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "kitchen_ticket",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    table_order_id = table.Column<long>(type: "bigint", nullable: false),
                    ticket_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sent_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kitchen_ticket", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kitchen_ticket_line",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kitchen_ticket_id = table.Column<long>(type: "bigint", nullable: false),
                    menu_item_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kitchen_ticket_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_kitchen_ticket_line_kitchen_tickets_kitchen_ticket_id",
                        column: x => x.kitchen_ticket_id,
                        principalSchema: "res",
                        principalTable: "kitchen_ticket",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_kitchen_ticket_company_id_status",
                schema: "res",
                table: "kitchen_ticket",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_kitchen_ticket_line_kitchen_ticket_id",
                schema: "res",
                table: "kitchen_ticket_line",
                column: "kitchen_ticket_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kitchen_ticket_line",
                schema: "res");

            migrationBuilder.DropTable(
                name: "kitchen_ticket",
                schema: "res");

            migrationBuilder.DropColumn(
                name: "fired",
                schema: "res",
                table: "order_line");
        }
    }
}
