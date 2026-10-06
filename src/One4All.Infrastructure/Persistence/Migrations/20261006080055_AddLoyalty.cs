using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyalty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loyalty_member",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    member_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loyalty_member", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_movement",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    member_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    points = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loyalty_movement", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_scheme",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    points_per_unit = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    value_per_point = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    expiry_months = table.Column<int>(type: "integer", nullable: true),
                    liability_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    expense_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loyalty_scheme", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loyalty_tier",
                schema: "pos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    loyalty_scheme_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    min_points = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    multiplier = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loyalty_tier", x => x.id);
                    table.ForeignKey(
                        name: "fk_loyalty_tier_loyalty_schemes_loyalty_scheme_id",
                        column: x => x.loyalty_scheme_id,
                        principalSchema: "pos",
                        principalTable: "loyalty_scheme",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_member_company_id_member_number",
                schema: "pos",
                table: "loyalty_member",
                columns: new[] { "company_id", "member_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_movement_member_id_date",
                schema: "pos",
                table: "loyalty_movement",
                columns: new[] { "member_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_scheme_company_id",
                schema: "pos",
                table: "loyalty_scheme",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_tier_loyalty_scheme_id",
                schema: "pos",
                table: "loyalty_tier",
                column: "loyalty_scheme_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loyalty_member",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "loyalty_movement",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "loyalty_tier",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "loyalty_scheme",
                schema: "pos");
        }
    }
}
