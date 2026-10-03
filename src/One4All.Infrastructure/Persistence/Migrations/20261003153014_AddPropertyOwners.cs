using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "property_owner",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    property_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    share_percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    is_ratepayer = table.Column<bool>(type: "boolean", nullable: false),
                    owned_from = table.Column<DateOnly>(type: "date", nullable: false),
                    owned_to = table.Column<DateOnly>(type: "date", nullable: true),
                    reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    deeds_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_owner", x => x.id);
                    table.CheckConstraint("ck_property_owner_share", "share_percent > 0 AND share_percent <= 100 AND (owned_to IS NULL OR owned_to >= owned_from)");
                    table.ForeignKey(
                        name: "fk_property_owner_property_property_id",
                        column: x => x.property_id,
                        principalSchema: "mun",
                        principalTable: "property",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_property_owner_company_id_reference",
                schema: "mun",
                table: "property_owner",
                columns: new[] { "company_id", "reference" });

            migrationBuilder.CreateIndex(
                name: "ix_property_owner_one_current_share",
                schema: "mun",
                table: "property_owner",
                columns: new[] { "property_id", "customer_id" },
                unique: true,
                filter: "owned_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_property_owner_property_id_owned_from",
                schema: "mun",
                table: "property_owner",
                columns: new[] { "property_id", "owned_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "property_owner",
                schema: "mun");
        }
    }
}
