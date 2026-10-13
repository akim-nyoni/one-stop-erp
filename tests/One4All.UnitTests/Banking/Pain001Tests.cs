using System.Xml.Linq;
using FluentAssertions;
using One4All.Application.Banking;
using Xunit;

namespace One4All.UnitTests.Banking;

/// <summary>Covers the ISO 20022 pain.001 writer: group header totals, IBAN and other accounts, agents and remittance text.</summary>
public sealed class Pain001Tests
{
    [Fact]
    public void A_credit_transfer_file_carries_the_totals_accounts_and_references()
    {
        string xml = Pain001.Write("MSG-1", new DateTimeOffset(2026, 3, 31, 9, 30, 0, TimeSpan.Zero), new DateOnly(2026, 3, 31),
            new Pain001Party("One4All Council", "GB29 NWBK 6016 1331 9268 19", "NWBKGB2L"),
            [
                new Pain001Payment("PAY-1", new Pain001Party("Fuel Co", "DE89370400440532013000", "COBADEFF"), "EUR", 500m, "Invoice 77"),
                new Pain001Payment("PAY-2", new Pain001Party("Local Stationer", "0012345678"), "USD", 150.255m),
            ]);

        XNamespace ns = Pain001.Namespace;
        XElement root = XDocument.Parse(xml).Root!;
        root.Name.Should().Be(ns + "Document");
        XElement header = root.Element(ns + "CstmrCdtTrfInitn")!.Element(ns + "GrpHdr")!;
        header.Element(ns + "NbOfTxs")!.Value.Should().Be("2");
        header.Element(ns + "CtrlSum")!.Value.Should().Be("650.26");
        XElement info = root.Element(ns + "CstmrCdtTrfInitn")!.Element(ns + "PmtInf")!;
        info.Element(ns + "ReqdExctnDt")!.Value.Should().Be("2026-03-31");
        info.Element(ns + "DbtrAcct")!.Descendants(ns + "IBAN").Single().Value.Should().Be("GB29NWBK60161331926819");
        info.Element(ns + "DbtrAgt")!.Descendants(ns + "BIC").Single().Value.Should().Be("NWBKGB2L");

        List<XElement> transfers = [.. info.Elements(ns + "CdtTrfTxInf")];
        transfers[0].Descendants(ns + "EndToEndId").Single().Value.Should().Be("PAY-1");
        transfers[0].Element(ns + "Amt")!.Element(ns + "InstdAmt")!.Attribute("Ccy")!.Value.Should().Be("EUR");
        transfers[0].Element(ns + "CdtrAgt")!.Descendants(ns + "BIC").Single().Value.Should().Be("COBADEFF");
        transfers[0].Descendants(ns + "Ustrd").Single().Value.Should().Be("Invoice 77");
        transfers[1].Element(ns + "CdtrAgt").Should().BeNull("no BIC is known for it");
        transfers[1].Element(ns + "CdtrAcct")!.Descendants(ns + "Othr").Single().Value.Should().Be("0012345678");
        transfers[1].Element(ns + "Amt")!.Element(ns + "InstdAmt")!.Value.Should().Be("150.26");
        Pain001.IsIban("ZW12").Should().BeFalse();
    }
}
