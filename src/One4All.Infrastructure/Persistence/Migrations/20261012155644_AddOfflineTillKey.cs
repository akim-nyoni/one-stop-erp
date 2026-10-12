using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineTillKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "offline_key",
                schema: "pos",
                table: "till_session",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_till_session_company_id_offline_key",
                schema: "pos",
                table: "till_session",
                columns: new[] { "company_id", "offline_key" },
                unique: true,
                filter: "offline_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_till_session_company_id_offline_key",
                schema: "pos",
                table: "till_session");

            migrationBuilder.DropColumn(
                name: "offline_key",
                schema: "pos",
                table: "till_session");
        }
    }
}
