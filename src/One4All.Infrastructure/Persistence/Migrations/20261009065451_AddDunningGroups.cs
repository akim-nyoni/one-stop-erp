using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDunningGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "customer_type",
                schema: "ar",
                table: "customer",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dunning_group_code",
                schema: "ar",
                table: "customer",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dunning_group",
                schema: "ar",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    first_reminder_days = table.Column<int>(type: "integer", nullable: false),
                    second_reminder_days = table.Column<int>(type: "integer", nullable: false),
                    final_notice_days = table.Column<int>(type: "integer", nullable: false),
                    is_exempt = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dunning_group", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dunning_log",
                schema: "ar",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    sent_on = table.Column<DateOnly>(type: "date", nullable: false),
                    overdue = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dunning_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dunning_group_company_id_code",
                schema: "ar",
                table: "dunning_group",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dunning_log_customer_id_level_sent_on",
                schema: "ar",
                table: "dunning_log",
                columns: new[] { "customer_id", "level", "sent_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dunning_group",
                schema: "ar");

            migrationBuilder.DropTable(
                name: "dunning_log",
                schema: "ar");

            migrationBuilder.DropColumn(
                name: "customer_type",
                schema: "ar",
                table: "customer");

            migrationBuilder.DropColumn(
                name: "dunning_group_code",
                schema: "ar",
                table: "customer");
        }
    }
}
