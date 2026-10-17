using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers change-of-ownership applications: one open application per stand; the sale checklist (live municipal clearance
/// and housing account, identity, spousal consent, originals, fees, tax); only permitted officers verify, and live items
/// cannot be ticked by hand; fees must be receipted on the ledger; submission waits for every item, including clearing the
/// transferor's arrears and a current clearance certificate; two different approvers, neither of whom verified it; the
/// live checks run again at completion; and completion updates the stand's ownership chain and the municipal owner register
/// together. A change of name has its own, shorter checklist and can be rejected.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TransferApplicationTests
{
    private readonly AccountingFixture _fixture;

    public TransferApplicationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_cession_is_verified_approved_twice_and_completed_across_both_registers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Role officer = new($"Transfer officer {companyId}");
        officer.Grant("Housing.OwnershipChange", OperationFlags.View | OperationFlags.Create | OperationFlags.Edit);
        Role director = new($"Transfer approver {companyId}");
        director.Grant("Housing.OwnershipApprove", OperationFlags.View | OperationFlags.Approve);
        db.Roles.AddRange(officer, director);
        User verifier = new($"trf-verifier-{companyId}", "Verifying Officer");
        User both = new($"trf-both-{companyId}", "Officer and Approver");
        User approverOne = new($"trf-approver1-{companyId}", "First Approver");
        User approverTwo = new($"trf-approver2-{companyId}", "Second Approver");
        User clerk = new($"trf-clerk-{companyId}", "Clerk");
        db.Users.AddRange(verifier, both, approverOne, approverTwo, clerk);
        Customer holder = new(companyId, "TRF-H", "Outgoing Holder", Scenario.Usd);
        Customer buyer = new(companyId, "TRF-B", "Incoming Holder", Scenario.Usd);
        db.Customers.AddRange(holder, buyer);
        LandParcel parcel = new(companyId, $"LP-TRF-{companyId}", "Transfer parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        verifier.AssignRole(officer.Id);
        both.AssignRole(officer.Id);
        both.AssignRole(director.Id);
        approverOne.AssignRole(director.Id);
        approverTwo.AssignRole(director.Id);
        HousingStand stand = new(companyId, parcel.Id, null, "TRF-1", 300m, 2000m);
        db.HousingStands.Add(stand);
        Property property = new(companyId, "TRF-1", "Ward 3", holder.Id);
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        (await housing.SellStandAsync(companyId, stand.Id, holder.Id, holder.Name, 6000m, new DateOnly(2026, 3, 1), "4100", "1460", "5100", $"TRF-INV-{companyId}")).IsSuccess.Should().BeTrue();

        ITransferApplicationService transfers = scope.ServiceProvider.GetRequiredService<ITransferApplicationService>();
        DateOnly day = new(2026, 5, 10);
        TransferLodgement Lodgement(TransferType type, long to) => new() { CompanyId = companyId, StandId = stand.Id, TransferType = type, ToCustomerId = to, Consideration = 9000m, Date = day };
        (await transfers.LodgeAsync(Lodgement(TransferType.ChangeOfName, buyer.Id))).Error.Code.Should().Be("Hse.TransferParties");
        string number = (await transfers.LodgeAsync(Lodgement(TransferType.Cession, buyer.Id))).Value;
        (await transfers.LodgeAsync(Lodgement(TransferType.Sale, buyer.Id))).Error.Code.Should().Be("Hse.TransferOpen");

        (await transfers.ChecklistAsync(companyId, number, day)).Value.Select(l => (l.Requirement, l.IsLive, l.Satisfied)).Should().Equal(
            (TransferRequirement.MunicipalClearance, true, false), (TransferRequirement.HousingAccountClear, true, false), (TransferRequirement.Identity, false, false),
            (TransferRequirement.SpousalConsent, false, false), (TransferRequirement.OriginalDocuments, false, false), (TransferRequirement.FeesReceipted, false, false),
            (TransferRequirement.TaxWithholding, false, false));

        // Verification: permitted officers only, documents only, items on this checklist only, fees on the ledger.
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.Identity, "ID copies", clerk.Id, day)).Error.Code.Should().Be("Hse.NotAuthorised");
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.MunicipalClearance, "Looks fine", verifier.Id, day)).Error.Code.Should().Be("Hse.LiveCheck");
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.EstateDocuments, "Death cert", verifier.Id, day)).Error.Code.Should().Be("Hse.NotRequired");
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.Identity, "ID copies + photos", both.Id, day)).IsSuccess.Should().BeTrue();
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.Identity, "Again", verifier.Id, day)).Error.Code.Should().Be("Hse.AlreadyVerified");
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.SpousalConsent, "Consent affidavit", verifier.Id, day)).IsSuccess.Should().BeTrue();
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.OriginalDocuments, "Original cession agreement", verifier.Id, day)).IsSuccess.Should().BeTrue();
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.TaxWithholding, "CGT certificate 55", verifier.Id, day)).IsSuccess.Should().BeTrue();
        string fee = $"TRF-FEE-{companyId}";
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.FeesReceipted, fee, verifier.Id, day)).Error.Code.Should().Be("Hse.FeesNotReceipted");
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = fee, ReceiptDate = day, Currency = Scenario.Usd, Amount = 150m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.FeesReceipted, fee, verifier.Id, day)).IsSuccess.Should().BeTrue();

        // Submission waits for the live checks: the transferor's arrears and a current clearance certificate.
        Error incomplete = (await transfers.SubmitAsync(companyId, number, verifier.Id, day)).Error;
        incomplete.Code.Should().Be("Hse.ChecklistIncomplete");
        incomplete.Message.Should().Contain("No current clearance certificate").And.Contain("owes 6,000.00");
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = holder.Id, DocumentNumber = $"TRF-R-{companyId}", ReceiptDate = day, Currency = Scenario.Usd, Amount = 6000m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        string certificate = $"CC-TRF-{companyId}";
        db.Set<ClearanceCertificate>().Add(new ClearanceCertificate(companyId, certificate, property.Id, new DateOnly(2026, 5, 1), new DateOnly(2026, 7, 31), 0m, 1));
        await db.SaveChangesAsync();
        (await transfers.SubmitAsync(companyId, number, verifier.Id, day)).IsSuccess.Should().BeTrue();

        // Two approvers, neither of whom verified it.
        (await transfers.ApproveAsync(companyId, number, verifier.Id, day)).Error.Code.Should().Be("Hse.NotAuthorised");
        (await transfers.ApproveAsync(companyId, number, both.Id, day)).Error.Code.Should().Be("Hse.ApproverVerified");
        (await transfers.ApproveAsync(companyId, number, approverOne.Id, day)).IsSuccess.Should().BeTrue();
        (await transfers.CompleteAsync(companyId, number, day)).Error.Code.Should().Be("Hse.TransferNotApproved");
        (await transfers.ApproveAsync(companyId, number, approverOne.Id, day)).Error.Code.Should().Be("Hse.AlreadyApproved");
        (await transfers.ApproveAsync(companyId, number, approverTwo.Id, day)).IsSuccess.Should().BeTrue();

        // New arrears before completion stop it; cleared, the transfer completes on both registers.
        DateOnly effective = new(2026, 6, 1);
        (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = holder.Id, DocumentNumber = $"TRF-SUN-{companyId}", DocumentDate = new DateOnly(2026, 5, 20), Currency = Scenario.Usd, NetAmount = 100m, SalesAccountCode = "4400" })).IsSuccess.Should().BeTrue();
        (await transfers.CompleteAsync(companyId, number, effective)).Error.Code.Should().Be("Hse.LiveCheckFailed");
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = holder.Id, DocumentNumber = $"TRF-R2-{companyId}", ReceiptDate = new DateOnly(2026, 5, 25), Currency = Scenario.Usd, Amount = 100m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        (await transfers.CompleteAsync(companyId, number, effective)).IsSuccess.Should().BeTrue();

        HousingStand after = await db.HousingStands.AsNoTracking().Include(s => s.Ownership).SingleAsync(s => s.Id == stand.Id);
        (after.CurrentHolderCustomerId, after.Ownership[^1].ChangeType, after.Ownership[^1].Consideration).Should().Be((buyer.Id, OwnershipChangeType.Cession, 9000m));
        after.VerifyOwnershipChain().Should().BeTrue();
    }

    [SkippableFact]
    public async Task A_balance_assumed_by_the_transferee_with_approval_moves_to_them_on_completion()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Role officer = new($"Transfer officer {companyId}");
        officer.Grant("Housing.OwnershipChange", OperationFlags.View | OperationFlags.Create | OperationFlags.Edit);
        Role director = new($"Transfer approver {companyId}");
        director.Grant("Housing.OwnershipApprove", OperationFlags.View | OperationFlags.Approve);
        db.Roles.AddRange(officer, director);
        User verifier = new($"tra-verifier-{companyId}", "Verifying Officer"), approverOne = new($"tra-approver1-{companyId}", "First Approver"), approverTwo = new($"tra-approver2-{companyId}", "Second Approver");
        db.Users.AddRange(verifier, approverOne, approverTwo);
        Customer holder = new(companyId, "TRA-H", "Outgoing Holder", Scenario.Usd), buyer = new(companyId, "TRA-B", "Incoming Holder", Scenario.Usd);
        db.Customers.AddRange(holder, buyer);
        LandParcel parcel = new(companyId, $"LP-TRA-{companyId}", "Assumption parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        if (!await db.Accounts.AnyAsync(a => a.CompanyId == companyId && a.AccountCode == "2900"))
        {
            db.Accounts.Add(new LedgerAccount(companyId, "2900", "Transfer clearing", AccountType.Liability));
        }

        await db.SaveChangesAsync();
        verifier.AssignRole(officer.Id);
        approverOne.AssignRole(director.Id);
        approverTwo.AssignRole(director.Id);
        HousingStand stand = new(companyId, parcel.Id, null, "TRA-1", 300m, 2000m);
        db.HousingStands.Add(stand);
        Property property = new(companyId, "TRA-1", "Ward 3", holder.Id);
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        (await housing.SellStandAsync(companyId, stand.Id, holder.Id, holder.Name, 6000m, new DateOnly(2026, 3, 1), "4100", "1460", "5100", $"TRA-INV-{companyId}")).IsSuccess.Should().BeTrue();

        ITransferApplicationService transfers = scope.ServiceProvider.GetRequiredService<ITransferApplicationService>();
        DateOnly day = new(2026, 5, 10);
        string number = (await transfers.LodgeAsync(new TransferLodgement { CompanyId = companyId, StandId = stand.Id, TransferType = TransferType.Cession, ToCustomerId = buyer.Id, Consideration = 9000m, Date = day })).Value;
        foreach (TransferRequirement requirement in new[] { TransferRequirement.Identity, TransferRequirement.SpousalConsent, TransferRequirement.OriginalDocuments, TransferRequirement.TaxWithholding })
        {
            (await transfers.VerifyAsync(companyId, number, requirement, $"{requirement} seen", verifier.Id, day)).IsSuccess.Should().BeTrue();
        }

        string fee = $"TRA-FEE-{companyId}";
        (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = fee, ReceiptDate = day, Currency = Scenario.Usd, Amount = 150m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        (await transfers.VerifyAsync(companyId, number, TransferRequirement.FeesReceipted, fee, verifier.Id, day)).IsSuccess.Should().BeTrue();
        db.Set<ClearanceCertificate>().Add(new ClearanceCertificate(companyId, $"CC-TRA-{companyId}", property.Id, new DateOnly(2026, 5, 1), new DateOnly(2026, 7, 31), 0m, 1));
        await db.SaveChangesAsync();

        // The transferor owes 6,000: instead of settling, the transferee assumes it — approved by an approving officer who did not verify the application (SOD-HSE-05).
        (await transfers.SubmitAsync(companyId, number, verifier.Id, day)).Error.Message.Should().Contain("owes 6,000.00");
        (await transfers.AssumeBalanceAsync(companyId, number, verifier.Id, "2900", day)).Error.Code.Should().Be("Hse.NotAuthorised");
        (await transfers.AssumeBalanceAsync(companyId, number, approverOne.Id, "NOPE", day)).Error.Code.Should().Be("Hse.ClearingAccount");
        (await transfers.AssumeBalanceAsync(companyId, number, approverOne.Id, "2900", day)).Value.Should().Be(6000m);
        (await transfers.ChecklistAsync(companyId, number, day)).Value.Single(l => l.Requirement == TransferRequirement.HousingAccountClear).Should().Match<TransferChecklistLine>(l => l.Satisfied && l.Detail!.Contains("assumed"));
        (await transfers.SubmitAsync(companyId, number, verifier.Id, day)).IsSuccess.Should().BeTrue();
        (await transfers.ApproveAsync(companyId, number, approverOne.Id, day)).IsSuccess.Should().BeTrue();
        (await transfers.ApproveAsync(companyId, number, approverTwo.Id, day)).IsSuccess.Should().BeTrue();
        DateOnly effective = new(2026, 6, 1);
        (await transfers.CompleteAsync(companyId, number, effective)).IsSuccess.Should().BeTrue();

        // The balance moved: the transferor owes nothing, the transferee owes 6,000 on the assumption invoice, the clearing account nets to nil, the stand has its new holder.
        (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == holder.Id && i.Ledger == TradingLedger.AccountsReceivable).SumAsync(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase)).Should().Be(0m);
        OpenItem assumed = await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.TraderId == buyer.Id && i.DocumentNumber == $"{number}-ASSUMED-USD");
        (assumed.OutstandingBase, assumed.Sign).Should().Be((6000m, LedgerSign.Debit));
        (await db.Set<JournalLine>().AsNoTracking().Where(l => l.CompanyId == companyId && l.AccountCode == "2900" && l.PostingDate == effective).SumAsync(l => l.BaseDebit - l.BaseCredit)).Should().Be(0m, "the clearing account nets to nil");
        (await db.HousingStands.AsNoTracking().SingleAsync(s => s.Id == stand.Id)).CurrentHolderCustomerId.Should().Be(buyer.Id);
        PropertyOwner owner = await db.Set<PropertyOwner>().AsNoTracking().SingleAsync(o => o.PropertyId == property.Id && o.OwnedTo == null);
        (owner.CustomerId, owner.Reference, owner.OwnedFrom).Should().Be((buyer.Id, certificate, effective));
        (await db.Set<TransferApplication>().AsNoTracking().SingleAsync(a => a.CompanyId == companyId && a.ApplicationNumber == number)).Status.Should().Be(TransferStatus.Completed);

        // A change of name has a shorter checklist (no clearance or account checks) and can be rejected.
        string rename = (await transfers.LodgeAsync(new TransferLodgement { CompanyId = companyId, StandId = stand.Id, TransferType = TransferType.ChangeOfName, ToCustomerId = buyer.Id, Date = effective })).Value;
        (await transfers.ChecklistAsync(companyId, rename, effective)).Value.Select(l => l.Requirement).Should().Equal(TransferRequirement.Identity, TransferRequirement.FeesReceipted);
        (await transfers.RejectAsync(companyId, rename, "Marriage certificate not certified", effective)).IsSuccess.Should().BeTrue();
        (await transfers.RejectAsync(companyId, rename, "Again", effective)).Error.Code.Should().Be("Hse.TransferClosed");
    }
}
