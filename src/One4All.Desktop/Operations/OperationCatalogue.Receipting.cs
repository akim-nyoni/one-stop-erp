using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Receipting;
using One4All.Core.Accounting;
using One4All.Core.Receipting;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Cashier receipting: the till session, taking payments (account or sundry), reversals, collection agents, banking, receipt imports and manual receipt books.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Receipting()
    {
        const string module = "Receipting";
        yield return new Operation
        {
            Module = module, Title = "Cash office drawer", ListTitle = "Payments awaiting clearance",
            Description = "During a session: a pickup moves surplus cash from the drawer to the safe; a petty cash payout pays an expense from the drawer; a supervisor verifies "
                + "another cashier's closed session; a receipt is reprinted (marked as a copy, counted); a cheque or transfer awaiting clearance is confirmed into the bank.",
            Fields = [Pick("Step", "Step", ["Pickup", "Petty cash payout", "Verify session", "Reprint receipt", "Confirm cleared tender"]), Num("Amount", "Amount", "0"),
                Opt("Cash", "Cash (drawer) account", "1000"), Opt("Safe", "Safe account (pickup)"), Opt("Expense", "Expense account (petty cash)"), Opt("Bank", "Bank account (cleared tender)"),
                Opt("Reason", "Reason"), OptNum("Session", "Session id (verify)"), Opt("Receipt", "Receipt number (reprint)"), OptNum("Pending", "Pending tender id (confirm)"), Today()],
            Run = async (c, i) =>
            {
                IReceiptingService receipting = c.Get<IReceiptingService>();
                string reason = i.Optional("Reason") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Pickup":
                        return $"Picked up ({Check(await receipting.PickupAsync(c.CompanyId, await MySessionAsync(c).ConfigureAwait(true), i.Number("Amount"), i.Date("Date"), i.Optional("Cash") ?? "1000",
                            i.Optional("Safe") ?? throw new OperationException("Give the safe account."), reason).ConfigureAwait(true))}).";
                    case "Petty cash payout":
                        return $"Paid out ({Check(await receipting.PettyCashPayoutAsync(c.CompanyId, await MySessionAsync(c).ConfigureAwait(true), i.Number("Amount"), i.Date("Date"), i.Optional("Cash") ?? "1000",
                            i.Optional("Expense") ?? throw new OperationException("Give the expense account."), reason).ConfigureAwait(true))}).";
                    case "Verify session":
                        return Check(await receipting.VerifySessionAsync(c.CompanyId, (long)(i.OptionalNumber("Session") ?? 0m), c.UserId).ConfigureAwait(true), "Session verified.");
                    case "Reprint receipt":
                        string number = i.Optional("Receipt") ?? throw new OperationException("Give the receipt number.");
                        long receipt = await c.Db.Receipts.AsNoTracking().Where(r => r.CompanyId == c.CompanyId && r.ReceiptNumber == number).Select(r => (long?)r.Id).FirstOrDefaultAsync().ConfigureAwait(true)
                            ?? throw new OperationException($"No receipt {number}.");
                        return $"Copy {Check(await receipting.ReprintReceiptAsync(c.CompanyId, receipt, i.Date("Date"), c.UserId).ConfigureAwait(true))} of {number}.";
                    default:
                        return $"Cleared into the bank ({Check(await receipting.ConfirmPendingTenderAsync(c.CompanyId, (long)(i.OptionalNumber("Pending") ?? 0m), i.Date("Date"),
                            i.Optional("Bank") ?? throw new OperationException("Give the bank account.")).ConfigureAwait(true))}).";
                }
            },
            List = async c => Table(await c.Db.PendingTenders.AsNoTracking().Where(p => p.CompanyId == c.CompanyId && p.ConfirmedOn == null).OrderBy(p => p.Date).Take(300).ToListAsync().ConfigureAwait(true),
                ("Id", p => p.Id), ("Receipt", p => p.ReceiptNumber), ("Tender", p => p.TenderType), ("Reference", p => p.Reference ?? string.Empty), ("Amount", p => M(p.Amount)),
                ("Date", p => p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
        };
        yield return new Operation
        {
            Module = module, Title = "Arrangements, dishonours and refunds", ListTitle = "Payment arrangements",
            Description = "A payment arrangement on a debtor's account (total and monthly instalment from a start date); a dishonoured cheque or reversed transfer, with an "
                + "optional penalty charged; money held in suspense allocated to an account; and a credit balance refunded to the debtor (approved by the user doing it).",
            Fields = [Pick("Step", "Step", ["Arrangement", "Dishonour", "Allocate from suspense", "Refund credit"]), Opt("Payer", "Customer code or stand number"), Num("Amount", "Amount / total", "0"),
                Num("Instalment", "Monthly instalment (arrangement)", "0"), Opt("Receipt", "Receipt number (dishonour)"), Num("Penalty", "Penalty (dishonour)", "0"), Opt("PenaltyAccount", "Penalty income account"),
                Opt("Suspense", "Suspense account"), Pick("Strategy", "Allocate to", Enum.GetNames<ReceiptAllocationStrategy>()), Opt("Bank", "Bank account (refund)"), Opt("Reason", "Reason"), Today()],
            Run = async (c, i) =>
            {
                IReceiptingService receipting = c.Get<IReceiptingService>();
                string reason = i.Optional("Reason") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Dishonour":
                        string number = i.Optional("Receipt") ?? throw new OperationException("Give the receipt number.");
                        long receipt = await c.Db.Receipts.AsNoTracking().Where(r => r.CompanyId == c.CompanyId && r.ReceiptNumber == number).Select(r => (long?)r.Id).FirstOrDefaultAsync().ConfigureAwait(true)
                            ?? throw new OperationException($"No receipt {number}.");
                        decimal penalty = i.Number("Penalty");
                        return Check(await receipting.RecordDishonourAsync(c.CompanyId, receipt, i.Date("Date"), penalty, penalty > 0m ? i.Optional("PenaltyAccount") : null,
                            penalty > 0m ? $"{number}-DH" : null, c.UserId, reason).ConfigureAwait(true), $"Receipt {number} dishonoured" + (penalty > 0m ? $"; penalty {M(penalty)} charged." : "."));
                    case "Allocate from suspense":
                        ReceiptResult moved = Check(await receipting.AllocateFromSuspenseAsync(c.CompanyId, await PayerAsync(c, i.Optional("Payer") ?? string.Empty).ConfigureAwait(true), i.Number("Amount"),
                            i.Date("Date"), i.Optional("Suspense") ?? throw new OperationException("Give the suspense account."), Enum.Parse<ReceiptAllocationStrategy>(i.Text("Strategy")), reason)
                            .ConfigureAwait(true));
                        return $"Receipt {moved.ReceiptNumber}: {M(moved.AllocatedToDebt)} to the account, {M(moved.CreditOnAccount)} credit on account.";
                    case "Refund credit":
                        RefundResult refunded = Check(await receipting.RefundCreditAsync(c.CompanyId, await PayerAsync(c, i.Optional("Payer") ?? string.Empty).ConfigureAwait(true), i.Number("Amount"),
                            i.Date("Date"), i.Optional("Bank") ?? throw new OperationException("Give the bank account."), c.UserId, reason).ConfigureAwait(true));
                        return $"Refunded {M(refunded.Amount)} (journal {refunded.JournalNumber}).";
                    default:
                        long arrangement = Check(await receipting.CaptureArrangementAsync(c.CompanyId, await PayerAsync(c, i.Optional("Payer") ?? string.Empty).ConfigureAwait(true), i.Number("Amount"),
                            i.Number("Instalment"), i.Date("Date")).ConfigureAwait(true));
                        return $"Arrangement {arrangement} captured.";
                }
            },
            List = async c => Table(await c.Db.PaymentArrangements.AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Take(300).ToListAsync().ConfigureAwait(true),
                ("Id", a => a.Id), ("Customer", a => a.CustomerId), ("Total", a => M(a.TotalAmount)), ("Instalment", a => M(a.InstalmentAmount)),
                ("From", a => a.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Status", a => a.Status)),
        };
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
                + "Pick the currency the payer hands over (the company's currencies are listed); a foreign-currency payment converts at today's rate. Tick Preview to see the allocation without posting.",
            Fields = [Pick("Kind", "Payment for", ["Account", "Sundry", "Deposit"]), Opt("Payer", "Customer code or stand number (account)"), Num("Amount", "Amount"),
                CurrencyPick("Currency", "Currency paid in"), Pick("Tender", "Tender", Enum.GetNames<TenderType>()), Opt("Reference", "Tender reference (card, cheque, mobile money)"), Opt("Account", "GL account (sundry / deposit)"),
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
                    Currency = i.Optional("Currency") is string paidIn ? Core.Accounting.CurrencyCode.Parse(paidIn) : null,
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
        yield return new Operation
        {
            Module = module, Title = "Collection agents", ListTitle = "Agents",
            Description = "Third-party agents who collect from customers for a commission: register one, record a collection (allocated to the customer's debt, owed by the agent) "
                + "and take the agent's remittance (commission kept, the net banked).",
            Fields = [Pick("Step", "Step", ["Register", "Collection", "Remittance"]), Req("Agent", "Agent code"), Opt("Name", "Name (register)"), Num("Commission", "Commission %", "0"),
                Opt("Customer", "Customer code or stand (collection)"), Num("Amount", "Amount", "0"), Opt("Control", "Agent control account"), Opt("Bank", "Bank account (remittance)"),
                Opt("CommissionAccount", "Commission expense account (remittance)"), Today()],
            Run = async (c, i) =>
            {
                IAgentService agents = c.Get<IAgentService>();
                string code = i.Text("Agent").Trim();
                if (i.Text("Step") == "Register")
                {
                    return Check(await agents.RegisterAgentAsync(c.CompanyId, code, i.Optional("Name") ?? code, i.Number("Commission")).ConfigureAwait(true), _ => $"Agent {code} registered.");
                }

                long agent = await c.Db.Agents.AsNoTracking().Where(a => a.CompanyId == c.CompanyId && a.Code.ToUpper() == code.ToUpper()).Select(a => (long?)a.Id).FirstOrDefaultAsync().ConfigureAwait(true)
                    ?? throw new OperationException($"No agent {code}.");
                if (i.Text("Step") == "Collection")
                {
                    AgentCollectionResult collected = Check(await agents.RecordCollectionAsync(c.CompanyId, agent, await PayerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true), i.Number("Amount"),
                        i.Date("Date"), i.Optional("Control") ?? string.Empty).ConfigureAwait(true));
                    return $"Collected {M(collected.Amount)} ({M(collected.AllocatedToDebt)} against the debt); the agent now owes {M(collected.AgentOutstanding)}.";
                }

                AgentRemittanceResult remitted = Check(await agents.RemitAsync(c.CompanyId, agent, i.Number("Amount"), i.Date("Date"), i.Optional("Control") ?? string.Empty, i.Optional("Bank") ?? string.Empty,
                    i.Optional("CommissionAccount") ?? string.Empty).ConfigureAwait(true));
                return $"Remitted {M(remitted.Gross)}: commission {M(remitted.Commission)}, banked {M(remitted.Net)} ({remitted.JournalNumber}); the agent still owes {M(remitted.AgentOutstanding)}.";
            },
            List = async c => Table(await c.Db.Agents.AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderBy(a => a.Code).ToListAsync().ConfigureAwait(true),
                ("Agent", a => a.Code), ("Name", a => a.Name), ("Commission %", a => a.CommissionPercent), ("Collected", a => M(a.CollectedToDate)), ("Remitted", a => M(a.RemittedToDate)),
                ("Active", a => a.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Banking", ListTitle = "Cash deposits",
            Description = "Takes cash to the bank (cash to cash-in-transit), confirms a deposit as banked (in transit to the bank account), moves money between accounts, "
                + "and posts bank charges or interest received from the statement.",
            Fields = [Pick("Step", "Step", ["Bank cash", "Confirm banked", "Transfer", "Bank charge", "Interest received"]), Num("Amount", "Amount", "0"), Opt("Reference", "Reference / deposit slip"),
                Opt("From", "Cash / from account"), Opt("Transit", "Cash-in-transit account"), Opt("Bank", "Bank / to account"), Opt("Other", "Charges or interest income account"), Today()],
            Run = async (c, i) =>
            {
                IBankingService banking = c.Get<IBankingService>();
                string reference = i.Optional("Reference") ?? string.Empty, from = i.Optional("From") ?? string.Empty, transit = i.Optional("Transit") ?? string.Empty,
                    bank = i.Optional("Bank") ?? string.Empty, other = i.Optional("Other") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Bank cash":
                        BankingResult deposit = Check(await banking.BankCashAsync(c.CompanyId, i.Number("Amount"), i.Date("Date"), reference, from, transit).ConfigureAwait(true));
                        return $"Deposit {deposit.Reference} in transit ({deposit.JournalNumber}).";
                    case "Confirm banked":
                        long id = await c.Db.BankDeposits.AsNoTracking().Where(d => d.CompanyId == c.CompanyId && d.Reference.ToUpper() == reference.ToUpper()).OrderByDescending(d => d.Id)
                            .Select(d => (long?)d.Id).FirstOrDefaultAsync().ConfigureAwait(true) ?? throw new OperationException($"No deposit {reference}.");
                        return Check(await banking.ConfirmBankedAsync(c.CompanyId, id, i.Date("Date"), transit, bank).ConfigureAwait(true), j => $"Banked ({j}).");
                    case "Transfer":
                        return Check(await banking.TransferAsync(c.CompanyId, i.Number("Amount"), i.Date("Date"), reference, from, bank).ConfigureAwait(true), j => $"Transferred ({j}).");
                    case "Bank charge":
                        return Check(await banking.PostBankChargeAsync(c.CompanyId, i.Number("Amount"), i.Date("Date"), reference, bank, other).ConfigureAwait(true), j => $"Charge posted ({j}).");
                    default:
                        return Check(await banking.PostInterestReceivedAsync(c.CompanyId, i.Number("Amount"), i.Date("Date"), reference, bank, other).ConfigureAwait(true), j => $"Interest posted ({j}).");
                }
            },
            List = async c => Table(await c.Db.BankDeposits.AsNoTracking().Where(d => d.CompanyId == c.CompanyId).OrderByDescending(d => d.Id).Take(200).ToListAsync().ConfigureAwait(true),
                ("Reference", d => d.Reference), ("Date", d => D(d.DepositDate)), ("Amount", d => M(d.Amount)), ("Status", d => d.Status), ("Banked", d => d.BankedOn is DateOnly b ? D(b) : string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Import receipts from a file", ListTitle = "Import batches",
            Description = "Receipts a bank statement, mobile money, card settlement, employer or agent file (CSV: payer reference, payer name, amount, value date): lines matching one "
                + "account are receipted to it, the rest go to suspense; the same file cannot be imported twice.",
            Fields = [Req("File", "CSV file path"), Pick("Source", "Source", Enum.GetNames<ImportSource>()), Opt("Bank", "Bank account"), Opt("Suspense", "Suspense account"), Today()],
            Run = async (c, i) =>
            {
                string path = i.Text("File");
                byte[] bytes = await System.IO.File.ReadAllBytesAsync(path).ConfigureAwait(true);
                List<ImportLineRequest> lines = [];
                int row = 0;
                foreach (string raw in System.Text.Encoding.UTF8.GetString(bytes).Split('\n'))
                {
                    row++;
                    string line = raw.Trim().TrimStart('\uFEFF');
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    string[] cells = line.Split(',', StringSplitOptions.TrimEntries).Select(x => x.Trim('"')).ToArray();
                    decimal amount = 0m;
                    DateOnly value = default;
                    bool parsed = cells.Length >= 4 && decimal.TryParse(cells[2], NumberStyles.Number, CultureInfo.InvariantCulture, out amount)
                        & DateOnly.TryParse(cells.Length >= 4 ? cells[3] : string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
                    if (!parsed && row == 1)
                    {
                        continue; // a heading row
                    }

                    lines.Add(parsed ? new ImportLineRequest(cells[0], cells[1], amount, value)
                        : throw new OperationException($"Line {row}: write payer reference, payer name, amount, value date (yyyy-mm-dd)."));
                }

                ImportResult imported = Check(await c.Get<IReceiptImportService>().ImportAsync(new ImportRequest
                {
                    CompanyId = c.CompanyId, FileReference = System.IO.Path.GetFileName(path), FileHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
                    Source = Enum.Parse<ImportSource>(i.Text("Source")), Date = i.Date("Date"), BankAccountCode = i.Optional("Bank") ?? string.Empty,
                    SuspenseAccountCode = i.Optional("Suspense") ?? string.Empty, Lines = lines,
                }).ConfigureAwait(true));
                return $"{imported.TotalLines} line(s): {imported.Matched} matched ({M(imported.MatchedAmount)}), {imported.Unmatched} to suspense ({M(imported.UnmatchedAmount)}).";
            },
            List = async c => Table(await c.Db.ImportBatches.AsNoTracking().Include(b => b.Lines).Where(b => b.CompanyId == c.CompanyId).OrderByDescending(b => b.Id).Take(100).ToListAsync().ConfigureAwait(true),
                ("File", b => b.FileReference), ("Source", b => b.Source), ("Date", b => D(b.ImportDate)), ("Matched", b => b.MatchedCount), ("Unmatched", b => b.UnmatchedCount)),
        };
        yield return new Operation
        {
            Module = module, Title = "Manual receipt books", ListTitle = "Receipt books",
            Description = "Pre-numbered manual receipt books for when the system is down: issue a book (serial range) to a cashier, capture a manual receipt written from it later, "
                + "and list the serials still unused (every serial must be accounted for).",
            Fields = [Pick("Step", "Step", ["Issue", "Capture", "Unused serials"]), Req("Book", "Book number"), OptNum("FromSerial", "First serial (issue)"), OptNum("ToSerial", "Last serial (issue)"),
                Opt("Cashier", "Issued to (user name; blank: me)"), OptNum("Serial", "Serial (capture)"), Num("Amount", "Amount (capture)", "0"), Today("Date", "Issue / receipt date")],
            Run = async (c, i) =>
            {
                IStationeryService stationery = c.Get<IStationeryService>();
                string number = i.Text("Book").Trim();
                if (i.Text("Step") == "Issue")
                {
                    long cashier = i.Optional("Cashier") is string name ? await UserAsync(c, name).ConfigureAwait(true) : c.UserId;
                    return Check(await stationery.IssueBookAsync(c.CompanyId, number, (long)(i.OptionalNumber("FromSerial") ?? 0m), (long)(i.OptionalNumber("ToSerial") ?? 0m), cashier, i.Date("Date"))
                        .ConfigureAwait(true), _ => $"Book {number} issued.");
                }

                long book = await c.Db.StationeryBooks.AsNoTracking().Where(b => b.CompanyId == c.CompanyId && b.BookNumber.ToUpper() == number.ToUpper()).Select(b => (long?)b.Id).FirstOrDefaultAsync()
                    .ConfigureAwait(true) ?? throw new OperationException($"No receipt book {number}.");
                if (i.Text("Step") == "Capture")
                {
                    ManualCaptureResult captured = Check(await stationery.CaptureManualReceiptAsync(c.CompanyId, book, (long)(i.OptionalNumber("Serial") ?? 0m), i.Number("Amount"), i.Date("Date")).ConfigureAwait(true));
                    return $"Serial {captured.Serial} captured; {captured.RemainingUnused} unused in the book.";
                }

                IReadOnlyList<long> unused = Check(await stationery.GetUnusedSerialsAsync(c.CompanyId, book).ConfigureAwait(true));
                return unused.Count == 0 ? "Every serial is used." : $"{unused.Count} unused: {string.Join(", ", unused)}";
            },
            List = async c => Table(await c.Db.StationeryBooks.AsNoTracking().Where(b => b.CompanyId == c.CompanyId).OrderByDescending(b => b.Id).Take(200).ToListAsync().ConfigureAwait(true),
                ("Book", b => b.BookNumber), ("From", b => b.FromSerial), ("To", b => b.ToSerial), ("Issued to", b => b.IssuedToUserId), ("Issued", b => D(b.IssuedOn)), ("Status", b => b.Status)),
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
