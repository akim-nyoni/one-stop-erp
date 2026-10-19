using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A bank to pay from.</summary>
/// <param name="GlAccountCode">The bank's GL account.</param>
/// <param name="Label">Code, name and currency.</param>
public sealed record PayFromBankVm(string GlAccountCode, string Label);

/// <summary>A proposal row (pre-formatted for display).</summary>
/// <param name="Supplier">Code and name.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Invoices">Invoices due.</param>
/// <param name="Due">Amount due.</param>
/// <param name="Credits">Credits applied (of those available).</param>
/// <param name="ToPay">Amount to pay.</param>
/// <param name="Status">"Pay", "Settled by credits" or "On hold".</param>
/// <param name="OnHold">Whether the supplier is held (drives row colouring).</param>
public sealed record PaymentProposalRowVm(string Supplier, string Currency, int Invoices, string Due, string Credits, string ToPay, string Status, bool OnHold);

/// <summary>
/// Backs the supplier payment run screen: propose (nothing posted) for a payment date, due date and bank, review the
/// per-supplier lines - credits netted, suppliers on hold skipped - then run it and save the EFT file. The rules live in
/// the tested <see cref="IPaymentRunService"/>; this class lists and forwards.
/// </summary>
public sealed partial class PaymentRunViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public PaymentRunViewModel(IServiceProvider services) => _services = services;

    /// <summary>The banks that can be paid from.</summary>
    public ObservableCollection<PayFromBankVm> Banks { get; } = [];

    /// <summary>The proposal lines.</summary>
    public ObservableCollection<PaymentProposalRowVm> Rows { get; } = [];

    /// <summary>The chosen bank.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProposeCommand), nameof(RunCommand))]
    private PayFromBankVm? _selectedBank;

    /// <summary>The payment date.</summary>
    [ObservableProperty]
    private DateTime _paymentDate = DateTime.Today;

    /// <summary>Pay invoices due on or before this date.</summary>
    [ObservableProperty]
    private DateTime _dueOnOrBefore = DateTime.Today;

    /// <summary>The proposal summary.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Choose the bank and dates, then propose the run.";

    /// <summary>True when the current proposal would make at least one payment.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _hasPayments;

    /// <summary>True when the last run produced an EFT file to save.</summary>
    [ObservableProperty]
    private bool _hasEftFile;

    /// <summary>True while working.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProposeCommand), nameof(RunCommand))]
    private bool _isBusy;

    /// <summary>The EFT file from the last run.</summary>
    public byte[] EftFile { get; private set; } = [];

    /// <summary>A suggested file name for the EFT file.</summary>
    public string EftFileName => $"EFT-{PaymentDate:yyyyMMdd}.txt";

    private bool CanPropose => SelectedBank is not null && !IsBusy;

    private bool CanRun => SelectedBank is not null && HasPayments && !IsBusy;

    /// <summary>Loads the banks.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task LoadAsync(CancellationToken ct = default)
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

            Banks.Clear();
            foreach (var b in await db.Set<BankAccount>().Where(b => b.CompanyId == _companyId).OrderBy(b => b.Code)
                .Select(b => new { b.Code, b.Name, b.Currency, b.GlAccountCode }).ToListAsync(ct).ConfigureAwait(true))
            {
                Banks.Add(new PayFromBankVm(b.GlAccountCode, $"{b.Code} - {b.Name} ({b.Currency})"));
            }

            SelectedBank = Banks.FirstOrDefault();
            if (Banks.Count == 0)
            {
                StatusMessage = "No bank accounts are set up.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load: {ex.Message}";
        }
    }

    /// <summary>Proposes the run (nothing is posted).</summary>
    [RelayCommand(CanExecute = nameof(CanPropose))]
    public async Task ProposeAsync()
    {
        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<PaymentRunProposal> result = await scope.ServiceProvider.GetRequiredService<IPaymentRunService>().ProposeAsync(Request()).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            Rows.Clear();
            foreach (PaymentProposalLine line in result.Value.Lines)
            {
                string status = line.OnHold ? "On hold" : line.ToPay > 0m ? "Pay" : "Settled by credits";
                Rows.Add(new PaymentProposalRowVm(
                    $"{line.SupplierCode} - {line.SupplierName}",
                    line.Currency,
                    line.InvoicesDue,
                    Money(line.AmountDue),
                    line.CreditsAvailable == 0m ? string.Empty : $"{Money(line.CreditsToApply)} of {Money(line.CreditsAvailable)}",
                    Money(line.ToPay),
                    status,
                    line.OnHold));
            }

            HasPayments = result.Value.PaymentCount > 0;
            Summary = result.Value.PaymentCount == 0
                ? "Nothing to pay."
                : $"{result.Value.PaymentCount} payment(s): {string.Join(", ", result.Value.ToPayByCurrency.Select(kv => $"{kv.Key} {Money(kv.Value)}"))}.";
            StatusMessage = "Proposal only - nothing has been posted. Review, then run.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not propose: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Runs the payment run: posts and allocates the payments and builds the EFT file.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task RunAsync()
    {
        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<PaymentRunResult> result = await scope.ServiceProvider.GetRequiredService<IPaymentRunService>().RunAsync(Request()).ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = $"Run failed (nothing was posted): {result.Error.Message}";
                return;
            }

            EftFile = result.Value.EftFile;
            HasEftFile = result.Value.Lines.Any(l => l.Amount > 0m);
            int paid = result.Value.Lines.Count(l => l.Amount > 0m);
            Rows.Clear();
            HasPayments = false;
            // The remittance advices go out with the run, to each supplier's remittance contacts.
            RemittanceBatch advices = await scope.ServiceProvider.GetRequiredService<ITraderDirectoryService>()
                .EmailRemittancesAsync(_companyId, [.. result.Value.Lines.Where(l => l.PaymentNumber.Length > 0).Select(l => l.PaymentNumber)]).ConfigureAwait(true);
            Summary = $"Paid {paid} supplier(s), {Money(result.Value.Total)} in base currency. {advices.Queued} remittance advice(s) queued for email"
                + (advices.ToPost.Count > 0 ? $"; print and post {string.Join(", ", advices.ToPost)}." : ".");
            StatusMessage = HasEftFile ? "Run complete. Save the EFT file for the bank." : "Run complete; everything was settled by credits.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Run failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private PaymentRunRequest Request() => new()
    {
        CompanyId = _companyId,
        PaymentDate = DateOnly.FromDateTime(PaymentDate),
        DueOnOrBefore = DateOnly.FromDateTime(DueOnOrBefore),
        BankAccountCode = SelectedBank?.GlAccountCode ?? string.Empty,
    };

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.CurrentCulture);
}
