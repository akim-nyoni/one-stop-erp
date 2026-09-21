using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuItemPriceLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "menu_item_price",
                schema: "res",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    menu_item_id = table.Column<long>(type: "bigint", nullable: false),
                    price_level = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_item_price", x => x.id);
                    table.ForeignKey(
                        name: "fk_menu_item_price_menu_items_menu_item_id",
                        column: x => x.menu_item_id,
                        principalSchema: "res",
                        principalTable: "menu_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_menu_item_price_menu_item_id_price_level",
                schema: "res",
                table: "menu_item_price",
                columns: new[] { "menu_item_id", "price_level" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "menu_item_price",
                schema: "res");
        }
    }
}
