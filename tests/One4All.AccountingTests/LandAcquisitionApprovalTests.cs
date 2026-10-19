using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Housing;
using One4All.Core.Security;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Fourteenth audit #17: under a land acquisition workflow an acquisition is submitted at its value and completes only once approved.</summary>
[Collection(AccountingCollection.Name)]
public sealed class LandAcquisitionApprovalTests
{
    private readonly AccountingFixture _fixture;

    public LandAcquisitionApprovalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task An_acquisition_completes_only_once_approved()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Role landRole = new($"Land approvals {companyId}");
        landRole.Grant("Housing.Acquisition", OperationFlags.View | OperationFlags.Create | OperationFlags.Edit);
        db.Roles.Add(landRole);
        User officer = new($"laa-officer-{companyId}", "Land Officer");
        User council = new($"laa-council-{companyId}", "Council");
        db.Users.AddRange(officer, council);
        await db.SaveChangesAsync();
        officer.AssignRole(landRole.Id);
        await db.SaveChangesAsync();

        ILandAcquisitionService acquisitions = scope.ServiceProvider.GetRequiredService<ILandAcquisitionService>();
        string number = (await acquisitions.OpenAsync(new AcquisitionCase
        {
            CompanyId = companyId, ParcelNumber = $"LP-LAA-{companyId}", Description = "Farm 4", AreaSquareMetres = 5000m, Method = AcquisitionMethod.Purchase,
            Counterparty = "Seller", ResolutionReference = "Resolution 7/2026", Date = new DateOnly(2026, 2, 1),
        })).Value;
        (await acquisitions.AddCostAsync(companyId, number, new AcquisitionCostInput
        {
            Component = AcquisitionCostComponent.PurchasePrice, Amount = 50_000m, Date = new DateOnly(2026, 3, 1), Reference = "Price", WipAccountCode = "1450", FundingAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();
        (await acquisitions.RecordValuationAsync(companyId, number, "Valuer", new DateOnly(2026, 2, 20), 52_000m)).IsSuccess.Should().BeTrue();
        foreach (DueDiligenceItem item in Enum.GetValues<DueDiligenceItem>())
        {
            (await acquisitions.SignOffAsync(companyId, number, item, "Nothing adverse", true, officer.Id, new DateOnly(2026, 3, 2))).IsSuccess.Should().BeTrue();
        }

        (await acquisitions.SubmitForApprovalAsync(companyId, number, officer.Id)).Error.Code.Should().Be("Hse.NoAcquisitionWorkflow");
        WorkflowDefinition definition = new(companyId, WorkflowDocumentTypes.LandAcquisition, "Land acquisitions");
        definition.AddStage(1, "Council").AddEligibleUser(council.Id);
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        (await acquisitions.CompleteAsync(companyId, number, new DateOnly(2026, 4, 1))).Error.Code.Should().Be("Hse.AcquisitionApproval");
        (await acquisitions.SubmitForApprovalAsync(companyId, number, officer.Id)).Value.Should().Be(50_000m);
        (await acquisitions.SubmitForApprovalAsync(companyId, number, officer.Id)).Error.Code.Should().Be("Hse.AcquisitionSubmitted");
        (await acquisitions.CompleteAsync(companyId, number, new DateOnly(2026, 4, 1))).Error.Message.Should().Contain("waiting for approval");

        long instance = await db.WorkflowInstances.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentReference == number).Select(i => i.Id).SingleAsync();
        (await scope.ServiceProvider.GetRequiredService<IWorkflowService>().ActAsync(instance, council.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        (await acquisitions.CompleteAsync(companyId, number, new DateOnly(2026, 4, 1))).IsSuccess.Should().BeTrue();
    }
}
