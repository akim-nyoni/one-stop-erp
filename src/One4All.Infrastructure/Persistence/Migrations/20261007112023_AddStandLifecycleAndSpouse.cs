using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStandLifecycleAndSpouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "consolidated_into_stand_id",
                schema: "hse",
                table: "stand",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "parent_stand_id",
                schema: "hse",
                table: "stand",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "status_before_hold",
                schema: "hse",
                table: "stand",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "marital_regime",
                schema: "hse",
                table: "applicant",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "marital_status",
                schema: "hse",
                table: "applicant",
                type: "integer",
                nullable: false,
                defaultValue: 1); // existing applicants: Single

            migrationBuilder.AddColumn<string>(
                name: "spouse_id_number",
                schema: "hse",
                table: "applicant",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "spouse_name",
                schema: "hse",
                table: "applicant",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stand_status_change",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    housing_stand_id = table.Column<long>(type: "bigint", nullable: false),
                    from = table.Column<int>(type: "integer", nullable: false),
                    to = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    authority = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stand_status_change", x => x.id);
                    table.ForeignKey(
                        name: "fk_stand_status_change_housing_stands_housing_stand_id",
                        column: x => x.housing_stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stand_consolidated_into_stand_id",
                schema: "hse",
                table: "stand",
                column: "consolidated_into_stand_id");

            migrationBuilder.CreateIndex(
                name: "ix_stand_parent_stand_id",
                schema: "hse",
                table: "stand",
                column: "parent_stand_id");

            migrationBuilder.CreateIndex(
                name: "ix_applicant_company_id_spouse_id_number",
                schema: "hse",
                table: "applicant",
                columns: new[] { "company_id", "spouse_id_number" });

            migrationBuilder.CreateIndex(
                name: "ix_stand_status_change_housing_stand_id",
                schema: "hse",
                table: "stand_status_change",
                column: "housing_stand_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stand_status_change",
                schema: "hse");

            migrationBuilder.DropIndex(
                name: "ix_stand_consolidated_into_stand_id",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropIndex(
                name: "ix_stand_parent_stand_id",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropIndex(
                name: "ix_applicant_company_id_spouse_id_number",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "consolidated_into_stand_id",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "parent_stand_id",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "status_before_hold",
                schema: "hse",
                table: "stand");

            migrationBuilder.DropColumn(
                name: "marital_regime",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "marital_status",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "spouse_id_number",
                schema: "hse",
                table: "applicant");

            migrationBuilder.DropColumn(
                name: "spouse_name",
                schema: "hse",
                table: "applicant");
        }
    }
}
