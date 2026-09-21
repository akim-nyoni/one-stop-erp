using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "delivery_address",
                schema: "res",
                table: "order",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "delivery_status",
                schema: "res",
                table: "order",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "driver_user_id",
                schema: "res",
                table: "order",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delivery_address",
                schema: "res",
                table: "order");

            migrationBuilder.DropColumn(
                name: "delivery_status",
                schema: "res",
                table: "order");

            migrationBuilder.DropColumn(
                name: "driver_user_id",
                schema: "res",
                table: "order");
        }
    }
}
