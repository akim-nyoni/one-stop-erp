using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTitleDeedPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "title_deed",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    application_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    holder_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    applied_on = table.Column<DateOnly>(type: "date", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    stage_since = table.Column<DateOnly>(type: "date", nullable: false),
                    conveyancer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    deed_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    registered_on = table.Column<DateOnly>(type: "date", nullable: true),
                    received_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_title_deed", x => x.id);
                    table.ForeignKey(
                        name: "fk_title_deed_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "title_deed_step",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title_deed_id = table.Column<long>(type: "bigint", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_title_deed_step", x => x.id);
                    table.ForeignKey(
                        name: "fk_title_deed_step_title_deed_title_deed_id",
                        column: x => x.title_deed_id,
                        principalSchema: "hse",
                        principalTable: "title_deed",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_title_deed_company_id_application_number",
                schema: "hse",
                table: "title_deed",
                columns: new[] { "company_id", "application_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_title_deed_stand_id",
                schema: "hse",
                table: "title_deed",
                column: "stand_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_title_deed_step_title_deed_id",
                schema: "hse",
                table: "title_deed_step",
                column: "title_deed_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "title_deed_step",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "title_deed",
                schema: "hse");
        }
    }
}
