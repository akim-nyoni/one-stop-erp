using FluentAssertions;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;
using Xunit;

namespace One4All.UnitTests.Reporting;

public class BurstingTests
{
    [Fact]
    public void Burst_splits_one_run_into_a_document_per_key()
    {
        ReportDefinition def = new() { Code = "STMT", Name = "Statement" };
        def.Bands.Add(new ReportBand
        {
            Type = BandType.Detail,
            Height = 12,
            Elements = [new ReportElement { Kind = ElementKind.Field, FieldName = "Line", Width = 200 }],
        });

        ReportInput input = new()
        {
            Definition = def,
            Rows =
            [
                new Dictionary<string, object?> { ["Customer"] = "C1", ["Line"] = "Invoice 1" },
                new Dictionary<string, object?> { ["Customer"] = "C1", ["Line"] = "Invoice 2" },
                new Dictionary<string, object?> { ["Customer"] = "C2", ["Line"] = "Invoice 3" },
            ],
        };

        List<(string Key, RenderModel Model)> documents = ReportBurster.Burst(new ReportEngine(), input, "Customer").ToList();

        documents.Should().HaveCount(2);
        documents.Single(d => d.Key == "C1").Model.Body.Should().HaveCount(2);
        documents.Single(d => d.Key == "C2").Model.Body.Should().HaveCount(1);
    }
}
