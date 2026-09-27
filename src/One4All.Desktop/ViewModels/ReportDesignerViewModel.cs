using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Core.Common;
using One4All.Core.Reporting;
using One4All.Infrastructure.Persistence;
using One4All.Reporting;
using One4All.Reporting.Model;

namespace One4All.Desktop.ViewModels;

/// <summary>One editable column on the report designer canvas.</summary>
public sealed partial class DesignColumn : ObservableObject
{
    /// <summary>Creates a design column.</summary>
    /// <param name="field">The bound data field.</param>
    /// <param name="header">The column header.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="money">Whether it is a right-aligned, N2, totalled money column.</param>
    public DesignColumn(string field, string header, double width, bool money)
    {
        _field = field;
        _header = header;
        _width = width;
        _money = money;
    }

    /// <summary>The bound data field name.</summary>
    [ObservableProperty]
    private string _field;

    /// <summary>The column header text.</summary>
    [ObservableProperty]
    private string _header;

    /// <summary>The column width in points.</summary>
    [ObservableProperty]
    private double _width;

    /// <summary>Whether this is a money column.</summary>
    [ObservableProperty]
    private bool _money;
}

/// <summary>
/// Backs the WPF report designer: open a catalogue listing report, edit and drag-reorder its columns on a
/// design canvas, then save it as a company-scoped variant that the report viewer can run. Layout is rebuilt
/// through the same <see cref="SystemReports.Listing"/> the system reports use, so a designed report renders
/// identically. Only the listing (columnar) shape is edited; the report's data command and parameters are
/// preserved from the source so the existing data provider keeps serving it.
/// </summary>
public sealed partial class ReportDesignerViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;
    private string _command = string.Empty;
    private (string Name, ParameterType Type)[] _parameters = [];

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider.</param>
    public ReportDesignerViewModel(IServiceProvider services) => _services = services;

    /// <summary>The catalogue reports available to open as a starting point.</summary>
    public ObservableCollection<ReportListItem> SourceReports { get; } = [];

    /// <summary>The columns being designed, in render order.</summary>
    public ObservableCollection<DesignColumn> Columns { get; } = [];

    /// <summary>The source report to open; selecting it loads its columns for editing.</summary>
    [ObservableProperty]
    private ReportListItem? _selectedSource;

    /// <summary>The currently selected column (for the property editor and moves).</summary>
    [ObservableProperty]
    private DesignColumn? _selectedColumn;

    /// <summary>The report code to save under (same as the source overrides it; a new code makes a variant).</summary>
    [ObservableProperty]
    private string _code = string.Empty;

    /// <summary>The report name.</summary>
    [ObservableProperty]
    private string _reportName = string.Empty;

    /// <summary>The owning module.</summary>
    [ObservableProperty]
    private string _module = string.Empty;

    /// <summary>A status line for the footer.</summary>
    [ObservableProperty]
    private string _statusMessage = "Open a report to start designing.";

    /// <summary>Loads the catalogue reports and the working company.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

        _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);

        List<ReportListItem> items = await db.ReportDefinitions
            .Where(r => r.Scope == ReportScope.System)
            .OrderBy(r => r.Module).ThenBy(r => r.Name)
            .Select(r => new ReportListItem(r.Code, r.Name, r.Module, r.DefinitionJson))
            .ToListAsync(ct).ConfigureAwait(true);

        SourceReports.Clear();
        foreach (ReportListItem item in items)
        {
            SourceReports.Add(item);
        }
    }

    partial void OnSelectedSourceChanged(ReportListItem? value)
    {
        Columns.Clear();
        if (value is null)
        {
            return;
        }

        ReportDefinition? definition = TryDeserialize(value.DefinitionJson);
        if (definition is null)
        {
            StatusMessage = $"Could not read {value.Code}.";
            return;
        }

        SystemReports.Column[] extracted = SystemReports.ExtractListingColumns(definition);
        if (extracted.Length == 0)
        {
            StatusMessage = $"{value.Code} is not a column listing and cannot be designed here.";
            return;
        }

        foreach (SystemReports.Column column in extracted)
        {
            Columns.Add(new DesignColumn(column.Field, column.Header, column.Width, column.Money));
        }

        Code = definition.Code;
        ReportName = definition.Name;
        Module = definition.Module ?? string.Empty;
        _command = SystemReports.CommandOf(definition);
        _parameters = definition.Parameters.Select(p => (p.Name, p.Type)).ToArray();
        SelectedColumn = Columns.FirstOrDefault();
        StatusMessage = $"Editing {extracted.Length} column(s) of {definition.Code}. Drag columns to reorder.";
    }

    /// <summary>Moves a column to a new index (used by drag-and-drop on the canvas).</summary>
    /// <param name="column">The column being moved.</param>
    /// <param name="targetIndex">The index to move it to.</param>
    public void MoveColumn(DesignColumn column, int targetIndex)
    {
        int from = Columns.IndexOf(column);
        if (from < 0)
        {
            return;
        }

        targetIndex = Math.Clamp(targetIndex, 0, Columns.Count - 1);
        if (from == targetIndex)
        {
            return;
        }

        Columns.Move(from, targetIndex);
        SelectedColumn = column;
    }

    [RelayCommand]
    private void MoveLeft()
    {
        if (SelectedColumn is { } c)
        {
            MoveColumn(c, Columns.IndexOf(c) - 1);
        }
    }

    [RelayCommand]
    private void MoveRight()
    {
        if (SelectedColumn is { } c)
        {
            MoveColumn(c, Columns.IndexOf(c) + 1);
        }
    }

    [RelayCommand]
    private void AddColumn()
    {
        DesignColumn column = new("NewField", "New Column", 100, false);
        Columns.Add(column);
        SelectedColumn = column;
    }

    [RelayCommand]
    private void RemoveColumn()
    {
        if (SelectedColumn is { } c)
        {
            int index = Columns.IndexOf(c);
            Columns.Remove(c);
            SelectedColumn = Columns.Count == 0 ? null : Columns[Math.Clamp(index, 0, Columns.Count - 1)];
        }
    }

    /// <summary>Saves the design as a company-scoped report variant.</summary>
    /// <returns>Success, or a validation error.</returns>
    public async Task<Result> SaveAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(ReportName))
        {
            return Result.Failure(Error.Validation("Design.Missing", "A report code and name are required."));
        }

        if (Columns.Count == 0 || Columns.Any(c => string.IsNullOrWhiteSpace(c.Field)))
        {
            return Result.Failure(Error.Validation("Design.Columns", "Add at least one column, and give every column a field."));
        }

        if (string.IsNullOrWhiteSpace(_command))
        {
            return Result.Failure(Error.Rule("Design.NoSource", "Open a source report first — a designed report reuses its data source."));
        }

        SystemReports.Column[] columns = Columns.Select(c => new SystemReports.Column(c.Field.Trim(), c.Header, c.Width, c.Money)).ToArray();
        ReportDefinition definition = SystemReports.Listing(Code.Trim(), ReportName.Trim(), Module.Trim(), string.Empty, _command, columns, _parameters);
        string json = ReportDefinitionSerializer.Serialize(definition);

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

        // A company-scoped record with this code overrides the system report for this company; replace any prior one.
        List<ReportDefinitionRecord> existing = await db.ReportDefinitions
            .Where(r => r.Scope == ReportScope.Company && r.CompanyId == _companyId && r.Code == definition.Code)
            .ToListAsync(ct).ConfigureAwait(true);
        db.ReportDefinitions.RemoveRange(existing);

        db.ReportDefinitions.Add(new ReportDefinitionRecord(definition.Code, definition.Name, json, ReportScope.Company, companyId: _companyId, module: string.IsNullOrWhiteSpace(Module) ? null : Module.Trim()));
        await db.SaveChangesAsync(ct).ConfigureAwait(true);

        StatusMessage = $"Saved company report {definition.Code} ({columns.Length} columns).";
        return Result.Success();
    }

    private static ReportDefinition? TryDeserialize(string json)
    {
        try
        {
            return ReportDefinitionSerializer.Deserialize(json);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
