using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeavePolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "forfeited_days",
                schema: "pay",
                table: "leave_balance",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "last_accrued_period",
                schema: "pay",
                table: "leave_balance",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_year_end_applied",
                schema: "pay",
                table: "leave_balance",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "leave_policy",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    annual_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    accrues_monthly = table.Column<bool>(type: "boolean", nullable: false),
                    leave_year_start_month = table.Column<int>(type: "integer", nullable: false),
                    carry_over_cap_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    pro_rata_joiners = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_policy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leave_service_band",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    leave_policy_id = table.Column<long>(type: "bigint", nullable: false),
                    from_years = table.Column<int>(type: "integer", nullable: false),
                    annual_days = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_service_band", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_service_band_leave_policy_leave_policy_id",
                        column: x => x.leave_policy_id,
                        principalSchema: "pay",
                        principalTable: "leave_policy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_policy_company_id_leave_type",
                schema: "pay",
                table: "leave_policy",
                columns: new[] { "company_id", "leave_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_leave_service_band_leave_policy_id",
                schema: "pay",
                table: "leave_service_band",
                column: "leave_policy_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_service_band",
                schema: "pay");

            migrationBuilder.DropTable(
                name: "leave_policy",
                schema: "pay");

            migrationBuilder.DropColumn(
                name: "forfeited_days",
                schema: "pay",
                table: "leave_balance");

            migrationBuilder.DropColumn(
                name: "last_accrued_period",
                schema: "pay",
                table: "leave_balance");

            migrationBuilder.DropColumn(
                name: "last_year_end_applied",
                schema: "pay",
                table: "leave_balance");
        }
    }
}
