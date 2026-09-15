using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "wf");

            migrationBuilder.CreateTable(
                name: "definition",
                schema: "wf",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    document_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_definition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "instance",
                schema: "wf",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    workflow_definition_id = table.Column<long>(type: "bigint", nullable: false),
                    document_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    document_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    initiated_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    current_stage_sequence = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instance", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stage",
                schema: "wf",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workflow_definition_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    quorum = table.Column<int>(type: "integer", nullable: false),
                    min_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    required_role_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    eligible_user_ids = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stage", x => x.id);
                    table.ForeignKey(
                        name: "fk_stage_definition_workflow_definition_id",
                        column: x => x.workflow_definition_id,
                        principalSchema: "wf",
                        principalTable: "definition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approval",
                schema: "wf",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workflow_instance_id = table.Column<long>(type: "bigint", nullable: false),
                    stage_sequence = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    occurred_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approval", x => x.id);
                    table.ForeignKey(
                        name: "fk_approval_workflow_instances_workflow_instance_id",
                        column: x => x.workflow_instance_id,
                        principalSchema: "wf",
                        principalTable: "instance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_approval_workflow_instance_id",
                schema: "wf",
                table: "approval",
                column: "workflow_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_definition_company_id_document_type",
                schema: "wf",
                table: "definition",
                columns: new[] { "company_id", "document_type" },
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_instance_company_id_document_type_document_reference",
                schema: "wf",
                table: "instance",
                columns: new[] { "company_id", "document_type", "document_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_instance_company_id_status",
                schema: "wf",
                table: "instance",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stage_workflow_definition_id_sequence",
                schema: "wf",
                table: "stage",
                columns: new[] { "workflow_definition_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval",
                schema: "wf");

            migrationBuilder.DropTable(
                name: "stage",
                schema: "wf");

            migrationBuilder.DropTable(
                name: "instance",
                schema: "wf");

            migrationBuilder.DropTable(
                name: "definition",
                schema: "wf");
        }
    }
}
