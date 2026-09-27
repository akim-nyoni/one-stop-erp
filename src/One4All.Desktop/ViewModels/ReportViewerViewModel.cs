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
    }

    /// <summary>The parameter name (key passed to the runner).</summary>
    public string Name { get; }

    /// <summary>The prompt label.</summary>
    public string Prompt { get; }

    /// <summary>The parameter type.</summary>
    public ParameterType Type { get; }

    /// <summary>A hint describing the expected input.</summary>
    public string Hint => Type switch
    {
        ParameterType.Date => "yyyy-mm-dd",
        ParameterType.Integer => "whole number",
        ParameterType.Decimal => "number",
        ParameterType.Boolean => "true / false",
        _ => "text",
    };

    /// <summary>The current text value.</summary>
    [ObservableProperty]
    private string _value;
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
        [ReportFormat.Html, ReportFormat.Pdf, ReportFormat.Csv, ReportFormat.Text];

    /// <summary>The selected report; changing it rebuilds the parameter prompts.</summary>
    [ObservableProperty]
    private ReportListItem? _selectedReport;

    /// <summary>The export format for the Save action.</summary>
    [ObservableProperty]
    private ReportFormat _selectedFormat = ReportFormat.Pdf;

    /// <summary>The rendered HTML shown in the preview pane.</summary>
    [ObservableProperty]
    private string _previewHtml = "<html><body style='font-family:Segoe UI;color:#667;padding:24px'>Select a report and click <b>Run</b>.</body></html>";

    /// <summary>A status line for the footer.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

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
            Parameters.Add(new ReportParameterInput(
                parameter.Name,
                string.IsNullOrWhiteSpace(parameter.Prompt) ? parameter.Name : parameter.Prompt!,
                parameter.Type,
                parameter.DefaultValue ?? string.Empty));
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
            Result<RenderedDocument> result = await RenderAsync(ReportFormat.Html).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.ToString();
                PreviewHtml = $"<html><body style='font-family:Segoe UI;color:#b00;padding:24px'>{System.Net.WebUtility.HtmlEncode(result.Error.Message)}</body></html>";
                return;
            }

            PreviewHtml = Encoding.UTF8.GetString(result.Value.Content);
            StatusMessage = $"Rendered {SelectedReport.Code} at {DateTime.Now:HH:mm:ss}.";
        }
        finally
        {
            IsBusy = false;
        }
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

        return await RenderAsync(format).ConfigureAwait(true);
    }

    private async Task<Result<RenderedDocument>> RenderAsync(ReportFormat format)
    {
        ReportListItem report = SelectedReport!;
        Dictionary<string, object?> parameters = BuildParameters();

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        return await runner.RenderAsync(_companyId, report.Code, parameters, format).ConfigureAwait(true);
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

            string raw = input.Value.Trim();
            parameters[input.Name] = input.Type switch
            {
                ParameterType.Date when DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out DateOnly d) => d,
                ParameterType.Integer when long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i) => i,
                ParameterType.Decimal when decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal m) => m,
                ParameterType.Boolean when bool.TryParse(raw, out bool b) => b,
                _ => raw,
            };
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
