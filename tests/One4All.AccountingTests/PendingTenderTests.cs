using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Receipting;
using One4All.Infrastructure.Persistence;
using Xunit;
using ReceiptRequest = One4All.Application.Abstractions.Receipting.ReceiptRequest;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class PendingTenderTests
{
    private const long Cashier = 10301;

    private readonly AccountingFixture _fixture;

    public PendingTenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Pending_tender_sits_in_uncleared_effects_then_clears_to_bank()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long stationId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            // Reuse Cash in Transit (1050) as the uncleared-effects control for the test.
            CashierStation station = new(companyId, "TILL-PT", "Counter PT", "PT");
            db.Set<CashierStation>().Add(station);
            await db.SaveChangesAsync();
            stationId = station.Id;
        }

        long pendingId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            long sessionId = (await rcp.OpenSessionAsync(companyId, stationId, Cashier, new DateOnly(2026, 3, 1), [new FloatDeclaration(Scenario.Usd, 0m)])).Value;
            // A claimed bank transfer of 500 → posts to uncleared effects (1050), not the bank.
            (await rcp.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, SessionId = sessionId, PayerKind = PayerKind.Anonymous, Date = new DateOnly(2026, 3, 1),
                CashAccountCode = "1100", Pending = true, PendingAccountCode = "1050",
                Lines = [new ReceiptLineRequest(ReceiptLineType.Sundry, 500m, "4400", "Bank transfer claimed")],
                Tenders = [new ReceiptTenderRequest(TenderType.BankTransfer, 500m, "TRF-777")],
            })).IsSuccess.Should().BeTrue();

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            pendingId = await db.Set<PendingTender>().Where(t => t.CompanyId == companyId).Select(t => t.Id).FirstAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.Rows.Should().Contain(r => r.AccountCode == "1050" && r.Debit == 500m); // sitting in uncleared effects

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var rc33 = await runner.RenderAsync(companyId, "RC33", new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Html);
            rc33.IsSuccess.Should().BeTrue(rc33.IsFailure ? rc33.Error.ToString() : null);
            Encoding.UTF8.GetString(rc33.Value.Content).Should().Contain("TRF-777").And.Contain("500.00");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReceiptingService rcp = scope.ServiceProvider.GetRequiredService<IReceiptingService>();
            (await rcp.ConfirmPendingTenderAsync(companyId, pendingId, new DateOnly(2026, 3, 5), "1100")).IsSuccess.Should().BeTrue();
            (await rcp.ConfirmPendingTenderAsync(companyId, pendingId, new DateOnly(2026, 3, 5), "1100")).IsFailure.Should().BeTrue(); // already confirmed
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().NotContain(r => r.AccountCode == "1050" && (r.Debit != 0m || r.Credit != 0m)); // cleared
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Debit == 500m);                       // now in the bank

            // No longer pending → drops off RC33.
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var rc33 = await runner.RenderAsync(companyId, "RC33", new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Html);
            Encoding.UTF8.GetString(rc33.Value.Content).Should().NotContain("TRF-777");
        }
    }
}
