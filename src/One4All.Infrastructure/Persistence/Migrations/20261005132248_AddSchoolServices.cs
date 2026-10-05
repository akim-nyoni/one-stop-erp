using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "hostel_id",
                schema: "sch",
                table: "student",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "sponsor_customer_id",
                schema: "sch",
                table: "student",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stream",
                schema: "sch",
                table: "student",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "transport_route_id",
                schema: "sch",
                table: "student",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hostel",
                schema: "sch",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    fee_per_term = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    revenue_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hostel", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transport_route",
                schema: "sch",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    fee_per_term = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    revenue_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transport_route", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hostel_company_id_code",
                schema: "sch",
                table: "hostel",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transport_route_company_id_code",
                schema: "sch",
                table: "transport_route",
                columns: new[] { "company_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hostel",
                schema: "sch");

            migrationBuilder.DropTable(
                name: "transport_route",
                schema: "sch");

            migrationBuilder.DropColumn(
                name: "hostel_id",
                schema: "sch",
                table: "student");

            migrationBuilder.DropColumn(
                name: "sponsor_customer_id",
                schema: "sch",
                table: "student");

            migrationBuilder.DropColumn(
                name: "stream",
                schema: "sch",
                table: "student");

            migrationBuilder.DropColumn(
                name: "transport_route_id",
                schema: "sch",
                table: "student");
        }
    }
}
