using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepossessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repossession",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    agreement_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    receivable_derecognised = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    previous_carrying_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    net_realisable_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    inventory_recognised = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    approved_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repossession", x => x.id);
                    table.CheckConstraint("ck_repossession_approver", "approved_by_user_id <> requested_by_user_id");
                    table.ForeignKey(
                        name: "fk_repossession_instalment_agreement_agreement_id",
                        column: x => x.agreement_id,
                        principalSchema: "hse",
                        principalTable: "instalment_agreement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_repossession_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_repossession_agreement_id",
                schema: "hse",
                table: "repossession",
                column: "agreement_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_repossession_company_id_reference",
                schema: "hse",
                table: "repossession",
                columns: new[] { "company_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_repossession_stand_id",
                schema: "hse",
                table: "repossession",
                column: "stand_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repossession",
                schema: "hse");
        }
    }
}
