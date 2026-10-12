using System.Data;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Core.Trading;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Customer and supplier documents: invoices and credit notes, receipts and payments, statements.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Trading()
    {
        foreach (string module in new[] { "Customers", "Suppliers" })
        {
            TradingLedger ledger = module == "Customers" ? TradingLedger.AccountsReceivable : TradingLedger.AccountsPayable;
            yield return new Operation
            {
                Module = module, Title = "Exchange-rate type", ListTitle = module == "Customers" ? "Customers on their own rate" : "Suppliers on their own rate",
                Description = "The rate type this trader's foreign documents convert at (e.g. a contract or bank rate kept in the rate table). Daily (or blank) uses the daily rate; "
                    + "a document dated before any rate of the type falls back to the daily rate.",
                Fields = [Req("Code", module == "Customers" ? "Customer code" : "Supplier code"), Pick("RateType", "Rate type", Enum.GetNames<ExchangeRateType>())],
                Run = async (c, i) =>
                {
                    string code = i.Text("Code").Trim().ToUpperInvariant();
                    ExchangeRateType type = Enum.Parse<ExchangeRateType>(i.Text("RateType"));
                    ExchangeRateType? stored = type == ExchangeRateType.Daily ? null : type;
                    if (module == "Customers")
                    {
                        Customer customer = await c.Db.Customers.FirstOrDefaultAsync(x => x.CompanyId == c.CompanyId && x.Code == code).ConfigureAwait(true) ?? throw new OperationException($"No customer {code}.");
                        customer.RateType = stored;
                    }
                    else
                    {
                        Supplier supplier = await c.Db.Suppliers.FirstOrDefaultAsync(x => x.CompanyId == c.CompanyId && x.Code == code).ConfigureAwait(true) ?? throw new OperationException($"No supplier {code}.");
                        supplier.RateType = stored;
                    }

                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"{code} now converts at the {type} rate.";
                },
                List = async c => module == "Customers"
                    ? Table(await c.Db.Customers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.RateType != null).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                        ("Code", x => x.Code), ("Name", x => x.Name), ("Currency", x => x.Currency), ("Rate type", x => x.RateType))
                    : Table(await c.Db.Suppliers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.RateType != null).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                        ("Code", x => x.Code), ("Name", x => x.Name), ("Currency", x => x.Currency), ("Rate type", x => x.RateType)),
            };
            yield return new Operation
            {
                Module = module, Title = "Addresses and contacts", ListTitle = "Directory",
                Description = "Postal, physical and delivery addresses (the default postal address prints on invoices, statements, orders, remittances and letters) and the people who "
                    + "receive statements, remittance advices and purchase orders by email. Remove takes an address or contact id. The list shows the trader typed in Code.",
                Fields = [Pick("Step", "Step", ["Add address", "Add contact", "Remove address", "Remove contact"]), Req("Code", module == "Customers" ? "Customer code" : "Supplier code"),
                    Pick("Kind", "Address kind", Enum.GetNames<AddressKind>()), Opt("Line1", "Address line 1"), Opt("Line2", "Address line 2"), Opt("City", "Town / city"), Opt("Region", "Province"),
                    Opt("Postal", "Postal code"), Opt("Country", "Country"), Opt("Name", "Contact name"), Opt("Role", "Role"), Opt("Email", "Email"), Opt("Phone", "Phone"),
                    Tick("Primary", "Primary contact"), Tick("Statements", "Receives statements"), Tick("Remittances", "Receives remittances"), Tick("Orders", "Receives purchase orders"),
                    OptNum("Id", "Address / contact id (remove)")],
                Run = async (c, i) =>
                {
                    ITraderDirectoryService directory = c.Get<ITraderDirectoryService>();
                    switch (i.Text("Step"))
                    {
                        case "Add contact":
                            long contact = Check(await directory.AddContactAsync(c.CompanyId, ledger, i.Text("Code"), new NewTraderContact(i.Optional("Name") ?? string.Empty, i.Optional("Role"),
                                i.Optional("Email"), i.Optional("Phone"), i.Flag("Primary"), i.Flag("Statements"), i.Flag("Remittances"), i.Flag("Orders"))).ConfigureAwait(true));
                            return $"Contact {contact} added.";
                        case "Remove address":
                            _ = Check(await directory.RemoveAddressAsync(c.CompanyId, (long)(i.OptionalNumber("Id") ?? 0)).ConfigureAwait(true), string.Empty);
                            return "Address removed.";
                        case "Remove contact":
                            _ = Check(await directory.RemoveContactAsync(c.CompanyId, (long)(i.OptionalNumber("Id") ?? 0)).ConfigureAwait(true), string.Empty);
                            return "Contact removed.";
                        default:
                            long address = Check(await directory.AddAddressAsync(c.CompanyId, ledger, i.Text("Code"), new NewTraderAddress(Enum.Parse<AddressKind>(i.Text("Kind")),
                                i.Optional("Line1") ?? string.Empty, i.Optional("Line2"), i.Optional("City"), i.Optional("Region"), i.Optional("Postal"), i.Optional("Country"))).ConfigureAwait(true));
                            return $"Address {address} added as the default {i.Text("Kind").ToLowerInvariant()} address.";
                    }
                },
            };
            yield return new Operation
            {
                Module = module, Title = module == "Customers" ? "Email a statement" : "Email a statement, remittance or order",
                Description = "Sends the document as an attachment to the contacts who receive it (else the primary contact"
                    + (module == "Customers" ? ", else the customer's own email)." : "). Remittance: the payment's open-item id; order: the purchase order id."),
                Fields = module == "Customers"
                    ? [Req("Code", "Customer code"), Today("AsAt", "Statement date")]
                    : [Pick("Document", "Document", ["Statement", "Remittance advice", "Purchase order"]), Opt("Code", "Supplier code (statement)"), Today("AsAt", "Statement date"), OptNum("Id", "Payment item / order id")],
                Run = async (c, i) =>
                {
                    ITraderDirectoryService directory = c.Get<ITraderDirectoryService>();
                    string document = module == "Customers" ? "Statement" : i.Text("Document");
                    long batch = Check(document switch
                    {
                        "Remittance advice" => await directory.EmailRemittanceAsync(c.CompanyId, (long)(i.OptionalNumber("Id") ?? 0)).ConfigureAwait(true),
                        "Purchase order" => await directory.EmailPurchaseOrderAsync(c.CompanyId, (long)(i.OptionalNumber("Id") ?? 0)).ConfigureAwait(true),
                        _ => await directory.EmailStatementAsync(c.CompanyId, ledger, i.Optional("Code") ?? string.Empty, i.Date("AsAt")).ConfigureAwait(true),
                    });
                    return $"{document} queued for email (batch {batch}).";
                },
            };
        }

        yield return new Operation
        {
            Module = "Customers", Title = "Customer invoice or credit note", ListTitle = "Open customer items",
            Description = "Posts a sales invoice or credit note to a customer's account (the customer's currency unless another is given; tax at the tax code's account).",
            Fields = [Pick("Document", "Document", ["Invoice", "Credit note"]), Req("Customer", "Customer code"), Req("Number", "Document number"), Today("Date", "Date"), Num("Net", "Net amount"),
                Num("Tax", "Tax amount", "0"), Req("Account", "Sales account"), Opt("TaxAccount", "Tax account"), Opt("TaxCode", "Tax code"), Opt("Currency", "Currency (blank: the customer's)"),
                Opt("Reason", "Reason code (credit note)"), Opt("Narrative", "Narrative"), Opt("Override", "Supervisor allowing it over the credit limit (user name)")],
            Run = async (c, i) =>
            {
                long customer = await CustomerAsync(c, i.Text("Customer")).ConfigureAwait(true);
                CurrencyCode currency = i.Optional("Currency") is string code ? CurrencyCode.Parse(code)
                    : await c.Db.Customers.AsNoTracking().Where(x => x.Id == customer).Select(x => x.Currency).SingleAsync().ConfigureAwait(true);
                if (i.Text("Document") != "Invoice")
                {
                    // A credit note goes through the company's credit-note approval workflow when there is one.
                    CreditNoteSubmission credit = Check(await c.Get<ICreditNoteApprovalService>().SubmitAsync(new SalesCreditNoteRequest
                    {
                        CompanyId = c.CompanyId, CustomerId = customer, DocumentNumber = i.Text("Number"), DocumentDate = i.Date("Date"), Currency = currency, NetAmount = i.Number("Net"),
                        TaxAmount = i.Number("Tax"), SalesAccountCode = i.Text("Account"), TaxAccountCode = i.Optional("TaxAccount"), ReasonCode = i.Optional("Reason"), Narrative = i.Optional("Narrative"),
                    }, c.UserId).ConfigureAwait(true));
                    return credit.Posted ? $"Credit note {i.Text("Number")} posted (journal {credit.JournalNumber})." : $"Credit note {i.Text("Number")} is awaiting approval.";
                }

                TradingPostingResult posted = Check(await c.Get<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
                {
                    CompanyId = c.CompanyId, CustomerId = customer, DocumentNumber = i.Text("Number"), DocumentDate = i.Date("Date"), Currency = currency, NetAmount = i.Number("Net"),
                    TaxAmount = i.Number("Tax"), SalesAccountCode = i.Text("Account"), TaxAccountCode = i.Optional("TaxAccount"), TaxCode = i.Optional("TaxCode"), Narrative = i.Optional("Narrative"),
                    EnteredByUserId = c.UserId, CreditLimitOverrideByUserId = await OptionalUserAsync(c, i.Optional("Override")).ConfigureAwait(true),
                }).ConfigureAwait(true));
                return $"Invoice {i.Text("Number")} posted: {M(posted.BaseAmount)} base (journal {posted.JournalNumber}).";
            },
            List = c => OpenItemsAsync(c, TradingLedger.AccountsReceivable),
        };
        yield return new Operation
        {
            Module = "Customers", Title = "Customer receipt (bank)", ListTitle = "Open customer items",
            Description = "A payment received into the bank (EFT, deposit) on a customer's account; allocate it to invoices with auto-allocation or the allocation screens.",
            Fields = [Req("Customer", "Customer code"), Req("Number", "Receipt number"), Today("Date", "Date"), Num("Amount", "Amount"), Req("Bank", "Bank account", "1100"),
                Opt("Currency", "Currency (blank: the customer's)"), Opt("Narrative", "Narrative")],
            Run = async (c, i) =>
            {
                long customer = await CustomerAsync(c, i.Text("Customer")).ConfigureAwait(true);
                CurrencyCode currency = i.Optional("Currency") is string code ? CurrencyCode.Parse(code)
                    : await c.Db.Customers.AsNoTracking().Where(x => x.Id == customer).Select(x => x.Currency).SingleAsync().ConfigureAwait(true);
                TradingPostingResult posted = Check(await c.Get<ISalesLedgerService>().PostReceiptAsync(new ReceiptRequest
                {
                    CompanyId = c.CompanyId, CustomerId = customer, DocumentNumber = i.Text("Number"), ReceiptDate = i.Date("Date"), Currency = currency, Amount = i.Number("Amount"),
                    BankAccountCode = i.Text("Bank"), Narrative = i.Optional("Narrative"),
                }).ConfigureAwait(true));
                return $"Receipt {i.Text("Number")} posted: {M(posted.BaseAmount)} base (journal {posted.JournalNumber}).";
            },
            List = c => OpenItemsAsync(c, TradingLedger.AccountsReceivable),
        };
        yield return new Operation
        {
            Module = "Customers", Title = "Customer statement", ListTitle = "Open customer items", Description = "The customer's statement as at a date, ready to print or send.",
            Fields = [Req("Customer", "Customer code"), Today("AsAt", "As at")],
            Run = async (c, i) => Check(await c.Get<IStatementService>().RenderCustomerStatementTextAsync(c.CompanyId, await CustomerAsync(c, i.Text("Customer")).ConfigureAwait(true), i.Date("AsAt"), 72)
                .ConfigureAwait(true)),
            List = c => OpenItemsAsync(c, TradingLedger.AccountsReceivable),
        };
        yield return new Operation
        {
            Module = "Suppliers", Title = "Supplier invoice or credit note", ListTitle = "Open supplier items",
            Description = "Posts a supplier's invoice or credit note that has no purchase order (services, utilities); invoices for ordered goods go through Purchasing → Match a supplier invoice.",
            Fields = [Pick("Document", "Document", ["Invoice", "Credit note"]), Req("Supplier", "Supplier code"), Req("Number", "Supplier's document number"), Today("Date", "Date"), Num("Net", "Net amount"),
                Num("Tax", "Tax amount", "0"), Req("Account", "Expense account"), Opt("TaxAccount", "Tax account"), Opt("TaxCode", "Tax code"), Opt("Currency", "Currency (blank: the supplier's)"),
                Opt("Narrative", "Narrative")],
            Run = async (c, i) =>
            {
                long supplier = await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true);
                CurrencyCode currency = i.Optional("Currency") is string code ? CurrencyCode.Parse(code)
                    : await c.Db.Suppliers.AsNoTracking().Where(x => x.Id == supplier).Select(x => x.Currency).SingleAsync().ConfigureAwait(true);
                IPurchaseLedgerService purchases = c.Get<IPurchaseLedgerService>();
                TradingPostingResult posted = Check(i.Text("Document") == "Invoice"
                    ? await purchases.PostInvoiceAsync(new PurchaseInvoiceRequest
                    {
                        CompanyId = c.CompanyId, SupplierId = supplier, DocumentNumber = i.Text("Number"), DocumentDate = i.Date("Date"), Currency = currency, NetAmount = i.Number("Net"),
                        TaxAmount = i.Number("Tax"), ExpenseAccountCode = i.Text("Account"), TaxAccountCode = i.Optional("TaxAccount"), TaxCode = i.Optional("TaxCode"), Narrative = i.Optional("Narrative"),
                    }).ConfigureAwait(true)
                    : await purchases.PostCreditNoteAsync(new PurchaseCreditNoteRequest
                    {
                        CompanyId = c.CompanyId, SupplierId = supplier, DocumentNumber = i.Text("Number"), DocumentDate = i.Date("Date"), Currency = currency, NetAmount = i.Number("Net"),
                        TaxAmount = i.Number("Tax"), ExpenseAccountCode = i.Text("Account"), TaxAccountCode = i.Optional("TaxAccount"), Narrative = i.Optional("Narrative"),
                    }).ConfigureAwait(true));
                return $"{i.Text("Document")} {i.Text("Number")} posted: {M(posted.BaseAmount)} base (journal {posted.JournalNumber})"
                    + (posted.WithheldBase == 0m ? "." : $"; {M(posted.WithheldBase)} withholding tax held back.");
            },
            List = c => OpenItemsAsync(c, TradingLedger.AccountsPayable),
        };
        yield return new Operation
        {
            Module = "Suppliers", Title = "Supplier payment", ListTitle = "Open supplier items",
            Description = "A single payment to a supplier from the bank (several at once go through the payment run).",
            Fields = [Req("Supplier", "Supplier code"), Req("Number", "Payment number"), Today("Date", "Date"), Num("Amount", "Amount"), Req("Bank", "Bank account", "1100"),
                Opt("Currency", "Currency (blank: the supplier's)"), Opt("Narrative", "Narrative")],
            Run = async (c, i) =>
            {
                long supplier = await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true);
                CurrencyCode currency = i.Optional("Currency") is string code ? CurrencyCode.Parse(code)
                    : await c.Db.Suppliers.AsNoTracking().Where(x => x.Id == supplier).Select(x => x.Currency).SingleAsync().ConfigureAwait(true);
                TradingPostingResult posted = Check(await c.Get<IPurchaseLedgerService>().PostPaymentAsync(new PaymentRequest
                {
                    CompanyId = c.CompanyId, SupplierId = supplier, DocumentNumber = i.Text("Number"), PaymentDate = i.Date("Date"), Currency = currency, Amount = i.Number("Amount"),
                    BankAccountCode = i.Text("Bank"), Narrative = i.Optional("Narrative"),
                }).ConfigureAwait(true));
                return $"Payment {i.Text("Number")} posted: {M(posted.BaseAmount)} base (journal {posted.JournalNumber}).";
            },
            List = c => OpenItemsAsync(c, TradingLedger.AccountsPayable),
        };
        yield return new Operation
        {
            Module = "Suppliers", Title = "Payment run approval", ListTitle = "Saved payment runs",
            Description = "Saves a proposed payment run for review: everyone due (held suppliers left out with the reason); lines can be left out or part-paid (oldest invoices "
                + "first); someone other than the proposer approves it, which pays exactly what it holds. Require approval stops direct payment runs for the company. With funds "
                + "available (and a currency), the proposal pays suppliers by payment priority (1 first) until the funds run out. Mobile money writes a payout file to each wallet.",
            Fields = [Pick("Step", "Step", ["Save proposal", "Set line", "Approve", "Cancel", "Show", "Require approval", "Allow direct runs"]), Opt("Run", "Run number"),
                Today("Date", "Payment date"), Today("Due", "Pay invoices due on or before"), Req("Bank", "Bank account", "1100"), Opt("Currency", "Currency (blank: all)"),
                Opt("Wht", "Withholding tax code"), Opt("Layout", "Bank file layout"), Pick("Method", "Pay by", Enum.GetNames<PaymentRunMethod>()), Opt("Cheque", "First cheque number (cheques)"),
                Opt("Supplier", "Supplier code (set line)"), Tick("Include", "Pay this supplier", true), OptNum("Amount", "Amount (blank: all due)"), OptNum("Funds", "Funds available (save proposal)")],
            Run = async (c, i) =>
            {
                IPaymentRunService runs = c.Get<IPaymentRunService>();
                string Show(SavedPaymentRun r) => $"Run {r.Number} ({r.Status}, {D(r.PaymentDate)}): "
                    + string.Join("; ", r.TotalByCurrency.Select(t => $"{M(t.Value)} {t.Key}")) + Environment.NewLine
                    + string.Join(Environment.NewLine, r.Lines.Select(l => $"  {l.SupplierCode,-12} {l.Currency} due {M(l.Due),14}  pay {(l.Included ? M(l.Amount) : "-"),14}  {l.Note}{l.PaymentNumber}"));
                string run = i.Optional("Run") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Require approval":
                    case "Allow direct runs":
                        bool required = i.Text("Step") == "Require approval";
                        (await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true)).PaymentRunApprovalRequired = required;
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return required ? "Payment runs must now be saved and approved by a second person." : "Direct payment runs are allowed again.";
                    case "Save proposal":
                        return Show(Check(await runs.SaveProposalAsync(new PaymentRunRequest
                        {
                            CompanyId = c.CompanyId, PaymentDate = i.Date("Date"), DueOnOrBefore = i.Date("Due"), BankAccountCode = i.Text("Bank"), Currency = i.Optional("Currency"),
                            WithholdingTaxCode = i.Optional("Wht"), EftLayoutCode = i.Optional("Layout"), Method = Enum.Parse<PaymentRunMethod>(i.Text("Method")),
                            FirstChequeNumber = i.Optional("Cheque"), FundsAvailable = i.OptionalNumber("Funds"),
                        }, c.UserId).ConfigureAwait(true)));
                    case "Set line":
                        return Show(Check(await runs.SetLineAsync(c.CompanyId, run, i.Text("Supplier"), i.Optional("Currency") ?? string.Empty, i.Flag("Include"), i.OptionalNumber("Amount"))
                            .ConfigureAwait(true)));
                    case "Approve":
                        PaymentRunResult paid = Check(await runs.ApproveAsync(c.CompanyId, run, c.UserId).ConfigureAwait(true));
                        return $"Run {run} approved: {paid.Lines.Count(l => l.PaymentNumber.Length > 0)} payment(s), {M(paid.Total)} base."
                            + (paid.ChequeNumbers is { Count: > 0 } cheques ? $" Cheques {cheques[0]} to {cheques[^1]} written; print them from Banking." : string.Empty);
                    case "Cancel":
                        return Check(await runs.CancelAsync(c.CompanyId, run).ConfigureAwait(true), $"Run {run} cancelled.");
                    default:
                        return Show(Check(await runs.GetAsync(c.CompanyId, run).ConfigureAwait(true)));
                }
            },
            List = async c => Table(await c.Db.Set<PaymentRunBatch>().AsNoTracking().Where(b => b.CompanyId == c.CompanyId).OrderByDescending(b => b.Id).Take(200).ToListAsync().ConfigureAwait(true),
                ("Run", b => b.Number), ("Status", b => b.Status), ("Pay date", b => D(b.PaymentDate)), ("Due by", b => D(b.DueOnOrBefore)), ("Bank", b => b.BankAccountCode),
                ("Currency", b => b.Currency), ("Proposed by", b => b.ProposedByUserId), ("Approved by", b => b.ApprovedByUserId)),
        };
        yield return new Operation
        {
            Module = "Suppliers", Title = "Supplier statement", ListTitle = "Open supplier items", Description = "The supplier account as we see it, as at a date.",
            Fields = [Req("Supplier", "Supplier code"), Today("AsAt", "As at")],
            Run = async (c, i) => Check(await c.Get<IStatementService>().RenderSupplierStatementTextAsync(c.CompanyId, await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true), i.Date("AsAt"), 72)
                .ConfigureAwait(true)),
            List = c => OpenItemsAsync(c, TradingLedger.AccountsPayable),
        };

        const string credit = "Customers";
        yield return new Operation
        {
            Module = credit, Title = "Dunning reminders",
            Description = "Lists the customers due a reminder by how long their oldest debt is overdue (first reminder, second, final notice — days configurable, or a dunning "
                + "group's own; exempt groups are not chased), renders one customer's letter, or emails every letter due (not the same level again within the resend days). "
                + "Groups and customer types are kept here too.",
            Fields = [Pick("Step", "Step", ["Assess", "Letter", "Send", "Save group", "Set customer"]), Opt("Customer", "Customer code (letter / set customer)"),
                Num("First", "First reminder after (days)", "7"), Num("Second", "Second after (days)", "30"), Num("Final", "Final notice after (days)", "60"), Today("Date", "As at"),
                Opt("Template", "Email template (send)", "REMINDER"), Num("Resend", "Resend after (days)", "14"), Opt("Group", "Dunning group code"), Opt("GroupName", "Group name (save group)"),
                Tick("Exempt", "Group is not chased"), Opt("Type", "Customer type (set customer)")],
            Run = async (c, i) =>
            {
                IDunningService dunning = c.Get<IDunningService>();
                One4All.Application.Trading.DunningStages stages = new(i.Whole("First"), i.Whole("Second"), i.Whole("Final"));
                switch (i.Text("Step"))
                {
                    case "Letter":
                        return Check(await dunning.RenderReminderTextAsync(c.CompanyId, await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true), i.Date("Date"), stages).ConfigureAwait(true));
                    case "Send":
                        DunningSend sent = Check(await dunning.SendAsync(c.CompanyId, i.Date("Date"), stages, i.Optional("Template") ?? "REMINDER", Localization.UiText.Catalog.Culture.TwoLetterISOLanguageName,
                            i.Whole("Resend")).ConfigureAwait(true));
                        return $"{sent.Queued} reminder(s) queued for email; {sent.NoEmail} to post (no email address); {sent.AlreadySent} already sent recently.";
                    case "Save group":
                        return Check(await dunning.SaveGroupAsync(c.CompanyId, new DunningGroupInput(i.Optional("Group") ?? string.Empty, i.Optional("GroupName") ?? i.Optional("Group") ?? string.Empty, stages,
                            i.Flag("Exempt"))).ConfigureAwait(true), "Dunning group saved.");
                    case "Set customer":
                        return Check(await dunning.SetCustomerAsync(c.CompanyId, i.Optional("Customer") ?? string.Empty, i.Optional("Group"), i.Optional("Type")).ConfigureAwait(true), "Customer updated.");
                }

                IReadOnlyList<DunningNotice> notices = Check(await dunning.AssessAsync(c.CompanyId, i.Date("Date"), stages).ConfigureAwait(true));
                return notices.Count == 0 ? "Nobody is due a reminder." : string.Join(Environment.NewLine, notices.Select(n =>
                    $"{n.CustomerName,-30} {n.LevelLabel,-16} {M(n.OverdueAmount),14}  oldest {n.OldestDaysOverdue} days"));
            },
        };
        yield return new Operation
        {
            Module = credit, Title = "Debt collection cases", ListTitle = "Summons register",
            Description = "Hands a customer's overdue debt to a collector or lawyer (a case), records the summons, asks for the case to be written off (approved by another user), "
                + "and closes it as settled or withdrawn.",
            Fields = [Pick("Step", "Step", ["Hand over", "Summons", "Request write-off", "Approve write-off", "Close"]), Opt("Customer", "Customer code (hand over)"), Opt("Case", "Case number"),
                Opt("Party", "Handed over to / court"), Opt("Summons", "Summons number"), Opt("Hearing", "Hearing date (yyyy-MM-dd)"), Opt("Reason", "Reason (write-off)"),
                Opt("BadDebt", "Bad debt account"), Pick("Outcome", "Outcome (close)", [nameof(Core.Trading.DebtCaseStage.Settled), nameof(Core.Trading.DebtCaseStage.Withdrawn)]), Today()],
            Run = async (c, i) =>
            {
                IDebtCollectionService collection = c.Get<IDebtCollectionService>();
                string caseNumber = i.Optional("Case") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Hand over":
                        return $"Case {Check(await collection.HandOverAsync(c.CompanyId, await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true), i.Optional("Party") ?? string.Empty, i.Date("Date")).ConfigureAwait(true))} opened.";
                    case "Summons":
                        DateOnly? hearing = i.Optional("Hearing") is string h ? DateOnly.ParseExact(h, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : null;
                        return Check(await collection.RecordSummonsAsync(c.CompanyId, caseNumber, i.Optional("Summons") ?? string.Empty, i.Optional("Party") ?? string.Empty, i.Date("Date"), hearing).ConfigureAwait(true),
                            "Summons recorded.");
                    case "Request write-off":
                        return Check(await collection.RequestWriteOffAsync(c.CompanyId, caseNumber, c.UserId, i.Optional("Reason") ?? string.Empty, i.Optional("BadDebt") ?? string.Empty).ConfigureAwait(true),
                            "Write-off requested; another user approves it.");
                    case "Approve write-off":
                        CaseWriteOff written = Check(await collection.ApproveWriteOffAsync(c.CompanyId, caseNumber, c.UserId, i.Date("Date"), i.Optional("BadDebt") ?? string.Empty).ConfigureAwait(true));
                        return $"Written off: {written.InvoicesWrittenOff} invoice(s), {M(written.Amount)}.";
                    default:
                        return Check(await collection.CloseAsync(c.CompanyId, caseNumber, Enum.Parse<Core.Trading.DebtCaseStage>(i.Text("Outcome")), i.Date("Date")).ConfigureAwait(true), "Case closed.");
                }
            },
            List = async c => Table(await c.Get<IDebtCollectionService>().SummonsRegisterAsync(c.CompanyId).ConfigureAwait(true),
                ("Case", x => x.CaseNumber), ("Customer", x => x.CustomerCode), ("Summons", x => x.SummonsNumber), ("Court", x => x.Court), ("Issued", x => D(x.IssuedOn)),
                ("Hearing", x => x.HearingDate is DateOnly d ? D(d) : "-"), ("Amount", x => M(x.AmountHandedOver))),
        };
        yield return new Operation
        {
            Module = credit, Title = "Raise finance charges",
            Description = "Charges interest on overdue customer balances past the grace days at the annual rate (simple or compound, with a minimum charge), optionally exempting "
                + "indigent customers.",
            Fields = [Today("Date", "As at"), Num("Rate", "Annual rate %"), Num("Grace", "Grace days", "30"), Num("Minimum", "Minimum charge", "0"), Req("Income", "Interest income account"),
                Req("Prefix", "Document prefix", "FC"), Tick("Compound", "Compound"), Tick("Indigent", "Exempt indigent customers", true)],
            Run = async (c, i) =>
            {
                FinanceChargeRunResult run = Check(await c.Get<IFinanceChargeService>().RaiseChargesAsync(new FinanceChargeRunRequest
                {
                    CompanyId = c.CompanyId, AsAtDate = i.Date("Date"), AnnualRatePercent = i.Number("Rate"), GraceDays = i.Whole("Grace"), MinimumCharge = i.Number("Minimum"),
                    InterestIncomeAccountCode = i.Text("Income"), DocumentPrefix = i.Text("Prefix"), Compound = i.Flag("Compound"), ExemptIndigent = i.Flag("Indigent"),
                }).ConfigureAwait(true));
                return $"{run.Charges.Count} customer(s) charged {M(run.TotalInterest)}{(run.CustomersExempted > 0 ? $"; {run.CustomersExempted} exempted" : string.Empty)}.";
            },
        };
        yield return new Operation
        {
            Module = credit, Title = "Credit limits", ListTitle = "Increases awaiting approval",
            Description = "Lowers a customer's credit limit at once; an increase is requested with the reason and approved (or rejected) by another user. Policy: what an invoice over "
                + "the limit does — Block (refused unless a supervisor overrides), Warn (posted, flagged and logged) or Ignore.",
            Fields = [Pick("Step", "Step", ["Lower", "Request increase", "Approve", "Reject", "Policy"]), Opt("Customer", "Customer code"), Num("Limit", "New limit", "0"), Opt("Reason", "Reason"),
                OptNum("Request", "Request id"), Pick("Policy", "Credit-limit policy", Enum.GetNames<Core.Trading.CreditLimitPolicy>())],
            Run = async (c, i) =>
            {
                ICreditLimitService limits = c.Get<ICreditLimitService>();
                long request = (long)(i.OptionalNumber("Request") ?? 0m);
                switch (i.Text("Step"))
                {
                    case "Lower":
                        return Check(await limits.LowerAsync(c.CompanyId, await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true), i.Number("Limit")).ConfigureAwait(true), "Limit lowered.");
                    case "Request increase":
                        return Check(await limits.RequestIncreaseAsync(c.CompanyId, await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true), i.Number("Limit"), i.Optional("Reason") ?? string.Empty, c.UserId)
                            .ConfigureAwait(true), id => $"Request {id} awaits another user's approval.");
                    case "Approve":
                        return Check(await limits.ApproveAsync(c.CompanyId, request, c.UserId).ConfigureAwait(true), "Limit raised.");
                    case "Policy":
                        long customerId = await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true);
                        Core.Trading.Customer customer = await c.Db.Customers.FirstAsync(x => x.Id == customerId).ConfigureAwait(true);
                        customer.CreditLimitPolicy = Enum.Parse<Core.Trading.CreditLimitPolicy>(i.Text("Policy"));
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return $"{customer.Code}: invoices over the limit now {customer.CreditLimitPolicy switch { Core.Trading.CreditLimitPolicy.Warn => "post with a warning", Core.Trading.CreditLimitPolicy.Ignore => "are not checked", _ => "are refused unless overridden" }}.";
                    default:
                        return Check(await limits.RejectAsync(c.CompanyId, request, c.UserId, i.Optional("Reason") ?? string.Empty).ConfigureAwait(true), "Request rejected.");
                }
            },
            List = async c => Table(await c.Get<ICreditLimitService>().PendingAsync(c.CompanyId).ConfigureAwait(true),
                ("Request", x => x.RequestId), ("Customer", x => x.CustomerCode), ("Current", x => M(x.CurrentLimit)), ("Requested", x => M(x.RequestedLimit)), ("Reason", x => x.Reason)),
        };
        yield return new Operation
        {
            Module = credit, Title = "Expected credit loss on debtors",
            Description = "Assesses the IFRS 9 loss allowance on trade debtors by arrears band (default provision matrix) against the allowance booked; Post books the difference.",
            Fields = [Pick("Step", "Step", ["Assess", "Post"]), Today("Date", "As at"), Req("Allowance", "Loss allowance account"), Opt("Expense", "Impairment expense account (post)")],
            Run = async (c, i) =>
            {
                IExpectedCreditLossService ecl = c.Get<IExpectedCreditLossService>();
                One4All.Application.Trading.EclMatrix matrix = One4All.Application.Trading.EclMatrix.Default;
                EclReport report;
                string? journal = null;
                if (i.Text("Step") == "Post")
                {
                    EclPostingResult posted = Check(await ecl.PostAsync(c.CompanyId, i.Date("Date"), matrix, i.Text("Allowance"), i.Optional("Expense") ?? string.Empty).ConfigureAwait(true));
                    (report, journal) = (posted.Report, posted.JournalNumber);
                }
                else
                {
                    report = Check(await ecl.AssessAsync(c.CompanyId, i.Date("Date"), matrix, i.Text("Allowance")).ConfigureAwait(true));
                }

                return string.Join(Environment.NewLine, report.Bands.Select(b => $"{b.Band,-14} exposure {M(b.Exposure),14}  provision {M(b.Provision),12}"))
                    + Environment.NewLine + $"Needed {M(report.TargetAllowance)}, booked {M(report.BookedAllowance)}, adjustment {M(report.Adjustment)}{(journal is null ? string.Empty : $" (posted {journal})")}.";
            },
        };
        yield return new Operation
        {
            Module = credit, Title = "Promises to pay", ListTitle = "Open promises",
            Description = "Records a customer's promise to pay an amount by a date; Review shows, as at a date, which promises were kept, which were broken and how many are still open.",
            Fields = [Pick("Step", "Step", ["Record", "Review"]), Opt("Customer", "Customer code"), Num("Amount", "Amount", "0"), Today("PayBy", "Pay by"), Opt("Note", "Note"), Today("Date", "Made on / as at")],
            Run = async (c, i) =>
            {
                IPromiseToPayService promises = c.Get<IPromiseToPayService>();
                if (i.Text("Step") == "Record")
                {
                    return Check(await promises.RecordAsync(c.CompanyId, await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true), i.Date("Date"), i.Date("PayBy"), i.Number("Amount"),
                        i.Optional("Note"), c.UserId).ConfigureAwait(true), id => $"Promise {id} recorded.");
                }

                PromiseReview review = await promises.ReviewAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true);
                return $"Kept: {review.Kept.Count}. Broken: {review.Broken.Count}{(review.Broken.Count == 0 ? string.Empty : " — " + string.Join(", ", review.Broken.Select(b => $"{b.CustomerCode} {M(b.Amount)} by {D(b.PayBy)}")))}. "
                    + $"Still open: {review.StillOpen}.";
            },
            List = async c => Table(await c.Get<IPromiseToPayService>().DiaryAsync(c.CompanyId, Core.Trading.PromiseStatus.Open).ConfigureAwait(true),
                ("Promise", x => x.PromiseId), ("Customer", x => x.CustomerCode), ("Made", x => D(x.MadeOn)), ("Pay by", x => D(x.PayBy)), ("Amount", x => M(x.Amount)), ("Note", x => x.Note)),
        };
    }

    private static async Task<DataTable> OpenItemsAsync(OperationContext c, TradingLedger ledger)
        => Table(await c.Db.OpenItems.AsNoTracking().Where(o => o.CompanyId == c.CompanyId && o.Ledger == ledger && o.OutstandingTransaction != 0m).OrderByDescending(o => o.Id).Take(500).ToListAsync()
                .ConfigureAwait(true), ("Trader", o => o.TraderId), ("Document", o => o.DocumentNumber), ("Type", o => o.ItemType), ("Date", o => D(o.DocumentDate)), ("Due", o => D(o.DueDate)),
            ("Currency", o => o.Currency), ("Original", o => M(o.Sign == LedgerSign.Debit ? o.OriginalTransaction : -o.OriginalTransaction)),
            ("Outstanding", o => M(o.Sign == LedgerSign.Debit ? o.OutstandingTransaction : -o.OutstandingTransaction)));
}
