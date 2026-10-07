using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBlindMultiCurrencyCashUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "blind_cash_up",
                schema: "pos",
                table: "terminal",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "till_cash_count",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    till_session_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    counted_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    counted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    blind = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_till_cash_count", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_till_cash_count_till_session_id_currency",
                schema: "pos",
                table: "till_cash_count",
                columns: new[] { "till_session_id", "currency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "till_cash_count",
                schema: "pos");

            migrationBuilder.DropColumn(
                name: "blind_cash_up",
                schema: "pos",
                table: "terminal");
        }
    }
}
