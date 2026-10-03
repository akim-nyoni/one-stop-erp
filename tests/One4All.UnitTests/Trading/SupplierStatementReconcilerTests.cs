using FluentAssertions;
using One4All.Application.Trading;
using Xunit;

namespace One4All.UnitTests.Trading;

/// <summary>Covers supplier statement reconciliation: number pairing, amount pairing, the four outcomes and the balance identity.</summary>
public sealed class SupplierStatementReconcilerTests
{
    private static readonly DateOnly D = new(2026, 5, 1);

    [Fact]
    public void Each_line_resolves_and_differences_sum_to_the_balance_difference()
    {
        SupplierStatementLine[] statement =
        [
            new("INV-0042", D, 500m),               // agreed by number
            new("inv 0043", D.AddDays(3), 330m),    // same document, they say 330, we say 300
            new("INV-0044", D.AddDays(5), 120m),    // we never posted it
            new("Payment received - thank you", D.AddDays(10), -200m), // our payment, their wording
        ];
        SupplierLedgerItem[] ledger =
        [
            new(1, "INV0042", D, 500m),
            new(2, "INV-0043", D.AddDays(3), 300m),
            new(3, "PAY-00009", D.AddDays(8), -200m),
            new(4, "INV-0045", D.AddDays(12), 50m), // in transit: not on their statement yet
        ];

        SupplierReconciliation recon = SupplierStatementReconciler.Reconcile(statement, ledger);

        recon.StatementBalance.Should().Be(750m);
        recon.LedgerBalance.Should().Be(650m);
        recon.Difference.Should().Be(100m);
        recon.Lines.Sum(l => l.Difference).Should().Be(recon.Difference);
        recon.IsAgreed.Should().BeFalse();

        recon.Lines.Should().HaveCount(5);
        recon.Lines.Single(l => l.OpenItemId == 1).Status.Should().Be(SupplierReconStatus.Agreed);
        SupplierReconLine differs = recon.Lines.Single(l => l.OpenItemId == 2);
        differs.Status.Should().Be(SupplierReconStatus.AmountDiffers);
        differs.Difference.Should().Be(30m);
        recon.Lines.Single(l => l.DocumentNumber == "INV-0044").Status.Should().Be(SupplierReconStatus.MissingFromLedger);
        SupplierReconLine payment = recon.Lines.Single(l => l.OpenItemId == 3);
        payment.Status.Should().Be(SupplierReconStatus.Agreed);
        payment.MatchedByAmount.Should().BeTrue();
        recon.Lines.Single(l => l.OpenItemId == 4).Status.Should().Be(SupplierReconStatus.NotOnStatement);

        // Mismatches are listed before agreed lines.
        recon.Lines.TakeWhile(l => l.Status != SupplierReconStatus.Agreed).Should().HaveCount(3);
    }

    [Fact]
    public void Amount_pass_respects_the_date_window()
    {
        SupplierStatementLine[] statement = [new("Payment", D.AddDays(30), -200m)];
        SupplierLedgerItem[] ledger = [new(9, "PAY-1", D, -200m)];

        SupplierStatementReconciler.Reconcile(statement, ledger, 7).Lines.Select(l => l.Status)
            .Should().BeEquivalentTo([SupplierReconStatus.MissingFromLedger, SupplierReconStatus.NotOnStatement]);
        SupplierStatementReconciler.Reconcile(statement, ledger, 31).IsAgreed.Should().BeTrue();
    }

    [Fact]
    public void Fully_agreed_statement_is_agreed_with_zero_difference()
    {
        SupplierReconciliation recon = SupplierStatementReconciler.Reconcile([new("A-1", D, 10m)], [new(1, "a1", D, 10m)]);
        recon.IsAgreed.Should().BeTrue();
        recon.Difference.Should().Be(0m);
    }
}
