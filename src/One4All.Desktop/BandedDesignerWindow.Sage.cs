using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using One4All.Reporting.Design;
using One4All.Reporting.Model;

namespace One4All.Desktop;

/// <summary>
/// The designer's chrome, laid out as the Sage Report Designer is: a menu bar (File, Edit, View, Toolbox, Sections, Report, Format, Tools, Help), a
/// standard and a formatting toolbar, a toolbox whose tools are placed by clicking in a section, Dynamic Help, Variables and File Explorer panes on
/// the left, Report Explorer and a categorised property grid with a description pane on the right, numbered section captions, and a status bar with
/// the selection's position, the page and the zoom.
/// </summary>
public partial class BandedDesignerWindow
{
    /// <summary>Returns to the pointer tool (Esc).</summary>
    public static RoutedCommand PointerCommand { get; } = new("Pointer", typeof(BandedDesignerWindow));

    private string _tool = "Pointer";
    private Point? _placeAt;
    private BandType? _placeBand;
    private bool _syncingFormat;
    private bool _buildingExplorer;
    private List<DesignerReport> _catalogue = [];

    private sealed record VariableNode(string Kind, string Name, string Insert);

    private BandType PlaceBand => _placeBand ?? TargetBand;

    private double PlaceX => _placeAt?.X ?? 4;

    private double PlaceY => _placeAt?.Y ?? 2;

    // ---- Set-up (from the constructor) ----
    private void InitialiseChrome()
    {
        HelpList.ItemsSource = DesignerCatalogue.HelpTasks;
        foreach (DesignerTool tool in DesignerCatalogue.Tools)
        {
            ToggleButton button = new() { Content = tool.Caption, Tag = tool.Key, ToolTip = tool.Hint, Padding = new Thickness(6, 2, 6, 2), IsChecked = tool.Key == "Pointer" };
            button.Click += Tool_Click;
            ToolsBar.Items.Add(button);
            MenuItem item = new() { Header = tool.Caption, Tag = tool.Key, ToolTip = tool.Hint };
            item.Click += Tool_Click;
            ToolboxMenu.Items.Add(item);
        }

        foreach (ReportTheme theme in ReportTheme.BuiltIn)
        {
            MenuItem item = new() { Header = theme.Name, Tag = theme };
            item.Click += (_, _) =>
            {
                ThemeBox.SelectedItem = theme;
                Theme_Click(this, new RoutedEventArgs());
            };
            ThemeMenu.Items.Add(item);
        }

        FontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        SizeBox.ItemsSource = new[] { "6", "7", "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48" };
        ColourBox.ItemsSource = new[] { "#000000", "#333333", "#666666", "#1B5E20", "#2E7D32", "#1F5FA8", "#B71C1C", "#E65100", "#FFFFFF" };
        ZoomText.Text = $"{ZoomSlider.Value * 100:0}%";
        PositionText.Text = "No selection";
    }

    // ---- Toolbox: pick a tool, click in a section to place it ----
    private void Tool_Click(object sender, RoutedEventArgs e) => SetTool((sender as FrameworkElement)?.Tag as string ?? "Pointer");

    private void Pointer_Click(object sender, RoutedEventArgs e) => SetTool("Pointer");

    private void SetTool(string key)
    {
        _tool = DesignerCatalogue.Tools.Any(t => t.Key == key) ? key : "Pointer";
        foreach (ToggleButton button in ToolsBar.Items.OfType<ToggleButton>())
        {
            button.IsChecked = Equals(button.Tag, _tool);
        }

        DesignerTool tool = DesignerCatalogue.Tools.First(t => t.Key == _tool);
        Status.Text = _tool == "Pointer" ? "Pointer: drag to move, drag the corner to resize, Ctrl+click to select several." : $"{tool.Caption}: click in a section to place it (Esc cancels).";
        Cursor = _tool == "Pointer" ? Cursors.Arrow : Cursors.Cross;
    }

