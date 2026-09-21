using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTableReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reservation",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    table_id = table.Column<long>(type: "bigint", nullable: false),
                    guest_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    guest_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    party_size = table.Column<int>(type: "integer", nullable: false),
                    reserved_for = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    seated_order_id = table.Column<long>(type: "bigint", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservation", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reservation_company_id_status",
                schema: "res",
                table: "reservation",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reservation_company_id_table_id_reserved_for",
                schema: "res",
                table: "reservation",
                columns: new[] { "company_id", "table_id", "reserved_for" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reservation",
                schema: "res");
        }
    }
}
