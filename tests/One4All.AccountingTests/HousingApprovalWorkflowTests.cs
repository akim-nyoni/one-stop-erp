using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers routing housing price rules and ownership transfers through the approval workflow: neither is approved directly while its
/// workflow runs; the workflow's approval puts the price in force and approves the transfer for completion.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingApprovalWorkflowTests
{
    private readonly AccountingFixture _fixture;

    public HousingApprovalWorkflowTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Prices_and_ownership_transfers_are_approved_through_their_workflows()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Role officer = new($"HAW officer {companyId}");
        officer.Grant("Housing.Pricing", OperationFlags.View | OperationFlags.Edit | OperationFlags.Approve);
        officer.Grant("Housing.OwnershipChange", OperationFlags.View | OperationFlags.Create | OperationFlags.Edit);
        officer.Grant("Housing.OwnershipApprove", OperationFlags.View | OperationFlags.Approve);
        db.Roles.Add(officer);
        User proposer = new($"haw-p-{companyId}", "Proposer"), manager = new($"haw-m-{companyId}", "Manager"), board = new($"haw-b-{companyId}", "Board");
        db.Users.AddRange(proposer, manager, board);
        Customer holder = new(companyId, "HAW-H", "Holder", Scenario.Usd), buyer = new(companyId, "HAW-B", "Buyer", Scenario.Usd);
        db.Customers.AddRange(holder, buyer);
        LandParcel parcel = new(companyId, $"LP-HAW-{companyId}", "Parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-HAW-{companyId}", "Scheme", SchemeType.CouncilDelivered);
        db.AddRange(parcel, scheme);
        await db.SaveChangesAsync();
        proposer.AssignRole(officer.Id);
        manager.AssignRole(officer.Id);
        foreach (string type in new[] { WorkflowDocumentTypes.StandPrice, WorkflowDocumentTypes.OwnershipTransfer })
        {
            WorkflowDefinition definition = new(companyId, type, type);
            definition.AddStage(1, "Board").AddEligibleUser(board.Id);
            db.WorkflowDefinitions.Add(definition);
        }

        HousingStand priced = new(companyId, parcel.Id, scheme.Id, "HAW-1", 300m, 10000m), sold = new(companyId, parcel.Id, null, "HAW-2", 300m, 2000m);
        db.HousingStands.AddRange(priced, sold);
        Property property = new(companyId, "HAW-2", "Ward 3", holder.Id);
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        IWorkflowService workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        async Task<long> Pending(string type) => await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.DocumentType == type && i.Status == WorkflowStatus.InProgress).Select(i => i.Id).SingleAsync();

        // A price waits for its workflow.
        IStandPricingService pricing = scope.ServiceProvider.GetRequiredService<IStandPricingService>();
        DateOnly january = new(2026, 1, 1);
        long rule = (await pricing.ProposeAsync(new PricingRuleInput
        {
            CompanyId = companyId, Scope = PricingScope.Scheme, SchemeId = scheme.Id, EffectiveFrom = january, OverheadPercent = 10m, LevyPerStand = 500m, MarginPercent = 20m, RoundTo = 100m, UserId = proposer.Id,
        })).Value;
        (await pricing.ApproveAsync(companyId, rule, manager.Id, january)).Error.Code.Should().Be("Wf.Governed");
        (await workflow.ActAsync(await Pending(WorkflowDocumentTypes.StandPrice), board.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        (await pricing.QuoteAsync(companyId, priced.Id, new DateOnly(2026, 2, 1))).IsSuccess.Should().BeTrue("the workflow put the price in force");

        // An ownership transfer: lodged, verified and submitted, then approved by its workflow instead of two officers.
        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        (await housing.SellStandAsync(companyId, sold.Id, holder.Id, holder.Name, 6000m, new DateOnly(2026, 3, 1), "4100", "1460", "5100", $"HAW-INV-{companyId}")).IsSuccess.Should().BeTrue();
        ITransferApplicationService transfers = scope.ServiceProvider.GetRequiredService<ITransferApplicationService>();
        DateOnly day = new(2026, 5, 10);
        string number = (await transfers.LodgeAsync(new TransferLodgement
        {
            CompanyId = companyId, StandId = sold.Id, TransferType = TransferType.Cession, ToCustomerId = buyer.Id, Consideration = 9000m, Date = day,
        })).Value;
        foreach (TransferRequirement requirement in new[] { TransferRequirement.Identity, TransferRequirement.SpousalConsent, TransferRequirement.OriginalDocuments, TransferRequirement.TaxWithholding })
        {
            (await transfers.VerifyAsync(companyId, number, requirement, $"{requirement} seen", proposer.Id, day)).IsSuccess.Should().BeTrue();
        }

        string fee = $"HAW-FEE-{companyId}";
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = fee, ReceiptDate = day, Currency = Scenario.Usd, Amount = 150m, BankAccountCode = "1100" }))
            .IsSuccess.Should().BeTrue();
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.FeesReceipted, fee, proposer.Id, day)).IsSuccess.Should().BeTrue();
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = holder.Id, DocumentNumber = $"HAW-R-{companyId}", ReceiptDate = day, Currency = Scenario.Usd, Amount = 6000m, BankAccountCode = "1100" }))
            .IsSuccess.Should().BeTrue();
        db.Set<ClearanceCertificate>().Add(new ClearanceCertificate(companyId, $"CC-HAW-{companyId}", property.Id, new DateOnly(2026, 5, 1), new DateOnly(2026, 7, 31), 0m, 1));
        await db.SaveChangesAsync();
        (await transfers.SubmitAsync(companyId, number, proposer.Id, day)).IsSuccess.Should().BeTrue();
        (await transfers.ApproveAsync(companyId, number, manager.Id, day)).Error.Code.Should().Be("Wf.Governed");
        (await workflow.ActAsync(await Pending(WorkflowDocumentTypes.OwnershipTransfer), board.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        (await db.Set<TransferApplication>().AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.ApplicationNumber == number)).Status.Should().Be(TransferStatus.Approved);
    }
}
