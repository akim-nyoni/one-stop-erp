using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers sign-in logging (brief §14 P 236, 249): successes open sessions on two terminals at once, a wrong password and an unknown
/// name are logged as failures, one session is ended; SYS236 and SYS249 read them back.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LoginActivityTests
{
    private readonly AccountingFixture _fixture;

    public LoginActivityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Sign_ins_are_logged_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        string name = $"login{Guid.NewGuid():N}"[..14];
        User user = new(name, "Login Tester");
        user.SetPasswordHash(scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash("Secret 123!"), mustChange: false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        AuthenticationResult first = await auth.AuthenticateAsync(name, "Secret 123!", "PC-1");
        first.Succeeded.Should().BeTrue();
        first.SessionId.Should().NotBeNullOrEmpty();
        (await auth.AuthenticateAsync(name, "Secret 123!", "PC-2")).Succeeded.Should().BeTrue();
        (await auth.AuthenticateAsync(name, "wrong", "PC-1")).Outcome.Should().Be(AuthenticationOutcome.InvalidCredentials);
        string ghost = $"ghost{Guid.NewGuid():N}"[..12];
        (await auth.AuthenticateAsync(ghost, "x")).Succeeded.Should().BeFalse();
        await auth.EndSessionAsync(first.SessionId!);
        (await db.LoginEvents.AsNoTracking().SingleAsync(l => l.SessionId == first.SessionId)).EndedOnUtc.Should().NotBeNull();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        async Task<string> Render(string code)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["FromDate"] = today.AddDays(-1), ["ToDate"] = today.AddDays(1) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string activity = await Render("SYS236");
        activity.Should().Contain($"{name},2,1,0,").And.Contain("\"PC-1, PC-2\",0").And.Contain($"(unknown) {ghost},0,1,0,-,-,0.0,-,0");
        string sessions = await Render("SYS249");
        sessions.Should().Contain($"{name},PC-1,Password,").And.Contain(",PC-2").And.Contain($"{name},PC-2,Password,").And.Contain(",open,-,PC-1");
    }
}