    private void PlaceTool(BandType band, double x, double y)
    {
        _placeBand = band;
        _placeAt = new Point(Math.Max(0, x), Math.Max(0, y));
        RoutedEventArgs args = new();
        try
        {
            switch (_tool)
            {
                case "Text": AddLabel_Click(this, args); break;
                case "Line": AddLine_Click(this, args); break;
                case "Box": Add(ElementKind.Label, null, ElementKind.Rectangle, 120, 40); break;
                case "Ellipse": Add(ElementKind.Label, null, ElementKind.Ellipse, 60, 40); break;
                case "Image": AddImage_Click(this, args); break;
                case "Logo": AddLogo_Click(this, args); break;
                case "DataField": AddField_Click(this, args); break;
                case "Barcode": AddBarcode_Click(this, args); break;
                case "Expression": AddExpression_Click(this, args); break;
                case "SubReport": AddSubReport_Click(this, args); break;
                case "Checkbox": AddCheckbox_Click(this, args); break;
                case "Signature": AddSignature_Click(this, args); break;
                case "Chart": AddChart_Click(this, args); break;
                case "RichText": AddRichText_Click(this, args); break;
                default: return;
            }
        }
        finally
        {
            _placeAt = null;
            _placeBand = null;
        }

        SetTool("Pointer");
    }

    // ---- Dynamic Help ----
    private void HelpTask_Click(object sender, RoutedEventArgs e)
    {
        switch ((sender as FrameworkElement)?.Tag as string)
        {
            case "ReportInfo": ReportInfo_Click(sender, e); break;
            case "AddText": SetTool(FieldList.SelectedItem is string ? "DataField" : "Text"); break;
            case "AddImage": SetTool("Image"); break;
            case "AddLine": SetTool("Line"); break;
            case "Restrict": DataSource_Click(sender, e); break;
            case "Sort": Sort_Click(sender, e); break;
            case "Email": Email_Click(sender, e); break;
            case "PageSetup": PageSetup_Click(sender, e); break;
        }
    }

    private void ShowHelp_Click(object sender, RoutedEventArgs e) => SetPane("Help", true);

    private void About_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show(this, "One4All Report Designer\n\nSections drawn to scale on a snap grid; a toolbox placed by clicking; variables, expressions, grouping, sorting, themes, "
            + "undo and redo; a live preview on sample data; versions saved per company or per user; layouts imported and exported as .mrpt files.", "About the Report Designer", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Email_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show(this, "Save the version, then open System Manager › Report subscription: choose this report, the recipients, the format (PDF or Excel) and when it runs; "
            + "the report is e-mailed on that schedule. A one-off e-mail is sent from the Report Viewer after running the report.", "Set up email", MessageBoxButton.OK, MessageBoxImage.Information);

    // ---- Panes ----
    private void Pane_Toggle(object sender, RoutedEventArgs e)
    {
        string key = (sender as FrameworkElement)?.Tag as string ?? string.Empty;
        bool show = sender is MenuItem menu ? menu.IsChecked : false;
        SetPane(key, show);
    }

    private void SetPane(string key, bool show)
    {
        (FrameworkElement? panel, MenuItem? menu) = key switch
        {
            "Help" => (HelpPanel, HelpMenu),
            "Variables" => (VariablesPanel, VariablesMenu),
            "Files" => (FilesPanel, FilesMenu),
            "Explorer" => (ExplorerPanel, ExplorerMenu),
            "Properties" => (PropertiesPanel, PropertiesMenu),
            "Expression" => (ExpressionPanel, ExpressionMenu),
            _ => ((FrameworkElement?)null, (MenuItem?)null),
        };
        if (panel is null || menu is null)
        {
            return;
        }

        panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        menu.IsChecked = show;
        if (key == "Explorer")
        {
            ExplorerRow.Height = show ? new GridLength(180) : new GridLength(0);
        }

        LeftColumn.Width = HelpPanel.Visibility == Visibility.Visible || VariablesPanel.Visibility == Visibility.Visible || FilesPanel.Visibility == Visibility.Visible ? new GridLength(250) : new GridLength(0);
        RightColumn.Width = ExplorerPanel.Visibility == Visibility.Visible || PropertiesPanel.Visibility == Visibility.Visible || ExpressionPanel.Visibility == Visibility.Visible ? new GridLength(320) : new GridLength(0);
    }

    private void ResetPanes_Click(object sender, RoutedEventArgs e)
    {
        foreach (string key in new[] { "Help", "Variables", "Files", "Explorer", "Properties", "Expression" })
        {
            SetPane(key, true);
        }
    }

    private void FocusExpression_Click(object sender, RoutedEventArgs e)
    {
        SetPane("Expression", true);
        ExpressionBox.Focus();
    }

    // ---- File, Edit, View menus ----
    private void NewMenu_Click(object sender, RoutedEventArgs e)
    {
        if (NewCodeBox.Text.Trim().Length == 0)
        {
            NewCodeBox.Text = Microsoft.VisualBasic.Interaction.InputBox("Code for the new report (based on the open report's data):", "New report", string.Empty);
        }

        if (NewCodeBox.Text.Trim().Length > 0)
        {
            New_Click(sender, e);
        }
    }

    private void MineMenu_Click(object sender, RoutedEventArgs e) => MineBox.IsChecked = MineMenu.IsChecked;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        BandType band = _selected.Count > 0 && _session.Find(_selected[0]).Band is ReportBand selectedBand ? selectedBand.Type : TargetBand;
        _selected.Clear();
        _selected.AddRange(_session.Definition.BandOfType(band)?.Elements.Select(el => el.Name) ?? []);
        Render();
    }

