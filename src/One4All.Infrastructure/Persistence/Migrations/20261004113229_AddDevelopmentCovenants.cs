using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDevelopmentCovenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "development_covenant",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    covenant_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    holder_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    deadline = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    extensions = table.Column<int>(type: "integer", nullable: false),
                    breached_on = table.Column<DateOnly>(type: "date", nullable: true),
                    notice_issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_development_covenant", x => x.id);
                    table.CheckConstraint("ck_development_covenant_deadline", "deadline >= start_date");
                    table.ForeignKey(
                        name: "fk_development_covenant_housing_stands_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "covenant_step",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    development_covenant_id = table.Column<long>(type: "bigint", nullable: false),
                    milestone = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_covenant_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_covenant_step_development_covenant_development_covenant_id",
                        column: x => x.development_covenant_id,
                        principalSchema: "hse",
                        principalTable: "development_covenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_covenant_step_development_covenant_id_milestone",
                schema: "hse",
                table: "covenant_step",
                columns: new[] { "development_covenant_id", "milestone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_development_covenant_company_id_covenant_number",
                schema: "hse",
                table: "development_covenant",
                columns: new[] { "company_id", "covenant_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_development_covenant_company_id_status_deadline",
                schema: "hse",
                table: "development_covenant",
                columns: new[] { "company_id", "status", "deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_development_covenant_one_live_per_stand",
                schema: "hse",
                table: "development_covenant",
                column: "stand_id",
                unique: true,
                filter: "status <> 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "covenant_step",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "development_covenant",
                schema: "hse");
        }
    }
}
