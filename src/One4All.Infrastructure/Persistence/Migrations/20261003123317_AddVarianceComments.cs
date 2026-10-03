using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVarianceComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "variance_comment",
                schema: "bud",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    fiscal_year_id = table.Column<long>(type: "bigint", nullable: false),
                    as_at_period = table.Column<int>(type: "integer", nullable: false),
                    account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    author_user_id = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variance_comment", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_variance_comment_company_id_fiscal_year_id_as_at_period_acc",
                schema: "bud",
                table: "variance_comment",
                columns: new[] { "company_id", "fiscal_year_id", "as_at_period", "account_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "variance_comment",
                schema: "bud");
        }
    }
}
