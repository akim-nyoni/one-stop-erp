using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Security;
using One4All.Desktop.ViewModels;
using One4All.Infrastructure;
using One4All.Infrastructure.Licensing;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Licensing.Core;

namespace One4All.Desktop;

/// <summary>
/// Application entry point. Runs in <b>embedded mode</b> (brief §2.2): the desktop app hosts the same
/// infrastructure services in-process, so there is a single code path. On startup it applies migrations,
/// seeds the permission catalogue and default administrator, ensures a licence is present, then shows
/// the login window.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    /// <summary>The composed service provider (available after startup).</summary>
    public IServiceProvider Services => _services ?? throw new InvalidOperationException("Services not initialised.");

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            string connectionString = ResolveConnectionString();

            LicensingOptions licensing = new();
            licensing.TrustedKeys[DevLicensing.KeyId] = DevLicensing.PublicKeyBase64;

            ServiceCollection services = new();
            services.AddOne4AllInfrastructure(connectionString, licensing);
            services.AddTransient<ViewModels.LoginViewModel>();
            _services = services.BuildServiceProvider();

            await InitialiseAsync().ConfigureAwait(true);

            // Dev-only convenience: ONE4ALL_AUTOLOGIN=1 signs in as admin and opens the shell directly
            // (used for smoke-testing/screenshots). Normal startup shows the login window.
            if (Environment.GetEnvironmentVariable("ONE4ALL_AUTOLOGIN") == "1" && await TryAutoLoginAsync().ConfigureAwait(true))
            {
                return;
            }

            LoginWindow login = new(_services);
            login.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"One4All could not start.\n\n{ex.Message}\n\nCheck that PostgreSQL is running and the connection string is correct " +
                "(set ONE4ALL_CONNECTION or appsettings.Development.json).",
                "One4All — Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task<bool> TryAutoLoginAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        AuthenticationResult result = await auth
            .AuthenticateAsync(AdministratorSeeder.AdminUsername, AdministratorSeeder.DefaultPassword)
            .ConfigureAwait(true);
        if (!result.Succeeded)
        {
            return false;
        }

        ILicenceService licence = Services.GetRequiredService<ILicenceService>();
        ShellWindow shell = new(new ShellViewModel(result, licence));
        MainWindow = shell;
        shell.Show();
        return true;
    }

    private async Task InitialiseAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        await sp.GetRequiredService<One4AllDbContext>().Database.MigrateAsync().ConfigureAwait(false);
        await sp.GetRequiredService<PermissionSeeder>().SeedAsync().ConfigureAwait(false);
        await sp.GetRequiredService<AdministratorSeeder>().SeedAsync().ConfigureAwait(false);

        ILicenceService licence = Services.GetRequiredService<ILicenceService>();
        await licence.ReloadAsync().ConfigureAwait(false);
        if (!licence.Current.IsUsable && !licence.Current.IsReadOnly)
        {
            await licence.InstallAsync(DevLicensing.LicenceToken).ConfigureAwait(false);
        }
    }

    private static string ResolveConnectionString()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("ONE4ALL_CONNECTION");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        IConfigurationRoot config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        string? fromConfig = config.GetConnectionString("One4All");
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig;
        }

        throw new InvalidOperationException(
            "No database connection string. Set the ONE4ALL_CONNECTION environment variable or " +
            $"provide ConnectionStrings:One4All in appsettings.Development.json (base path: {AppContext.BaseDirectory}).");
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
