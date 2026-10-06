using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers sibling discounts: the family's children in admission order take the rule for their place (second 10%, third and later 20%),
/// after any bursary; an only child pays in full.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SiblingDiscountTests
{
    private readonly AccountingFixture _fixture;

    public SiblingDiscountTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Younger_siblings_are_billed_at_the_family_discount()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer family = new(companyId, "SIB-FAM", "Family", Scenario.Usd), single = new(companyId, "SIB-ONE", "Single", Scenario.Usd);
        db.Customers.AddRange(family, single);
        FeeStructure structure = new(companyId, "SIB-G1T1", "Grade 1 Term 1", "SIBG1", "Term 1", "4000");
        structure.AddItem("Tuition", 1_000m, true);
        db.FeeStructures.Add(structure);
        await db.SaveChangesAsync();
        List<Student> children = [];
        foreach ((string number, long guardian) in new[] { ("SIB-1", family.Id), ("SIB-2", family.Id), ("SIB-3", family.Id), ("SIB-4", family.Id), ("SIB-5", single.Id) })
        {
            Student child = new(companyId, number, number, "SIBG1", guardian);
            if (number == "SIB-3")
            {
                child.GrantBursary(BursaryType.Value, 100m);
            }

            db.Students.Add(child);
            await db.SaveChangesAsync();
            children.Add(child);
        }

        ISchoolBillingService billing = scope.ServiceProvider.GetRequiredService<ISchoolBillingService>();
        (await billing.SetSiblingDiscountAsync(companyId, 1, 10m)).Error.Code.Should().Be("School.SiblingDiscountInvalid");
        (await billing.SetSiblingDiscountAsync(companyId, 2, 10m)).IsSuccess.Should().BeTrue();
        (await billing.SetSiblingDiscountAsync(companyId, 3, 20m)).IsSuccess.Should().BeTrue();
        (await billing.SetSiblingDiscountAsync(companyId, 5, 30m)).IsSuccess.Should().BeTrue();
        (await billing.SetSiblingDiscountAsync(companyId, 5, 0m)).IsSuccess.Should().BeTrue();

        TermBillResult run = (await billing.BillTermAsync(companyId, structure.Id, "SIB-T1-", new DateOnly(2026, 1, 10))).Value;

        // 1,000 + 900 + (1,000 − 100 bursary) × 80% + 800 + 1,000 for the only child.
        run.Bills.Select(b => (b.NetFees, b.SiblingDiscount)).Should().Equal((1_000m, 0m), (900m, 100m), (720m, 180m), (800m, 200m), (1_000m, 0m));
        run.TotalNetBilled.Should().Be(4_420m);
    }
}
