using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstalmentDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "penalty_rate_percent",
                schema: "hse",
                table: "instalment_agreement",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "instalment_notice",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    agreement_id = table.Column<long>(type: "bigint", nullable: false),
                    notice_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    arrears = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    days_overdue = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", maxLength: 256, nullable: false),
                    served_on = table.Column<DateOnly>(type: "date", nullable: true),
                    service_method = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    served_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instalment_notice", x => x.id);
                    table.ForeignKey(
                        name: "fk_instalment_notice_instalment_agreement_agreement_id",
                        column: x => x.agreement_id,
                        principalSchema: "hse",
                        principalTable: "instalment_agreement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_instalment_notice_agreement_id_stage",
                schema: "hse",
                table: "instalment_notice",
                columns: new[] { "agreement_id", "stage" });

            migrationBuilder.CreateIndex(
                name: "ix_instalment_notice_company_id_notice_number",
                schema: "hse",
                table: "instalment_notice",
                columns: new[] { "company_id", "notice_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instalment_notice",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "penalty_rate_percent",
                schema: "hse",
                table: "instalment_agreement");
        }
    }
}
