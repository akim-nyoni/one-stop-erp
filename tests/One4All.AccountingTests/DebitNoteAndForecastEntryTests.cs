using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Banking;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers customer debit notes (a further charge against an invoice, kept as a debit-note open item) and flows keyed into the cash forecast
/// by hand, once or monthly.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DebitNoteAndForecastEntryTests
{
    private readonly AccountingFixture _fixture;

    public DebitNoteAndForecastEntryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Debit_notes_add_to_an_invoice_and_hand_keyed_flows_join_the_forecast()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "DN-C", "Debit Note Customer", Scenario.Usd);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        SalesInvoiceRequest Charge(string number, decimal amount) => new()
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd, NetAmount = amount, SalesAccountCode = "4000",
        };
        (await ar.PostInvoiceAsync(Charge("DN-INV-1", 1_000m))).IsSuccess.Should().BeTrue();
        (await ar.PostDebitNoteAsync(Charge("DN-1", 40m), "DN-INV-9")).Error.Code.Should().Be("Ar.OriginalInvoice");
        (await ar.PostDebitNoteAsync(Charge("DN-1", 40m), "DN-INV-1")).IsSuccess.Should().BeTrue();
        OpenItem note = await db.OpenItems.AsNoTracking().SingleAsync(o => o.CompanyId == companyId && o.DocumentNumber == "DN-1");
        (note.ItemType, note.Narrative).Should().Be((OpenItemType.DebitNote, "Debit note on DN-INV-1"));
        (await db.Journals.AsNoTracking().SingleAsync(j => j.CompanyId == companyId && j.DocumentNumber == "DN-1")).DocumentType.Should().Be("SalesDebitNote");

        // A loan repayment each month and a one-off grant, in a twelve-week forecast from 1 April.
        ICashForecastService forecast = scope.ServiceProvider.GetRequiredService<ICashForecastService>();
        (await forecast.AddEntryAsync(companyId, new DateOnly(2026, 4, 15), -500m, "Loan repayment", new DateOnly(2026, 12, 15))).IsSuccess.Should().BeTrue();
        long grant = (await forecast.AddEntryAsync(companyId, new DateOnly(2026, 5, 20), 2_000m, "Grant")).Value;
        (await forecast.AddEntryAsync(companyId, new DateOnly(2026, 5, 20), 0m, "Nothing")).Error.Code.Should().Be("Cash.Entry");
        CashForecast weeks = await forecast.ForecastAsync(companyId, new DateOnly(2026, 4, 1), 12, 0m);
        weeks.Lines.Where(l => l.Source == CashFlowSource.Manual).Select(l => (l.Date, l.Amount)).Should().BeEquivalentTo(
            [(new DateOnly(2026, 4, 15), -500m), (new DateOnly(2026, 5, 15), -500m), (new DateOnly(2026, 6, 15), -500m), (new DateOnly(2026, 5, 20), 2_000m)]);
        (await forecast.RemoveEntryAsync(companyId, grant)).IsSuccess.Should().BeTrue();
        (await forecast.ForecastAsync(companyId, new DateOnly(2026, 4, 1), 12, 0m)).Lines.Count(l => l.Source == CashFlowSource.Manual).Should().Be(3);
    }
}
