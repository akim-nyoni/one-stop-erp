using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHyperinflationRestatement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ias29",
                schema: "gl",
                table: "account",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "price_index",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    index_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_index", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_price_index_company_id_index_code_month",
                schema: "gl",
                table: "price_index",
                columns: new[] { "company_id", "index_code", "month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_index",
                schema: "gl");

            migrationBuilder.DropColumn(
                name: "ias29",
                schema: "gl",
                table: "account");
        }
    }
}
