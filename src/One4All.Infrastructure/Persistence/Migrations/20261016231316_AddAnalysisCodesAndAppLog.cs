using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisCodesAndAppLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "analysis_code1",
                schema: "ap",
                table: "supplier",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "analysis_code2",
                schema: "ap",
                table: "supplier",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "analysis_code3",
                schema: "ap",
                table: "supplier",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "analysis_code1",
                schema: "ar",
                table: "customer",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "analysis_code2",
                schema: "ar",
                table: "customer",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "analysis_code3",
                schema: "ar",
                table: "customer",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "app_log",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    logged_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    message = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    exception = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    properties = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    host = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    machine = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_log_logged_on_utc",
                schema: "sys",
                table: "app_log",
                column: "logged_on_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_log",
                schema: "sys");

            migrationBuilder.DropColumn(
                name: "analysis_code1",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "analysis_code2",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "analysis_code3",
                schema: "ap",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "analysis_code1",
                schema: "ar",
                table: "customer");

            migrationBuilder.DropColumn(
                name: "analysis_code2",
                schema: "ar",
                table: "customer");

            migrationBuilder.DropColumn(
                name: "analysis_code3",
                schema: "ar",
                table: "customer");
        }
    }
}
