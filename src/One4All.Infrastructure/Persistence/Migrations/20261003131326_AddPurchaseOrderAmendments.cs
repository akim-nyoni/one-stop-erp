using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "awaiting_reapproval",
                schema: "pop",
                table: "purchase_order",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "last_amended_by_user_id",
                schema: "pop",
                table: "purchase_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "pop",
                table: "purchase_order",
                type: "integer",
                nullable: false,
                defaultValue: 1); // existing orders are at their first version

            migrationBuilder.CreateTable(
                name: "purchase_order_revision",
                schema: "pop",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    purchase_order_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    total_net = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    lines_snapshot = table.Column<string>(type: "jsonb", maxLength: 256, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amended_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    amended_on = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_revision", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_revision_purchase_order_id_version",
                schema: "pop",
                table: "purchase_order_revision",
                columns: new[] { "purchase_order_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_order_revision",
                schema: "pop");

            migrationBuilder.DropColumn(
                name: "awaiting_reapproval",
                schema: "pop",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "last_amended_by_user_id",
                schema: "pop",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "pop",
                table: "purchase_order");
        }
    }
}
