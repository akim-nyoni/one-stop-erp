using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashierCardAndBiometric : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "biometric_id",
                schema: "sec",
                table: "user",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "card_hash",
                schema: "sec",
                table: "user",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_biometric_id",
                schema: "sec",
                table: "user",
                column: "biometric_id",
                unique: true,
                filter: "biometric_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_card_hash",
                schema: "sec",
                table: "user",
                column: "card_hash",
                unique: true,
                filter: "card_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_biometric_id",
                schema: "sec",
                table: "user");

            migrationBuilder.DropIndex(
                name: "ix_user_card_hash",
                schema: "sec",
                table: "user");

            migrationBuilder.DropColumn(
                name: "biometric_id",
                schema: "sec",
                table: "user");

            migrationBuilder.DropColumn(
                name: "card_hash",
                schema: "sec",
                table: "user");
        }
    }
}
