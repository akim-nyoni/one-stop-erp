using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetVirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "virement_id",
                schema: "bud",
                table: "budget_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "budget_virement",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    budget_id = table.Column<long>(type: "bigint", nullable: false),
                    from_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    to_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    justification = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reviewed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_virement", x => x.id);
                    table.CheckConstraint("ck_budget_virement_reviewer_not_requester", "status <> 1 OR reviewed_by_user_id <> requested_by_user_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_budget_virement_company_id_budget_id_status",
                schema: "bud",
                table: "budget_virement",
                columns: new[] { "company_id", "budget_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_virement",
                schema: "bud");

            migrationBuilder.DropColumn(
                name: "virement_id",
                schema: "bud",
                table: "budget_line");
        }
    }
}
