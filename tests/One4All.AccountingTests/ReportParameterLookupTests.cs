using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers report parameters with lists (brief §13.1): a list-of-values query runs for the company only, a cascading list waits for its parent
/// and then filters by it, a query that does not keep to the company is refused, and a multi-select parameter reaches the report's query as
/// an array.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportParameterLookupTests
{
    private readonly AccountingFixture _fixture;

    public ReportParameterLookupTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Lists_cascade_keep_to_the_company_and_multi_select_reaches_the_query()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long company = await Scenario.SeedAsync(db);
        long other = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        db.Customers.AddRange(
            new Customer(company, $"N{tag}1", "North One", Scenario.Usd), new Customer(company, $"N{tag}2", "North Two", Scenario.Usd),
            new Customer(company, $"S{tag}1", "South One", Scenario.Usd), new Customer(other, $"N{tag}9", "Elsewhere", Scenario.Usd));
        await db.SaveChangesAsync();

        string code = $"LOV{tag}";
        ReportDefinition definition = new()
        {
            Code = code, Name = "Customers by prefix", Module = "AR",
            Parameters =
            [
                new ReportParameter { Name = "Prefix", LookupQuery = "SELECT DISTINCT left(code, 1) AS value FROM ar.customer WHERE company_id = @CompanyId ORDER BY 1" },
                new ReportParameter
                {
                    Name = "Codes", MultiSelect = true, DependsOn = ["Prefix"],
                    LookupQuery = "SELECT code, name FROM ar.customer WHERE company_id = @CompanyId AND code LIKE @Prefix || '%' ORDER BY code",
                },
                new ReportParameter { Name = "Leaky", LookupQuery = "SELECT code FROM ar.customer" },
            ],
            DataSources = [new ReportDataSource { Kind = DataSourceKind.Sql, Command = "SELECT code, name FROM ar.customer WHERE company_id = @CompanyId AND code = ANY(@Codes) ORDER BY code" }],
            Bands = [new ReportBand { Type = BandType.Detail, Height = 16, Elements = [new ReportElement { Kind = ElementKind.Field, Name = "c", FieldName = "code", Width = 100, Height = 14 }] }],
        };
        // A lookup that does not bind the company is refused when the report is saved, with the same check the lookup itself applies.
        IReportDefinitionService definitions = scope.ServiceProvider.GetRequiredService<IReportDefinitionService>();
        (await definitions.SaveAsync(company, code, definition.Name, "AR", ReportDefinitionSerializer.Serialize(definition), null, null)).Error.Message.Should().Contain("Leaky");
        definition.Parameters.RemoveAll(p => p.Name == "Leaky");
        (await definitions.SaveAsync(company, code, definition.Name, "AR", ReportDefinitionSerializer.Serialize(definition), null, null)).IsSuccess.Should().BeTrue();

        IReportParameterLookupService lookups = scope.ServiceProvider.GetRequiredService<IReportParameterLookupService>();
        (await lookups.OptionsAsync(company, code, "Prefix", new Dictionary<string, string>())).Value.Select(o => o.Value).Should().Contain(["N", "S"]);

        (await lookups.OptionsAsync(company, code, "Codes", new Dictionary<string, string>())).Error.Code.Should().Be("Report.ParameterWaiting");
        IReadOnlyList<ParameterOption> north = (await lookups.OptionsAsync(company, code, "Codes", new Dictionary<string, string> { ["Prefix"] = $"N{tag}" })).Value;
        north.Select(o => (o.Value, o.Label)).Should().Equal(($"N{tag}1", "North One"), ($"N{tag}2", "North Two"));

        (await lookups.OptionsAsync(company, code, "Nope", new Dictionary<string, string>())).Error.Code.Should().Be("Report.NoParameter");

        RenderedDocument csv = (await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(company, code,
            new Dictionary<string, object?> { ["Codes"] = $"N{tag}2, S{tag}1" }, ReportFormat.Csv)).Value;
        string text = Encoding.UTF8.GetString(csv.Content);
        text.Should().Contain($"N{tag}2").And.Contain($"S{tag}1").And.NotContain($"N{tag}1");
    }
}
