using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attachment",
                schema: "sys",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    target = table.Column<int>(type: "integer", nullable: false),
                    target_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    removed_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    removal_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attachment_content",
                schema: "sys",
                columns: table => new
                {
                    attachment_id = table.Column<long>(type: "bigint", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachment_content", x => x.attachment_id);
                    table.ForeignKey(
                        name: "fk_attachment_content_attachment_attachment_id",
                        column: x => x.attachment_id,
                        principalSchema: "sys",
                        principalTable: "attachment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachment_company_id_target_target_key",
                schema: "sys",
                table: "attachment",
                columns: new[] { "company_id", "target", "target_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachment_content",
                schema: "sys");

            migrationBuilder.DropTable(
                name: "attachment",
                schema: "sys");
        }
    }
}
