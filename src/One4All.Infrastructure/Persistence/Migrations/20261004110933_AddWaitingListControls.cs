using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitingListControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "last_confirmed_on",
                schema: "hse",
                table: "applicant",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "applicant_notice",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    applicant_id = table.Column<long>(type: "bigint", nullable: false),
                    notice_type = table.Column<int>(type: "integer", nullable: false),
                    sent_on = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applicant_notice", x => x.id);
                    table.ForeignKey(
                        name: "fk_applicant_notice_housing_applicants_applicant_id",
                        column: x => x.applicant_id,
                        principalSchema: "hse",
                        principalTable: "applicant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registration_date_change",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    applicant_id = table.Column<long>(type: "bigint", nullable: false),
                    old_date = table.Column<DateOnly>(type: "date", nullable: false),
                    new_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registration_date_change", x => x.id);
                    table.CheckConstraint("ck_registration_date_change_decider", "decided_by_user_id IS NULL OR decided_by_user_id <> requested_by_user_id");
                    table.ForeignKey(
                        name: "fk_registration_date_change_applicant_applicant_id",
                        column: x => x.applicant_id,
                        principalSchema: "hse",
                        principalTable: "applicant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_applicant_notice_applicant_id_sent_on",
                schema: "hse",
                table: "applicant_notice",
                columns: new[] { "applicant_id", "sent_on" });

            migrationBuilder.CreateIndex(
                name: "ix_registration_date_change_one_pending",
                schema: "hse",
                table: "registration_date_change",
                column: "applicant_id",
                unique: true,
                filter: "status = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applicant_notice",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "registration_date_change",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "last_confirmed_on",
                schema: "hse",
                table: "applicant");
        }
    }
}
