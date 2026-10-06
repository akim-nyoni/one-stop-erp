using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowDelegationEscalation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "escalate_after_hours",
                schema: "wf",
                table: "stage",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "escalate_to_user_id",
                schema: "wf",
                table: "stage",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stage_entered_on_utc",
                schema: "wf",
                table: "instance",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "on_behalf_of_user_id",
                schema: "wf",
                table: "approval",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "delegation",
                schema: "wf",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    from_user_id = table.Column<long>(type: "bigint", nullable: false),
                    to_user_id = table.Column<long>(type: "bigint", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    withdrawn = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delegation", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "instance_approver",
                schema: "wf",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workflow_instance_id = table.Column<long>(type: "bigint", nullable: false),
                    stage_sequence = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    added_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    added_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instance_approver", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delegation_company_id_to_user_id",
                schema: "wf",
                table: "delegation",
                columns: new[] { "company_id", "to_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_instance_approver_workflow_instance_id_stage_sequence",
                schema: "wf",
                table: "instance_approver",
                columns: new[] { "workflow_instance_id", "stage_sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delegation",
                schema: "wf");

            migrationBuilder.DropTable(
                name: "instance_approver",
                schema: "wf");

            migrationBuilder.DropColumn(
                name: "escalate_after_hours",
                schema: "wf",
                table: "stage");

            migrationBuilder.DropColumn(
                name: "escalate_to_user_id",
                schema: "wf",
                table: "stage");

            migrationBuilder.DropColumn(
                name: "stage_entered_on_utc",
                schema: "wf",
                table: "instance");

            migrationBuilder.DropColumn(
                name: "on_behalf_of_user_id",
                schema: "wf",
                table: "approval");
        }
    }
}
