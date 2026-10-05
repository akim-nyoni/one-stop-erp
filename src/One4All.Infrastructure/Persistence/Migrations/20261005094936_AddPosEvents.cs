using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPosEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: false),
                    till_session_id = table.Column<long>(type: "bigint", nullable: true),
                    cashier_user_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    occurred_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sale_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    item_id = table.Column<long>(type: "bigint", nullable: true),
                    sale_line_id = table.Column<long>(type: "bigint", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    authorised_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    customer_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    outcome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_company_id_kind_event_date",
                schema: "pos",
                table: "event",
                columns: new[] { "company_id", "kind", "event_date" });

            migrationBuilder.CreateIndex(
                name: "ix_event_company_id_sale_number",
                schema: "pos",
                table: "event",
                columns: new[] { "company_id", "sale_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event",
                schema: "pos");
        }
    }
}
