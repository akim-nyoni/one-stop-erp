using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierBankChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_bank_change",
                schema: "ap",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    account_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    account_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reviewed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_bank_change", x => x.id);
                    table.CheckConstraint("ck_supplier_bank_change_reviewer_not_requester", "status <> 1 OR reviewed_by_user_id <> requested_by_user_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_bank_change_one_pending",
                schema: "ap",
                table: "supplier_bank_change",
                column: "supplier_id",
                unique: true,
                filter: "status = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_bank_change",
                schema: "ap");
        }
    }
}
