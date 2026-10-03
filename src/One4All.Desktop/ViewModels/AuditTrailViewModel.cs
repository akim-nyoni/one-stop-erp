using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Audit;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>An audit row (pre-formatted for display), keeping the full entry for the detail pane.</summary>
/// <param name="Entry">The entry.</param>
public sealed record AuditRowVm(AuditEntryView Entry)
{
    /// <summary>When, local time.</summary>
    public string When => Entry.OccurredOnUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    /// <summary>Who.</summary>
    public string User => Entry.UserName;

    /// <summary>What.</summary>
    public string Action => Entry.Action.ToString();

    /// <summary>The record affected.</summary>
    public string Record => string.IsNullOrEmpty(Entry.EntityId) ? Entry.EntityType : $"{Entry.EntityType} #{Entry.EntityId}";

    /// <summary>The fields that changed (updates).</summary>
    public string Fields => string.Join(", ", Entry.ChangedFields);
}

/// <summary>One field's before / after in the detail pane.</summary>
/// <param name="Field">The field.</param>
/// <param name="Before">The old value, or blank.</param>
/// <param name="After">The new value, or blank.</param>
public sealed record AuditFieldVm(string Field, string Before, string After);

/// <summary>
/// Backs the audit trail screen: filter the change log by date, record type / id, user and action, inspect an
/// entry's before / after values, and verify the tamper-evident hash chain. Everything comes from the tested
/// <see cref="IAuditService"/>.
/// </summary>
public sealed partial class AuditTrailViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public AuditTrailViewModel(IServiceProvider services) => _services = services;

    /// <summary>The entries.</summary>
    public ObservableCollection<AuditRowVm> Entries { get; } = [];

    /// <summary>The selected entry's fields.</summary>
    public ObservableCollection<AuditFieldVm> Fields { get; } = [];

    /// <summary>Action filter choices (null = any).</summary>
    public IReadOnlyList<AuditAction?> Actions { get; } = [null, .. Enum.GetValues<AuditAction>().Cast<AuditAction?>()];

    /// <summary>From date.</summary>
    [ObservableProperty]
    private DateTime? _fromDate = DateTime.Today.AddDays(-7);

    /// <summary>To date.</summary>
    [ObservableProperty]
    private DateTime? _toDate = DateTime.Today;

    /// <summary>Record type filter (e.g. Customer).</summary>
    [ObservableProperty]
    private string _entityType = string.Empty;

    /// <summary>Record id filter.</summary>
    [ObservableProperty]
    private string _entityId = string.Empty;

    /// <summary>User filter.</summary>
    [ObservableProperty]
    private string _userName = string.Empty;

    /// <summary>Action filter.</summary>
    [ObservableProperty]
    private AuditAction? _action;

    /// <summary>The selected entry.</summary>
    [ObservableProperty]
    private AuditRowVm? _selectedEntry;

    /// <summary>The chain verification banner.</summary>
    [ObservableProperty]
    private string _chainStatus = "Chain not yet verified.";

    /// <summary>True when the last verification found the chain intact.</summary>
    [ObservableProperty]
    private bool? _chainIntact;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>True while working.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(VerifyCommand))]
    private bool _isBusy;

    private bool CanRun => !IsBusy;

    partial void OnSelectedEntryChanged(AuditRowVm? value)
    {
        Fields.Clear();
        if (value is null)
        {
            return;
        }

        Dictionary<string, string> before = Parse(value.Entry.OldValues);
        Dictionary<string, string> after = Parse(value.Entry.NewValues);
        foreach (string field in before.Keys.Union(after.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            Fields.Add(new AuditFieldVm(field, before.GetValueOrDefault(field, string.Empty), after.GetValueOrDefault(field, string.Empty)));
        }
    }

    /// <summary>Runs the search.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task SearchAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            if (_companyId == 0)
            {
                One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
                _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
                if (_companyId == 0)
                {
                    StatusMessage = "No company is set up.";
                    return;
                }
            }

            IReadOnlyList<AuditEntryView> entries = await scope.ServiceProvider.GetRequiredService<IAuditService>().QueryAsync(new AuditQuery
            {
                CompanyId = _companyId,
                From = FromDate is { } f ? DateOnly.FromDateTime(f) : null,
                To = ToDate is { } t ? DateOnly.FromDateTime(t) : null,
                EntityType = EntityType,
                EntityId = EntityId,
                UserName = UserName,
                Action = Action,
                Take = 1_000,
            }, ct).ConfigureAwait(true);

            Entries.Clear();
            foreach (AuditEntryView entry in entries)
            {
                Entries.Add(new AuditRowVm(entry));
            }

            SelectedEntry = null;
            StatusMessage = entries.Count >= 1_000 ? "Showing the newest 1,000 entries — narrow the filter to see more." : $"{entries.Count} entr{(entries.Count == 1 ? "y" : "ies")}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not read the audit trail: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Verifies the company's hash chain.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task VerifyAsync()
    {
        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            AuditChainVerification result = await scope.ServiceProvider.GetRequiredService<IAuditService>().VerifyChainAsync(_companyId == 0 ? null : _companyId).ConfigureAwait(true);
            ChainIntact = result.IsIntact;
            ChainStatus = result.Message;
        }
        catch (Exception ex)
        {
            ChainIntact = false;
            ChainStatus = $"Verification failed to run: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Dictionary<string, string> Parse(string? json)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return values;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.GetRawText();
            }
        }
        catch (JsonException)
        {
            values["(raw)"] = json;
        }

        return values;
    }
}
