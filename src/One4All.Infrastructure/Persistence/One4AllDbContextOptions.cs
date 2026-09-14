using Microsoft.EntityFrameworkCore;

namespace One4All.Infrastructure.Persistence;

/// <summary>
/// Central place that configures the <see cref="One4AllDbContext"/> provider options so runtime DI and
/// the design-time factory build an identical model (same Npgsql provider, snake_case column naming,
/// and migrations-history location in the <c>sys</c> schema).
/// </summary>
public static class One4AllDbContextOptions
{
    /// <summary>Applies the standard One4All provider configuration to an options builder.</summary>
    /// <param name="builder">The options builder.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static DbContextOptionsBuilder UseOne4All(this DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", Schemas.System))
            .UseSnakeCaseNamingConvention();
}
