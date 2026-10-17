using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>One service on the stand, as the enquiry desk lists it.</summary>
/// <param name="Service">The service.</param>
/// <param name="Tariff">Its tariff.</param>
/// <param name="State">Connected or disconnected.</param>
/// <param name="Meter">The meter and its last reading, if metered.</param>
public sealed record EnquiryServiceVm(string Service, string Tariff, string State, string Meter);

/// <summary>One recent transaction on the ratepayer's account.</summary>
/// <param name="Date">The date.</param>
/// <param name="Document">The document.</param>
/// <param name="Type">Invoice, receipt and so on.</param>
/// <param name="Amount">The amount (charges positive).</param>
/// <param name="Outstanding">What is still open.</param>
public sealed record EnquiryTransactionVm(string Date, string Document, string Type, string Amount, string Outstanding);

/// <summary>
/// Backs the municipal enquiry desk (brief §4.9): a stand looked up at the counter - the ratepayer, balance and overdue, services and meters,
/// recent transactions, arrangement, open orders and licences, a consumption graph per meter, and the statement ready to print.
/// </summary>
public sealed partial class EnquiryDeskViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider.</param>
    public EnquiryDeskViewModel(IServiceProvider services) => _services = services;

    /// <summary>Gets the stand's services.</summary>
    public ObservableCollection<EnquiryServiceVm> Services { get; } = [];

    /// <summary>Gets the recent transactions, newest first.</summary>
    public ObservableCollection<EnquiryTransactionVm> Transactions { get; } = [];

    /// <summary>The stand looked up.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookUpCommand))]
    private string _standNumber = string.Empty;

    /// <summary>The enquiry date.</summary>
    [ObservableProperty]
    private DateTime _asAt = DateTime.Today;

    /// <summary>The heading: stand, ward and ratepayer.</summary>
    [ObservableProperty]
    private string _heading = "Type a stand number and look it up.";

    /// <summary>The balance line.</summary>
    [ObservableProperty]
    private string _balance = string.Empty;

    /// <summary>Arrangement, indigent subsidy, clearance, open orders and licences.</summary>
    [ObservableProperty]
    private string _notes = string.Empty;

    /// <summary>The consumption graph of each meter.</summary>
    [ObservableProperty]
    private string _graph = string.Empty;

    /// <summary>The statement, for printing.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    private string _statement = string.Empty;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = "Ready.";

    private bool CanLookUp => StandNumber.Trim().Length > 0;

    private bool CanPrint => Statement.Length > 0;

    /// <summary>Looks the stand up as at the date.</summary>
    /// <returns>A task.</returns>
    [RelayCommand(CanExecute = nameof(CanLookUp))]
    public async Task LookUpAsync()
    {
        try
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            if (_companyId == 0)
            {
                _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync().ConfigureAwait(true);
            }

            string stand = StandNumber.Trim().ToUpperInvariant();
            var property = await db.Properties.AsNoTracking().Where(p => p.CompanyId == _companyId && p.StandNumber.ToUpper() == stand)
                .Select(p => new { p.Id, p.RatepayerCustomerId }).FirstOrDefaultAsync().ConfigureAwait(true);
            if (property is null)
            {
                StatusMessage = $"No stand {stand}.";
                return;
            }

            DateOnly asAt = DateOnly.FromDateTime(AsAt);
            Result<PropertyEnquiry> found = await scope.ServiceProvider.GetRequiredService<IPropertyEnquiryService>().EnquireAsync(_companyId, property.Id, asAt, 15).ConfigureAwait(true);
            if (found.IsFailure)
            {
                StatusMessage = found.Error.Message;
                return;
            }

            PropertyEnquiry e = found.Value;
            Heading = $"Stand {e.StandNumber} · ward {e.Ward} · valuation {M(e.Valuation)} · {e.RatepayerCode} {e.RatepayerName}";
            Balance = $"Balance {M(e.Balance)}   ·   overdue {M(e.Overdue)}";
            Services.Clear();
            foreach (EnquiryService s in e.Services)
            {
                Services.Add(new EnquiryServiceVm(s.ServiceType.ToString(), s.TariffCode, s.Connected ? "Connected" : "Disconnected",
                    s.MeterSerial is null ? "-" : $"{s.MeterSerial}: {s.LastReading?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-"} on {s.LastReadingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-"}"));
            }

            Transactions.Clear();
            foreach (EnquiryTransaction t in e.RecentTransactions)
            {
                Transactions.Add(new EnquiryTransactionVm(t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), t.DocumentNumber, t.ItemType.ToString(), M(t.Amount), M(t.Outstanding)));
            }

            List<string> notes = [];
            if (e.Arrangement is EnquiryArrangement a)
            {
                notes.Add($"Arrangement: {M(a.TotalAmount)} at {M(a.InstalmentAmount)} a month from {a.StartDate:yyyy-MM-dd}");
            }

            if (e.IndigentSubsidyPercent is decimal subsidy)
            {
                notes.Add($"Indigent subsidy {subsidy:0.##}%");
            }

            if (e.ClearanceValidUntil is DateOnly clearance)
            {
                notes.Add($"Rates clearance valid until {clearance:yyyy-MM-dd}");
            }

            notes.AddRange(e.OpenOrders.Select(o => $"Open {o.Kind.ToString().ToLowerInvariant()} order {o.OrderNumber} for {o.ServiceType} raised {o.RaisedOn:yyyy-MM-dd}"));
            notes.AddRange(e.Licences.Select(l => $"Licence {l.LicenceNumber} ({l.Kind}) {l.Status}, expires {l.ExpiresOn:yyyy-MM-dd}"));
            Notes = notes.Count == 0 ? "No arrangement, subsidy, open orders or licences." : string.Join(Environment.NewLine, notes);

            Result<IReadOnlyList<MeterConsumption>> meters = await scope.ServiceProvider.GetRequiredService<IPropertyRegisterService>().ConsumptionAsync(_companyId, stand, asAt, 12)
                .ConfigureAwait(true);
            Graph = meters.IsSuccess && meters.Value.Count > 0 ? string.Join(Environment.NewLine + Environment.NewLine, meters.Value.Select(m => ConsumptionGraph.Render(m, 64))) : "No metered services.";

            Result<string> statement = await scope.ServiceProvider.GetRequiredService<IStatementService>().RenderCustomerStatementTextAsync(_companyId, property.RatepayerCustomerId, asAt, 72)
                .ConfigureAwait(true);
            Statement = statement.IsSuccess ? statement.Value : string.Empty;
            StatusMessage = $"Stand {e.StandNumber} as at {asAt:yyyy-MM-dd}.";
        }
#pragma warning disable CA1031 // The desk reports a failed lookup rather than crashing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            StatusMessage = $"Could not look the stand up: {ex.GetBaseException().Message}";
        }
    }

    /// <summary>Prints the ratepayer's statement with the consumption graph.</summary>
    [RelayCommand(CanExecute = nameof(CanPrint))]
    public void Print()
    {
        System.Windows.Controls.PrintDialog dialog = new();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        System.Windows.Documents.FlowDocument document = new(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(Statement + Environment.NewLine + Environment.NewLine + Graph)))
        {
            FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 10, PagePadding = new System.Windows.Thickness(48), ColumnWidth = double.PositiveInfinity,
        };
        dialog.PrintDocument(((System.Windows.Documents.IDocumentPaginatorSource)document).DocumentPaginator, $"Statement {StandNumber.Trim().ToUpperInvariant()}");
        StatusMessage = "Statement sent to the printer.";
    }

    private static string M(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
