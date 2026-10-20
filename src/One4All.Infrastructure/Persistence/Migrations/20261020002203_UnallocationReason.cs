using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnallocationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reversal_reason",
                schema: "trd",
                table: "allocation",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reversed_by_user_id",
                schema: "trd",
                table: "allocation",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reversal_reason",
                schema: "trd",
                table: "allocation");

            migrationBuilder.DropColumn(
                name: "reversed_by_user_id",
                schema: "trd",
                table: "allocation");
        }
    }
}
