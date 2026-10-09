using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Core.Reporting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Reporting.Model;

namespace One4All.Desktop.ViewModels;

/// <summary>One report in the picker (with its serialised definition, used to build the parameter prompts).</summary>
/// <param name="Code">The report code.</param>
/// <param name="Name">The report name.</param>
/// <param name="Module">The owning module (for grouping).</param>
/// <param name="DefinitionJson">The serialised <c>.mrpt</c> definition.</param>
public sealed record ReportListItem(string Code, string Name, string? Module, string DefinitionJson)
{
    /// <summary>The display label in the picker.</summary>
    public string Display => $"{Name}  ({Code})";
}

/// <summary>An editable value for one report parameter.</summary>
public sealed partial class ReportParameterInput : ObservableObject
{
    /// <summary>Creates a parameter input.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="prompt">The prompt shown to the user.</param>
    /// <param name="type">The parameter type.</param>
    /// <param name="value">The initial value.</param>
    public ReportParameterInput(string name, string prompt, ParameterType type, string value)
    {
        Name = name;
        Prompt = prompt;
        Type = type;
        _value = value;
        Definition = new ReportParameter { Name = name, Type = type, Prompt = prompt };
    }

    /// <summary>Creates a parameter input from the report's parameter.</summary>
    /// <param name="parameter">The parameter.</param>
    public ReportParameterInput(ReportParameter parameter)
        : this(parameter.Name, string.IsNullOrWhiteSpace(parameter.Prompt) ? parameter.Name : parameter.Prompt!, parameter.Type, parameter.DefaultValue ?? string.Empty)
        => Definition = parameter;

    /// <summary>The report's parameter.</summary>
    public ReportParameter Definition { get; }

    /// <summary>The choices from the parameter's list-of-values query.</summary>
    public ObservableCollection<ParameterChoice> Options { get; } = [];

    /// <summary>Whether the parameter offers a list of choices.</summary>
    public bool HasLookup => !string.IsNullOrWhiteSpace(Definition.LookupQuery);

    /// <summary>Whether the parameter is typed as text (no list).</summary>
    public bool IsPlain => !HasLookup;

    /// <summary>The parameter name (key passed to the runner).</summary>
    public string Name { get; }

    /// <summary>The prompt label.</summary>
    public string Prompt { get; }

    /// <summary>The parameter type.</summary>
    public ParameterType Type { get; }

    /// <summary>A hint describing the expected input.</summary>
    public string Hint => (Type switch
    {
        ParameterType.Date => "yyyy-mm-dd",
        ParameterType.Integer => "whole number",
        ParameterType.Decimal => "number",
        ParameterType.Boolean => "true / false",
        _ => "text",
    }) + (Definition.MultiSelect ? ", several separated by commas" : string.Empty) + (LookupNote is { Length: > 0 } note ? " — " + note : string.Empty);

    /// <summary>Why the list is empty (waiting on another parameter, refused), shown under the input.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hint))]
    private string _lookupNote = string.Empty;

    /// <summary>The current text value.</summary>
    [ObservableProperty]
    private string _value;

    /// <summary>Raised when the value changes (a dependent list reloads).</summary>
    public event EventHandler? ValueChanged;

