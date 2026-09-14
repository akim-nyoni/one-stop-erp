using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace One4All.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core CLI (<c>dotnet ef migrations add …</c>). It reads the
/// connection string from the <c>ONE4ALL_CONNECTION</c> environment variable, falling back to the
/// local development default. Runtime wiring uses DI, not this factory.
/// </summary>
public sealed class One4AllDbContextFactory : IDesignTimeDbContextFactory<One4AllDbContext>
{
    /// <summary>The local development connection string (see ADR-0002). Password must be supplied via env var in real use.</summary>
    public const string DevelopmentConnectionString =
        "Host=localhost;Port=5432;Database=one4all;Username=postgres;Password=postgres";

    /// <inheritdoc />
    public One4AllDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ONE4ALL_CONNECTION") ?? DevelopmentConnectionString;

        DbContextOptionsBuilder<One4AllDbContext> builder = new();
        builder.UseOne4All(connectionString);
        return new One4AllDbContext(builder.Options);
    }
}
