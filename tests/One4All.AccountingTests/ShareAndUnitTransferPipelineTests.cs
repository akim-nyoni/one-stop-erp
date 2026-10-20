using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Housing;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// A share of a jointly held stand and a sectional-title unit pass through the same transfer pipeline as a stand: lodged, the checklist verified by
/// an officer, approved by two others, and only then moved on their own registers - the share between the holders, the unit to its new owner.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ShareAndUnitTransferPipelineTests
{
    private readonly AccountingFixture _fixture;

    public ShareAndUnitTransferPipelineTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Shares_and_units_change_hands_only_through_an_approved_transfer()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Role officer = new($"Share officer {companyId}");
        officer.Grant("Housing.OwnershipChange", OperationFlags.View | OperationFlags.Create | OperationFlags.Edit);
        Role director = new($"Share approver {companyId}");
        director.Grant("Housing.OwnershipApprove", OperationFlags.View | OperationFlags.Approve);
        db.Roles.AddRange(officer, director);
        User verifier = new($"sut-verifier-{companyId}", "Verifier");
        User approverOne = new($"sut-approver1-{companyId}", "First Approver");
        User approverTwo = new($"sut-approver2-{companyId}", "Second Approver");
        db.Users.AddRange(verifier, approverOne, approverTwo);
        Customer a = new(companyId, "SUT-A", "Holder A", Scenario.Usd);
        Customer b = new(companyId, "SUT-B", "Holder B", Scenario.Usd);
        Customer c = new(companyId, "SUT-C", "Buyer C", Scenario.Usd);
        db.Customers.AddRange(a, b, c);
        LandParcel parcel = new(companyId, $"LP-SUT-{companyId}", "Parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        verifier.AssignRole(officer.Id);
        approverOne.AssignRole(director.Id);
        approverTwo.AssignRole(director.Id);
        HousingStand house = new(companyId, parcel.Id, null, "SUT-1", 300m, 1000m);
        HousingStand flats = new(companyId, parcel.Id, null, "SUT-2", 300m, 1000m);
        db.HousingStands.AddRange(house, flats);
        await db.SaveChangesAsync();
        house.Sell(a.Id, a.Name, 5000m, new DateOnly(2026, 1, 5));
        flats.Sell(a.Id, a.Name, 5000m, new DateOnly(2026, 1, 5));
        await db.SaveChangesAsync();
        IJointOwnershipService joint = scope.ServiceProvider.GetRequiredService<IJointOwnershipService>();
        (await joint.RegisterAsync(companyId, house.Id, CoOwnershipRegime.InCommon, [new(a.Id, 60m), new(b.Id, 40m)], new DateOnly(2026, 1, 10))).IsSuccess.Should().BeTrue();
        ISectionalTitleService sectional = scope.ServiceProvider.GetRequiredService<ISectionalTitleService>();
        (await sectional.RegisterAsync(companyId, "SUTB", "Block", flats.Id, [new("1A", 50m)])).IsSuccess.Should().BeTrue();
        (await sectional.TransferUnitAsync(companyId, "SUTB", "1A", a.Id, 30000m, new DateOnly(2026, 1, 20))).IsSuccess.Should().BeTrue();

        ITransferApplicationService transfers = scope.ServiceProvider.GetRequiredService<ITransferApplicationService>();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        DateOnly day = new(2026, 3, 1);
        async Task Approve(string number)
        {
            foreach (TransferChecklistLine line in (await transfers.ChecklistAsync(companyId, number, day)).Value.Where(l => !l.IsLive && !l.Satisfied))
            {
                string reference = $"{number}-{line.Requirement}";
                if (line.Requirement == TransferRequirement.FeesReceipted)
                {
                    (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = c.Id, DocumentNumber = reference, ReceiptDate = day, Currency = Scenario.Usd, Amount = 50m, BankAccountCode = "1100" }))
                        .IsSuccess.Should().BeTrue();
                }

                (await transfers.VerifyAsync(companyId, number, line.Requirement, reference, verifier.Id, day)).IsSuccess.Should().BeTrue(line.Requirement.ToString());
            }

            var submitted = await transfers.SubmitAsync(companyId, number, verifier.Id, day);
            submitted.IsSuccess.Should().BeTrue(submitted.IsFailure ? submitted.Error.ToString() : null);
            (await transfers.ApproveAsync(companyId, number, approverOne.Id, day)).IsSuccess.Should().BeTrue();
            (await transfers.ApproveAsync(companyId, number, approverTwo.Id, day)).IsSuccess.Should().BeTrue();
        }

        // B sells 25% of the house to C: nothing moves until the transfer is approved and completed.
        TransferLodgement Share(decimal percent) => new()
        {
            CompanyId = companyId, StandId = house.Id, TransferType = TransferType.Sale, FromCustomerId = b.Id, ToCustomerId = c.Id, SharePercent = percent, Date = day,
        };
        (await transfers.LodgeAsync(Share(45m))).Error.Code.Should().Be("Hse.ShareTransfer");
        string share = (await transfers.LodgeAsync(Share(25m))).Value;
        (await joint.HoldersAsync(companyId, house.Id, day)).Select(h => (h.Name, h.SharePercent)).Should().Equal(("Holder A", 60m), ("Holder B", 40m));
        await Approve(share);
        var completed = await transfers.CompleteAsync(companyId, share, day);
        completed.IsSuccess.Should().BeTrue(completed.IsFailure ? completed.Error.ToString() : null);
        (await joint.HoldersAsync(companyId, house.Id, day)).Select(h => (h.Name, h.SharePercent)).Should().BeEquivalentTo(new[] { ("Holder A", 60m), ("Holder B", 15m), ("Buyer C", 25m) });

        // A sells flat 1A to C the same way.
        string unit = (await transfers.LodgeAsync(new TransferLodgement
        {
            CompanyId = companyId, TransferType = TransferType.Sale, SectionalSchemeCode = "sutb", UnitNumber = "1a", ToCustomerId = c.Id, Consideration = 35000m, Date = day,
        })).Value;
        await Approve(unit);
        (await transfers.CompleteAsync(companyId, unit, new DateOnly(2026, 3, 2))).IsSuccess.Should().BeTrue();
        (await sectional.UnitsAsync(companyId, "SUTB")).Value.Single().Owner.Should().Be("Buyer C");
        (await db.HousingStands.AsNoTracking().SingleAsync(s => s.Id == flats.Id)).CurrentHolderCustomerId.Should().Be(a.Id, "the building's stand stays with its holder");
    }
}
