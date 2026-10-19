using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.System;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>A workstation can have several receipt printers, Windows or network; receipts go to each, and none set up leaves the till's own device.</summary>
[Collection(AccountingCollection.Name)]
public sealed class ReceiptPrintersTests
{
    private readonly AccountingFixture _fixture;

    public ReceiptPrintersTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Receipts_go_to_every_receipt_printer_of_the_workstation()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        long companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        IWorkstationPrinterService printers = scope.ServiceProvider.GetRequiredService<IWorkstationPrinterService>();
        IReceiptPrintService printing = scope.ServiceProvider.GetRequiredService<IReceiptPrintService>();
        (await printing.PrintAsync(companyId, "TILL-PC", null, "Receipt", openDrawer: false)).Should().BeNull("no receipt printer is set up: the till's own device prints");

        // Two receipt printers on the till's computer: a network one that answers nobody, and another network one.
        PrinterSetting counter = new("till-pc", PrintPurpose.Receipts, "Counter", null, 1, PrinterConnection.Network, "127.0.0.1", 9);
        (await printers.SetAsync(companyId, counter)).IsSuccess.Should().BeTrue();
        (await printers.AddAsync(companyId, counter with { PrinterName = "Office copy", Port = 10 })).IsSuccess.Should().BeTrue();
        (await printers.AddAsync(companyId, counter with { PrinterName = "No address", Host = null })).Error.Code.Should().Be("Sys.PrinterAddress");
        (await printers.ResolveAllAsync(companyId, "TILL-PC", PrintPurpose.Receipts)).Select(p => p.PrinterName).Should().Equal("Counter", "Office copy");
        (await printers.ResolveAllAsync(companyId, "OTHER-PC", PrintPurpose.Receipts)).Should().BeEmpty();

        ReceiptPrintOutcome outcome = (await printing.PrintAsync(companyId, "TILL-PC", null, "Receipt", openDrawer: false))!;
        (outcome.Printed, outcome.Failures.Count).Should().Be((0, 2), "both printers were tried; neither is there");

        // Set replaces the purpose's printers with one again; Remove one takes a printer away by name.
        (await printers.RemovePrinterAsync(companyId, "TILL-PC", PrintPurpose.Receipts, "Office copy")).IsSuccess.Should().BeTrue();
        (await printers.ResolveAllAsync(companyId, "TILL-PC", PrintPurpose.Receipts)).Should().ContainSingle();
        (await printers.AddAsync(companyId, counter with { PrinterName = "Second" })).IsSuccess.Should().BeTrue();
        (await printers.SetAsync(companyId, counter with { PrinterName = "Only" })).IsSuccess.Should().BeTrue();
        (await printers.ResolveAllAsync(companyId, "TILL-PC", PrintPurpose.Receipts)).Select(p => p.PrinterName).Should().Equal("Only");
    }
}
