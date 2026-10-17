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
using One4All.Infrastructure.Logging;
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
        UiScaling.Register();

        try
        {
            string connectionString = ResolveConnectionString();

            // The vendor's keys come from the Licensing section; only a development install (appsettings.Development.json beside the app, which is
            // never shipped, or DOTNET_ENVIRONMENT=Development) also trusts the development key and falls back to the development licence.
            IConfigurationRoot config = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true).Build();
            LicensingOptions licensing = new();
            config.GetSection(LicensingOptions.SectionName).Bind(licensing);
            licensing.Enforce = config.GetValue("Licensing:Enforce", true);
            _development = IsDevelopment();
            if (_development)
            {
                licensing.TrustedKeys[DevLicensing.KeyId] = DevLicensing.PublicKeyBase64;
            }

            ServiceCollection services = new();
            Application.Abstractions.System.UpdateOptions updates = new();
            config.GetSection("Updates").Bind(updates);
            services.AddSingleton(updates);
            services.AddOne4AllInfrastructure(connectionString, licensing);
            services.AddOne4AllLogging(connectionString, "Desktop", config, config["Logging:Directory"]);
            services.AddSingleton(PosDevices());
            services.AddTransient<ViewModels.LoginViewModel>();
            services.AddSingleton<DesktopSession>();
            services.AddScoped<Application.Abstractions.Security.ICurrentUser>(sp => new DesktopCurrentUser(sp.GetRequiredService<DesktopSession>()));
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

        Services.GetRequiredService<DesktopSession>().SignIn(result);
        await DesktopRowScope.LoadAsync(Services, result.UserId).ConfigureAwait(true);
        ILicenceService licence = Services.GetRequiredService<ILicenceService>();
        await Localization.UiText.LoadAsync(Services).ConfigureAwait(true);
        ShellWindow shell = new(new ShellViewModel(result, licence, Services.GetService<One4All.Application.Modules.IModuleRegistry>()), Services);
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
        await sp.GetRequiredService<MessageTemplateSeeder>().SeedAsync().ConfigureAwait(false);

        ILicenceService licence = Services.GetRequiredService<ILicenceService>();
        await licence.ReloadAsync().ConfigureAwait(false);
        if (_development && !licence.Current.IsUsable && !licence.Current.IsReadOnly)
        {
            await licence.InstallAsync(DevLicensing.LicenceToken).ConfigureAwait(false);
        }
    }

    private bool _development;

    // A development install: appsettings.Development.json beside the app (never shipped) or DOTNET_ENVIRONMENT / ONE4ALL_ENVIRONMENT = Development.
    private static bool IsDevelopment()
        => File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"))
            || string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("ONE4ALL_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);

    // The till's peripherals from appsettings (Pos:Devices); ONE4ALL_POS_SIMULATE=1 puts every device on its simulator.
    private static One4All.Infrastructure.Pos.PosDeviceOptions PosDevices()
    {
        One4All.Infrastructure.Pos.PosDeviceOptions options = new();
        new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true).AddJsonFile("appsettings.Development.json", optional: true)
            .Build().GetSection("Pos:Devices").Bind(options);
        if (Environment.GetEnvironmentVariable("ONE4ALL_POS_SIMULATE") == "1")
        {
            foreach (One4All.Infrastructure.Pos.DeviceSettings device in new[] { options.Printer, options.Scale, options.Display, options.CardTerminal, options.LabelPrinter })
            {
                device.Connection = One4All.Infrastructure.Pos.DeviceConnection.Simulator;
            }
        }

        return options;
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
        // Close the sign-in session so the login report shows how long it ran.
        if (_services?.GetService<DesktopSession>()?.SessionId is string session)
        {
            try
            {
                using IServiceScope scope = _services.CreateScope();
                scope.ServiceProvider.GetRequiredService<IAuthenticationService>().EndSessionAsync(session).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Closing must not fail because the database is unreachable; the session stays open in the log.
            }
        }

        _services?.Dispose();
        base.OnExit(e);
    }
}
