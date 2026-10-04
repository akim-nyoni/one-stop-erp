using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeveloperAgreements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "developer_agreement",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    agreement_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    scheme_id = table.Column<long>(type: "bigint", nullable: false),
                    developer_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    council_quota = table.Column<int>(type: "integer", nullable: false),
                    developer_quota = table.Column<int>(type: "integer", nullable: false),
                    signed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    delivery_deadline = table.Column<DateOnly>(type: "date", nullable: false),
                    penalty_per_stand_per_month = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    penalties_charged = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_developer_agreement", x => x.id);
                    table.ForeignKey(
                        name: "fk_developer_agreement_housing_schemes_scheme_id",
                        column: x => x.scheme_id,
                        principalSchema: "hse",
                        principalTable: "scheme",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_milestone",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    developer_agreement_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    completed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_milestone", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_milestone_developer_agreement_developer_agreement_",
                        column: x => x.developer_agreement_id,
                        principalSchema: "hse",
                        principalTable: "developer_agreement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "developer_agreement_stand",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    developer_agreement_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    quota = table.Column<int>(type: "integer", nullable: false),
                    assigned_on = table.Column<DateOnly>(type: "date", nullable: false),
                    handed_over_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_developer_agreement_stand", x => x.id);
                    table.ForeignKey(
                        name: "fk_developer_agreement_stand_developer_agreement_developer_agr",
                        column: x => x.developer_agreement_id,
                        principalSchema: "hse",
                        principalTable: "developer_agreement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_developer_agreement_stand_housing_stands_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_milestone_developer_agreement_id",
                schema: "hse",
                table: "delivery_milestone",
                column: "developer_agreement_id");

            migrationBuilder.CreateIndex(
                name: "ix_developer_agreement_company_id_agreement_number",
                schema: "hse",
                table: "developer_agreement",
                columns: new[] { "company_id", "agreement_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_developer_agreement_scheme_id",
                schema: "hse",
                table: "developer_agreement",
                column: "scheme_id");

            migrationBuilder.CreateIndex(
                name: "ix_developer_agreement_stand_developer_agreement_id",
                schema: "hse",
                table: "developer_agreement_stand",
                column: "developer_agreement_id");

            migrationBuilder.CreateIndex(
                name: "ix_developer_agreement_stand_stand_id",
                schema: "hse",
                table: "developer_agreement_stand",
                column: "stand_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_milestone",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "developer_agreement_stand",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "developer_agreement",
                schema: "hse");
        }
    }
}
