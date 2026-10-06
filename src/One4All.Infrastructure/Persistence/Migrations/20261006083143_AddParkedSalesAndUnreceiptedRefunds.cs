using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParkedSalesAndUnreceiptedRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "unreceipted_refund_limit",
                schema: "sys",
                table: "company",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "parked_sale",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: false),
                    tag = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    lines_json = table.Column<string>(type: "jsonb", maxLength: 256, nullable: false),
                    total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    parked_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    parked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    closed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    resumed_at_terminal_id = table.Column<long>(type: "bigint", nullable: true),
                    discard_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parked_sale", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parked_sale_company_id_status",
                schema: "pos",
                table: "parked_sale",
                columns: new[] { "company_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parked_sale",
                schema: "pos");

            migrationBuilder.DropColumn(
                name: "unreceipted_refund_limit",
                schema: "sys",
                table: "company");
        }
    }
}
