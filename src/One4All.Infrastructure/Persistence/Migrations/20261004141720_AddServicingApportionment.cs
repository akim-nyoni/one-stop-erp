using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServicingApportionment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "frontage_metres",
                schema: "hse",
                table: "stand",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "zoning_weight",
                schema: "hse",
                table: "stand",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "servicing_link",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    scheme_id = table.Column<long>(type: "bigint", nullable: false),
                    job_contract_id = table.Column<long>(type: "bigint", nullable: false),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    apportioned_to_date = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_servicing_link", x => x.id);
                    table.ForeignKey(
                        name: "fk_servicing_link_contract_job_contract_id",
                        column: x => x.job_contract_id,
                        principalSchema: "job",
                        principalTable: "contract",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_servicing_link_scheme_scheme_id",
                        column: x => x.scheme_id,
                        principalSchema: "hse",
                        principalTable: "scheme",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "servicing_charge",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    servicing_link_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    journal_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_servicing_charge", x => x.id);
                    table.ForeignKey(
                        name: "fk_servicing_charge_servicing_link_servicing_link_id",
                        column: x => x.servicing_link_id,
                        principalSchema: "hse",
                        principalTable: "servicing_link",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_servicing_charge_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_servicing_charge_servicing_link_id",
                schema: "hse",
                table: "servicing_charge",
                column: "servicing_link_id");

            migrationBuilder.CreateIndex(
                name: "ix_servicing_charge_stand_id",
                schema: "hse",
                table: "servicing_charge",
                column: "stand_id");

            migrationBuilder.CreateIndex(
                name: "ix_servicing_link_job_contract_id",
                schema: "hse",
                table: "servicing_link",
                column: "job_contract_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_servicing_link_scheme_id",
                schema: "hse",
                table: "servicing_link",
                column: "scheme_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "servicing_charge",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "servicing_link",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "frontage_metres",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "zoning_weight",
                schema: "hse",
                table: "stand");
        }
    }
}
