using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.Reporting;

/// <summary>
/// Opens the source behind a report value (brief §13.3 drill-through): a journal with its lines, an account in the account enquiry, a
/// customer's or supplier's statement, an item with its stock by warehouse.
/// </summary>
public static class DrillThroughOpener
{
    /// <summary>Opens a target.</summary>
    /// <param name="services">The application services.</param>
    /// <param name="owner">The window opening it.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="target">Journal, Account, Customer, Supplier or Item.</param>
    /// <param name="key">The record's number or code.</param>
    /// <returns>A task.</returns>
    public static async Task OpenAsync(IServiceProvider services, Window owner, long companyId, string target, string key)
    {
        if (target == "Account")
        {
            new AccountEnquiryWindow(services, key) { Owner = owner }.Show();
            return;
        }

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        string? text = target switch
        {
            "Journal" => await JournalAsync(db, companyId, key).ConfigureAwait(true),
            "Customer" => await db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.Code == key).Select(c => (long?)c.Id).FirstOrDefaultAsync().ConfigureAwait(true) is long customer
                ? Text(await scope.ServiceProvider.GetRequiredService<IStatementService>().RenderCustomerStatementTextAsync(companyId, customer, today, 80).ConfigureAwait(true))
                : null,
            "Supplier" => await db.Suppliers.AsNoTracking().Where(s => s.CompanyId == companyId && s.Code == key).Select(s => (long?)s.Id).FirstOrDefaultAsync().ConfigureAwait(true) is long supplier
                ? Text(await scope.ServiceProvider.GetRequiredService<IStatementService>().RenderSupplierStatementTextAsync(companyId, supplier, today, 80).ConfigureAwait(true))
                : null,
            "Item" => await ItemAsync(db, companyId, key).ConfigureAwait(true),
            _ => null,
        };
        ShowText(owner, $"{target} {key}", text ?? $"{target} {key} was not found.");
    }

    /// <summary>Shows text in a read-only, monospaced window.</summary>
    /// <param name="owner">The owner.</param>
    /// <param name="title">The title.</param>
    /// <param name="text">The text.</param>
    public static void ShowText(Window owner, string title, string text)
        => new Window
        {
            Owner = owner, Title = title, Width = 820, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox
            {
                Text = text, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8),
            },
        }.Show();

    private static string Text(Result<string> result) => result.IsSuccess ? result.Value : result.Error.Message;

    private static async Task<string?> JournalAsync(One4AllDbContext db, long companyId, string number)
    {
        JournalEntry? journal = await db.Journals.AsNoTracking().Include(j => j.Lines).FirstOrDefaultAsync(j => j.CompanyId == companyId && j.JournalNumber == number).ConfigureAwait(true);
        if (journal is null)
        {
            return null;
        }

        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"Journal {journal.JournalNumber}   {journal.PostingDate:yyyy-MM-dd}   {journal.SourceModule} {journal.DocumentType} {journal.DocumentNumber}");
        text.AppendLine(CultureInfo.InvariantCulture, $"{journal.Narrative}   status {journal.Status}");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Account",-12}{"Cost centre",-12}{"Department",-12}{"Debit",16}{"Credit",16}  Narrative");
        foreach (JournalLine line in journal.Lines)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{line.AccountCode,-12}{line.CostCentre,-12}{line.Department,-12}{(line.BaseDebit == 0 ? string.Empty : line.BaseDebit.ToString("N2", CultureInfo.InvariantCulture)),16}"
                + $"{(line.BaseCredit == 0 ? string.Empty : line.BaseCredit.ToString("N2", CultureInfo.InvariantCulture)),16}  {line.Narrative}");
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"{string.Empty,-36}{journal.Lines.Sum(l => l.BaseDebit),16:N2}{journal.Lines.Sum(l => l.BaseCredit),16:N2}");
        return text.ToString();
    }

    private static async Task<string?> ItemAsync(One4AllDbContext db, long companyId, string code)
    {
        var item = await db.Items.AsNoTracking().Where(i => i.CompanyId == companyId && i.Code == code).Select(i => new { i.Id, i.Code, i.Description, i.Category, i.UnitOfMeasure })
            .FirstOrDefaultAsync().ConfigureAwait(true);
        if (item is null)
        {
            return null;
        }

        var stock = await db.StockBalances.AsNoTracking().Where(b => b.CompanyId == companyId && b.ItemId == item.Id)
            .Join(db.Warehouses, b => b.WarehouseId, w => w.Id, (b, w) => new { w.Code, b.QuantityOnHand, b.AverageCost, b.TotalValue }).ToListAsync().ConfigureAwait(true);
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"Item {item.Code}  {item.Description}  ({item.Category}, {item.UnitOfMeasure})");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Warehouse",-12}{"On hand",14}{"Average cost",16}{"Value",16}");
        foreach (var s in stock)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{s.Code,-12}{s.QuantityOnHand,14:N3}{s.AverageCost,16:N4}{s.TotalValue,16:N2}");
        }

        return text.ToString();
    }
}
