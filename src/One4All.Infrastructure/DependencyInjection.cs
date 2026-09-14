using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.RawSql;
using One4All.Infrastructure.Persistence.Seeding;

namespace One4All.Infrastructure;

/// <summary>
/// Registers the infrastructure services (EF Core context, raw-SQL runner, seeders) into the DI
/// container. Both the ASP.NET Core host and the embedded desktop host call this with the same
/// connection string, so there is a single data-access code path (brief §2.2).
/// </summary>
public static class DependencyInjection
{
    /// <summary>Adds One4All infrastructure services.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOne4AllInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<One4AllDbContext>(options => options.UseOne4All(connectionString));

        services.AddScoped<PermissionSeeder>();
        services.AddSingleton<ISqlScriptRunner>(_ => new SqlScriptRunner(connectionString));

        return services;
    }
}
