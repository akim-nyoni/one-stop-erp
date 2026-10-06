using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Receipting;
using One4All.Core.Accounting;
using One4All.Core.Receipting;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Cashier receipting: the till session, taking payments (account or sundry), and reversals.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Receipting()
    {
        const string module = "Receipting";
        yield return new Operation
        {
            Module = module, Title = "Open or close my session", ListTitle = "Cashier sessions",
            Description = "Open a session at a station with the float, or close yours by declaring the cash counted (shortages and overs are posted).",
            Fields = [Pick("Step", "Step", ["Open", "Close"]), Opt("Station", "Station code (open)"), Opt("Currency", "Currency", "USD"), OptNum("Amount", "Float (open) / cash counted (close)"),
                Today("Date", "Date"), Opt("Cash", "Cash account", "1000"), Opt("Shortages", "Cash shortages / overs account (close)")],
            Run = async (c, i) =>
            {
                IReceiptingService receipting = c.Get<IReceiptingService>();
                FloatDeclaration[] cash = [new FloatDeclaration(CurrencyCode.Parse(i.Optional("Currency") ?? "USD"), i.OptionalNumber("Amount") ?? 0m)];
                if (i.Text("Step") == "Open")
                {
                    string code = i.Optional("Station") ?? throw new OperationException("Give the station code.");
                    long station = await c.Db.CashierStations.AsNoTracking().Where(s => s.CompanyId == c.CompanyId && s.Code == code).Select(s => (long?)s.Id).FirstOrDefaultAsync().ConfigureAwait(true)
                        ?? throw new OperationException($"No station {code}.");
                    return $"Session {Check(await receipting.OpenSessionAsync(c.CompanyId, station, c.UserId, i.Date("Date"), cash).ConfigureAwait(true))} opened.";
                }

                CashUpResult up = Check(await receipting.CloseSessionAsync(c.CompanyId, await MySessionAsync(c).ConfigureAwait(true), i.Date("Date"), cash, i.Optional("Cash") ?? "1000",
                    i.Optional("Shortages") ?? throw new OperationException("Give the account cash shortages and overs post to.")).ConfigureAwait(true));
                return $"Closed: expected {M(up.ExpectedCashBase)}, counted {M(up.CountedCashBase)}, variance {M(up.VarianceBase)}"
                    + (up.VarianceJournalNumber is null ? "." : $" (journal {up.VarianceJournalNumber}).");
            },
            List = async c => Table(await c.Db.CashierSessions.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).OrderByDescending(s => s.Id).Take(100).ToListAsync().ConfigureAwait(true),
                ("Session", s => s.Id), ("Station", s => s.StationId), ("Cashier", s => s.CashierUserId), ("Opened", s => s.OpenedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("Closed", s => s.ClosedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Verified by", s => s.VerifiedByUserId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Take a payment", ListTitle = "My receipts today",
            Description = "A payment on an account (customer code, or a stand number for its ratepayer), allocated oldest first unless chosen otherwise; or a sundry receipt or deposit to an account. "
                + "Tick Preview to see the allocation without posting.",
            Fields = [Pick("Kind", "Payment for", ["Account", "Sundry", "Deposit"]), Opt("Payer", "Customer code or stand number (account)"), Num("Amount", "Amount"),
                Pick("Tender", "Tender", Enum.GetNames<TenderType>()), Opt("Reference", "Tender reference (card, cheque, mobile money)"), Opt("Account", "GL account (sundry / deposit)"),
                Opt("Narrative", "Narrative"), Pick("Strategy", "Allocate", Enum.GetNames<ReceiptAllocationStrategy>().Where(s => s != nameof(ReceiptAllocationStrategy.UserDirected)).ToArray()),
                Today("Date", "Date"), Opt("Cash", "Cash account", "1000"), Tick("Preview", "Preview only")],
            Run = async (c, i) =>
            {
                IReceiptingService receipting = c.Get<IReceiptingService>();
                decimal amount = i.Number("Amount");
                ReceiptAllocationStrategy strategy = Enum.Parse<ReceiptAllocationStrategy>(i.Text("Strategy"));
                long? customer = i.Text("Kind") == "Account" ? await PayerAsync(c, i.Optional("Payer") ?? throw new OperationException("Give the customer code or stand.")).ConfigureAwait(true) : null;
                if (i.Flag("Preview"))
                {
                    AllocationPreview preview = Check(await receipting.PreviewAllocationAsync(c.CompanyId, customer ?? throw new OperationException("Preview applies to account payments."), amount, strategy)
                        .ConfigureAwait(true));
                    return string.Join(Environment.NewLine, preview.Lines.Select(l => $"{l.DocumentNumber,-14} {l.DocumentDate:yyyy-MM-dd} outstanding {M(l.Outstanding),12} → applied {M(l.Applied)}")
                        .Append($"Allocated {M(preview.Allocated)}; credit on account {M(preview.CreditOnAccount)}."));
                }

                ReceiptLineRequest line = i.Text("Kind") switch
                {
                    "Account" => new ReceiptLineRequest(ReceiptLineType.DebtorPayment, amount, Narrative: i.Optional("Narrative")),
                    "Deposit" => new ReceiptLineRequest(ReceiptLineType.Deposit, amount, i.Optional("Account") ?? throw new OperationException("Give the deposit account."), i.Optional("Narrative")),
                    _ => new ReceiptLineRequest(ReceiptLineType.Sundry, amount, i.Optional("Account") ?? throw new OperationException("Give the income account."), i.Optional("Narrative")),
                };
                ReceiptResult receipt = Check(await receipting.PostReceiptAsync(new ReceiptRequest
                {
                    CompanyId = c.CompanyId, SessionId = await MySessionAsync(c).ConfigureAwait(true), PayerKind = customer is null ? PayerKind.Anonymous : PayerKind.ArCustomer,
                    PayerAccountId = customer, Date = i.Date("Date"), CashAccountCode = i.Optional("Cash") ?? "1000", AllocationStrategy = strategy, Lines = [line],
                    Tenders = [new ReceiptTenderRequest(Enum.Parse<TenderType>(i.Text("Tender")), amount, i.Optional("Reference"))],
                }).ConfigureAwait(true));
                return $"Receipt {receipt.ReceiptNumber}: {M(receipt.Total)} — {M(receipt.AllocatedToDebt)} to the account, {M(receipt.CreditOnAccount)} credit on account.";
            },
            List = async c =>
            {
                DateOnly today = DateOnly.FromDateTime(DateTime.Today);
                return Table(await c.Db.Receipts.AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == c.CompanyId && r.CashierUserId == c.UserId && r.Date == today).OrderByDescending(r => r.Id)
                        .ToListAsync().ConfigureAwait(true),
                    ("Id", r => r.Id), ("Receipt", r => r.ReceiptNumber), ("Payer", r => r.PayerKind), ("Account", r => r.PayerAccountId), ("Amount", r => M(r.Lines.Sum(l => l.Amount))),
                    ("Status", r => r.Status), ("Journal", r => r.JournalNumber));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Reverse a receipt", ListTitle = "My receipts today",
            Description = "Reverses a receipt posted in error (the allocations and journal are undone); a supervisor may be required by your permissions.",
            Fields = [Req("Receipt", "Receipt number"), Req("Reason", "Reason"), Today("Date", "Date")],
            Run = async (c, i) =>
            {
                string number = i.Text("Receipt").Trim();
                long id = await c.Db.Receipts.AsNoTracking().Where(r => r.CompanyId == c.CompanyId && r.ReceiptNumber == number).Select(r => (long?)r.Id).FirstOrDefaultAsync().ConfigureAwait(true)
                    ?? throw new OperationException($"No receipt {number}.");
                ReceiptReversalResult reversed = Check(await c.Get<IReceiptingService>().ReverseReceiptAsync(c.CompanyId, id, i.Date("Date"), c.UserId, i.Text("Reason")).ConfigureAwait(true));
                return $"Receipt {number} reversed ({M(reversed.Amount)}).";
            },
            List = async c =>
            {
                DateOnly today = DateOnly.FromDateTime(DateTime.Today);
                return Table(await c.Db.Receipts.AsNoTracking().Where(r => r.CompanyId == c.CompanyId && r.CashierUserId == c.UserId && r.Date == today).OrderByDescending(r => r.Id).ToListAsync()
                        .ConfigureAwait(true), ("Receipt", r => r.ReceiptNumber), ("Payer", r => r.PayerKind), ("Account", r => r.PayerAccountId), ("Status", r => r.Status));
            },
        };
    }

    // The signed-in cashier's open session.
    private static async Task<long> MySessionAsync(OperationContext c)
        => await c.Db.CashierSessions.AsNoTracking().Where(s => s.CompanyId == c.CompanyId && s.CashierUserId == c.UserId && s.ClosedOn == null).OrderByDescending(s => s.Id)
            .Select(s => (long?)s.Id).FirstOrDefaultAsync().ConfigureAwait(true) ?? throw new OperationException("You have no open session: open one first.");

    // A customer code, or a stand number standing for its ratepayer.
    private static async Task<long> PayerAsync(OperationContext c, string payer)
    {
        string key = payer.Trim();
        long? customer = await c.Db.Customers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.Code.ToUpper() == key.ToUpper()).Select(x => (long?)x.Id).FirstOrDefaultAsync().ConfigureAwait(true);
        return customer ?? await c.Db.Properties.AsNoTracking().Where(p => p.CompanyId == c.CompanyId && p.StandNumber == key).Select(p => (long?)p.RatepayerCustomerId).FirstOrDefaultAsync().ConfigureAwait(true)
            ?? throw new OperationException($"No customer or stand {key}.");
    }
}
