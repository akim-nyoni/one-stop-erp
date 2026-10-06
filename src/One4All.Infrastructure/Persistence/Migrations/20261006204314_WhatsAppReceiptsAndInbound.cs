using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppReceiptsAndInbound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivered_at",
                schema: "msg",
                table: "outbound_message",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "read_at",
                schema: "msg",
                table: "outbound_message",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inbound_message",
                schema: "msg",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    from = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sender_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    lead_id = table.Column<long>(type: "bigint", nullable: true),
                    handled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    handled_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_message", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_message_provider_reference",
                schema: "msg",
                table: "outbound_message",
                column: "provider_reference");

            migrationBuilder.CreateIndex(
                name: "ix_inbound_message_company_id_handled_at",
                schema: "msg",
                table: "inbound_message",
                columns: new[] { "company_id", "handled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_message_provider_reference",
                schema: "msg",
                table: "inbound_message",
                column: "provider_reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_message",
                schema: "msg");

            migrationBuilder.DropIndex(
                name: "ix_outbound_message_provider_reference",
                schema: "msg",
                table: "outbound_message");

            migrationBuilder.DropColumn(
                name: "delivered_at",
                schema: "msg",
                table: "outbound_message");

            migrationBuilder.DropColumn(
                name: "read_at",
                schema: "msg",
                table: "outbound_message");
        }
    }
}