    partial void OnValueChanged(string value) => ValueChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>One choice in a parameter's list.</summary>
/// <param name="Value">The value passed to the report.</param>
/// <param name="Label">What the user sees.</param>
public sealed record ParameterChoice(string Value, string Label)
{
    /// <summary>The text in the drop-down.</summary>
    public string Display => Label == Value ? Value : $"{Value} — {Label}";
}

/// <summary>
/// Backs the WPF report viewer: lists the catalogue's system reports, prompts their parameters, and runs them
/// through the generic <see cref="IReportRunner"/> to preview (HTML) on screen and export (HTML/PDF/CSV/text).
/// All rendering goes through the same runner the server uses, so the desktop shows exactly the same output.
/// </summary>
public sealed partial class ReportViewerViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per run).</param>
    public ReportViewerViewModel(IServiceProvider services) => _services = services;

    /// <summary>The catalogue's system reports.</summary>
    public ObservableCollection<ReportListItem> Reports { get; } = [];

    /// <summary>The parameter inputs for the selected report.</summary>
    public ObservableCollection<ReportParameterInput> Parameters { get; } = [];

    /// <summary>The export formats offered.</summary>
    public IReadOnlyList<ReportFormat> AvailableFormats { get; } =
        [ReportFormat.Html, ReportFormat.Pdf, ReportFormat.XlsxLayout, ReportFormat.Docx, ReportFormat.CsvLayout, ReportFormat.Xlsx, ReportFormat.Csv, ReportFormat.Text];

    /// <summary>The selected report; changing it rebuilds the parameter prompts.</summary>
    [ObservableProperty]
    private ReportListItem? _selectedReport;

    /// <summary>The export format for the Save action.</summary>
    [ObservableProperty]
    private ReportFormat _selectedFormat = ReportFormat.Pdf;

    /// <summary>A status line for the footer.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Row filters for any report, by its columns: "Branch=HQ; Currency=USD; CostCentre=ADM".</summary>
    [ObservableProperty]
    private string _rowFilter = string.Empty;

    /// <summary>True while a report is running.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Loads the report catalogue and resolves the working company.</summary>
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

        // The company's report set (its industries' documents and key reports) comes first, starred.
        HashSet<string> featured = [.. await db.FeaturedReports.AsNoTracking().Where(f => f.CompanyId == _companyId).Select(f => f.ReportCode).ToListAsync(ct).ConfigureAwait(true)];
        items = [.. items.Where(i => featured.Contains(i.Code)).Select(i => i with { Name = "★ " + i.Name }), .. items.Where(i => !featured.Contains(i.Code))];

        Reports.Clear();
        foreach (ReportListItem item in items)
        {
            Reports.Add(item);
        }

        StatusMessage = $"{Reports.Count} report(s) in the catalogue.";
    }

    partial void OnSelectedReportChanged(ReportListItem? value)
    {
        Parameters.Clear();
        if (value is null)
        {
            return;
        }

        ReportDefinition? definition = TryDeserialize(value.DefinitionJson);
        foreach (ReportParameter parameter in definition?.Parameters ?? [])
        {
            ReportParameterInput input = new(parameter);
            input.ValueChanged += (_, _) => ReloadDependants(input.Name);
            Parameters.Add(input);
        }

        foreach (ReportParameterInput input in Parameters.Where(p => p.HasLookup))
        {
            _ = LoadOptionsAsync(input);
        }
    }

    private CancellationTokenSource? _reload;

    // A parent's value changed: once typing pauses, the lists that depend on it reload.
    private void ReloadDependants(string parent)
    {
        List<ReportParameterInput> dependants = [.. Parameters.Where(p => p.HasLookup && p.Definition.DependsOn.Contains(parent, StringComparer.OrdinalIgnoreCase))];
        if (dependants.Count == 0)
        {
            return;
        }

        _reload?.Cancel();
        CancellationTokenSource reload = _reload = new CancellationTokenSource();
        _ = Task.Delay(400, reload.Token).ContinueWith(
            async t =>
            {
                if (!t.IsCanceled)
                {
                    foreach (ReportParameterInput dependant in dependants)
                    {
                        await LoadOptionsAsync(dependant).ConfigureAwait(true);
                    }
                }
            },
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // The parameter's choices, from its list-of-values query run with the values entered so far.
    private async Task LoadOptionsAsync(ReportParameterInput input)
    {
        if (SelectedReport is not ReportListItem report)
        {
            return;
        }

        Dictionary<string, string> entered = Parameters.Where(p => !ReferenceEquals(p, input) && !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Name, p => p.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<IReadOnlyList<ParameterOption>> options = await scope.ServiceProvider.GetRequiredService<IReportParameterLookupService>()
                .OptionsAsync(_companyId, report.Code, input.Name, entered).ConfigureAwait(true);
            input.Options.Clear();
            if (options.IsFailure)
            {
                input.LookupNote = options.Error.Message;
                return;
            }

            foreach (ParameterOption option in options.Value)
            {
                input.Options.Add(new ParameterChoice(option.Value, option.Label));
            }

            input.LookupNote = input.Options.Count == 0 ? "no choices" : string.Empty;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Npgsql.NpgsqlException or TimeoutException)
        {
            input.LookupNote = "the list could not be loaded: " + ex.Message;
        }
    }

    /// <summary>Runs the selected report and shows the HTML preview.</summary>
    [RelayCommand]
    private async Task RunAsync()
    {
        if (SelectedReport is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Running…";
        try
        {
            Result<RenderedDocument> result = await RenderAsync(ReportFormat.Layout, ReportRunPurpose.View).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                Layout = null;
                return;
            }

            Layout = One4All.Reporting.Engine.ReportLayout.Deserialize(result.Value.Content);
            StatusMessage = $"Rendered {SelectedReport.Code} at {DateTime.Now:HH:mm:ss}. Blue values open their source.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Gets the laid-out report on screen (null before a run or after a failure).</summary>
    [ObservableProperty]
    private One4All.Reporting.Engine.RenderModel? _layout;

    /// <summary>Gets the company the reports run for.</summary>
    public long CompanyId => _companyId;

    /// <summary>
    /// Runs the report again for printing, through the audited runner (so the print, or numbered reprint, is in the run log), returning the
    /// laid-out report to print and its copy number.
    /// </summary>
    /// <returns>The layout and copy number, or why not.</returns>
    public async Task<Result<(One4All.Reporting.Engine.RenderModel Layout, int? Copy)>> RenderForPrintAsync()
    {
        if (SelectedReport is null)
        {
            return Error.Validation("Report.None", "No report is selected.");
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        long? userId = _services.GetService<DesktopSession>()?.UserId;
        Result<AuditedReport> run = await scope.ServiceProvider.GetRequiredService<IAuditedReportRunner>()
            .RunAsync(_companyId, SelectedReport.Code, BuildParameters(), ReportFormat.Layout, ReportRunPurpose.Print, userId).ConfigureAwait(true);
        return run.IsSuccess ? (One4All.Reporting.Engine.ReportLayout.Deserialize(run.Value.Document.Content), run.Value.CopyNumber) : run.Error;
    }

    /// <summary>Renders the selected report in the chosen export format (for saving to a file).</summary>
    /// <param name="format">The export format.</param>
    /// <returns>The rendered document, or an error.</returns>
    public async Task<Result<RenderedDocument>> RenderForExportAsync(ReportFormat format)
    {
        if (SelectedReport is null)
        {
            return Error.Validation("Report.None", "No report is selected.");
        }

        return await RenderAsync(format, ReportRunPurpose.Export).ConfigureAwait(true);
    }

    // Every preview and export goes through the audited runner as the signed-in user, so it is permission-checked and in
    // the report run log (brief §13.3).
    private async Task<Result<RenderedDocument>> RenderAsync(ReportFormat format, ReportRunPurpose purpose)
    {
        ReportListItem report = SelectedReport!;
        Dictionary<string, object?> parameters = BuildParameters();

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IAuditedReportRunner runner = scope.ServiceProvider.GetRequiredService<IAuditedReportRunner>();
        long? userId = _services.GetService<DesktopSession>()?.UserId;
        Result<AuditedReport> run = await runner.RunAsync(_companyId, report.Code, parameters, format, purpose, userId).ConfigureAwait(true);
        return run.IsSuccess ? run.Value.Document : run.Error;
    }

    private Dictionary<string, object?> BuildParameters()
    {
        Dictionary<string, object?> parameters = new(StringComparer.OrdinalIgnoreCase);
        foreach (ReportParameterInput input in Parameters)
        {
            if (string.IsNullOrWhiteSpace(input.Value))
            {
                continue; // let the report's own defaults apply
            }

            parameters[input.Name] = ReportParameterValues.Parse(input.Definition, input.Value);
        }

        foreach (KeyValuePair<string, object?> filter in Application.Reporting.ReportRowFilters.Parse(RowFilter))
        {
            parameters[filter.Key] = filter.Value;
        }

        return parameters;
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