    private void ZoomMenu_Click(object sender, RoutedEventArgs e)
    {
        if (double.TryParse((sender as FrameworkElement)?.Tag as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double zoom))
        {
            ZoomSlider.Value = zoom;
        }
    }

    private void GridMenu_Click(object sender, RoutedEventArgs e)
    {
        if (double.TryParse((sender as FrameworkElement)?.Tag as string, NumberStyles.Float, CultureInfo.InvariantCulture, out double grid))
        {
            GridBox.SelectedItem = grid;
        }
    }

    // ---- Sections and Report menus ----
    private void AddBand_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || !Enum.TryParse((sender as FrameworkElement)?.Tag as string, out BandType type))
        {
            return;
        }

        if (type is BandType.GroupHeader or BandType.GroupFooter)
        {
            string expression = Microsoft.VisualBasic.Interaction.InputBox("Group the records by (a field or expression; an existing group adds the section to it):", "Group section",
                _session.Definition.Groups.LastOrDefault()?.Expression ?? FieldList.SelectedItem as string ?? string.Empty).Trim();
            if (expression.Length == 0)
            {
                return;
            }

            _session.AddGroupBand(type, expression);
        }
        else
        {
            _session.AddBand(type);
        }

        BandBox.SelectedItem = type;
        Render();
        Status.Text = $"{type} section added.";
    }

    private void BandFlowMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null && CurrentBand() is ReportBand band)
        {
            BandFlow_Click(band);
        }
    }

    private void BandHeightMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || CurrentBand() is not ReportBand band)
        {
            return;
        }

        string text = Microsoft.VisualBasic.Interaction.InputBox($"Height of the {DesignerCatalogue.BandCaption(_session.Definition.Bands.IndexOf(band) + 1, band, _session.Definition)} section, in points:",
            "Section height", band.Height.ToString("0.#", CultureInfo.InvariantCulture));
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double height) && height > 0)
        {
            _session.SetBandHeight(band.Type, height);
            Render();
        }
    }

    private ReportBand? CurrentBand()
        => _session is null ? null : _selected.Count > 0 && _session.Find(_selected[0]).Band is ReportBand band ? band : _session.Definition.BandOfType(TargetBand);

    private void ReportInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        string name = Microsoft.VisualBasic.Interaction.InputBox("Report name:", "Report name and description", _session.Definition.Name);
        if (name.Trim().Length == 0)
        {
            return;
        }

        string description = Microsoft.VisualBasic.Interaction.InputBox("Description (what the report shows; blank for none):", "Report name and description", _session.Definition.Description ?? string.Empty);
        if (_session.SetReportInfo(name, description) is string problem)
        {
            Status.Text = problem;
            return;
        }

        Render();
        Status.Text = $"Report named {name.Trim()}.";
    }

    private void Grouping_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        string text = Microsoft.VisualBasic.Interaction.InputBox("Group the records by (Field; Other desc; …). Blank removes the groups; add the group's header and footer sections from the Sections menu.",
            "Grouping", _session.GroupsText());
        if (_session.SetGroups(text) is string problem)
        {
            MessageBox.Show(this, problem, "Grouping", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Render();
    }

    // ---- Formatting toolbar ----
    private void SyncFormatBar()
    {
        _syncingFormat = true;
        try
        {
            ReportElement? element = _session is not null && _selected.Count > 0 ? _session.Find(_selected[0]).Element : null;
            FontBox.Text = element?.FontFamily ?? string.Empty;
            SizeBox.Text = element?.FontSize.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty;
            BoldButton.IsChecked = element?.Bold ?? false;
            ItalicButton.IsChecked = element?.Italic ?? false;
            UnderlineButton.IsChecked = element?.Underline ?? false;
            ColourBox.SelectedItem = element is null ? null : ColourBox.Items.OfType<string>().FirstOrDefault(c => c.Equals(element.Foreground, StringComparison.OrdinalIgnoreCase));
            PositionText.Text = element is null ? (_selected.Count == 0 ? "No selection" : string.Empty)
                : $"X {element.X / PointsPerMm:0.0}  Y {element.Y / PointsPerMm:0.0}  W {element.Width / PointsPerMm:0.0}  H {element.Height / PointsPerMm:0.0} mm{(_selected.Count > 1 ? $"  (+{_selected.Count - 1})" : string.Empty)}";
            ZoomText.Text = $"{ZoomSlider.Value * 100:0}%";
        }
        finally
        {
            _syncingFormat = false;
        }
    }

    private void ApplyFormat(string property, string? value)
    {
        if (_syncingFormat || !IsLoaded || _session is null || _selected.Count == 0 || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (_session.SetPropertyOnAll(_selected, property, value) is string problem)
        {
            Status.Text = problem;
            return;
        }

        Render();
    }

    private void Font_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender == FontBox && FontBox.SelectedItem is string family)
        {
            ApplyFormat(nameof(ReportElement.FontFamily), family);
        }
        else if (sender == SizeBox && SizeBox.SelectedItem is string size)
        {
            ApplyFormat(nameof(ReportElement.FontSize), size);
        }
    }

    private void Font_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ComboBox box)
        {
            ApplyFormat(box == FontBox ? nameof(ReportElement.FontFamily) : nameof(ReportElement.FontSize), box.Text);
            e.Handled = true;
        }
    }

    private void Style_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string property)
        {
            ApplyFormat(property, button.IsChecked == true ? "true" : "false");
        }
    }

    private void Colour_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ColourBox.SelectedItem is string colour)
        {
            ApplyFormat(nameof(ReportElement.Foreground), colour);
        }
    }

    private void HAlign_Click(object sender, RoutedEventArgs e) => ApplyFormat(nameof(ReportElement.HAlign), (sender as FrameworkElement)?.Tag as string);

    // ---- Variables pane ----
    private void BuildVariables()
    {
        VariablesTree.Items.Clear();
        if (_session is null)
        {
            return;
        }

        string filter = VariableFind.Text.Trim();
        bool Shown(string name) => filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        TreeViewItem Node(string header, IEnumerable<VariableNode> children)
        {
            TreeViewItem node = new() { Header = header, IsExpanded = true, FontWeight = FontWeights.SemiBold };
            foreach (VariableNode child in children.Where(c => Shown(c.Name)))
            {
                node.Items.Add(new TreeViewItem { Header = child.Name, Tag = child, FontWeight = FontWeights.Normal, ToolTip = child.Kind == "Field" ? "Double-click to add to the section" : child.Insert });
            }

            return node;
        }

        string source = _session.Definition.DataSources.FirstOrDefault()?.Name ?? "Data";
        VariablesTree.Items.Add(Node($"{source} fields", _session.Fields(_sample.FirstOrDefault()).Select(f => new VariableNode("Field", f, f))));
        VariablesTree.Items.Add(Node("Parameters", _session.Definition.Parameters.Select(p => new VariableNode("Expression", p.Name, $"Param(\"{p.Name}\")"))));
        VariablesTree.Items.Add(Node("System values", One4All.Reporting.Expressions.ReportExpressionEngine.Builtins.Select(b => new VariableNode("Expression", b, b))));
        VariablesTree.Items.Add(Node("Functions", One4All.Reporting.Expressions.ReportExpressionEngine.Functions.Select(f => new VariableNode("Function", f, f + "("))));
    }

    private void VariableFind_Changed(object sender, TextChangedEventArgs e) => BuildVariables();

    private void Variables_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (VariablesTree.SelectedItem is TreeViewItem { Tag: VariableNode { Kind: "Field" } node })
        {
            FieldList.SelectedItem = node.Name;
        }
    }

    private void Variables_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VariablesTree.SelectedItem is not TreeViewItem { Tag: VariableNode node } || _session is null)
        {
            return;
        }

        switch (node.Kind)
        {
            case "Field":
                FieldList.SelectedItem = node.Name;
                Field_DoubleClick(sender, e);
                break;
            case "Expression":
                ExpressionBox.Text = node.Insert;
                AddExpression_Click(sender, e);
                break;
            default:
                ExpressionBox.Text += node.Insert;
                ExpressionBox.CaretIndex = ExpressionBox.Text.Length;
                ExpressionBox.Focus();
                break;
        }

        e.Handled = true;
    }

    // ---- File Explorer ----
    private void BuildFiles()
    {
        FilesTree.Items.Clear();
        foreach (IGrouping<string, DesignerReport> module in _catalogue.GroupBy(r => r.Module ?? "Other").OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            TreeViewItem folder = new() { Header = $"{module.Key} ({module.Count()})", IsExpanded = _catalogue.Count < 40 };
            foreach (DesignerReport report in module.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                folder.Items.Add(new TreeViewItem { Header = report.Display, Tag = report, ToolTip = report.IsCompanyVersion ? "The company's own version" : "A system report; saving makes the company's version" });
            }

            FilesTree.Items.Add(folder);
        }
    }

    private void Files_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesTree.SelectedItem is TreeViewItem { Tag: DesignerReport report })
        {
            ReportBox.SelectedItem = report;
            e.Handled = true;
        }
    }

    // ---- Report Explorer ----
    private void BuildExplorer()
    {
        _buildingExplorer = true;
        try
        {
            ExplorerTree.Items.Clear();
            if (_session is null)
            {
                return;
            }

            TreeViewItem root = new() { Header = $"{_session.Definition.Name} ({_session.Definition.Code})", IsExpanded = true, FontWeight = FontWeights.SemiBold };
            TreeViewItem page = new() { Header = "Page 1", IsExpanded = true, FontWeight = FontWeights.Normal };
            int number = 0;
            foreach (ReportBand band in _session.Definition.Bands)
            {
                number++;
                TreeViewItem section = new() { Header = DesignerCatalogue.BandCaption(number, band, _session.Definition), Tag = band.Type, IsExpanded = band.Elements.Any(el => _selected.Contains(el.Name)) };
                foreach (ReportElement element in band.Elements)
                {
                    string caption = element.Kind switch
                    {
                        ElementKind.Field or ElementKind.Checkbox or ElementKind.Barcode => $"{element.Name} [{element.FieldName}]",
                        ElementKind.Expression => $"{element.Name} = {element.Expression}",
                        ElementKind.Label or ElementKind.RichText => $"{element.Name} \"{element.Text}\"",
                        _ => $"{element.Name} ({element.Kind})",
                    };
                    section.Items.Add(new TreeViewItem { Header = caption, Tag = element.Name, IsSelected = _selected.Count > 0 && _selected[0] == element.Name });
                }

                page.Items.Add(section);
            }

            root.Items.Add(page);
            ExplorerTree.Items.Add(root);
        }
        finally
        {
            _buildingExplorer = false;
        }
    }

    private void Explorer_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_buildingExplorer || _session is null || ExplorerTree.SelectedItem is not TreeViewItem item)
        {
            return;
        }

        if (item.Tag is string name && !(_selected.Count == 1 && _selected[0] == name))
        {
            Select(name);
        }
        else if (item.Tag is BandType band)
        {
            BandBox.SelectedItem = band;
        }
    }

    // ---- Properties: categorised or alphabetical, with a description of the focused property ----
    private void PropertyView_Click(object sender, RoutedEventArgs e)
    {
        bool categorised = sender == CategorizedButton ? CategorizedButton.IsChecked == true : AlphabeticalButton.IsChecked != true;
        CategorizedButton.IsChecked = categorised;
        AlphabeticalButton.IsChecked = !categorised;
        System.ComponentModel.ICollectionView view = System.Windows.Data.CollectionViewSource.GetDefaultView(_properties);
        view.GroupDescriptions.Clear();
        view.SortDescriptions.Clear();
        if (categorised)
        {
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(PropertyRow.Category)));
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(PropertyRow.Category), System.ComponentModel.ListSortDirection.Ascending));
        }

        view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(PropertyRow.Name), System.ComponentModel.ListSortDirection.Ascending));
    }

    private void Property_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: string property })
        {
            PropertyHelpTitle.Text = property;
            PropertyHelpText.Text = DesignerCatalogue.Describe(property);
        }
    }
}
