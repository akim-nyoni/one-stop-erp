using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Licensing;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Licensing;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the volume limits of a licence: journals a calendar year (transactions_per_year) and the departments budgeted for (budget_departments),
/// refused past the limit at the point of creation.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LicenceVolumeLimitTests
{
    private readonly AccountingFixture _fixture;

    public LicenceVolumeLimitTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Transactions_a_year_and_budget_departments_are_limited()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Ed25519KeyPair keys = Ed25519KeyPair.Generate();
        LicensingOptions options = new() { Enforce = true };
        options.TrustedKeys["vol-test"] = keys.PublicKeyBase64;
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton(options));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        string tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        int journals = await db.Journals.IgnoreQueryFilters().CountAsync(j => j.PostingDate >= new DateOnly(2026, 1, 1) && j.PostingDate <= new DateOnly(2026, 12, 31));
        int departments = (await db.Set<Budget>().IgnoreQueryFilters().Where(b => b.Department != null).Select(b => b.Department!).Distinct().ToListAsync())
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        string token = new LicenseSigner(keys.PrivateKeyBase64).Sign(new LicensePayload
        {
            KeyId = "vol-test", Product = LicenceService.Product, Customer = new LicenceCustomer { Id = "V", Name = "Volume Co" }, ValidFrom = new DateOnly(2025, 1, 1), ValidTo = today.AddYears(1),
            Modules = [new LicenceModuleGrant { Code = "GL" }, new LicenceModuleGrant { Code = "BUD" }],
            Limits = new Dictionary<string, int?> { ["transactions_per_year"] = journals + 1, ["budget_departments"] = departments + 1 },
        });
        (await scope.ServiceProvider.GetRequiredService<ILicenceService>().InstallAsync(token)).IsUsable.Should().BeTrue();

        IPostingEngine posting = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest Journal(string number) => new()
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 4, 1), TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false,
            DocumentType = "Journal", DocumentNumber = number, Narrative = "Volume",
            Lines =
            [
                new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 10m },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 10m },
            ],
        };
        (await posting.PostAsync(Journal($"VOL-{tag}-1"))).IsSuccess.Should().BeTrue();
        Func<Task> second = () => posting.PostAsync(Journal($"VOL-{tag}-2"));
        (await second.Should().ThrowAsync<LicenceLimitException>()).Which.Message.Should().Contain("transactions per year");
        db.ChangeTracker.Clear();

        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        (await budgeting.CreateBudgetAsync(companyId, yearId, "Vol 1", BudgetType.OriginalApproved, $"D1-{tag}", [])).IsSuccess.Should().BeTrue();
        (await budgeting.CreateBudgetAsync(companyId, yearId, "Vol 1 v2", BudgetType.OriginalApproved, $"D1-{tag}", [])).IsSuccess.Should().BeTrue();   // same department
        Func<Task> another = () => budgeting.CreateBudgetAsync(companyId, yearId, "Vol 2", BudgetType.OriginalApproved, $"D2-{tag}", []);
        (await another.Should().ThrowAsync<LicenceLimitException>()).Which.Message.Should().Contain("budget departments");
    }
}
