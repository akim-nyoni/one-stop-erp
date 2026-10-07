using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Pos;

namespace One4All.Desktop;

/// <summary>How one payer of a split bill pays.</summary>
/// <param name="TenderType">Cash, card, mobile money or voucher.</param>
/// <param name="VoucherNumber">The gift card, for a voucher.</param>
public sealed record SplitPayment(TenderType TenderType, string? VoucherNumber);

/// <summary>
/// Split the bill (brief §4.1): choose how many payers, give each a share of every line (or split evenly), and how each pays; every
/// payer's amount is worked out live with the same rules the till posts with.
/// </summary>
public sealed class SplitBillWindow : Window
{
    private static readonly TenderType[] Payable = [TenderType.Cash, TenderType.Card, TenderType.MobileMoney, TenderType.Voucher];
    private readonly IReadOnlyList<PosSaleLineRequest> _lines;
    private readonly ComboBox _ways = new() { Width = 60, Margin = new Thickness(6, 0, 12, 0), ItemsSource = Enumerable.Range(2, 7).ToList(), SelectedIndex = 0 };
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, Margin = new Thickness(10, 0, 10, 6), HeadersVisibility = DataGridHeadersVisibility.Column };
    private readonly StackPanel _payers = new() { Margin = new Thickness(10, 0, 10, 6) };
    private readonly TextBlock _problem = new() { Foreground = Brushes.Firebrick, Margin = new Thickness(10, 0, 10, 6), TextWrapping = TextWrapping.Wrap };
    private readonly List<(ComboBox Tender, TextBox Voucher, TextBlock Amount)> _payerRows = [];
    private DataTable _table = new();

    /// <summary>Creates the window.</summary>
    /// <param name="lines">The basket's lines.</param>
    public SplitBillWindow(IReadOnlyList<PosSaleLineRequest> lines)
    {
        _lines = lines;
        Title = "Split the bill";
        Width = 820;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        Button even = new() { Content = "Split evenly", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0) };
        even.Click += (_, _) => Fill(SplitBill.Evenly(_lines, Ways));
        StackPanel bar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
        bar.Children.Add(new TextBlock { Text = "Payers", VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(_ways);
        bar.Children.Add(even);
        bar.Children.Add(new TextBlock { Text = "Type each payer's quantity of every line; each line must be shared out in full.", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray });
        _ways.SelectionChanged += (_, _) => Rebuild();

        Button ok = new() { Content = "Take payments", Padding = new Thickness(16, 4, 16, 4), FontWeight = FontWeights.SemiBold, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        Button cancel = new() { Content = "Cancel", Padding = new Thickness(16, 4, 16, 4), IsCancel = true };
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        DockPanel root = new();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        DockPanel.SetDock(_problem, Dock.Bottom);
        root.Children.Add(_problem);
        DockPanel.SetDock(_payers, Dock.Bottom);
        root.Children.Add(_payers);
        root.Children.Add(_grid);
        Content = root;
        Rebuild();
    }

    /// <summary>Each payer's shares of the lines, once accepted.</summary>
    public IReadOnlyList<IReadOnlyList<SplitShare>> Parts { get; private set; } = [];

    /// <summary>How each payer pays, once accepted.</summary>
    public IReadOnlyList<SplitPayment> Payments { get; private set; } = [];

    private int Ways => _ways.SelectedItem is int ways ? ways : 2;

    private void Rebuild()
    {
        _table = new DataTable();
        _table.Columns.Add("Line", typeof(string));
        _table.Columns.Add("Quantity", typeof(decimal));
        for (int p = 1; p <= Ways; p++)
        {
            _table.Columns.Add($"P{p}", typeof(decimal));
        }

        foreach (PosSaleLineRequest line in _lines)
        {
            DataRow row = _table.NewRow();
            row["Line"] = $"{line.Description} @ {(line.LineNet + line.TaxAmount).ToString("N2", CultureInfo.CurrentCulture)}";
            row["Quantity"] = line.Quantity;
            for (int p = 1; p <= Ways; p++)
            {
                row[$"P{p}"] = p == 1 ? line.Quantity : 0m;
            }

            _table.Rows.Add(row);
        }

        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridTextColumn { Header = "Line", Binding = new System.Windows.Data.Binding("Line"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Qty", Binding = new System.Windows.Data.Binding("Quantity"), IsReadOnly = true, Width = 60 });
        for (int p = 1; p <= Ways; p++)
        {
            _grid.Columns.Add(new DataGridTextColumn { Header = $"Payer {p}", Binding = new System.Windows.Data.Binding($"P{p}") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 80 });
        }

        _grid.ItemsSource = _table.DefaultView;
        _table.ColumnChanged += (_, _) => Recalculate();

        _payers.Children.Clear();
        _payerRows.Clear();
        for (int p = 1; p <= Ways; p++)
        {
            ComboBox tender = new() { ItemsSource = Payable, SelectedIndex = 0, Width = 120, Margin = new Thickness(6, 0, 6, 0) };
            TextBox voucher = new() { Width = 140, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Gift card number (voucher payers)" };
            TextBlock amount = new() { Width = 120, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            StackPanel line = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            line.Children.Add(new TextBlock { Text = $"Payer {p}", Width = 60, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(tender);
            line.Children.Add(new TextBlock { Text = "Gift card", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            line.Children.Add(voucher);
            line.Children.Add(amount);
            _payers.Children.Add(line);
            _payerRows.Add((tender, voucher, amount));
        }

        Recalculate();
    }

    private void Fill(IReadOnlyList<IReadOnlyList<SplitShare>> parts)
    {
        foreach (DataRow row in _table.Rows)
        {
            for (int p = 1; p <= Ways; p++)
            {
                row[$"P{p}"] = 0m;
            }
        }

        for (int p = 0; p < parts.Count; p++)
        {
            foreach (SplitShare share in parts[p])
            {
                _table.Rows[share.LineIndex][$"P{p + 1}"] = share.Quantity;
            }
        }

        Recalculate();
    }

    private List<IReadOnlyList<SplitShare>> Shares()
    {
        List<IReadOnlyList<SplitShare>> parts = [];
        for (int p = 1; p <= Ways; p++)
        {
            List<SplitShare> shares = [];
            for (int i = 0; i < _table.Rows.Count; i++)
            {
                if (_table.Rows[i][$"P{p}"] is decimal quantity && quantity != 0m)
                {
                    shares.Add(new SplitShare(i, quantity));
                }
            }

            parts.Add(shares);
        }

        return parts;
    }

    private bool Recalculate()
    {
        var plan = SplitBill.Plan(_lines, Shares());
        for (int p = 0; p < _payerRows.Count; p++)
        {
            _payerRows[p].Amount.Text = plan.IsSuccess ? $"pays {SplitBill.Gross(plan.Value[p]).ToString("N2", CultureInfo.CurrentCulture)}" : "—";
        }

        _problem.Text = plan.IsSuccess ? string.Empty : plan.Error.Message;
        return plan.IsSuccess;
    }

    private void Accept()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!Recalculate())
        {
            return;
        }

        List<SplitPayment> payments = [.. _payerRows.Select(r => new SplitPayment((TenderType)r.Tender.SelectedItem!, string.IsNullOrWhiteSpace(r.Voucher.Text) ? null : r.Voucher.Text.Trim()))];
        if (payments.Any(p => p.TenderType == TenderType.Voucher && p.VoucherNumber is null))
        {
            _problem.Text = "A payer paying by voucher needs the gift card number.";
            return;
        }

        Parts = Shares();
        Payments = payments;
        DialogResult = true;
    }
}
