using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transfer_application",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    application_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    transfer_type = table.Column<int>(type: "integer", nullable: false),
                    from_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    to_customer_id = table.Column<long>(type: "bigint", nullable: false),
                    consideration = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    lodged_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    submitted_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_on = table.Column<DateOnly>(type: "date", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    clearance_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_application", x => x.id);
                    table.ForeignKey(
                        name: "fk_transfer_application_stand_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_approval",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_application_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    approved_on = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_approval", x => x.id);
                    table.ForeignKey(
                        name: "fk_transfer_approval_transfer_application_transfer_application",
                        column: x => x.transfer_application_id,
                        principalSchema: "hse",
                        principalTable: "transfer_application",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_check",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_application_id = table.Column<long>(type: "bigint", nullable: false),
                    requirement = table.Column<int>(type: "integer", nullable: false),
                    document_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    verified_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    verified_on = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_check", x => x.id);
                    table.ForeignKey(
                        name: "fk_transfer_check_transfer_application_transfer_application_id",
                        column: x => x.transfer_application_id,
                        principalSchema: "hse",
                        principalTable: "transfer_application",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_application_company_id_application_number",
                schema: "hse",
                table: "transfer_application",
                columns: new[] { "company_id", "application_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_application_stand_id",
                schema: "hse",
                table: "transfer_application",
                column: "stand_id",
                unique: true,
                filter: "status IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_approval_transfer_application_id_user_id",
                schema: "hse",
                table: "transfer_approval",
                columns: new[] { "transfer_application_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfer_check_transfer_application_id_requirement",
                schema: "hse",
                table: "transfer_check",
                columns: new[] { "transfer_application_id", "requirement" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfer_approval",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "transfer_check",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "transfer_application",
                schema: "hse");
        }
    }
}
