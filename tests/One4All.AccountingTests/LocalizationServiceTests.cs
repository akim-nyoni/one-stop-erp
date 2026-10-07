using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.System;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using One4All.Reporting.Engine;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers a company's language and terminology: an unknown locale refused, terms set and removed, the catalog in the company's language with
/// its terms, and reports run in the company's locale with its terms on the labels.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LocalizationServiceTests
{
    private readonly AccountingFixture _fixture;

    public LocalizationServiceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_company_speaks_its_own_language_and_words()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        ILocalizationService localization = scope.ServiceProvider.GetRequiredService<ILocalizationService>();

        (await localization.SetLocaleAsync(companyId, "xx-NOWHERE")).Error.Code.Should().Be("Locale.Unknown");
        (await localization.SetLocaleAsync(companyId, "pt-MZ")).IsSuccess.Should().BeTrue();
        (await localization.SetTermAsync(companyId, "Journal", "Diary")).IsSuccess.Should().BeTrue();
        (await localization.SetTermAsync(companyId, "Stand", "Erf")).IsSuccess.Should().BeTrue();
        (await localization.SetTermAsync(companyId, "Stand", "")).IsSuccess.Should().BeTrue();
        (await localization.TermsAsync(companyId)).Should().Equal(new Dictionary<string, string> { ["Journal"] = "Diary" });

        Application.Localization.TextCatalog catalog = await localization.CatalogAsync(companyId);
        catalog.Culture.Name.Should().Be("pt-MZ");
        catalog["Exit"].Should().Be("Sair");
        catalog["Journal entries"].Should().Be("Diary entries", "the company's terms apply to untranslated text too");

        // Reports: labels carry the company's word, dates follow the locale.
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        AuditedReport report = (await scope.ServiceProvider.GetRequiredService<IAuditedReportRunner>().RunAsync(companyId, SystemReports.JournalDaybookCode,
            new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 12, 31) }, ReportFormat.Layout, ReportRunPurpose.View, null)).Value;
        RenderModel model = ReportLayout.Deserialize(report.Document.Content);
        model.PageHeader!.Cells.Select(c => c.Text).Should().Contain("Diary").And.NotContain("Journal");
    }
}
