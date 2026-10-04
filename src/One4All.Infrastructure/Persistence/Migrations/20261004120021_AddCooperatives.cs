using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCooperatives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cooperative",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    registration_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cooperative", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cooperative_member",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cooperative_id = table.Column<long>(type: "bigint", nullable: false),
                    id_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cooperative_member", x => x.id);
                    table.ForeignKey(
                        name: "fk_cooperative_member_cooperative_cooperative_id",
                        column: x => x.cooperative_id,
                        principalSchema: "hse",
                        principalTable: "cooperative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cooperative_stand",
                schema: "hse",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<long>(type: "bigint", nullable: false),
                    cooperative_id = table.Column<long>(type: "bigint", nullable: false),
                    stand_id = table.Column<long>(type: "bigint", nullable: false),
                    allocated_on = table.Column<DateOnly>(type: "date", nullable: false),
                    member_id = table.Column<long>(type: "bigint", nullable: true),
                    sub_allocated_on = table.Column<DateOnly>(type: "date", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cooperative_stand", x => x.id);
                    table.ForeignKey(
                        name: "fk_cooperative_stand_cooperative_cooperative_id",
                        column: x => x.cooperative_id,
                        principalSchema: "hse",
                        principalTable: "cooperative",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cooperative_stand_cooperative_member_member_id",
                        column: x => x.member_id,
                        principalSchema: "hse",
                        principalTable: "cooperative_member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cooperative_stand_housing_stands_stand_id",
                        column: x => x.stand_id,
                        principalSchema: "hse",
                        principalTable: "stand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cooperative_company_id_code",
                schema: "hse",
                table: "cooperative",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cooperative_member_cooperative_id_id_number",
                schema: "hse",
                table: "cooperative_member",
                columns: new[] { "cooperative_id", "id_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cooperative_stand_cooperative_id",
                schema: "hse",
                table: "cooperative_stand",
                column: "cooperative_id");

            migrationBuilder.CreateIndex(
                name: "ix_cooperative_stand_member_id",
                schema: "hse",
                table: "cooperative_stand",
                column: "member_id",
                unique: true,
                filter: "member_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cooperative_stand_stand_id",
                schema: "hse",
                table: "cooperative_stand",
                column: "stand_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cooperative_stand",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "cooperative_member",
                schema: "hse");

            migrationBuilder.DropTable(
                name: "cooperative",
                schema: "hse");
        }
    }
}
