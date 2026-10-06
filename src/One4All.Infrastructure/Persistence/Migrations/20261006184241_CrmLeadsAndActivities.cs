using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmLeadsAndActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "quotation_id",
                schema: "crm",
                table: "opportunity",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "activity",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    owner_user_id = table.Column<long>(type: "bigint", nullable: false),
                    due_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lead_id = table.Column<long>(type: "bigint", nullable: true),
                    opportunity_id = table.Column<long>(type: "bigint", nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lead",
                schema: "crm",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    organisation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    owner_user_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    disqualified_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    possible_duplicate_of_id = table.Column<long>(type: "bigint", nullable: true),
                    opportunity_id = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_company_id_owner_user_id_due_on_utc",
                schema: "crm",
                table: "activity",
                columns: new[] { "company_id", "owner_user_id", "due_on_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_customer_id",
                schema: "crm",
                table: "activity",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_lead_id",
                schema: "crm",
                table: "activity",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_opportunity_id",
                schema: "crm",
                table: "activity",
                column: "opportunity_id");

            migrationBuilder.CreateIndex(
                name: "ix_lead_company_id_email",
                schema: "crm",
                table: "lead",
                columns: new[] { "company_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_lead_company_id_status",
                schema: "crm",
                table: "lead",
                columns: new[] { "company_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "lead",
                schema: "crm");

            migrationBuilder.DropColumn(
                name: "quotation_id",
                schema: "crm",
                table: "opportunity");
        }
    }
}
