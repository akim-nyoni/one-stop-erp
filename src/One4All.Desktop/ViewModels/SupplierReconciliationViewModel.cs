using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A reconciliation line (pre-formatted for display).</summary>
/// <param name="Status">The outcome, in words.</param>
/// <param name="Document">The document number.</param>
/// <param name="Date">The document date.</param>
/// <param name="Theirs">The supplier's amount, or blank.</param>
/// <param name="Ours">Our amount, or blank.</param>
/// <param name="Difference">Theirs less ours, or blank when nil.</param>
/// <param name="IsAgreed">Whether the line agrees (drives colouring).</param>
public sealed record SupplierReconRowVm(string Status, string Document, string Date, string Theirs, string Ours, string Difference, bool IsAgreed);

/// <summary>
/// Backs the supplier statement reconciliation screen: choose a supplier, paste their statement
/// (document, date, amount per line), optionally auto-allocate the supplier first, and reconcile. Parsing,
/// allocation and reconciliation all live in tested Application/Infrastructure code; this class forwards
/// and formats.
/// </summary>
public sealed partial class SupplierReconciliationViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public SupplierReconciliationViewModel(IServiceProvider services) => _services = services;

    /// <summary>The suppliers.</summary>
    public ObservableCollection<SupplierChoiceVm> Suppliers { get; } = [];

    /// <summary>The reconciliation lines.</summary>
    public ObservableCollection<SupplierReconRowVm> Lines { get; } = [];

    /// <summary>The selected supplier.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReconcileCommand), nameof(AutoAllocateCommand))]
    private SupplierChoiceVm? _selectedSupplier;

    /// <summary>The pasted statement text.</summary>
    [ObservableProperty]
    private string _statementText = string.Empty;

    /// <summary>The date format in the pasted text.</summary>
    [ObservableProperty]
    private string _dateFormat = "yyyy-MM-dd";

    /// <summary>Whether the first pasted line is a header.</summary>
    [ObservableProperty]
    private bool _hasHeader;

    /// <summary>Whether the text is tab-separated (pasted from a spreadsheet).</summary>
    [ObservableProperty]
    private bool _tabSeparated;

    /// <summary>The supplier's statement total.</summary>
    [ObservableProperty]
    private string _statementBalance = "–";

    /// <summary>Our ledger balance.</summary>
    [ObservableProperty]
    private string _ledgerBalance = "–";

    /// <summary>The difference.</summary>
    [ObservableProperty]
    private string _difference = "–";

    /// <summary>True when everything agrees.</summary>
    [ObservableProperty]
    private bool _isAgreed;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>True while working.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReconcileCommand), nameof(AutoAllocateCommand))]
    private bool _isBusy;

    private bool CanRun => SelectedSupplier is not null && !IsBusy;

    /// <summary>Loads the suppliers.</summary>
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
            if (_companyId == 0)
            {
                StatusMessage = "No company is set up.";
                return;
            }

            Suppliers.Clear();
            foreach (var s in await db.Suppliers.Where(s => s.CompanyId == _companyId).OrderBy(s => s.Code).Select(s => new { s.Id, s.Code, s.Name }).ToListAsync(ct).ConfigureAwait(true))
            {
                Suppliers.Add(new SupplierChoiceVm(s.Id, $"{s.Code} — {s.Name}"));
            }

            StatusMessage = "Choose a supplier, paste their statement (document, date, amount per line) and press Reconcile.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load suppliers: {ex.Message}";
        }
    }

    /// <summary>Auto-allocates the supplier's payments and credits against their invoices.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task AutoAllocateAsync()
    {
        SupplierChoiceVm? supplier = SelectedSupplier;
        if (supplier is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<AutoAllocationResult> result = await scope.ServiceProvider.GetRequiredService<IAutoAllocationService>()
                .AllocateTraderAsync(_companyId, TradingLedger.AccountsPayable, supplier.Id, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true);
            StatusMessage = result.IsFailure
                ? result.Error.Message
                : $"Auto-allocated {result.Value.Allocations} pair(s), {result.Value.AmountAllocated.ToString("N2", CultureInfo.CurrentCulture)} in total"
                  + (result.Value.Failures.Count > 0 ? $"; {result.Value.Failures.Count} refused." : ".");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Auto-allocation failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Parses the pasted statement and reconciles it to the ledger.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task ReconcileAsync()
    {
        SupplierChoiceVm? supplier = SelectedSupplier;
        if (supplier is null)
        {
            return;
        }

        SupplierStatementParseResult parsed = SupplierStatementText.Parse(StatementText, DateFormat, HasHeader, TabSeparated ? '\t' : ',');
        if (!parsed.IsClean)
        {
            StatusMessage = $"{parsed.Errors.Count} statement line(s) could not be read — "
                + string.Join("; ", parsed.Errors.Take(3).Select(e => e.LineNumber > 0 ? $"line {e.LineNumber}: {e.Reason}" : e.Reason));
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<SupplierReconciliation> result = await scope.ServiceProvider.GetRequiredService<ISupplierStatementService>()
                .ReconcileAsync(_companyId, supplier.Id, parsed.Lines).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            SupplierReconciliation recon = result.Value;
            Lines.Clear();
            foreach (SupplierReconLine line in recon.Lines)
            {
                Lines.Add(new SupplierReconRowVm(
                    Describe(line),
                    line.DocumentNumber,
                    line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    line.StatementAmount is { } t ? Money(t) : string.Empty,
                    line.LedgerAmount is { } o ? Money(o) : string.Empty,
                    line.Difference == 0m ? string.Empty : Money(line.Difference),
                    line.Status == SupplierReconStatus.Agreed));
            }

            StatementBalance = Money(recon.StatementBalance);
            LedgerBalance = Money(recon.LedgerBalance);
            Difference = Money(recon.Difference);
            IsAgreed = recon.IsAgreed;
            int open = recon.Lines.Count(l => l.Status != SupplierReconStatus.Agreed);
            StatusMessage = recon.IsAgreed ? "Statement agrees with the ledger." : $"{open} line(s) to investigate.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Reconciliation failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Describe(SupplierReconLine line) => line.Status switch
    {
        SupplierReconStatus.Agreed => line.MatchedByAmount ? "Agreed (by amount)" : "Agreed",
        SupplierReconStatus.AmountDiffers => "Amount differs",
        SupplierReconStatus.MissingFromLedger => "Not on our ledger",
        SupplierReconStatus.NotOnStatement => "Not on their statement",
        _ => line.Status.ToString(),
    };

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);
}
