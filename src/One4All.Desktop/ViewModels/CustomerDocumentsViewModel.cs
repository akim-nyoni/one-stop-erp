using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A customer choice.</summary>
/// <param name="Id">The customer id.</param>
/// <param name="Label">Code and name.</param>
public sealed record CustomerChoiceVm(long Id, string Label);

/// <summary>An invoice or credit note on the customer's account (pre-formatted for display).</summary>
/// <param name="OpenItemId">The AR open item.</param>
/// <param name="Kind">"Invoice" or "Credit note".</param>
/// <param name="Number">The document number.</param>
/// <param name="Date">The document date.</param>
/// <param name="Due">The due date.</param>
/// <param name="Amount">The original amount.</param>
/// <param name="Outstanding">The outstanding amount.</param>
public sealed record CustomerDocumentVm(long OpenItemId, string Kind, string Number, string Date, string Due, string Amount, string Outstanding);

/// <summary>
/// Backs the customer documents screen: choose a customer, see their invoices and credit notes, and preview
/// the printable invoice / credit note or the statement of account. Rendering lives in the tested document
/// services; this class lists, previews and hands the text to the window for saving.
/// </summary>
public sealed partial class CustomerDocumentsViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public CustomerDocumentsViewModel(IServiceProvider services) => _services = services;

    /// <summary>The customers.</summary>
    public ObservableCollection<CustomerChoiceVm> Customers { get; } = [];

    /// <summary>The selected customer's invoices and credit notes.</summary>
    public ObservableCollection<CustomerDocumentVm> Documents { get; } = [];

    /// <summary>The selected customer.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewStatementCommand))]
    private CustomerChoiceVm? _selectedCustomer;

    /// <summary>The selected document.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewDocumentCommand))]
    private CustomerDocumentVm? _selectedDocument;

    /// <summary>The statement date.</summary>
    [ObservableProperty]
    private DateTime _statementDate = DateTime.Today;

    /// <summary>The previewed document text (what Save writes).</summary>
    [ObservableProperty]
    private string _previewText = string.Empty;

    /// <summary>A suggested file name for the preview.</summary>
    [ObservableProperty]
    private string _previewFileName = "document.txt";

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Loading…";

    /// <summary>True while working.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewDocumentCommand), nameof(PreviewStatementCommand))]
    private bool _isBusy;

    private bool CanPreviewDocument => SelectedDocument is not null && !IsBusy;

    private bool CanPreviewStatement => SelectedCustomer is not null && !IsBusy;

    partial void OnSelectedCustomerChanged(CustomerChoiceVm? value) => _ = LoadDocumentsAsync();

    /// <summary>Loads the customers.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            if (_companyId == 0)
            {
                _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
                if (_companyId == 0)
                {
                    StatusMessage = "No company is set up.";
                    return;
                }
            }

            Customers.Clear();
            foreach (var c in await db.Customers.Where(c => c.CompanyId == _companyId).OrderBy(c => c.Code).Select(c => new { c.Id, c.Code, c.Name }).ToListAsync(ct).ConfigureAwait(true))
            {
                Customers.Add(new CustomerChoiceVm(c.Id, $"{c.Code} - {c.Name}"));
            }

            StatusMessage = $"{Customers.Count} customer(s). Choose one to see their documents.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load customers: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDocumentsAsync()
    {
        Documents.Clear();
        SelectedDocument = null;
        CustomerChoiceVm? customer = SelectedCustomer;
        if (customer is null)
        {
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<OpenItem> items = await db.OpenItems
                .Where(i => i.CompanyId == _companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customer.Id
                    && (i.ItemType == OpenItemType.Invoice || i.ItemType == OpenItemType.CreditNote || i.ItemType == OpenItemType.Interest))
                .OrderByDescending(i => i.DocumentDate).ThenByDescending(i => i.Id)
                .ToListAsync().ConfigureAwait(true);

            foreach (OpenItem i in items)
            {
                Documents.Add(new CustomerDocumentVm(i.Id, i.ItemType switch { OpenItemType.Invoice => "Invoice", OpenItemType.Interest => "Finance charge", _ => "Credit note" }, i.DocumentNumber,
                    i.DocumentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), i.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Money(i.OriginalTransaction), Money(i.OutstandingTransaction)));
            }

            StatusMessage = $"{Documents.Count} document(s) on {customer.Label}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load documents: {ex.Message}";
        }
    }

    /// <summary>Previews the selected invoice / credit note.</summary>
    [RelayCommand(CanExecute = nameof(CanPreviewDocument))]
    public async Task PreviewDocumentAsync()
    {
        CustomerDocumentVm? doc = SelectedDocument;
        if (doc is null)
        {
            return;
        }

        await PreviewAsync(
            sp => sp.GetRequiredService<IInvoiceDocumentService>().RenderTextAsync(_companyId, doc.OpenItemId),
            $"{doc.Number}.txt",
            $"{doc.Kind} {doc.Number} ready - Save writes it to a file.").ConfigureAwait(true);
    }

    /// <summary>Previews the selected customer's statement as at the statement date.</summary>
    [RelayCommand(CanExecute = nameof(CanPreviewStatement))]
    public async Task PreviewStatementAsync()
    {
        CustomerChoiceVm? customer = SelectedCustomer;
        if (customer is null)
        {
            return;
        }

        DateOnly asAt = DateOnly.FromDateTime(StatementDate);
        await PreviewAsync(
            sp => sp.GetRequiredService<IStatementService>().RenderCustomerStatementTextAsync(_companyId, customer.Id, asAt),
            $"statement-{customer.Label.Split(' ')[0]}-{asAt:yyyyMMdd}.txt",
            $"Statement as at {asAt:yyyy-MM-dd} ready - Save writes it to a file.").ConfigureAwait(true);
    }

    private async Task PreviewAsync(Func<IServiceProvider, Task<Result<string>>> render, string fileName, string done)
    {
        IsBusy = true;
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            Result<string> text = await render(scope.ServiceProvider).ConfigureAwait(true);
            if (text.IsFailure)
            {
                StatusMessage = text.Error.Message;
                return;
            }

            PreviewText = text.Value;
            PreviewFileName = fileName;
            StatusMessage = done;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not render: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);
}
