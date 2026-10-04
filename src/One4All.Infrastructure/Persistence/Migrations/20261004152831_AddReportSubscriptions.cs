using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "attachment_content",
                schema: "msg",
                table: "outbound_message",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_content_type",
                schema: "msg",
                table: "outbound_message",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_name",
                schema: "msg",
                table: "outbound_message",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "report_subscription",
                schema: "rpt",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    report_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    parameters_json = table.Column<string>(type: "jsonb", maxLength: 256, nullable: false),
                    recipients = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_subscription", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_delivery",
                schema: "rpt",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    subscription_id = table.Column<long>(type: "bigint", nullable: false),
                    run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recipient = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_delivery", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_delivery_report_subscription_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "rpt",
                        principalTable: "report_subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_report_delivery_subscription_id",
                schema: "rpt",
                table: "report_delivery",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_subscription_company_id_code",
                schema: "rpt",
                table: "report_subscription",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_delivery",
                schema: "rpt");

            migrationBuilder.DropTable(
                name: "report_subscription",
                schema: "rpt");

            migrationBuilder.DropColumn(
                name: "attachment_content",
                schema: "msg",
                table: "outbound_message");

            migrationBuilder.DropColumn(
                name: "attachment_content_type",
                schema: "msg",
                table: "outbound_message");

            migrationBuilder.DropColumn(
                name: "attachment_name",
                schema: "msg",
                table: "outbound_message");
        }
    }
}
