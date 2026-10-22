using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "applied_on",
                schema: "hr",
                table: "detail_change_request",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_from",
                schema: "hr",
                table: "detail_change_request",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_detail_change_request_company_id_employee_id_field",
                schema: "hr",
                table: "detail_change_request",
                columns: new[] { "company_id", "employee_id", "field" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_detail_change_request_company_id_employee_id_field",
                schema: "hr",
                table: "detail_change_request");

            migrationBuilder.DropColumn(
                name: "applied_on",
                schema: "hr",
                table: "detail_change_request");

            migrationBuilder.DropColumn(
                name: "effective_from",
                schema: "hr",
                table: "detail_change_request");
        }
    }
}
