using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_amended_by_user_id",
                schema: "sop",
                table: "sales_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "sop",
                table: "sales_order",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "sales_order_revision",
                schema: "sop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    sales_order_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    total_net = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    lines_snapshot = table.Column<string>(type: "jsonb", maxLength: 256, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amended_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    amended_on = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_revision", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_revision_sales_order_id_version",
                schema: "sop",
                table: "sales_order_revision",
                columns: new[] { "sales_order_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_order_revision",
                schema: "sop");

            migrationBuilder.DropColumn(
                name: "last_amended_by_user_id",
                schema: "sop",
                table: "sales_order");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "sop",
                table: "sales_order");
        }
    }
}
