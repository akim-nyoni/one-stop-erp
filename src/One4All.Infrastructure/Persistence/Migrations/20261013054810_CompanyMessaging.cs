using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanyMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_messaging",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    email_provider = table.Column<int>(type: "integer", nullable: false),
                    smtp_host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    smtp_port = table.Column<int>(type: "integer", nullable: false),
                    smtp_use_tls = table.Column<bool>(type: "boolean", nullable: false),
                    smtp_username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    smtp_password = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    from_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    from_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    graph_tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    graph_client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    graph_client_secret = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    sms_url = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    sms_body_template = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sms_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sms_auth_header = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sms_auth_value = table.Column<string>(type: "text", maxLength: 256, nullable: true),
                    sms_from = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_messaging", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_messaging_company_id",
                schema: "sys",
                table: "company_messaging",
                column: "company_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_messaging",
                schema: "sys");
        }
    }
}
