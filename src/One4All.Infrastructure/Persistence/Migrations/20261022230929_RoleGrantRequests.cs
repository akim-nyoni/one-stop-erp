using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoleGrantRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role_grant_request",
                schema: "sec",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    scopes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    conflicts = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_grant_request", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_grant_request_status_user_id",
                schema: "sec",
                table: "role_grant_request",
                columns: new[] { "status", "user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_grant_request",
                schema: "sec");
        }
    }
}
