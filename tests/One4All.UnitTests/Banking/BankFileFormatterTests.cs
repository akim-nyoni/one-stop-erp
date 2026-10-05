using FluentAssertions;
using One4All.Application.Banking;
using Xunit;

namespace One4All.UnitTests.Banking;

/// <summary>Covers rendering bank payment files from templates: delimited and fixed-width layouts, cents, the hash total.</summary>
public sealed class BankFileFormatterTests
{
    private static readonly BankFileRow[] Rows =
    [
        new(1, "SAL-001", "Ada Lovelace", "First Bank", "250655", "62001234567", "USD", 1234.5m),
        new(2, "SAL-002", "Bartholomew Longname-Smith", "First Bank", "250655", "620-0987-6543", "USD", 99.99m),
    ];

    [Fact]
    public void Delimited_layout_with_header_and_trailer()
    {
        BankFileHeader file = new(new DateOnly(2026, 9, 25), "ACC-1100", "USD", 2, 1334.49m, BankFileFormatter.HashTotal(Rows.Select(r => r.Account)));

        string text = BankFileFormatter.Render("H|{Date}|{Originator}|{Count}|{Total}", "D|{Seq}|{Name}|{Account}|{Amount}", "T|{Count}|{Total}|{Hash}", file, Rows, "\n");

        text.Should().Be("H|20260925|ACC-1100|2|1334.49\nD|1|Ada Lovelace|62001234567|1234.50\nD|2|Bartholomew Longname-Smith|620-0987-6543|99.99\nT|2|1334.49|124011111110\n");
    }

    [Fact]
    public void Fixed_width_layout_pads_cuts_and_writes_cents()
    {
        BankFileHeader file = new(new DateOnly(2026, 9, 25), "ACC", "USD", 2, 1334.49m, 0);

        string text = BankFileFormatter.Render(null, "{Account,-12}{Name,-10}{Amount,010:cents}{Date:ddMMyy}", null, file, Rows, "\n");

        text.Should().Be("62001234567 Ada Lovela0000123450250926\n620-0987-654Bartholome0000009999250926\n");
    }

    [Fact]
    public void Hash_total_uses_the_last_eleven_digits_of_each_account()
    {
        BankFileFormatter.HashTotal(["12345678901234", "1-2", "no digits"]).Should().Be(45678901234 + 12);
    }

    [Fact]
    public void Unknown_placeholders_are_reported()
    {
        BankFileFormatter.UnknownFields("{Name}{Amount:cents}{Iban}{Swift,-8}", BankFileFormatter.DetailFields).Should().Equal("Iban", "Swift");
        BankFileFormatter.UnknownFields("{Count}{Name}", BankFileFormatter.FileFields).Should().Equal("Name");
    }
}
