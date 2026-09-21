using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantPolicyAndPartySize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "party_size",
                schema: "res",
                table: "order",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "policy",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    auto_gratuity_min_party = table.Column<int>(type: "integer", nullable: false),
                    auto_gratuity_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    service_charge_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_policy", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_policy_company_id",
                schema: "res",
                table: "policy",
                column: "company_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "policy",
                schema: "res");

            migrationBuilder.DropColumn(
                name: "party_size",
                schema: "res",
                table: "order");
        }
    }
}
