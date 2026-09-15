using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Schools;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class SchoolTests
{
    private readonly AccountingFixture _fixture;

    public SchoolTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Term_billing_applies_bursary_and_raises_fees_on_guardian_ar()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long structureId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            Customer guardianA = new(companyId, "GUAR-A", "Guardian A", Scenario.Usd);
            Customer guardianB = new(companyId, "GUAR-B", "Guardian B", Scenario.Usd);
            db.Customers.AddRange(guardianA, guardianB);

            FeeStructure structure = new(companyId, "G1-T1", "Grade 1 Term 1", "G1", "Term 1", "4200");
            structure.AddItem("Tuition", 900m, true);
            structure.AddItem("Levy", 100m, true);
            structure.AddItem("Optional trip", 50m, false); // not billed by default
            db.FeeStructures.Add(structure);
            await db.SaveChangesAsync();

            // Full-fee student, and a 25% bursary student.
            Student s1 = new(companyId, "ADM-001", "Student One", "G1", guardianA.Id);
            Student s2 = new(companyId, "ADM-002", "Student Two", "G1", guardianB.Id);
            s2.GrantBursary(BursaryType.Percentage, 25m);
            db.Students.AddRange(s1, s2);
            await db.SaveChangesAsync();

            structureId = structure.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISchoolBillingService school = scope.ServiceProvider.GetRequiredService<ISchoolBillingService>();
            var run = await school.BillTermAsync(companyId, structureId, "FEE-", new DateOnly(2026, 1, 15));
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            run.Value.Count.Should().Be(2);
            // Compulsory total 1000; full fee 1000 + (1000 − 250) = 1750.
            run.Value.TotalNetBilled.Should().Be(1750m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport debtors = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 1, 31));
            debtors.Total.Should().Be(1750m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 1, 31));
            tb.IsBalanced.Should().BeTrue();
        }
    }
}
