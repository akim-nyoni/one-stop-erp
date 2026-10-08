using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTillFavouritesRatePolicyAndApprovalHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "parent_code",
                schema: "inv",
                table: "item_category",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "refund_window_days",
                schema: "sys",
                table: "company",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "machine",
                schema: "wf",
                table: "approval",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_hash",
                schema: "wf",
                table: "approval",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "exchange_rate_policy",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rate_type = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rate_policy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "favourite",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: true),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favourite", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rate_policy_company_id_document_type",
                schema: "sys",
                table: "exchange_rate_policy",
                columns: new[] { "company_id", "document_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_favourite_company_id_terminal_id_item_id",
                schema: "pos",
                table: "favourite",
                columns: new[] { "company_id", "terminal_id", "item_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exchange_rate_policy",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "favourite",
                schema: "pos");

            migrationBuilder.DropColumn(
                name: "parent_code",
                schema: "inv",
                table: "item_category");

            migrationBuilder.DropColumn(
                name: "refund_window_days",
                schema: "sys",
                table: "company");

            migrationBuilder.DropColumn(
                name: "machine",
                schema: "wf",
                table: "approval");

            migrationBuilder.DropColumn(
                name: "snapshot_hash",
                schema: "wf",
                table: "approval");
        }
    }
}
