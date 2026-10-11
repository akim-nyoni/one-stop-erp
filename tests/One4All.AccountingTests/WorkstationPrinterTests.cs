using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.System;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers printers per workstation: a workstation's own printer wins over the default for every workstation, removal falls back to it.</summary>
[Collection(AccountingCollection.Name)]
public sealed class WorkstationPrinterTests
{
    private readonly AccountingFixture _fixture;

    public WorkstationPrinterTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_workstation_uses_its_own_printer_else_the_default()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IWorkstationPrinterService printers = scope.ServiceProvider.GetRequiredService<IWorkstationPrinterService>();

        (await printers.ResolveAsync(companyId, "till-1", PrintPurpose.Receipts)).Should().BeNull();
        (await printers.SetAsync(companyId, new PrinterSetting("*", PrintPurpose.Receipts, "Office Laser", null, 1))).IsSuccess.Should().BeTrue();
        (await printers.SetAsync(companyId, new PrinterSetting("till-1", PrintPurpose.Receipts, "EPSON TM-T20", "Auto", 2))).IsSuccess.Should().BeTrue();
        (await printers.SetAsync(companyId, new PrinterSetting("till-1", PrintPurpose.Receipts, "EPSON TM-T88", "Auto", 2))).IsSuccess.Should().BeTrue("replaces the setting");
        (await printers.SetAsync(companyId, new PrinterSetting("till-1", PrintPurpose.Cheques, "Cheque printer", null, 0))).Error.Code.Should().Be("Sys.Printer");

        (await printers.ResolveAsync(companyId, "TILL-1", PrintPurpose.Receipts)).Should().Be(new PrinterSetting("TILL-1", PrintPurpose.Receipts, "EPSON TM-T88", "Auto", 2));
        (await printers.ResolveAsync(companyId, "till-2", PrintPurpose.Receipts))!.PrinterName.Should().Be("Office Laser");
        (await printers.ListAsync(companyId)).Should().HaveCount(2);

        (await printers.RemoveAsync(companyId, "till-1", PrintPurpose.Receipts)).IsSuccess.Should().BeTrue();
        (await printers.ResolveAsync(companyId, "till-1", PrintPurpose.Receipts))!.PrinterName.Should().Be("Office Laser");
        (await printers.RemoveAsync(companyId, "till-1", PrintPurpose.Receipts)).Error.Code.Should().Be("Sys.PrinterNotFound");
    }
}
