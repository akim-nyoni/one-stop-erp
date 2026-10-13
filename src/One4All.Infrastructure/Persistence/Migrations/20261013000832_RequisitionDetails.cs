using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequisitionDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cost_centre",
                schema: "pop",
                table: "requisition",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "department",
                schema: "pop",
                table: "requisition",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "justification",
                schema: "pop",
                table: "requisition",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "project",
                schema: "pop",
                table: "requisition",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "required_by",
                schema: "pop",
                table: "requisition",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cost_centre",
                schema: "pop",
                table: "purchase_order",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "department",
                schema: "pop",
                table: "purchase_order",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "project",
                schema: "pop",
                table: "purchase_order",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cost_centre",
                schema: "pop",
                table: "requisition");

            migrationBuilder.DropColumn(
                name: "department",
                schema: "pop",
                table: "requisition");

            migrationBuilder.DropColumn(
                name: "justification",
                schema: "pop",
                table: "requisition");

            migrationBuilder.DropColumn(
                name: "project",
                schema: "pop",
                table: "requisition");

            migrationBuilder.DropColumn(
                name: "required_by",
                schema: "pop",
                table: "requisition");

            migrationBuilder.DropColumn(
                name: "cost_centre",
                schema: "pop",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "department",
                schema: "pop",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "project",
                schema: "pop",
                table: "purchase_order");
        }
    }
}
