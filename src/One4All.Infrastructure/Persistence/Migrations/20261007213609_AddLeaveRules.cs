using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "carry_over_expiry_months",
                schema: "pay",
                table: "leave_policy",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_accumulation_days",
                schema: "pay",
                table: "leave_policy",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "negative_balance_days",
                schema: "pay",
                table: "leave_policy",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "carried_over_days",
                schema: "pay",
                table: "leave_balance",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "carry_expires_on",
                schema: "pay",
                table: "leave_balance",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "leave_sick_tier",
                schema: "pay",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    leave_policy_id = table.Column<long>(type: "bigint", nullable: false),
                    from_day = table.Column<int>(type: "integer", nullable: false),
                    pay_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_sick_tier", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_sick_tier_leave_policy_leave_policy_id",
                        column: x => x.leave_policy_id,
                        principalSchema: "pay",
                        principalTable: "leave_policy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leave_sick_tier_leave_policy_id",
                schema: "pay",
                table: "leave_sick_tier",
                column: "leave_policy_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leave_sick_tier",
                schema: "pay");

            migrationBuilder.DropColumn(
                name: "carry_over_expiry_months",
                schema: "pay",
                table: "leave_policy");

            migrationBuilder.DropColumn(
                name: "max_accumulation_days",
                schema: "pay",
                table: "leave_policy");

            migrationBuilder.DropColumn(
                name: "negative_balance_days",
                schema: "pay",
                table: "leave_policy");

            migrationBuilder.DropColumn(
                name: "carried_over_days",
                schema: "pay",
                table: "leave_balance");

            migrationBuilder.DropColumn(
                name: "carry_expires_on",
                schema: "pay",
                table: "leave_balance");
        }
    }
}
