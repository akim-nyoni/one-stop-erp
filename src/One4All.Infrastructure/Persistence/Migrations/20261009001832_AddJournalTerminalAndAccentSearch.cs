using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace One4All.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalTerminalAndAccentSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "terminal",
                schema: "gl",
                table: "journal",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            // Accent-insensitive search: unaccent behind an immutable wrapper so expression indexes can use it, and trigram indexes on the unaccented names.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS unaccent;
                CREATE OR REPLACE FUNCTION public.f_unaccent(text) RETURNS text LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                    AS $func$ SELECT public.unaccent('public.unaccent'::regdictionary, $1) $func$;
                DROP INDEX IF EXISTS inv.ix_item_description_trgm;
                DROP INDEX IF EXISTS ar.ix_customer_name_trgm;
                DROP INDEX IF EXISTS ap.ix_supplier_name_trgm;
                CREATE INDEX IF NOT EXISTS ix_item_description_unaccent_trgm ON inv.item USING gin (public.f_unaccent(description) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_customer_name_unaccent_trgm ON ar.customer USING gin (public.f_unaccent(name) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_supplier_name_unaccent_trgm ON ap.supplier USING gin (public.f_unaccent(name) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_property_address_unaccent_trgm ON mun.property USING gin (public.f_unaccent(coalesce(address, '')) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_property_stand_trgm ON mun.property USING gin (stand_number gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_student_name_unaccent_trgm ON sch.student USING gin (public.f_unaccent(full_name) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_student_admission_trgm ON sch.student USING gin (admission_number gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "terminal",
                schema: "gl",
                table: "journal");

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS sch.ix_student_admission_trgm;
                DROP INDEX IF EXISTS sch.ix_student_name_unaccent_trgm;
                DROP INDEX IF EXISTS mun.ix_property_stand_trgm;
                DROP INDEX IF EXISTS mun.ix_property_address_unaccent_trgm;
                DROP INDEX IF EXISTS ap.ix_supplier_name_unaccent_trgm;
                DROP INDEX IF EXISTS ar.ix_customer_name_unaccent_trgm;
                DROP INDEX IF EXISTS inv.ix_item_description_unaccent_trgm;
                CREATE INDEX IF NOT EXISTS ix_item_description_trgm ON inv.item USING gin (description gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_customer_name_trgm ON ar.customer USING gin (name gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_supplier_name_trgm ON ap.supplier USING gin (name gin_trgm_ops);
                DROP FUNCTION IF EXISTS public.f_unaccent(text);
                """);
        }
    }
}
