using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowRulesAndSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "manager_user_id",
                schema: "sec",
                table: "user",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "condition",
                schema: "wf",
                table: "stage",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dynamic_approver",
                schema: "wf",
                table: "stage",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "attributes_json",
                schema: "wf",
                table: "instance",
                type: "text",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot",
                schema: "wf",
                table: "approval",
                type: "text",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "manager_user_id",
                schema: "sec",
                table: "user");

            migrationBuilder.DropColumn(
                name: "condition",
                schema: "wf",
                table: "stage");

            migrationBuilder.DropColumn(
                name: "dynamic_approver",
                schema: "wf",
                table: "stage");

            migrationBuilder.DropColumn(
                name: "attributes_json",
                schema: "wf",
                table: "instance");

            migrationBuilder.DropColumn(
                name: "snapshot",
                schema: "wf",
                table: "approval");
        }
    }
}
