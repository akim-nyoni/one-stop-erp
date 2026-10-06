using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthenticationHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "password_changed_utc",
                schema: "sec",
                table: "user",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "two_factor_secret",
                schema: "sec",
                table: "user",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_utc",
                schema: "sec",
                table: "login_event",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "password_history",
                schema: "sec",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    set_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_history", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_password_history_user_id_set_on_utc",
                schema: "sec",
                table: "password_history",
                columns: new[] { "user_id", "set_on_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_history",
                schema: "sec");

            migrationBuilder.DropColumn(
                name: "password_changed_utc",
                schema: "sec",
                table: "user");

            migrationBuilder.DropColumn(
                name: "two_factor_secret",
                schema: "sec",
                table: "user");

            migrationBuilder.DropColumn(
                name: "last_seen_utc",
                schema: "sec",
                table: "login_event");
        }
    }
}
