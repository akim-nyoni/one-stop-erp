using FluentAssertions;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Receipting;
using One4All.Core.Receipting;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers a teller payment split across tenders.</summary>
public sealed class TenderSplitTests
{
    [Fact]
    public void Parts_of_the_same_tender_and_reference_merge_and_the_total_is_their_sum()
    {
        ReceiptTenderRequest[] parts =
        [
            new(TenderType.Cash, 50m), new(TenderType.Card, 30m, "SLIP-1 "), new(TenderType.Cash, 20m), new(TenderType.Card, 10m, "slip-1"), new(TenderType.Card, 5m, "SLIP-2"),
        ];

        TenderSplit.Problem(parts).Should().BeNull();
        TenderSplit.Merge(parts).Should().Equal(new ReceiptTenderRequest(TenderType.Cash, 70m), new ReceiptTenderRequest(TenderType.Card, 40m, "SLIP-1"),
            new ReceiptTenderRequest(TenderType.Card, 5m, "SLIP-2"));
        TenderSplit.Total(parts).Should().Be(115m);
    }

    [Fact]
    public void A_split_needs_parts_each_above_zero_and_a_reference_on_all_but_cash()
    {
        TenderSplit.Problem([]).Should().Contain("at least one part");
        TenderSplit.Problem([new ReceiptTenderRequest(TenderType.Cash, 0m)]).Should().Contain("more than zero");
        TenderSplit.Problem([new ReceiptTenderRequest(TenderType.Cash, 10m), new ReceiptTenderRequest(TenderType.MobileMoney, 5m)]).Should().Contain("MobileMoney part needs its reference");
    }
}
