using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrepaymentSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prepayment_schedule",
                schema: "gl",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prepayment_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expense_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cost_centre = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    department = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    total = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    first_month = table.Column<DateOnly>(type: "date", nullable: false),
                    months = table.Column<int>(type: "integer", nullable: false),
                    released_months = table.Column<int>(type: "integer", nullable: false),
                    released = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prepayment_schedule", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_prepayment_schedule_company_id_reference",
                schema: "gl",
                table: "prepayment_schedule",
                columns: new[] { "company_id", "reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prepayment_schedule",
                schema: "gl");
        }
    }
}
