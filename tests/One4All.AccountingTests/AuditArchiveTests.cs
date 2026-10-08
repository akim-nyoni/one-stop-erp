using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Licensing;
using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the partitioned audit log's retention (brief §5.1): a closed year is archived only with the licence holder's signed authorisation
/// for that year, oldest first; its rows move to the archive schema (still readable, still append-only) and the company's hash chain verifies
/// across the gap, carrying on from where the archive left it.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AuditArchiveTests
{
    private readonly AccountingFixture _fixture;

    public AuditArchiveTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_closed_year_is_archived_with_the_licence_holders_key_and_the_chain_still_verifies()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Ed25519KeyPair vendor = Ed25519KeyPair.Generate(), stranger = Ed25519KeyPair.Generate();
        await using ServiceProvider provider = _fixture.BuildServiceProvider(services =>
            services.AddSingleton<ITrustedLicenceKeys>(new Keys(new Dictionary<string, byte[]> { ["vendor-1"] = vendor.PublicKey })));
        long company = 900_000 + Random.Shared.Next(99_999);

        // A company's chain: two entries in 2021, one in 2022.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            string previous = string.Empty;
            foreach (DateTimeOffset when in new[] { new DateTimeOffset(2021, 3, 1, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2021, 11, 30, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2022, 2, 1, 9, 0, 0, TimeSpan.Zero) })
            {
                ChangeLogEntry entry = ChangeLogEntry.Create(previous, when, company, 1, "tester", "GL", "Journal", "1", AuditAction.Insert, newValues: "{}");
                db.ChangeLog.Add(entry);
                previous = entry.RowHash;
            }

            await db.SaveChangesAsync();
            (await db.Database.SqlQuery<string>($"SELECT tableoid::regclass::text AS \"Value\" FROM audit.change_log WHERE company_id = {company} ORDER BY id").ToListAsync())
                .Should().Equal("audit.change_log_y2021", "audit.change_log_y2021", "audit.change_log_y2022");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAuditArchiveService archives = scope.ServiceProvider.GetRequiredService<IAuditArchiveService>();
            string Token(string scopeYear, Ed25519KeyPair key, string kid = "vendor-1", int days = 7) => MaintenanceAuthorisationCodec.Sign(new MaintenanceAuthorisation
            {
                KeyId = kid, Action = "audit.archive", Scope = scopeYear, IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(days),
            }, key.PrivateKeyBase64);

            (await archives.ArchiveYearAsync(DateTime.UtcNow.Year, Token(DateTime.UtcNow.Year.ToString(), vendor), 1)).Error.Code.Should().Be("Audit.YearOpen");
            (await archives.ArchiveYearAsync(2021, Token("2022", vendor), 1)).Error.Code.Should().Be("Audit.NotAuthorised", "the authorisation names another year");
            (await archives.ArchiveYearAsync(2021, Token("2021", stranger), 1)).Error.Code.Should().Be("Audit.NotAuthorised", "a key that is not the licence holder's");
            (await archives.ArchiveYearAsync(2021, Token("2021", vendor, days: -1), 1)).Error.Code.Should().Be("Audit.NotAuthorised", "an expired authorisation");
            (await archives.ArchiveYearAsync(2022, Token("2022", vendor), 1)).Error.Code.Should().Be("Audit.ArchiveOldestFirst");

            for (int year = 2020; year <= 2021; year++)
            {
                if ((await archives.ArchivesAsync()).All(a => a.Year != year))
                {
                    var archived = await archives.ArchiveYearAsync(year, Token(year.ToString(), vendor), 1);
                    archived.IsSuccess.Should().BeTrue(archived.IsFailure ? archived.Error.ToString() : null);
                }
            }

            (await archives.ArchiveYearAsync(2021, Token("2021", vendor), 1)).Error.Code.Should().Be("Audit.AlreadyArchived");
            AuditArchiveEntry entry2021 = (await archives.ArchivesAsync()).Single(a => a.Year == 2021);
            entry2021.TableName.Should().Be("audit_archive.change_log_y2021");
            entry2021.RowCount.Should().BeGreaterThanOrEqualTo(2);
            (await archives.YearsAsync()).Should().Contain(y => y.Year == 2021 && y.Archived).And.Contain(y => y.Year == 2022 && !y.Archived);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.ChangeLog.CountAsync(c => c.CompanyId == company)).Should().Be(1, "only 2022 stays in the live log");
            (await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM audit_archive.change_log_y2021 WHERE company_id = {company}").SingleAsync()).Should().Be(2,
                "the archived year is still readable");
            Func<Task> alter = () => db.Database.ExecuteSqlAsync($"DELETE FROM audit_archive.change_log_y2021 WHERE company_id = {company}");
            await alter.Should().ThrowAsync<Npgsql.PostgresException>("the archive stays append-only");

            AuditChainVerification verified = await scope.ServiceProvider.GetRequiredService<IAuditService>().VerifyChainAsync(company);
            verified.IsIntact.Should().BeTrue(verified.Message);
            verified.EntriesChecked.Should().Be(1);
        }
    }

    private sealed class Keys(IReadOnlyDictionary<string, byte[]> keys) : ITrustedLicenceKeys
    {
        public IReadOnlyDictionary<string, byte[]> PublicKeysByKid { get; } = keys;
    }
}
