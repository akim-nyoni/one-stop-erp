using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetHolderThresholdsAndAllocationBlockLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "alert_thresholds",
                schema: "bud",
                table: "department_unit",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "allocation_block_log",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    run_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    attempted_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    attempted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_block_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_allocation_block_log_company_id_stand_id",
                schema: "hse",
                table: "allocation_block_log",
                columns: new[] { "company_id", "stand_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allocation_block_log",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "alert_thresholds",
                schema: "bud",
                table: "department_unit");
        }
    }
}
