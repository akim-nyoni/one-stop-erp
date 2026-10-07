using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "gl",
                table: "journal",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "idempotency_record",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    started_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    response_content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    response_body = table.Column<byte[]>(type: "bytea", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_record", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_company_id_idempotency_key",
                schema: "gl",
                table: "journal",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_record_started_on_utc",
                schema: "sys",
                table: "idempotency_record",
                column: "started_on_utc");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_record_user_id_key",
                schema: "sys",
                table: "idempotency_record",
                columns: new[] { "user_id", "key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_record",
                schema: "sys");

            migrationBuilder.DropIndex(
                name: "ix_journal_company_id_idempotency_key",
                schema: "gl",
                table: "journal");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "gl",
                table: "journal");
        }
    }
}
