using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftCardHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "replaced_by_card_id",
                schema: "vou",
                table: "gift_card",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gift_card_transaction",
                schema: "vou",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    gift_card_id = table.Column<long>(type: "bigint", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    @event = table.Column<int>(name: "event", type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_card_transaction", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gift_card_transaction_gift_card_id",
                schema: "vou",
                table: "gift_card_transaction",
                column: "gift_card_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gift_card_transaction",
                schema: "vou");

            migrationBuilder.DropColumn(
                name: "replaced_by_card_id",
                schema: "vou",
                table: "gift_card");
        }
    }
}
