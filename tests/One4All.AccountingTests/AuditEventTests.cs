using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the audit trail's explicit events and its second net: overrides need a reason; sign-in and sign-out, role assignments and
/// password resets are logged in the hash chain, which stays intact; a change made over a direct connection is caught by the database
/// trigger and reported as outside the application, with secrets left out.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AuditEventTests
{
    private readonly AccountingFixture _fixture;

    public AuditEventTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Explicit_events_and_direct_changes_are_audited()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        IAuditService audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(-1);
        string tag = Guid.NewGuid().ToString("N")[..8];

        (await audit.RecordAsync(new AuditEvent(null, 1, "tester", "POS", "Sale", "S-1", AuditAction.Override))).Error.Code.Should().Be("Audit.ReasonRequired");
        (await audit.RecordAsync(new AuditEvent(null, 1, "tester", "POS", "Sale", "S-1", AuditAction.Override, "Price matched a competitor"))).IsSuccess.Should().BeTrue();

        long userId = (await admin.CreateUserAsync($"ae.{tag}", "Audited", null, "Audit2026a")).Value;
        long role = (await admin.CreateRoleAsync($"Audited role {tag}", null)).Value;
        (await admin.AssignRoleAsync(userId, role, 900_001)).IsSuccess.Should().BeTrue();
        (await admin.ResetPasswordAsync(userId, "Audit2026b")).IsSuccess.Should().BeTrue();
        AuthenticationResult signedIn = await auth.AuthenticateAsync($"ae.{tag}", "Audit2026b", "TEST-PC");
        await auth.EndSessionAsync(signedIn.SessionId!);

        List<ChangeLogEntry> entries = await db.ChangeLog.AsNoTracking().Where(c => c.CompanyId == null && c.OccurredOnUtc >= start).ToListAsync();
        entries.Should().Contain(e => e.Action == AuditAction.PermissionChange && e.EntityId == userId.ToString() && e.Reason!.Contains($"Role {role} assigned"));
        entries.Should().Contain(e => e.Action == AuditAction.ConfigChange && e.Reason == "Password reset by an administrator");
        entries.Should().Contain(e => e.Action == AuditAction.Login && e.SessionId == signedIn.SessionId && e.Terminal == "TEST-PC");
        entries.Should().Contain(e => e.Action == AuditAction.Logout && e.SessionId == signedIn.SessionId);
        AuditChainVerification chain = await audit.VerifyChainAsync(null);
        chain.IsIntact.Should().BeTrue(chain.Message);

        // Someone edits the role over a direct connection: the trigger catches it as outside the application.
        NpgsqlConnectionStringBuilder direct = new(_fixture.ConnectionString) { ApplicationName = "psql" };
        await using (NpgsqlConnection connection = new(direct.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = new($"UPDATE sec.role SET description = 'edited by hand' WHERE id = {role}", connection);
            (await command.ExecuteNonQueryAsync()).Should().Be(1);
        }

        IReadOnlyList<DatabaseChangeView> outside = await audit.DatabaseChangesAsync(start, DateTimeOffset.UtcNow.AddMinutes(1), outsideApplicationOnly: true);
        outside.Should().Contain(c => c.TableName == "sec.role" && c.Operation == "UPDATE" && c.RowId == role.ToString() && c.ApplicationName == "psql" && c.OutsideApplication);
        IReadOnlyList<DatabaseChangeView> all = await audit.DatabaseChangesAsync(start, DateTimeOffset.UtcNow.AddMinutes(1), outsideApplicationOnly: false);
        all.Should().Contain(c => c.TableName == "sec.user" && c.RowId == userId.ToString() && !c.OutsideApplication);
        db.DatabaseChanges.AsNoTracking().Where(c => c.TableName == "sec.user" && c.RowId == userId.ToString()).AsEnumerable()
            .Should().OnlyContain(c => c.NewValues == null || !c.NewValues.Contains("password_hash"));
    }
}
