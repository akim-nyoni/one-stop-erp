using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreRequisitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "store_requisition",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    charge_account_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    cost_centre = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    approved_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_requisition", x => x.id);
                    table.CheckConstraint("ck_store_requisition_approver_not_requester", "approved_by_user_id IS NULL OR approved_by_user_id <> requested_by_user_id");
                });

            migrationBuilder.CreateTable(
                name: "store_requisition_line",
                schema: "inv",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    store_requisition_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    requested = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    issued = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_requisition_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_store_requisition_line_store_requisition_store_requisition_",
                        column: x => x.store_requisition_id,
                        principalSchema: "inv",
                        principalTable: "store_requisition",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_store_requisition_company_id_number",
                schema: "inv",
                table: "store_requisition",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_store_requisition_line_store_requisition_id",
                schema: "inv",
                table: "store_requisition_line",
                column: "store_requisition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_requisition_line",
                schema: "inv");

            migrationBuilder.DropTable(
                name: "store_requisition",
                schema: "inv");
        }
    }
}
