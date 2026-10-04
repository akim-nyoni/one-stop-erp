using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCemeteryRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cemetery_plot",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cemetery = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    section = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    plot_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    holder_customer_id = table.Column<long>(type: "bigint", nullable: true),
                    sale_invoice_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    sold_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cemetery_plot", x => x.id);
                    table.CheckConstraint("ck_cemetery_plot_terms", "price >= 0 AND capacity >= 1");
                });

            migrationBuilder.CreateTable(
                name: "cemetery_interment",
                schema: "mun",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cemetery_plot_id = table.Column<long>(type: "bigint", nullable: false),
                    interment_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    deceased_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    date_of_death = table.Column<DateOnly>(type: "date", nullable: false),
                    interment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cemetery_interment", x => x.id);
                    table.CheckConstraint("ck_cemetery_interment_dates", "interment_date >= date_of_death");
                    table.ForeignKey(
                        name: "fk_cemetery_interment_cemetery_plot_cemetery_plot_id",
                        column: x => x.cemetery_plot_id,
                        principalSchema: "mun",
                        principalTable: "cemetery_plot",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cemetery_interment_cemetery_plot_id",
                schema: "mun",
                table: "cemetery_interment",
                column: "cemetery_plot_id");

            migrationBuilder.CreateIndex(
                name: "ix_cemetery_interment_interment_date",
                schema: "mun",
                table: "cemetery_interment",
                column: "interment_date");

            migrationBuilder.CreateIndex(
                name: "ix_cemetery_interment_interment_number",
                schema: "mun",
                table: "cemetery_interment",
                column: "interment_number");

            migrationBuilder.CreateIndex(
                name: "ix_cemetery_plot_company_id_cemetery_status",
                schema: "mun",
                table: "cemetery_plot",
                columns: new[] { "company_id", "cemetery", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_cemetery_plot_company_id_reference",
                schema: "mun",
                table: "cemetery_plot",
                columns: new[] { "company_id", "reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cemetery_interment",
                schema: "mun");

            migrationBuilder.DropTable(
                name: "cemetery_plot",
                schema: "mun");
        }
    }
}
