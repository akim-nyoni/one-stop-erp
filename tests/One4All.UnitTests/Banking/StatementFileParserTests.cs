using FluentAssertions;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Banking;
using Xunit;

namespace One4All.UnitTests.Banking;

/// <summary>Covers the MT940 and camt.053 statement parsers.</summary>
public sealed class StatementFileParserTests
{
    private const string Mt940 = """
        {1:F01BANKBEBBAXXX0000000000}{4:
        :20:STMT0326
        :25:123456789
        :28C:00012/001
        :60F:C260301USD1000,00
        :61:2603050305C250,00NTRFINV-1001//BR1
        :86:Payment from Acme Ltd
        invoice INV-1001
        :61:260306D80,5NCHGNONREF
        :86:Bank charges
        :61:260307RD15,00NTRFREV-9
        :86:Reversed charge
        :62F:C260331USD1184,50
        -}
        """;

    [Fact]
    public void Mt940_lines_carry_date_sign_amount_reference_and_narrative()
    {
        BankCsvParseResult result = Mt940Parser.Parse(Mt940);

        result.Errors.Should().BeEmpty();
        result.Lines.Should().Equal(
            new StatementLineImport(new DateOnly(2026, 3, 5), "Payment from Acme Ltd invoice INV-1001", 250m, "INV-1001"),
            new StatementLineImport(new DateOnly(2026, 3, 6), "Bank charges", -80.5m, null),
            new StatementLineImport(new DateOnly(2026, 3, 7), "Reversed charge", 15m, "REV-9"));
    }

    [Fact]
    public void An_unreadable_mt940_line_is_reported()
    {
        BankCsvParseResult result = Mt940Parser.Parse(":20:X\n:61:not-a-line\n:86:?\n");

        result.Lines.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.LineNumber.Should().Be(2);
    }

    private const string Camt053 = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.08">
          <BkToCstmrStmt>
            <Stmt>
              <Ntry>
                <Amt Ccy="USD">250.00</Amt>
                <CdtDbtInd>CRDT</CdtDbtInd>
                <BookgDt><Dt>2026-03-05</Dt></BookgDt>
                <AcctSvcrRef>BR1</AcctSvcrRef>
                <NtryDtls><TxDtls><Refs><EndToEndId>INV-1001</EndToEndId></Refs><RmtInf><Ustrd>Acme Ltd INV-1001</Ustrd></RmtInf></TxDtls></NtryDtls>
              </Ntry>
              <Ntry>
                <Amt Ccy="USD">80.50</Amt>
                <CdtDbtInd>DBIT</CdtDbtInd>
                <ValDt><Dt>2026-03-06</Dt></ValDt>
                <AcctSvcrRef>CHG-0306</AcctSvcrRef>
                <AddtlNtryInf>Monthly service fee</AddtlNtryInf>
              </Ntry>
            </Stmt>
          </BkToCstmrStmt>
        </Document>
        """;

    [Fact]
    public void Camt053_entries_carry_date_sign_amount_reference_and_description()
    {
        BankCsvParseResult result = Camt053Parser.Parse(Camt053);

        result.Errors.Should().BeEmpty();
        result.Lines.Should().Equal(
            new StatementLineImport(new DateOnly(2026, 3, 5), "Acme Ltd INV-1001", 250m, "INV-1001"),
            new StatementLineImport(new DateOnly(2026, 3, 6), "Monthly service fee", -80.5m, "CHG-0306"));
    }

    [Fact]
    public void Invalid_xml_is_reported()
    {
        Camt053Parser.Parse("<Document><Ntry>").Errors.Should().ContainSingle();
    }
}
