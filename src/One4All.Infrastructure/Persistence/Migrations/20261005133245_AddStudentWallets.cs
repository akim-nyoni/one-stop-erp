using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentWallets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "daily_spend_limit",
                schema: "sch",
                table: "student",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "wallet_entry",
                schema: "sch",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    student_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    description = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    journal_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_entry", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_entry_student_id_date",
                schema: "sch",
                table: "wallet_entry",
                columns: new[] { "student_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallet_entry",
                schema: "sch");

            migrationBuilder.DropColumn(
                name: "daily_spend_limit",
                schema: "sch",
                table: "student");
        }
    }
}
