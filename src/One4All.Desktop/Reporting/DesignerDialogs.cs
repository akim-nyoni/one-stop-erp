using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using One4All.Reporting.Model;

namespace One4All.Desktop.Reporting;

/// <summary>The report designer's small forms: a band's flow across pages, and the page setup.</summary>
public static class DesignerDialogs
{
    /// <summary>A band's flow options as the user leaves them.</summary>
    /// <param name="SuppressWhen">The suppress-when expression.</param>
    /// <param name="RepeatOnNewPage">Repeat on each page its group continues on.</param>
    /// <param name="KeepTogether">Keep with the next band.</param>
    /// <param name="PageBreakBefore">New page before.</param>
    /// <param name="PageBreakAfter">New page after.</param>
    public sealed record BandFlow(string? SuppressWhen, bool RepeatOnNewPage, bool KeepTogether, bool PageBreakBefore, bool PageBreakAfter);

    /// <summary>Asks for a band's flow options.</summary>
    /// <param name="owner">The designer window.</param>
    /// <param name="band">The band.</param>
    /// <returns>The options, or null when cancelled.</returns>
    public static BandFlow? AskBandFlow(Window owner, ReportBand band)
    {
        ArgumentNullException.ThrowIfNull(band);
        TextBox suppress = new() { Text = band.SuppressWhen ?? string.Empty, FontFamily = new System.Windows.Media.FontFamily("Consolas"), ToolTip = "e.g. Amount == 0 - the band is left out where this is true" };
        CheckBox repeat = new() { Content = "Repeat at the top of each page its group continues on", IsChecked = band.RepeatOnNewPage, IsEnabled = band.Type == BandType.GroupHeader };
        CheckBox keep = new() { Content = "Keep with the first row (never alone at a page's foot)", IsChecked = band.KeepTogether, IsEnabled = band.Type == BandType.GroupHeader };
        CheckBox before = new() { Content = "Start a new page before this band", IsChecked = band.PageBreakBefore };
        CheckBox after = new() { Content = "Start a new page after this band", IsChecked = band.PageBreakAfter };
        return Ask(owner, $"{band.Type} - flow", [("Hide when", suppress), (string.Empty, repeat), (string.Empty, keep), (string.Empty, before), (string.Empty, after)])
            ? new BandFlow(suppress.Text, repeat.IsChecked == true, keep.IsChecked == true, before.IsChecked == true, after.IsChecked == true)
            : null;
    }

    /// <summary>Edits a copy of the page setup.</summary>
    /// <param name="owner">The designer window.</param>
    /// <param name="page">The current setup (left unchanged).</param>
    /// <returns>The edited copy, or null when cancelled.</returns>
    public static PageSetup? AskPageSetup(Window owner, PageSetup page)
    {
        ArgumentNullException.ThrowIfNull(page);
        ComboBox paper = new() { ItemsSource = Enum.GetValues<PaperSize>(), SelectedItem = page.PaperSize };
        ComboBox orientation = new() { ItemsSource = Enum.GetValues<ReportOrientation>(), SelectedItem = page.Orientation };
        TextBox customWidth = Number(page.CustomWidth), customHeight = Number(page.CustomHeight);
        TextBox left = Number(page.MarginLeft), right = Number(page.MarginRight), top = Number(page.MarginTop), bottom = Number(page.MarginBottom);
        TextBox columns = Number(page.Columns), gap = Number(page.ColumnGap);
        TextBox watermark = new() { Text = page.Watermark ?? string.Empty }, watermarkExpression = new() { Text = page.WatermarkExpression ?? string.Empty, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        ComboBox tray = new() { IsEditable = true, ItemsSource = new[] { string.Empty, "Auto", "Manual", "Cassette", "Tractor", "Sheet feeder" }, Text = page.Tray ?? string.Empty };
        ComboBox duplex = new() { ItemsSource = Enum.GetValues<DuplexMode>(), SelectedItem = page.Duplex };
        string? background = page.BackgroundImage;
        TextBlock backgroundState = new() { Text = background is null ? "none" : "a picture is set", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        Button choose = new() { Content = "Choose…", Padding = new Thickness(6, 1, 6, 1) };
        Button clear = new() { Content = "Clear", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(4, 0, 0, 0) };
        choose.Click += (_, _) =>
        {
            Microsoft.Win32.OpenFileDialog dialog = new() { Filter = "Pictures (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
            if (dialog.ShowDialog(owner) == true)
            {
                byte[] bytes = System.IO.File.ReadAllBytes(dialog.FileName);
                string type = dialog.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpeg";
                background = $"data:image/{type};base64,{Convert.ToBase64String(bytes)}";
                backgroundState.Text = System.IO.Path.GetFileName(dialog.FileName);
            }
        };
        clear.Click += (_, _) =>
        {
            background = null;
            backgroundState.Text = "none";
        };
        StackPanel backgroundRow = new() { Orientation = Orientation.Horizontal, Children = { choose, clear, backgroundState } };

        if (!Ask(owner, "Page setup",
        [
            ("Paper", paper), ("Orientation", orientation), ("Custom width (pt)", customWidth), ("Custom height (pt)", customHeight), ("Margin left", left), ("Margin right", right),
            ("Margin top", top), ("Margin bottom", bottom), ("Columns (labels)", columns), ("Column gap", gap), ("Watermark", watermark), ("Watermark expression", watermarkExpression),
            ("Background picture", backgroundRow), ("Printer tray", tray), ("Two-sided", duplex),
        ]))
        {
            return null;
        }

        return new PageSetup
        {
            PaperSize = paper.SelectedItem is PaperSize size ? size : page.PaperSize, Orientation = orientation.SelectedItem is ReportOrientation o ? o : page.Orientation,
            CustomWidth = Read(customWidth, page.CustomWidth), CustomHeight = Read(customHeight, page.CustomHeight), MarginLeft = Read(left, page.MarginLeft),
            MarginRight = Read(right, page.MarginRight), MarginTop = Read(top, page.MarginTop), MarginBottom = Read(bottom, page.MarginBottom),
            Columns = Math.Clamp((int)Read(columns, page.Columns), 1, 20), ColumnGap = Read(gap, page.ColumnGap),
            Watermark = Blank(watermark.Text), WatermarkExpression = Blank(watermarkExpression.Text), BackgroundImage = background, Tray = Blank(tray.Text),
            Duplex = duplex.SelectedItem is DuplexMode d ? d : page.Duplex,
        };
    }

    // A modal form: one labelled row per field, OK and Cancel.
    private static bool Ask(Window owner, string title, IReadOnlyList<(string Label, FrameworkElement Editor)> fields)
    {
        Grid grid = new() { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < fields.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock label = new() { Text = fields[i].Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 10, 3) };
            Grid.SetRow(label, i);
            grid.Children.Add(label);
            fields[i].Editor.Margin = new Thickness(0, 3, 0, 3);
            Grid.SetRow(fields[i].Editor, i);
            Grid.SetColumn(fields[i].Editor, 1);
            grid.Children.Add(fields[i].Editor);
        }

        Window window = new()
        {
            Title = title, Owner = owner, Width = 460, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        Button ok = new() { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        Button cancel = new() { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => window.DialogResult = true;
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 12), Children = { ok, cancel } };
        window.Content = new StackPanel { Children = { grid, buttons } };
        return window.ShowDialog() == true;
    }

    private static TextBox Number(double value) => new() { Text = value.ToString("0.##", CultureInfo.InvariantCulture) };

    private static double Read(TextBox box, double fallback)
        => double.TryParse(box.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out double value) && value >= 0 ? value : fallback;

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
