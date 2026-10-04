using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers land acquisition with due diligence: a purchase is opened once per parcel; its costs are capitalised into land
/// WIP element by element and built up to the total parcel cost; it cannot complete until every due-diligence item is
/// signed off acceptably by a permitted officer, a valuation is obtained and the price recorded; an unacceptable finding
/// can be revised once resolved; completed, the parcel enters the land bank at the built-up cost and the case is closed;
/// an abandoned acquisition's costs are written off.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LandAcquisitionTests
{
    private readonly AccountingFixture _fixture;

    public LandAcquisitionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Acquisitions_complete_only_after_due_diligence_at_their_built_up_cost()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Role landRole = new($"Land officer {companyId}");
        landRole.Grant("Housing.Acquisition", OperationFlags.View | OperationFlags.Create | OperationFlags.Edit);
        db.Roles.Add(landRole);
        User officer = new($"acq-officer-{companyId}", "Land Officer");
        User clerk = new($"acq-clerk-{companyId}", "Clerk");
        db.Users.AddRange(officer, clerk);
        await db.SaveChangesAsync();
        officer.AssignRole(landRole.Id);
        await db.SaveChangesAsync();

        ILandAcquisitionService acquisitions = scope.ServiceProvider.GetRequiredService<ILandAcquisitionService>();
        string parcelNumber = $"LP-ACQ-{companyId}";
        AcquisitionCase Case(string parcel, AcquisitionMethod method) => new()
        {
            CompanyId = companyId, ParcelNumber = parcel, Description = "Farm 12 remainder", AreaSquareMetres = 20000m, Method = method,
            Counterparty = "Estate Late J. Ncube", ResolutionReference = "Council resolution 33/2026", Date = new DateOnly(2026, 2, 1),
        };
        string number = (await acquisitions.OpenAsync(Case(parcelNumber, AcquisitionMethod.Purchase))).Value;
        (await acquisitions.OpenAsync(Case(parcelNumber.ToLowerInvariant(), AcquisitionMethod.Purchase))).Error.Code.Should().Be("Hse.ParcelExists");

        async Task Cost(AcquisitionCostComponent component, decimal amount, int month) =>
            (await acquisitions.AddCostAsync(companyId, number, new AcquisitionCostInput
            {
                Component = component, Amount = amount, Date = new DateOnly(2026, month, 10), Reference = $"{component}-{amount}", WipAccountCode = "1450", FundingAccountCode = "1100",
            })).IsSuccess.Should().BeTrue();
        await Cost(AcquisitionCostComponent.Survey, 5000m, 2);
        await Cost(AcquisitionCostComponent.Legal, 3000m, 3);
        await Cost(AcquisitionCostComponent.Legal, 1000m, 4);
        await Cost(AcquisitionCostComponent.TransferDuty, 2000m, 4);

        AcquisitionPosition early = (await acquisitions.PositionAsync(companyId, number)).Value;
        early.BuildUp.Should().Equal(new CostElement(AcquisitionCostComponent.Survey, 5000m), new CostElement(AcquisitionCostComponent.Legal, 4000m), new CostElement(AcquisitionCostComponent.TransferDuty, 2000m));
        early.Outstanding.Should().HaveCount(3); // not signed off, no valuation, no purchase price
        (await acquisitions.CompleteAsync(companyId, number, new DateOnly(2026, 5, 1))).Error.Code.Should().Be("Hse.DueDiligenceIncomplete");

        // Due diligence: a permitted officer signs off every item; the occupants are a problem at first.
        DateOnly signed = new(2026, 4, 15);
        (await acquisitions.SignOffAsync(companyId, number, DueDiligenceItem.TitleSearch, "Clean title", true, clerk.Id, signed)).Error.Code.Should().Be("Hse.NotAuthorised");
        foreach (DueDiligenceItem item in Enum.GetValues<DueDiligenceItem>())
        {
            bool acceptable = item != DueDiligenceItem.Occupants;
            (await acquisitions.SignOffAsync(companyId, number, item, acceptable ? "Nothing adverse" : "Three families occupy the north corner", acceptable, officer.Id, signed)).IsSuccess.Should().BeTrue();
        }

        await Cost(AcquisitionCostComponent.PurchasePrice, 100000m, 4);
        Error blocked = (await acquisitions.CompleteAsync(companyId, number, new DateOnly(2026, 5, 1))).Error;
        blocked.Message.Should().Contain("Not acceptable: Occupants").And.Contain("No valuation obtained").And.NotContain("Not signed off").And.NotContain("purchase price");

        (await acquisitions.RecordValuationAsync(companyId, number, "Valuations (Pvt) Ltd", new DateOnly(2026, 3, 1), 104000m)).IsSuccess.Should().BeTrue();
        await Cost(AcquisitionCostComponent.OccupantCompensation, 6000m, 4);
        (await acquisitions.SignOffAsync(companyId, number, DueDiligenceItem.Occupants, "Compensation agreed and paid; families relocated", true, officer.Id, new DateOnly(2026, 4, 30))).IsSuccess.Should().BeTrue();

        long parcelId = (await acquisitions.CompleteAsync(companyId, number, new DateOnly(2026, 5, 1))).Value;
        LandParcel parcel = await db.LandParcels.AsNoTracking().SingleAsync(p => p.Id == parcelId);
        (parcel.ParcelNumber, parcel.AcquisitionCost, parcel.Status).Should().Be((parcelNumber, 117000m, LandParcelStatus.Acquired));
        AcquisitionPosition done = (await acquisitions.PositionAsync(companyId, number)).Value;
        (done.Status, done.TotalCost, done.Outstanding.Count, done.BuildUp[0]).Should().Be((AcquisitionStatus.Completed, 117000m, 0, new CostElement(AcquisitionCostComponent.PurchasePrice, 100000m)));
        (await acquisitions.AddCostAsync(companyId, number, new AcquisitionCostInput { Component = AcquisitionCostComponent.Legal, Amount = 1m, Date = new DateOnly(2026, 5, 2), Reference = "Late", WipAccountCode = "1450", FundingAccountCode = "1100" }))
            .Error.Code.Should().Be("Hse.AcquisitionClosed");

        // Abandoned: the costs capitalised so far are written off.
        string donation = (await acquisitions.OpenAsync(Case($"LP-ACQ2-{companyId}", AcquisitionMethod.Donation))).Value;
        (await acquisitions.AddCostAsync(companyId, donation, new AcquisitionCostInput { Component = AcquisitionCostComponent.Legal, Amount = 1500m, Date = new DateOnly(2026, 5, 5), Reference = "Opinion", WipAccountCode = "1450", FundingAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        (await acquisitions.AbandonAsync(companyId, donation, new DateOnly(2026, 6, 1), "Donor withdrew", "5000", "1450")).Value.Should().Be(1500m);
        (await acquisitions.AbandonAsync(companyId, donation, new DateOnly(2026, 6, 2), "Again", "5000", "1450")).Error.Code.Should().Be("Hse.AcquisitionClosed");
        (await acquisitions.PositionAsync(companyId, donation)).Value.Status.Should().Be(AcquisitionStatus.Abandoned);
    }
}
