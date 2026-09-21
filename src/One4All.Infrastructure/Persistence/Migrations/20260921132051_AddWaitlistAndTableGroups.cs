using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlistAndTableGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "group_id",
                schema: "res",
                table: "table",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "table_group",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    primary_table_id = table.Column<long>(type: "bigint", nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: false),
                    member_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_group", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "waitlist",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    guest_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    guest_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    party_size = table.Column<int>(type: "integer", nullable: false),
                    quoted_minutes = table.Column<int>(type: "integer", nullable: false),
                    added_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    seated_table_id = table.Column<long>(type: "bigint", nullable: true),
                    seated_order_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waitlist", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_table_group_id",
                schema: "res",
                table: "table",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_table_group_company_id_status",
                schema: "res",
                table: "table_group",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_company_id_status_added_on",
                schema: "res",
                table: "waitlist",
                columns: new[] { "company_id", "status", "added_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "table_group",
                schema: "res");

            migrationBuilder.DropTable(
                name: "waitlist",
                schema: "res");

            migrationBuilder.DropIndex(
                name: "ix_table_group_id",
                schema: "res",
                table: "table");

            migrationBuilder.DropColumn(
                name: "group_id",
                schema: "res",
                table: "table");
        }
    }
}
