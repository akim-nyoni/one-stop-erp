using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Municipal;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the teller's payer search: by account, by stand (its ratepayer) and by student (whoever pays), with each account's balance.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PayerSearchTests
{
    private readonly AccountingFixture _fixture;

    public PayerSearchTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_teller_finds_the_payer_by_account_stand_or_student()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer moyo = new(companyId, "PS-MOYO", "Tendai Moyo", Scenario.Usd), dube = new(companyId, "PS-DUBE", "Rudo Dube", Scenario.Usd);
        db.Customers.AddRange(moyo, dube);
        await db.SaveChangesAsync();
        db.Properties.Add(new Property(companyId, "PSX-414", "Ward 2", moyo.Id));
        db.Students.Add(new Student(companyId, "ADM-PS-77", "Farai Dube", "F2", dube.Id));
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = moyo.Id, DocumentNumber = "PS-INV-1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 75m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();

        IPayerSearchService search = scope.ServiceProvider.GetRequiredService<IPayerSearchService>();
        (await search.SearchAsync(companyId, "m")).Should().BeEmpty("two characters at least");
        (await search.SearchAsync(companyId, "moyo")).Should().ContainSingle().Which.Should().Be(new PayerHit(moyo.Id, "PS-MOYO", "Tendai Moyo", "Account", 75m));
        (await search.SearchAsync(companyId, "psx-414")).Should().ContainSingle(h => h.CustomerId == moyo.Id && h.Matched == "Rates on stand PSX-414" && h.Balance == 75m);
        IReadOnlyList<PayerHit> dubes = await search.SearchAsync(companyId, "dube");
        dubes.Select(h => h.Matched).Should().BeEquivalentTo(["Account", "Fees for Farai Dube (ADM-PS-77)"]);
        dubes.Should().OnlyContain(h => h.CustomerId == dube.Id && h.Balance == 0m);
    }
}
