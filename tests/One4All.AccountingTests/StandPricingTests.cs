using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers stand pricing: rules are proposed and approved by different officers with the pricing permission; only approved
/// rules price stands; a size-band rule beats the scheme rule; a stand's price builds up from its capitalised cost; the
/// price in force on the offer date is frozen on the allocation; and a later price change does not touch it.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StandPricingTests
{
    private readonly AccountingFixture _fixture;

    public StandPricingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Prices_are_approved_built_up_and_frozen_at_the_offer_date()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Role setter = new($"Price setter {companyId}");
        setter.Grant("Housing.Pricing", OperationFlags.View | OperationFlags.Edit);
        Role approverRole = new($"Price approver {companyId}");
        approverRole.Grant("Housing.Pricing", OperationFlags.View | OperationFlags.Edit | OperationFlags.Approve);
        db.Roles.AddRange(setter, approverRole);
        User proposer = new($"price-proposer-{companyId}", "Pricing Officer");
        User approver = new($"price-approver-{companyId}", "Pricing Manager");
        User clerk = new($"price-clerk-{companyId}", "Clerk");
        db.Users.AddRange(proposer, approver, clerk);
        LandParcel parcel = new(companyId, $"LP-PR-{companyId}", "Pricing parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-PR-{companyId}", "Pricing Scheme", SchemeType.CouncilDelivered);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        proposer.AssignRole(setter.Id);
        approver.AssignRole(approverRole.Id);
        HousingStand small = new(companyId, parcel.Id, scheme.Id, "PR-1", 300m, 10000m);
        HousingStand large = new(companyId, parcel.Id, scheme.Id, "PR-2", 600m, 18000m);
        HousingStand third = new(companyId, parcel.Id, scheme.Id, "PR-3", 300m, 10000m);
        db.HousingStands.AddRange(small, large, third);
        await db.SaveChangesAsync();

        IStandPricingService pricing = scope.ServiceProvider.GetRequiredService<IStandPricingService>();
        DateOnly january = new(2026, 1, 1);
        PricingRuleInput SchemeRule(long user, DateOnly from, decimal margin) => new()
        {
            CompanyId = companyId, Scope = PricingScope.Scheme, SchemeId = scheme.Id, EffectiveFrom = from, OverheadPercent = 10m, LevyPerStand = 500m, MarginPercent = margin, RoundTo = 100m, UserId = user,
        };
        (await pricing.ProposeAsync(SchemeRule(clerk.Id, january, 20m))).Error.Code.Should().Be("Hse.NotAuthorised");
        (await pricing.ProposeAsync(SchemeRule(proposer.Id, january, 20m))).IsSuccess.Should().BeTrue();
        long schemeRule = await db.Set<StandPricingRule>().Where(r => r.CompanyId == companyId).Select(r => r.Id).SingleAsync();
        (await pricing.QuoteAsync(companyId, small.Id, new DateOnly(2026, 2, 1))).Error.Code.Should().Be("Hse.NoPrice"); // not approved yet
        (await pricing.ApproveAsync(companyId, schemeRule, proposer.Id, january)).Error.Code.Should().Be("Hse.NotAuthorised");
        (await pricing.ApproveAsync(companyId, schemeRule, approver.Id, january)).IsSuccess.Should().BeTrue();

        long bandRule = (await pricing.ProposeAsync(new PricingRuleInput
        {
            CompanyId = companyId, Scope = PricingScope.SizeBand, SchemeId = scheme.Id, MinSize = 500m, MaxSize = 1000m, EffectiveFrom = january,
            OverheadPercent = 10m, LevyPerStand = 1000m, MarginPercent = 30m, UserId = approver.Id,
        })).Value;
        (await pricing.ApproveAsync(companyId, bandRule, approver.Id, january)).Error.Code.Should().Be("Hse.SelfApproval");
        (await pricing.ProposeAsync(new PricingRuleInput { CompanyId = companyId, Scope = PricingScope.Stand, SchemeId = scheme.Id, StandId = third.Id, EffectiveFrom = january, UserId = proposer.Id }))
            .Error.Code.Should().Be("Hse.PricingRule");
        // The band rule needs an approver other than the manager who proposed it.
        User manager = new($"price-manager-{companyId}", "Second Manager");
        db.Users.Add(manager);
        await db.SaveChangesAsync();
        manager.AssignRole(approverRole.Id);
        await db.SaveChangesAsync();
        (await pricing.ApproveAsync(companyId, bandRule, manager.Id, january)).IsSuccess.Should().BeTrue();

        (await pricing.QuoteAsync(companyId, small.Id, new DateOnly(2025, 12, 31))).Error.Code.Should().Be("Hse.NoPrice");
        StandPriceQuote smallQuote = (await pricing.QuoteAsync(companyId, small.Id, new DateOnly(2026, 2, 1))).Value;
        (smallQuote.Scope, smallQuote.BuildUp.Overhead, smallQuote.BuildUp.Levy, smallQuote.BuildUp.Price).Should().Be((PricingScope.Scheme, 1000m, 500m, 13700m));
        (await pricing.QuoteAsync(companyId, large.Id, new DateOnly(2026, 2, 1))).Value.BuildUp.Price.Should().Be(26740m); // 18,000 + 1,800 + 1,000 + 30% of 19,800

        // Allocated on 1 March: each beneficiary's price is frozen at the offer date.
        foreach ((string id, int year) in new[] { ("PR-A", 2015), ("PR-B", 2016) })
        {
            (await TestClock.RegisterCapturedAsync(scope.ServiceProvider, new WaitingListApplication
            {
                CompanyId = companyId, IdNumber = $"{id}-{companyId}", FullName = id, Category = HousingCategory.HighDensity, DateOfBirth = new DateOnly(1980, 1, 1),
                ResidentSince = new DateOnly(2000, 1, 1), MonthlyIncome = 500m, Tenure = CurrentTenure.Tenant, RegistrationDate = new DateOnly(year, 1, 1),
            })).IsSuccess.Should().BeTrue();
        }

        IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        AllocationRunView run = (await allocations.ProposeAsync(new AllocationProposal
        {
            CompanyId = companyId, SchemeId = scheme.Id, Category = HousingCategory.HighDensity, Method = AllocationMethod.Seniority, AsAtDate = new DateOnly(2026, 3, 1), UserId = 71,
        })).Value;
        AllocationRunView offered = (await allocations.ApproveAsync(companyId, run.RunNumber, 72, new DateOnly(2026, 3, 1), 30)).Value;
        offered.Lines.Select(l => (l.StandNumber, l.OfferedPrice)).Should().Equal(("PR-1", (decimal?)13700m), ("PR-2", (decimal?)26740m));

        // A price rise from April: new quotes change, the offered prices do not.
        long rise = (await pricing.ProposeAsync(SchemeRule(proposer.Id, new DateOnly(2026, 4, 1), 50m))).Value;
        (await pricing.ApproveAsync(companyId, rise, approver.Id, new DateOnly(2026, 3, 20))).IsSuccess.Should().BeTrue();
        (await pricing.QuoteAsync(companyId, third.Id, new DateOnly(2026, 4, 2))).Value.BuildUp.Price.Should().Be(17000m);
        (await pricing.QuoteAsync(companyId, third.Id, new DateOnly(2026, 3, 31))).Value.BuildUp.Price.Should().Be(13700m);
        (await db.Set<StandAllocation>().AsNoTracking().Where(a => a.CompanyId == companyId).Select(a => a.OfferedPrice).ToListAsync()).Should().BeEquivalentTo([13700m, 26740m]);
    }
}
