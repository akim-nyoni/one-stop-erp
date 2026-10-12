using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Core.Reporting;
using One4All.Desktop.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Reporting.Design;
using One4All.Reporting.Engine;
using One4All.Reporting.Model;

namespace One4All.Desktop;

/// <summary>
/// The banded report designer (brief §13.2): bands drawn to scale on a snap grid; elements placed, dragged, resized, aligned, sized and
/// distributed; a property panel; a field list from the report's data; an expression editor previewed on sample data; house-style themes;
/// undo / redo; a live preview; and saving as a new company version (system reports are never edited in place).
/// </summary>
public partial class BandedDesignerWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly List<string> _selected = [];
    private readonly ObservableCollection<PropertyRow> _properties = [];
    private ReportDesignSession? _session;
    private IReadOnlyList<IReadOnlyDictionary<string, object?>> _sample = [];
    private Dictionary<string, object?> _parameters = [];
    private long _companyId;
    private DesignerReport? _report;
    private Point _dragStart;
    private FrameworkElement? _dragging;
    private bool _resizing;

    /// <summary>Creates the designer.</summary>
    /// <param name="services">The application service provider.</param>
    public BandedDesignerWindow(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
        PropertyList.ItemsSource = _properties;
        BandBox.ItemsSource = Enum.GetValues<BandType>();
        BandBox.SelectedItem = BandType.Detail;
        GridBox.ItemsSource = new[] { 0d, 2d, 4d, 8d };
        GridBox.SelectedItem = 4d;
        ThemeBox.ItemsSource = ReportTheme.BuiltIn;
        ThemeBox.DisplayMemberPath = nameof(ReportTheme.Name);
        ThemeBox.SelectedIndex = 0;
        Loaded += async (_, _) => await LoadCatalogueAsync().ConfigureAwait(true);
    }

    private double Zoom => ZoomSlider.Value;

    private async Task LoadCatalogueAsync()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync().ConfigureAwait(true);
        List<ReportDefinitionRecord> records = await db.ReportDefinitions.AsNoTracking()
            .Where(r => r.Scope == ReportScope.System || (r.Scope == ReportScope.Company && r.CompanyId == _companyId)).ToListAsync().ConfigureAwait(true);

        // The company's version stands in for the system report it copies.
        ReportBox.ItemsSource = records.GroupBy(r => r.Code).Select(g => g.OrderByDescending(r => r.Scope).First())
            .OrderBy(r => r.Module).ThenBy(r => r.Name).Select(r => new DesignerReport(r.Code, r.Name, r.Module, r.DefinitionJson, r.Scope == ReportScope.Company)).ToList();
        Status.Text = "Choose a report to design (system reports are saved as your company's own version).";
    }

    private async void Report_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ReportBox.SelectedItem is DesignerReport report)
        {
            await OpenAsync(report, ReportDefinitionSerializer.Deserialize(report.DefinitionJson)).ConfigureAwait(true);
        }
    }

    private async Task OpenAsync(DesignerReport report, ReportDefinition definition)
    {
        _report = report;
        _session = new ReportDesignSession(definition, GridBox.SelectedItem is double grid ? grid : 4);
        _selected.Clear();
        await LoadSampleAsync(definition).ConfigureAwait(true);
        FieldList.ItemsSource = _session.Fields(_sample.FirstOrDefault());
        Render();
        Status.Text = $"{report.Name}: {_sample.Count} sample row(s). Drag to move, drag the corner to resize, Ctrl+click to select several.";
    }

    // A few rows from the report's own data, with its parameters defaulted (dates: the start of the year to today).
    private async Task LoadSampleAsync(ReportDefinition definition)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        _parameters = definition.Parameters.ToDictionary(p => p.Name, p => (object?)(p.Type switch
        {
            ParameterType.Date when DateOnly.TryParse(p.DefaultValue, CultureInfo.InvariantCulture, out DateOnly d) => d,
            ParameterType.Date => p.Name.Contains("From", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Start", StringComparison.OrdinalIgnoreCase) ? new DateOnly(today.Year, 1, 1) : today,
            _ => p.DefaultValue,
        }), StringComparer.OrdinalIgnoreCase);
        string command = ReportCommands.For(definition);
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            IReportDataProvider? provider = scope.ServiceProvider.GetServices<IReportDataProvider>().FirstOrDefault(p => p.CanHandle(command));
            _sample = provider is null ? [] : [.. (await provider.GetRowsAsync(_companyId, command, _parameters).ConfigureAwait(true)).Take(50)];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _sample = [];
            Status.Text = $"No sample data ({ex.Message}); the layout can still be designed.";
        }
    }

    // ---- Drawing the bands ----
    private void Render()
    {
        BandsPanel.Children.Clear();
        if (_session is null)
        {
            return;
        }

        double width = _session.PrintableWidth * Zoom;
        foreach (ReportBand band in _session.Definition.Bands)
        {
            DockPanel strip = new() { Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE6, 0xDD)), Width = width };
            TextBox height = new() { Text = band.Height.ToString("0.#", CultureInfo.InvariantCulture), Width = 44, Tag = band.Type, ToolTip = "Band height (points)" };
            height.KeyDown += BandHeight_KeyDown;
            DockPanel.SetDock(height, Dock.Right);
            strip.Children.Add(height);
            Button flow = new() { Content = "Flow…", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 4, 0), ToolTip = "Hide when…, repeat, keep together, page breaks" };
            flow.Click += (_, _) => BandFlow_Click(band);
            DockPanel.SetDock(flow, Dock.Right);
            strip.Children.Add(flow);
            string flags = string.Join(" · ", new[]
            {
                string.IsNullOrWhiteSpace(band.SuppressWhen) ? null : "hidden when " + band.SuppressWhen, band.RepeatOnNewPage ? "repeats" : null, band.KeepTogether ? "kept with next" : null,
                band.PageBreakBefore ? "new page before" : null, band.PageBreakAfter ? "new page after" : null,
            }.OfType<string>());
            strip.Children.Add(new TextBlock
            {
                Text = $" {band.Type}{(band.Type is BandType.GroupHeader or BandType.GroupFooter ? $" {band.GroupLevel}" : string.Empty)}{(flags.Length > 0 ? "   (" + flags + ")" : string.Empty)}",
                FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            });
            BandsPanel.Children.Add(strip);

            Canvas canvas = new() { Width = width, Height = band.Height * Zoom, Background = GridBrush(), ClipToBounds = true, Tag = band.Type, Margin = new Thickness(0, 0, 0, 6) };
            canvas.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource == canvas)
                {
                    BandBox.SelectedItem = band.Type;
                    _selected.Clear();
                    Render();
                }
            };
            foreach (ReportElement element in band.Elements)
            {
                canvas.Children.Add(ElementVisual(element));
            }

            BandsPanel.Children.Add(canvas);
        }

        ShowProperties();
    }

    private Brush GridBrush()
    {
        double grid = (_session?.Grid ?? 0) * Zoom;
        if (grid < 4)
        {
            return Brushes.White;
        }

        DrawingBrush brush = new(new GeometryDrawing(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)), 0.5), new RectangleGeometry(new Rect(0, 0, grid * 2, grid * 2))))
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, grid * 2, grid * 2), ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    private FrameworkElement ElementVisual(ReportElement element)
    {
        bool selected = _selected.Contains(element.Name);
        string caption = element.Kind switch
        {
            ElementKind.Field => $"[{element.FieldName}]",
            ElementKind.Expression => $"={element.Expression}",
            ElementKind.Aggregate => $"{element.Aggregate}({element.AggregateField ?? element.FieldName})",
            ElementKind.Line => string.Empty,
            ElementKind.Checkbox => $"☐ [{element.FieldName}]",
            ElementKind.Signature => $"____________ {element.Text}",
            ElementKind.Chart => $"▇▅▃ chart of {element.AggregateField} by {element.ChartCategoryField ?? "(set ChartCategoryField)"}",
            ElementKind.RichText => $"¶ {element.Text}",
            ElementKind.Image => element.Expression is string logo ? $"🖼 {logo}" : "🖼 picture",
            ElementKind.Barcode => $"▌▌▌ {element.BarcodeSymbology ?? "Code128"} of [{element.FieldName ?? element.Expression ?? element.Text}]",
            ElementKind.SubReport => $"⧉ sub-report {element.SubReportCode ?? "(set SubReportCode)"} {ReportDesignSession.FormatLinks(element.SubReportLinks)}",
            _ => element.Text ?? string.Empty,
        };
        Grid visual = new() { Width = Math.Max(4, element.Width * Zoom), Height = Math.Max(element.Kind == ElementKind.Line ? 3 : 6, element.Height * Zoom), Tag = element.Name, Cursor = Cursors.SizeAll };
        visual.Children.Add(new Border
        {
            BorderBrush = selected ? Brushes.DodgerBlue : new SolidColorBrush(Color.FromRgb(0xB0, 0xB8, 0xC0)), BorderThickness = new Thickness(selected ? 2 : 1),
            Background = element.Kind == ElementKind.Line ? Brushes.Black : TryBrush(element.Background) ?? new SolidColorBrush(Color.FromArgb(18, 0x1B, 0x5E, 0x20)),
            Child = new TextBlock
            {
                Text = caption, FontFamily = new FontFamily(element.FontFamily), FontSize = Math.Max(5, element.FontSize * Zoom * 96 / 72 * 0.75), TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal, Foreground = TryBrush(element.Foreground) ?? Brushes.Black, Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = element.HAlign switch { ReportHAlign.Center => TextAlignment.Center, ReportHAlign.Right => TextAlignment.Right, _ => TextAlignment.Left },
            },
        });
        if (selected)
        {
            System.Windows.Shapes.Rectangle grip = new() { Width = 8, Height = 8, Fill = Brushes.DodgerBlue, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Tag = "grip" };
            visual.Children.Add(grip);
        }

        Canvas.SetLeft(visual, element.X * Zoom);
        Canvas.SetTop(visual, element.Y * Zoom);
        visual.MouseLeftButtonDown += Element_MouseDown;
        visual.MouseMove += Element_MouseMove;
        visual.MouseLeftButtonUp += Element_MouseUp;
        return visual;
    }

    private void Element_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement visual || visual.Tag is not string name)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (!_selected.Remove(name))
            {
                _selected.Add(name);
            }
        }
        else if (!_selected.Contains(name))
        {
            _selected.Clear();
            _selected.Add(name);
        }
        else
        {
            // Clicking an already-selected element makes it the anchor for alignment.
            _selected.Remove(name);
            _selected.Insert(0, name);
        }

        _resizing = e.OriginalSource is System.Windows.Shapes.Rectangle { Tag: "grip" };
        _dragging = visual;
        _dragStart = e.GetPosition(BandsPanel);
        visual.CaptureMouse();
        e.Handled = true;
        if (!_resizing)
        {
            ShowProperties();
            foreach (FrameworkElement other in BandsPanel.Children.OfType<Canvas>().SelectMany(c => c.Children.OfType<FrameworkElement>()))
            {
                if (other.Tag is string n && other.Children().FirstOrDefault() is Border border)
                {
                    border.BorderBrush = _selected.Contains(n) ? Brushes.DodgerBlue : new SolidColorBrush(Color.FromRgb(0xB0, 0xB8, 0xC0));
                    border.BorderThickness = new Thickness(_selected.Contains(n) ? 2 : 1);
                }
            }
        }
    }

    private void Element_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Vector moved = e.GetPosition(BandsPanel) - _dragStart;
        _dragging.RenderTransform = _resizing ? new ScaleTransform(Math.Max(0.1, (_dragging.ActualWidth + moved.X) / Math.Max(1, _dragging.ActualWidth)),
            Math.Max(0.1, (_dragging.ActualHeight + moved.Y) / Math.Max(1, _dragging.ActualHeight))) : new TranslateTransform(moved.X, moved.Y);
    }

    private void Element_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is null || _session is null)
        {
            return;
        }

        Vector moved = e.GetPosition(BandsPanel) - _dragStart;
        FrameworkElement visual = _dragging;
        visual.ReleaseMouseCapture();
        _dragging = null;
        if (Math.Abs(moved.X) < 2 && Math.Abs(moved.Y) < 2)
        {
            visual.RenderTransform = Transform.Identity;
            return;
        }

        if (_resizing && visual.Tag is string name && _session.Find(name).Element is ReportElement element)
        {
            _session.Resize(name, element.Width + (moved.X / Zoom), element.Height + (moved.Y / Zoom));
        }
        else
        {
            _session.Move(_selected, moved.X / Zoom, moved.Y / Zoom);
        }

        Render();
    }

    // ---- Properties ----
    private void ShowProperties()
    {
        _properties.Clear();
        if (_session is null || _selected.Count == 0)
        {
            SelectionTitle.Text = "Properties (select an element)";
            return;
        }

        string name = _selected[0];
        SelectionTitle.Text = _selected.Count == 1 ? $"Properties — {name}" : $"Properties — {name} (+{_selected.Count - 1} more; anchor for alignment)";
        foreach (string property in ReportDesignSession.EditableProperties)
        {
            _properties.Add(new PropertyRow(property, _session.GetProperty(name, property) ?? string.Empty));
        }
    }

    private void Property_LostFocus(object sender, RoutedEventArgs e) => ApplyProperty((TextBox)sender);

    private void Property_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyProperty((TextBox)sender);
        }
    }

    private void ApplyProperty(TextBox box)
    {
        if (_session is null || _selected.Count == 0 || box.Tag is not string property)
        {
            return;
        }

        string current = _session.GetProperty(_selected[0], property) ?? string.Empty;
        if (box.Text == current)
        {
            return;
        }

        if (_session.SetProperty(_selected[0], property, box.Text) is string problem)
        {
            Status.Text = problem;
            box.Text = current;
            return;
        }

        Status.Text = $"{property} set.";
        Render();
    }

    private void BandFlow_Click(ReportBand band)
    {
        if (_session is null || DesignerDialogs.AskBandFlow(this, band) is not DesignerDialogs.BandFlow flow)
        {
            return;
        }

        _session.SetBandOptions(band.Type, band.GroupLevel, flow.SuppressWhen, flow.RepeatOnNewPage, flow.KeepTogether, flow.PageBreakBefore, flow.PageBreakAfter);
        Status.Text = $"{band.Type} flow set.";
        Render();
    }

    // The report's data source: SQL (a SELECT filtered by @CompanyId), a reporting view or function, or a view built from columns and an order;
    // "Discover fields" reads the columns without fetching rows (SELECT … LIMIT 0).
    private async void DataSource_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        ReportDataSource? current = _session.Definition.DataSources.FirstOrDefault();
        ComboBox kind = new() { ItemsSource = new[] { DataSourceKind.Sql, DataSourceKind.View, DataSourceKind.Function, DataSourceKind.InMemory }, SelectedItem = current?.Kind ?? DataSourceKind.Sql };
        TextBox command = new() { Text = current?.Command ?? string.Empty, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120, FontFamily = new FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        TextBox view = new() { ToolTip = "rpt.<view> to build a query from" }, columns = new() { ToolTip = "Columns, comma-separated (blank: all)" }, order = new() { ToolTip = "Order by, e.g. posting_date DESC" };
        TextBlock found = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, MinHeight = 40 };
        List<string> discovered = [];
        Button build = new() { Content = "Build query from view", Margin = new Thickness(0, 4, 0, 0) };
        build.Click += (_, _) =>
        {
            (string? built, string? refusal) = ReportQueryBuilder.Build(view.Text.Trim(), [.. columns.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)], [],
                [.. order.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]);
            if (built is null)
            {
                found.Text = refusal;
                return;
            }

            kind.SelectedItem = DataSourceKind.Sql;
            command.Text = built["sql:".Length..];
        };
        Button discover = new() { Content = "Discover fields", Margin = new Thickness(0, 4, 0, 0) };
        discover.Click += async (_, _) =>
        {
            string text = kind.SelectedItem switch { DataSourceKind.View => "view:", DataSourceKind.Function => "function:", DataSourceKind.Sql => "sql:", _ => string.Empty } + command.Text.Trim();
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<IReadOnlyList<ReportFieldInfo>> fields = await scope.ServiceProvider.GetRequiredService<IReportFieldDiscovery>().DescribeAsync(_companyId, text, _parameters).ConfigureAwait(true);
            discovered = fields.IsSuccess ? [.. fields.Value.Select(f => f.Name)] : [];
            found.Text = fields.IsSuccess ? string.Join(", ", fields.Value.Select(f => $"{f.Name} ({f.TypeName})")) : fields.Error.Message;
        };

        StackPanel panel = new() { Margin = new Thickness(12) };
        foreach ((string label, UIElement editor) in new (string, UIElement)[]
        {
            ("Kind", kind), ("Query / view / function", command), ("Query builder: view", view), ("Columns", columns), ("Order by", order),
        })
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });
            panel.Children.Add(editor);
        }

        panel.Children.Add(build);
        panel.Children.Add(discover);
        panel.Children.Add(found);
        Window window = new() { Title = "Data source", Owner = this, Width = 560, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        Button ok = new() { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => window.DialogResult = true;
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0),
            Children = { ok, new Button { Content = "Cancel", Width = 80, IsCancel = true } } });
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 720 };
        if (window.ShowDialog() != true || string.IsNullOrWhiteSpace(command.Text))
        {
            return;
        }

        _session.Definition.DataSources.Clear();
        _session.Definition.DataSources.Add(new ReportDataSource { Name = current?.Name ?? "Main", Kind = kind.SelectedItem is DataSourceKind k ? k : DataSourceKind.Sql, Command = command.Text.Trim() });
        await LoadSampleAsync(_session.Definition).ConfigureAwait(true);
        FieldList.ItemsSource = _sample.Count > 0 || discovered.Count == 0 ? _session.Fields(_sample.FirstOrDefault()) : discovered;
        Render();
        Status.Text = $"Data source set: {_sample.Count} sample row(s); save a version to keep it.";
    }

    private void PageSetup_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || DesignerDialogs.AskPageSetup(this, _session.Definition.Page) is not PageSetup page)
        {
            return;
        }

        _session.SetPage(page);
        Status.Text = page.Columns > 1 ? $"Page setup saved: {page.PaperSize}, {page.Columns} columns." : $"Page setup saved: {page.PaperSize} {page.Orientation}.";
        Render();
    }

    private void BandHeight_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { Tag: BandType type } box && double.TryParse(box.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out double height))
        {
            _session?.SetBandHeight(type, height);
            Render();
        }
    }

    // ---- Expression editor ----
    private void Expression_Changed(object sender, TextChangedEventArgs e)
        => ExpressionResult.Text = string.IsNullOrWhiteSpace(ExpressionBox.Text) ? string.Empty : "= " + ReportDesignSession.Preview(ExpressionBox.Text, _sample.FirstOrDefault(), _parameters);

    private void UseExpression_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null && _selected.Count > 0 && _session.SetProperty(_selected[0], nameof(ReportElement.Expression), ExpressionBox.Text) is null)
        {
            Render();
        }
    }

    // ---- Toolbar ----
    private BandType TargetBand => BandBox.SelectedItem is BandType type ? type : BandType.Detail;

    private void AddLabel_Click(object sender, RoutedEventArgs e) => Add(ElementKind.Label, "Label");

    private void AddField_Click(object sender, RoutedEventArgs e) => Add(ElementKind.Field, FieldList.SelectedItem as string);

    private void AddExpression_Click(object sender, RoutedEventArgs e) => Add(ElementKind.Expression, string.IsNullOrWhiteSpace(ExpressionBox.Text) ? "CompanyName" : ExpressionBox.Text);

    private void AddLine_Click(object sender, RoutedEventArgs e) => Add(ElementKind.Line, null);

    private void AddCheckbox_Click(object sender, RoutedEventArgs e) => Add(ElementKind.Field, FieldList.SelectedItem as string, ElementKind.Checkbox);

    private void AddSignature_Click(object sender, RoutedEventArgs e) => Add(ElementKind.Label, "Authorised signature", ElementKind.Signature, 200, 32);

    private void AddChart_Click(object sender, RoutedEventArgs e) => Add(ElementKind.Field, FieldList.SelectedItem as string, ElementKind.Chart, 360, 140);

    private void AddRichText_Click(object sender, RoutedEventArgs e) => Add(ElementKind.Label, "**Bold** and *italic* text with {Field} values", ElementKind.RichText, 300, 40);

    private void AddSubReport_Click(object sender, RoutedEventArgs e) => Add(ElementKind.Label, null, ElementKind.SubReport, 400, 60);

    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog dialog = new() { Filter = "Pictures (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (_session is null || dialog.ShowDialog(this) != true)
        {
            return;
        }

        byte[] bytes = System.IO.File.ReadAllBytes(dialog.FileName);
        if (bytes.Length > 512 * 1024)
        {
            Status.Text = "Pictures in a layout are limited to 512 KB.";
            return;
        }

        string name = _session.AddElement(TargetBand, ElementKind.Label, 4, 2, null);
        if (_session.Find(name).Element is ReportElement element)
        {
            element.Kind = ElementKind.Image;
            string type = dialog.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpeg";
            element.ImageData = $"data:image/{type};base64,{Convert.ToBase64String(bytes)}";
            _session.SetBandHeight(TargetBand, Math.Max(_session.Definition.BandOfType(TargetBand)!.Height, 64));
            _session.Resize(name, 120, 60);
        }

        Select(name);
    }

    private void AddLogo_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        string name = _session.AddElement(TargetBand, ElementKind.Label, 4, 2, null);
        if (_session.Find(name).Element is ReportElement element)
        {
            element.Kind = ElementKind.Image;
            element.Expression = "CompanyLogo";
            _session.SetBandHeight(TargetBand, Math.Max(_session.Definition.BandOfType(TargetBand)!.Height, 64));
            _session.Resize(name, 120, 60);
        }

        Select(name);
    }

    private void AddBarcode_Click(object sender, RoutedEventArgs e)
    {
        Add(ElementKind.Field, FieldList.SelectedItem as string, ElementKind.Barcode, 180, 40);
        if (_session is not null && _selected.Count == 1 && _session.Find(_selected[0]).Element is ReportElement element && element.Kind == ElementKind.Barcode)
        {
            element.BarcodeSymbology = "Code128";
        }
    }

    private void Field_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FieldList.SelectedItem is string field && _session is not null)
        {
            double x = _session.Definition.BandOfType(BandType.Detail)?.Elements.Select(el => el.X + el.Width).DefaultIfEmpty(0).Max() ?? 0;
            Select(_session.AddElement(BandType.Detail, ElementKind.Field, x + 4, 2, field));
        }
    }

    // Tick boxes and charts read a field; a signature is a captioned label; each then becomes its own kind and size.
    private void Add(ElementKind kind, string? content, ElementKind? asKind = null, double width = 0, double height = 0)
    {
        if (_session is null)
        {
            return;
        }

        if (kind == ElementKind.Field && content is null)
        {
            Status.Text = "Pick a field in the list first.";
            return;
        }

        string name = _session.AddElement(TargetBand, kind, 4, 2, content);
        if (asKind is ElementKind finalKind && _session.Find(name).Element is ReportElement element)
        {
            element.Kind = finalKind;
            if (finalKind == ElementKind.Chart)
            {
                element.AggregateField = content;
            }

            if (width > 0)
            {
                _session.SetBandHeight(TargetBand, Math.Max(_session.Definition.BandOfType(TargetBand)!.Height, height + 4));
                _session.Resize(name, width, height);
            }
        }

        Select(name);
    }

    private void Select(string name)
    {
        _selected.Clear();
        _selected.Add(name);
        Render();
    }

    private void Align_Click(object sender, RoutedEventArgs e) => Act(s => s.Align(_selected, Enum.Parse<DesignAlignment>((string)((Button)sender).Tag)));

    private void SameWidth_Click(object sender, RoutedEventArgs e) => Act(s => s.SameSize(_selected, true, false));

    private void SameHeight_Click(object sender, RoutedEventArgs e) => Act(s => s.SameSize(_selected, false, true));

    private void Distribute_Click(object sender, RoutedEventArgs e) => Act(s => s.DistributeHorizontally(_selected));

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null)
        {
            IReadOnlyList<string> copies = _session.Duplicate(_selected);
            _selected.Clear();
            _selected.AddRange(copies);
            Render();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return; // Delete inside a text box edits the text, not the layout.
        }

        Act(s => s.Delete(_selected));
        _selected.Clear();
        Render();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Act(s => s.Undo());

    private void Redo_Click(object sender, RoutedEventArgs e) => Act(s => s.Redo());

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (ThemeBox.SelectedItem is ReportTheme theme)
        {
            Act(s => s.ApplyTheme(theme));
        }
    }

    private void Grid_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_session is not null && GridBox.SelectedItem is double grid)
        {
            _session.Grid = grid;
            Render();
        }
    }

    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded)
        {
            Render();
        }
    }

    private void Act(Action<ReportDesignSession> action)
    {
        if (_session is null)
        {
            return;
        }

        action(_session);
        _selected.RemoveAll(n => _session.Find(n).Element is null);
        Render();
    }

    // ---- New, preview, save ----
    private async void New_Click(object sender, RoutedEventArgs e)
    {
        string code = NewCodeBox.Text.Trim().ToUpperInvariant();
        if (code.Length == 0 || _report is null || _session is null)
        {
            Status.Text = "Choose the report whose data the new report uses, then type a code for it.";
            return;
        }

        ReportDefinition blank = new()
        {
            Code = code, Name = $"New report {code}", Module = _report.Module, Page = _session.Definition.Page, Parameters = _session.Definition.Parameters, DataSources = _session.Definition.DataSources,
            Bands =
            [
                new ReportBand { Type = BandType.ReportHeader, Height = 30, Elements = [new ReportElement { Kind = ElementKind.Expression, Name = "Company", Expression = "CompanyName", Width = 300, Height = 16, Bold = true, FontSize = 13 }] },
                new ReportBand { Type = BandType.PageHeader, Height = 18 },
                new ReportBand { Type = BandType.Detail, Height = 16 },
                new ReportBand { Type = BandType.PageFooter, Height = 16, Elements = [new ReportElement { Kind = ElementKind.Label, Name = "PageNo", Text = "Page [Page] of [Pages]", X = 400, Width = 120, Height = 14, HAlign = ReportHAlign.Right }] },
            ],
        };
        await OpenAsync(new DesignerReport(code, blank.Name, blank.Module, ReportDefinitionSerializer.Serialize(blank), true), blank).ConfigureAwait(true);
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        RenderModel model = new ReportEngine().Build(new ReportInput { Definition = _session.Definition, Rows = _sample, Parameters = _parameters, CompanyName = "Preview", UserName = "designer" });
        (FixedDocument document, _) = FixedReportDocument.Build(model, drill: null);
        new Window { Owner = this, Title = $"Preview — {_session.Definition.Name} (sample data)", Width = 900, Height = 900, Content = new DocumentViewer { Document = document } }.Show();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || _report is null)
        {
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        ReportDefinition definition = _session.Definition;
        long? user = _services.GetService<DesktopSession>()?.UserId;
        string note = string.IsNullOrWhiteSpace(NoteBox.Text) ? "Designer" : NoteBox.Text.Trim();
        IReportDefinitionService definitions = scope.ServiceProvider.GetRequiredService<IReportDefinitionService>();
        bool mine = MineBox.IsChecked == true && user is not null;
        Result<int> saved = mine
            ? await definitions.SaveUserVersionAsync(_companyId, user!.Value, definition.Code, definition.Name, definition.Module, ReportDefinitionSerializer.Serialize(definition), note).ConfigureAwait(true)
            : await definitions.SaveAsync(_companyId, definition.Code, definition.Name, definition.Module, ReportDefinitionSerializer.Serialize(definition), user, note).ConfigureAwait(true);
        Status.Text = saved.IsSuccess
            ? $"Saved {definition.Code} as {(mine ? "your own" : "the company")} version {saved.Value}; run it from the Report Viewer."
            : saved.Error.Message;
        if (saved.IsSuccess)
        {
            NoteBox.Clear();
        }
    }

    private static Brush? TryBrush(string? color)
    {
        try
        {
            return string.IsNullOrWhiteSpace(color) ? null : (Brush?)new BrushConverter().ConvertFromString(color);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>A report in the designer's list.</summary>
    /// <param name="Code">The code.</param>
    /// <param name="Name">The name.</param>
    /// <param name="Module">The module.</param>
    /// <param name="DefinitionJson">The definition.</param>
    /// <param name="IsCompanyVersion">Whether it is already the company's own version.</param>
    private sealed record DesignerReport(string Code, string Name, string? Module, string DefinitionJson, bool IsCompanyVersion)
    {
        public string Display => $"{Name} ({Code}){(IsCompanyVersion ? " ✎" : string.Empty)}";
    }

    /// <summary>A row of the property panel.</summary>
    /// <param name="Name">The property.</param>
    /// <param name="Value">Its value as text.</param>
    private sealed record PropertyRow(string Name, string Value);
}

/// <summary>Helpers for the designer's visuals.</summary>
internal static class DesignerVisuals
{
    /// <summary>The children of a panel-like element.</summary>
    /// <param name="element">The element.</param>
    /// <returns>Its children.</returns>
    public static IEnumerable<UIElement> Children(this FrameworkElement element) => element is Panel panel ? panel.Children.OfType<UIElement>() : [];
}
