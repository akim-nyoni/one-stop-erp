using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class TaxAndFxTests
{
    private readonly AccountingFixture _fixture;

    public TaxAndFxTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tax_uses_the_effective_dated_rate()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            TaxCode standard = new(companyId, "STD", "Standard VAT", TaxType.Standard) { OutputAccountCode = "2200" };
            standard.AddRate(new DateOnly(2025, 1, 1), 14m);
            standard.AddRate(new DateOnly(2026, 1, 1), 15m); // rate rose in 2026
            db.TaxCodes.Add(standard);

            db.TaxCodes.Add(new TaxCode(companyId, "EXE", "Exempt", TaxType.Exempt));
            await db.SaveChangesAsync();
        }

        await using AsyncServiceScope calcScope = provider.CreateAsyncScope();
        ITaxCalculator tax = calcScope.ServiceProvider.GetRequiredService<ITaxCalculator>();

        // 2026 uses 15%.
        Result<TaxCalculation> y2026 = await tax.OnNetAsync(companyId, "STD", 100m, new DateOnly(2026, 6, 1));
        y2026.Value.TaxAmount.Should().Be(15m);
        y2026.Value.GrossAmount.Should().Be(115m);

        // A back-dated 2025 document uses the 14% that applied then.
        Result<TaxCalculation> y2025 = await tax.OnNetAsync(companyId, "STD", 100m, new DateOnly(2025, 6, 1));
        y2025.Value.TaxAmount.Should().Be(14m);

        // Tax-inclusive extraction.
        Result<TaxCalculation> incl = await tax.FromGrossAsync(companyId, "STD", 115m, new DateOnly(2026, 6, 1));
        incl.Value.NetAmount.Should().Be(100m);
        incl.Value.TaxAmount.Should().Be(15m);

        // Exempt yields no tax.
        Result<TaxCalculation> exempt = await tax.OnNetAsync(companyId, "EXE", 100m, new DateOnly(2026, 6, 1));
        exempt.Value.TaxAmount.Should().Be(0m);
    }

    [SkippableFact]
    public async Task Fx_revaluation_posts_unrealised_gain_and_auto_reverses()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        }

        // Receive 100 EUR into the EUR bank at the daily rate 1.10 -> base 110.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            Result<PostingReceipt> post = await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "CB", PostingDate = new DateOnly(2026, 6, 1),
                TransactionCurrency = Scenario.Eur,
                Lines =
                [
                    new JournalLineRequest { AccountCode = "1150", Side = JournalSide.Debit, TransactionAmount = 100m },
                    new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = 100m },
                ],
            });
            post.IsSuccess.Should().BeTrue(post.IsFailure ? post.Error.ToString() : null);
        }

        // Revalue at the 30 Jun closing rate 1.20 -> base 120, so a 10 unrealised gain.
        FxRevaluationResult result;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFxRevaluationService fx = scope.ServiceProvider.GetRequiredService<IFxRevaluationService>();
            Result<FxRevaluationResult> run = await fx.RevalueAsync(companyId, new DateOnly(2026, 6, 30), autoReverse: true);
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            result = run.Value;
        }

        result.NetDifference.Should().Be(10m);
        result.Accounts.Should().ContainSingle(a => a.AccountCode == "1150" && a.Difference == 10m && a.BaseAfter == 120m);
        result.JournalNumber.Should().NotBeNull();
        result.ReversalJournalNumber.Should().NotBeNull();

        // After revaluation + auto-reversal, the EUR bank base balance is back to 110 (unrealised only).
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            var reports = scope.ServiceProvider.GetRequiredService<Application.Abstractions.Reporting.IGeneralLedgerReports>();
            var activity = await reports.NominalActivityAsync(companyId, "1150", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            activity.ClosingBalance.Should().Be(110m);
        }
    }
}
