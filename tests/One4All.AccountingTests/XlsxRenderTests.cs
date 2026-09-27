using System.IO.Compression;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class XlsxRenderTests
{
    private readonly AccountingFixture _fixture;

    public XlsxRenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Report_runner_renders_a_report_to_a_valid_xlsx_workbook()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            // Chart of Accounts has rows straight from the seeded chart, no parameters needed.
            var rendered = await runner.RenderAsync(companyId, "GL-COA", new Dictionary<string, object?>(), ReportFormat.Xlsx);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.ToString() : null);

            RenderedDocument doc = rendered.Value;
            doc.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            doc.FileName.Should().EndWith(".xlsx");

            using MemoryStream stream = new(doc.Content);
            using ZipArchive zip = new(stream, ZipArchiveMode.Read);
            zip.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
            zip.GetEntry("xl/workbook.xml").Should().NotBeNull();
        }
    }
}
