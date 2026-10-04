using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "housing_agreement",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    agreement_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    agreement_type = table.Column<int>(type: "integer", nullable: false),
                    party_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_housing_agreement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agreement_version",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    housing_agreement_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    terms = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    special_conditions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    amendment_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    document = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    document_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    signed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    witnesses = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agreement_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_agreement_version_housing_agreement_housing_agreement_id",
                        column: x => x.housing_agreement_id,
                        principalSchema: "hse",
                        principalTable: "housing_agreement",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agreement_version_housing_agreement_id_version",
                schema: "hse",
                table: "agreement_version",
                columns: new[] { "housing_agreement_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_housing_agreement_company_id_agreement_number",
                schema: "hse",
                table: "housing_agreement",
                columns: new[] { "company_id", "agreement_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_housing_agreement_stand_id",
                schema: "hse",
                table: "housing_agreement",
                column: "stand_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_version",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "housing_agreement",
                schema: "hse");
        }
    }
}
