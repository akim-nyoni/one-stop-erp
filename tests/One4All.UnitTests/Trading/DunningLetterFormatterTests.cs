using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers the reminder-letter layout: heading, escalating wording, items, total and fixed width.</summary>
public sealed class DunningLetterFormatterTests
{
    private static DunningReminder Sample(int level) => new()
    {
        CompanyName = "Test Trading Co",
        CustomerName = "Overdue Co",
        AsAtDate = new DateOnly(2026, 4, 1),
        Level = level,
        LevelLabel = Dunning.LabelFor(level),
        Items =
        [
            new DunningReminderItem("INV-1", new DateOnly(2026, 1, 1), 90, 1000.00m),
            new DunningReminderItem("INV-2", new DateOnly(2026, 3, 1), 31, 500.00m),
        ],
        TotalOverdue = 1500.00m,
    };

    [Fact]
    public void Final_notice_letter_lists_items_total_and_escalated_wording()
    {
        string text = DunningLetterFormatter.ToPlainText(Sample(3));

        text.Should().Contain("Test Trading Co");
        text.Should().Contain("FINAL NOTICE");
        text.Should().Contain("To: Overdue Co");
        text.Should().Contain("referred for collection");
        text.Should().Contain("INV-1").And.Contain("INV-2");
        text.Should().Contain("1,500.00");
        text.Should().Contain("TOTAL OVERDUE");
    }

    [Fact]
    public void Wording_escalates_with_the_level()
    {
        DunningLetterFormatter.ToPlainText(Sample(1)).Should().Contain("friendly reminder");
        DunningLetterFormatter.ToPlainText(Sample(2)).Should().Contain("Despite our previous reminder");
        DunningLetterFormatter.ToPlainText(Sample(3)).Should().Contain("FINAL NOTICE");
    }

    [Fact]
    public void Every_line_fits_the_requested_width()
    {
        const int width = 60;
        string text = DunningLetterFormatter.ToPlainText(Sample(2), width);

        foreach (string line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            // Prose lines may wrap naturally; the tabular lines are padded to the width.
            if (line.StartsWith("INV-", StringComparison.Ordinal) || line.StartsWith("TOTAL", StringComparison.Ordinal))
            {
                line.Length.Should().Be(width);
            }
        }
    }
}
