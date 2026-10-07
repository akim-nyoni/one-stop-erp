using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompulsoryAcquisition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "assessed_amount",
                schema: "hse",
                table: "compensation_claim",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "assessed_on",
                schema: "hse",
                table: "compensation_claim",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assessor",
                schema: "hse",
                table: "compensation_claim",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notice_reference",
                schema: "hse",
                table: "compensation_claim",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "notice_served_on",
                schema: "hse",
                table: "compensation_claim",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "objection_deadline",
                schema: "hse",
                table: "compensation_claim",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "paid_to_date",
                schema: "hse",
                table: "compensation_claim",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "possession_on",
                schema: "hse",
                table: "compensation_claim",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "compensation_step",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    compensation_claim_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    party = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    raised_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    resolved_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compensation_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_compensation_step_compensation_claim_compensation_claim_id",
                        column: x => x.compensation_claim_id,
                        principalSchema: "hse",
                        principalTable: "compensation_claim",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_compensation_step_compensation_claim_id",
                schema: "hse",
                table: "compensation_step",
                column: "compensation_claim_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compensation_step",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "assessed_amount",
                schema: "hse",
                table: "compensation_claim");

            migrationBuilder.DropColumn(
                name: "assessed_on",
                schema: "hse",
                table: "compensation_claim");

            migrationBuilder.DropColumn(
                name: "assessor",
                schema: "hse",
                table: "compensation_claim");

            migrationBuilder.DropColumn(
                name: "notice_reference",
                schema: "hse",
                table: "compensation_claim");

            migrationBuilder.DropColumn(
                name: "notice_served_on",
                schema: "hse",
                table: "compensation_claim");

            migrationBuilder.DropColumn(
                name: "objection_deadline",
                schema: "hse",
                table: "compensation_claim");

            migrationBuilder.DropColumn(
                name: "paid_to_date",
                schema: "hse",
                table: "compensation_claim");

            migrationBuilder.DropColumn(
                name: "possession_on",
                schema: "hse",
                table: "compensation_claim");
        }
    }
}
