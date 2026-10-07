using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolOptionsAndProRata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "admitted_on",
                schema: "sch",
                table: "student",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "left_on",
                schema: "sch",
                table: "student",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pro_rata",
                schema: "sch",
                table: "fee_structure",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "revenue_account_code",
                schema: "sch",
                table: "fee_item",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fee_option",
                schema: "sch",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    student_id = table.Column<long>(type: "bigint", nullable: false),
                    fee_item_id = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_option", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fee_option_student_id_fee_item_id",
                schema: "sch",
                table: "fee_option",
                columns: new[] { "student_id", "fee_item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fee_option",
                schema: "sch");

            migrationBuilder.DropColumn(
                name: "admitted_on",
                schema: "sch",
                table: "student");

            migrationBuilder.DropColumn(
                name: "left_on",
                schema: "sch",
                table: "student");

            migrationBuilder.DropColumn(
                name: "pro_rata",
                schema: "sch",
                table: "fee_structure");

            migrationBuilder.DropColumn(
                name: "revenue_account_code",
                schema: "sch",
                table: "fee_item");
        }
    }
}
