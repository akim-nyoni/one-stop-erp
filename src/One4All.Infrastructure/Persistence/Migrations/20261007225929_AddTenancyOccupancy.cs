using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenancyOccupancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_occupants",
                schema: "hse",
                table: "rental_unit",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lodger_permit",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    permit_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tenancy_id = table.Column<long>(type: "bigint", nullable: false),
                    lodger_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    id_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    rooms = table.Column<int>(type: "integer", nullable: false),
                    monthly_fee = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    fee_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    revoked_on = table.Column<DateOnly>(type: "date", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lodger_permit", x => x.id);
                    table.CheckConstraint("ck_lodger_permit_terms", "rooms >= 1 AND monthly_fee >= 0 AND expires_on >= issued_on");
                    table.ForeignKey(
                        name: "fk_lodger_permit_tenancy_tenancy_id",
                        column: x => x.tenancy_id,
                        principalSchema: "hse",
                        principalTable: "tenancy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenancy_occupant",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenancy_id = table.Column<long>(type: "bigint", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    id_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    relationship = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    moved_in_on = table.Column<DateOnly>(type: "date", nullable: false),
                    moved_out_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenancy_occupant", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenancy_occupant_tenancy_tenancy_id",
                        column: x => x.tenancy_id,
                        principalSchema: "hse",
                        principalTable: "tenancy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lodger_permit_company_id_permit_number",
                schema: "hse",
                table: "lodger_permit",
                columns: new[] { "company_id", "permit_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lodger_permit_tenancy_id",
                schema: "hse",
                table: "lodger_permit",
                column: "tenancy_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenancy_occupant_tenancy_id",
                schema: "hse",
                table: "tenancy_occupant",
                column: "tenancy_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lodger_permit",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "tenancy_occupant",
                schema: "hse");

            migrationBuilder.DropColumn(
                name: "max_occupants",
                schema: "hse",
                table: "rental_unit");
        }
    }
}
