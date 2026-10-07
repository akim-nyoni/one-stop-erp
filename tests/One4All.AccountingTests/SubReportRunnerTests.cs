using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Reporting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers sub-reports through the report runner: a company report lists the sales reps and, inside each row, runs the AR46 commission
/// statement for that rep only (the link carries the rep's code, the period comes from the parent's parameters); its rich-text heading
/// is filled from the row.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SubReportRunnerTests
{
    private readonly AccountingFixture _fixture;

    public SubReportRunnerTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_report_runs_a_linked_sub_report_per_row()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();

        SalesRep north = new(companyId, "SR-NORTH", "North Rep", CommissionBasis.Invoiced, 10m), south = new(companyId, "SR-SOUTH", "South Rep", CommissionBasis.Invoiced, 10m);
        db.AddRange(north, south);
        await db.SaveChangesAsync();
        Customer alpha = new(companyId, "CUST-ALPHA", "Alpha", Scenario.Usd) { SalesRepId = north.Id }, beta = new(companyId, "CUST-BETA", "Beta", Scenario.Usd) { SalesRepId = south.Id };
        db.Customers.AddRange(alpha, beta);
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        foreach ((Customer customer, string number, decimal amount) in new[] { (alpha, "SR-1", 400m), (beta, "SR-2", 900m) })
        {
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 5, 4), Currency = Scenario.Usd, NetAmount = amount, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        // The parent lists the commission rows; each row's sub-report is the statement for that row's rep.
        ReportDefinition parent = new()
        {
            Code = "REP-PACK", Name = "Rep Pack",
            DataSources = [new ReportDataSource { Name = "Main", Kind = DataSourceKind.InMemory, Command = "StmtCommission" }],
            Bands =
            [
                new ReportBand
                {
                    Type = BandType.Detail, Height = 20,
                    Elements =
                    [
                        new ReportElement { Kind = ElementKind.RichText, Name = "head", Text = "Rep **{Rep}** — {Customer}", Width = 300, Height = 14 },
                        new ReportElement
                        {
                            Kind = ElementKind.SubReport, Name = "statement", SubReportCode = "AR46", X = 10, Y = 16, Width = 400, Height = 20,
                            SubReportLinks = [new SubReportLink { ChildParameter = "RepCode", ParentField = "Rep" }],
                        },
                    ],
                },
            ],
        };
        string code = $"REP-PACK-{Guid.NewGuid():N}"[..20];
        db.Add(new ReportDefinitionRecord(code, "Rep Pack", ReportDefinitionSerializer.Serialize(parent), ReportScope.Company, companyId));
        await db.SaveChangesAsync();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 5, 1), ["ToDate"] = new DateOnly(2026, 5, 31) }, ReportFormat.Html);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.ToString() : null);
        string html = Encoding.UTF8.GetString(rendered.Value.Content);
        html.Should().Contain("Rep <strong>SR-NORTH</strong> — CUST-ALPHA").And.Contain("Rep <strong>SR-SOUTH</strong> — CUST-BETA").And.NotContain("not available");

        // Each detail row is followed by its own rep's statement only.
        int northRow = html.IndexOf("Rep <strong>SR-NORTH</strong> — CUST-ALPHA", StringComparison.Ordinal);
        int nextRow = html.IndexOf("data-band=\"Detail\"", northRow, StringComparison.Ordinal);
        string northBand = html[northRow..(nextRow < 0 ? html.Length : nextRow)];
        northBand.Should().Contain("CUST-ALPHA").And.Contain("40").And.NotContain("CUST-BETA");
        Encoding.ASCII.GetString((await runner.RenderAsync(companyId, code, new Dictionary<string, object?>(), ReportFormat.Pdf)).Value.Content, 0, 5).Should().Be("%PDF-");
    }
}
