using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Vouchers;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Vouchers: payment, receipt, journal and petty cash vouchers; fuel and meal allowance vouchers; promotional codes; and gift card servicing.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Vouchers()
    {
        const string ledger = "General Ledger", pos = "Point of Sale", suppliers = "Suppliers";
        yield return new Operation
        {
            Module = ledger, Title = "Accounting vouchers", ListTitle = "Vouchers",
            Description = "Payment, receipt, journal and petty cash vouchers: prepared by one user (lines ACCOUNT:amount; … - for a journal, debits positive and credits negative), "
                + "checked by a second, approved and posted by another (or through the approvals inbox when an AccountingVoucher workflow is defined), cancelled while a draft, "
                + "and printed with prepared / checked / approved signature blocks. Supporting documents attach under Attachments (target AccountingVoucher, key the voucher number).",
            Fields = [Pick("Step", "Step", ["Prepare", "Check", "Approve", "Cancel", "Print"]), Pick("Type", "Type (prepare)", Enum.GetNames<AccountingVoucherType>()), Opt("Number", "Voucher number"),
                Opt("Party", "Paid to / received from / about (prepare)"), Opt("Cash", "Cash or bank account (not a journal)"), Opt("Lines", "Lines: ACCOUNT:amount[:narrative[:cost centre[:department[:project[:tax code]]]]]; … (a taxed line is gross; its VAT is split out)"),
                Opt("Narrative", "Narrative"), Today()],
            Run = async (c, i) =>
            {
                IAccountingVoucherService vouchers = c.Get<IAccountingVoucherService>();
                if (i.Text("Step") == "Prepare")
                {
                    List<VoucherLineRequest> lines = [];
                    foreach (string part in (i.Optional("Lines") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string[] bits = part.Split(':', 7, StringSplitOptions.TrimEntries);
                        string? Bit(int n) => bits.Length > n && bits[n].Length > 0 ? bits[n] : null;
                        lines.Add(bits.Length >= 2 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount)
                            ? new VoucherLineRequest(bits[0], amount, Bit(2), Bit(3), Bit(4), Bit(5), Bit(6))
                            : throw new OperationException($"Write each line as ACCOUNT:amount[:narrative[:cost centre[:department[:project[:tax code]]]]] (\"{part}\")."));
                    }

                    PreparedVoucher prepared = Check(await vouchers.PrepareAsync(c.CompanyId, new VoucherRequest(Enum.Parse<AccountingVoucherType>(i.Text("Type")), i.Date("Date"), i.Optional("Party") ?? string.Empty,
                        i.Optional("Cash"), i.Optional("Narrative"), lines, c.UserId)).ConfigureAwait(true));
                    return $"Voucher {prepared.Number} prepared ({M(prepared.Total)}); another user approves it.";
                }

                string number = i.Optional("Number") ?? string.Empty;
                long id = await FindAsync(c.Db.Set<AccountingVoucher>(), v => v.CompanyId == c.CompanyId && v.Number.ToUpper() == number.ToUpper(), v => v.Id, "voucher", number).ConfigureAwait(true);
                return i.Text("Step") switch
                {
                    "Check" => Check(await vouchers.CheckAsync(c.CompanyId, id, c.UserId).ConfigureAwait(true), $"Voucher {number} checked."),
                    "Approve" => Check(await vouchers.ApproveAsync(c.CompanyId, id, c.UserId).ConfigureAwait(true), j => $"Voucher {number} approved and posted ({j})."),
                    "Cancel" => Check(await vouchers.CancelAsync(c.CompanyId, id).ConfigureAwait(true), $"Voucher {number} cancelled."),
                    _ => Check(await vouchers.RenderAsync(c.CompanyId, id).ConfigureAwait(true)),
                };
            },
            List = async c => Table(await c.Db.Set<AccountingVoucher>().AsNoTracking().Where(v => v.CompanyId == c.CompanyId).OrderByDescending(v => v.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
                ("Voucher", v => v.Number), ("Type", v => v.Type), ("Date", v => D(v.Date)), ("Party", v => v.Party), ("Status", v => v.Status),
                ("Checked", v => v.CheckedByUserId is null ? string.Empty : "yes"), ("Journal", v => v.JournalNumber ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = suppliers, Title = "Allowance vouchers", ListTitle = "Outstanding allowance vouchers",
            Description = "Numbered fuel and meal vouchers issued to staff or a department, then reconciled against the supplier's invoice (serial@date; … as billed) - "
                + "showing any difference between the vouchers' value and the amount invoiced.",
            Fields = [Pick("Step", "Step", ["Issue", "Reconcile invoice"]), Opt("Prefix", "Serial prefix (issue)"), OptNum("First", "First number (issue)"), OptNum("Count", "How many (issue)"),
                Pick("Kind", "Kind", Enum.GetNames<AllowanceKind>()), Num("Face", "Face value", "0"), Opt("IssuedTo", "Issued to (issue)"), Today("Expires", "Expires on (issue)"),
                Opt("Supplier", "Supplier code (reconcile)"), Opt("Invoice", "Invoice number"), Num("Invoiced", "Invoiced amount", "0"), Opt("Vouchers", "Billed: SERIAL@yyyy-mm-dd; …"), Today()],
            Run = async (c, i) =>
            {
                IAllowanceVoucherService allowances = c.Get<IAllowanceVoucherService>();
                if (i.Text("Step") == "Issue")
                {
                    IReadOnlyList<string> serials = Check(await allowances.IssueAsync(c.CompanyId, new AllowanceIssueRequest(i.Optional("Prefix") ?? string.Empty, (int)(i.OptionalNumber("First") ?? 1m),
                        (int)(i.OptionalNumber("Count") ?? 0m), Enum.Parse<AllowanceKind>(i.Text("Kind")), i.Number("Face"), i.Optional("IssuedTo") ?? string.Empty, i.Date("Date"), i.Date("Expires")))
                        .ConfigureAwait(true));
                    return serials.Count == 0 ? "Nothing issued." : $"{serials.Count} voucher(s) issued: {serials[0]} to {serials[^1]}.";
                }

                List<BilledVoucher> billed = [];
                foreach (string part in (i.Optional("Vouchers") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = part.Split('@', StringSplitOptions.TrimEntries);
                    billed.Add(bits.Length == 2 && DateOnly.TryParse(bits[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly used)
                        ? new BilledVoucher(bits[0], used)
                        : bits.Length == 1 ? new BilledVoucher(bits[0], i.Date("Date")) : throw new OperationException($"Write each voucher as SERIAL@yyyy-mm-dd (\"{part}\")."));
                }

                VoucherReconciliation rec = Check(await allowances.ReconcileInvoiceAsync(c.CompanyId, await SupplierAsync(c, i.Optional("Supplier") ?? string.Empty).ConfigureAwait(true),
                    i.Optional("Invoice") ?? string.Empty, i.Number("Invoiced"), billed).ConfigureAwait(true));
                return $"Invoice {rec.InvoiceNumber}: {rec.VouchersMatched} voucher(s) worth {M(rec.VoucherValue)} against {M(rec.InvoicedAmount)} invoiced; difference {M(rec.Difference)}.";
            },
            List = async c => Table(await c.Get<IAllowanceVoucherService>().OutstandingAsync(c.CompanyId, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true),
                ("Issued to", o => o.IssuedTo), ("Kind", o => o.Kind), ("Vouchers", o => o.Count), ("Value", o => M(o.Value)), ("Expired", o => o.Expired)),
        };
        yield return new Operation
        {
            Module = pos, Title = "Promotional codes", ListTitle = "Promo campaigns", KeyColumn = "Campaign",
            Delete = Retire<PromoCampaign>(x => x.Code, x => x.IsActive = false, "campaign"),
            Description = "A campaign of discount codes (a % or an amount off, a minimum spend, uses per code, a cap on redemptions, a validity window); quote or redeem a code against "
                + "a spend; and analyse a campaign (codes used, redemptions, spend and discount given). A campaign may be limited to customer types and to item categories "
                + "(the minimum spend and discount then count only those lines: give the basket as CATEGORY:amount; …). Create campaign with an existing campaign code changes it "
                + "(and adds any new codes given; the discount is fixed once a code is redeemed); Delete stops its codes working.",
            Fields = [Pick("Step", "Step", ["Create campaign", "Quote", "Redeem", "Analyse"]), Opt("Campaign", "Campaign code"), Opt("Name", "Name (create)"),
                Pick("DiscountType", "Discount", Enum.GetNames<PromoDiscountType>()), Num("Discount", "Discount value", "0"), Today("From", "Valid from"), Today("To", "Valid to"),
                Num("MinSpend", "Minimum spend", "0"), Num("Uses", "Uses per code", "1"), OptNum("Max", "Most redemptions (blank: no cap)"), Opt("Codes", "Codes: A1, A2, … (create) / the code (quote, redeem)"),
                Num("Spend", "Spend (quote, redeem)", "0"), Opt("Reference", "Sale reference (redeem)"), Today(),
                Opt("CustomerTypes", "Customer types (create: who may use it, comma-separated; quote/redeem: the customer's type)"),
                Opt("Categories", "Item categories (create: what it discounts, comma-separated; quote/redeem: the basket CATEGORY:amount; …)")],
            Run = async (c, i) =>
            {
                IPromoService promos = c.Get<IPromoService>();
                string campaign = i.Optional("Campaign") ?? string.Empty, codes = i.Optional("Codes") ?? string.Empty;
                PromoEligibility Eligibility()
                {
                    List<PromoBasketLine> basket = [];
                    foreach (string part in (i.Optional("Categories") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                        basket.Add(bits.Length == 2 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount)
                            ? new PromoBasketLine(bits[0], amount)
                            : throw new OperationException($"Write the basket as CATEGORY:amount; … (\"{part}\")."));
                    }

                    return new PromoEligibility(i.Optional("CustomerTypes"), basket);
                }

                switch (i.Text("Step"))
                {
                    case "Create campaign":
                        IReadOnlyList<string> list = codes.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (await ExistingAsync<PromoCampaign>(c, campaign, x => x.Code).ConfigureAwait(true) is { } existing)
                        {
                            return await ChangeCampaignAsync(c, i, existing, list).ConfigureAwait(true);
                        }

                        return Check(await promos.CreateCampaignAsync(c.CompanyId, new PromoCampaignRequest(campaign, i.Optional("Name") ?? campaign, Enum.Parse<PromoDiscountType>(i.Text("DiscountType")),
                            i.Number("Discount"), i.Date("From"), i.Date("To"), i.Number("MinSpend"), i.Whole("Uses"), i.OptionalNumber("Max") is decimal max ? (int)max : null, list,
                            i.Optional("CustomerTypes"), i.Optional("Categories"))).ConfigureAwait(true),
                            _ => $"Campaign {campaign} created with {list.Count} code(s).");
                    case "Quote":
                        PromoQuote quote = Check(await promos.QuoteAsync(c.CompanyId, codes.Trim(), i.Date("Date"), i.Number("Spend"), Eligibility()).ConfigureAwait(true));
                        return $"{quote.Code} ({quote.CampaignCode}) takes {M(quote.Discount)} off.";
                    case "Redeem":
                        PromoQuote redeemed = Check(await promos.RedeemAsync(c.CompanyId, codes.Trim(), i.Date("Date"), i.Number("Spend"), i.Optional("Reference") ?? string.Empty, Eligibility()).ConfigureAwait(true));
                        return $"{redeemed.Code} redeemed: {M(redeemed.Discount)} off.";
                    default:
                        long id = await FindAsync(c.Db.Set<PromoCampaign>(), p => p.CompanyId == c.CompanyId && p.Code.ToUpper() == campaign.ToUpper(), p => p.Id, "campaign", campaign).ConfigureAwait(true);
                        PromoAnalysis a = Check(await promos.AnalysisAsync(c.CompanyId, id).ConfigureAwait(true));
                        return $"{a.CampaignCode}: {a.CodesUsed} of {a.CodesIssued} code(s) used, {a.Redemptions} redemption(s); spend {M(a.Spend)}, discount {M(a.Discount)}.";
                }
            },
            List = async c => Table(await c.Db.Set<PromoCampaign>().AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderByDescending(p => p.ValidTo).ToListAsync().ConfigureAwait(true),
                ("Campaign", p => p.Code), ("Name", p => p.Name), ("Discount type", p => p.DiscountType), ("Discount value", p => p.DiscountValue.ToString("0.##", CultureInfo.InvariantCulture)),
                ("From", p => D(p.ValidFrom)), ("To", p => D(p.ValidTo)), ("Minimum spend", p => M(p.MinimumSpend)), ("Uses per code", p => p.UsesPerCode),
                ("Max", p => p.MaxRedemptions?.ToString(CultureInfo.InvariantCulture) ?? string.Empty), ("Customer types", p => p.CustomerTypes ?? string.Empty),
                ("Categories", p => p.ItemCategories ?? string.Empty), ("Redemptions", p => p.Redemptions), ("Active", p => p.IsActive)),
        };
        yield return new Operation
        {
            Module = pos, Title = "Gift cards: balance, top up, block, replace, breakage", ListTitle = "Gift cards",
            Description = "Services a gift card: its balance and history, a top-up, blocking a lost card and moving its balance to a replacement, and recognising breakage "
                + "(the unspent balance of one card, or of every expired card) as income.",
            Fields = [Pick("Step", "Step", ["Balance and history", "Top up", "Block", "Replace", "Breakage (one card)", "Breakage (expired cards)"]), Opt("Card", "Card number"),
                Opt("NewCard", "Replacement card number"), Num("Amount", "Top-up amount", "0"), Opt("Reason", "Reason (block)"), Opt("Cash", "Cash account (top up)"),
                Opt("Liability", "Gift card liability account"), Opt("Breakage", "Breakage income account"), Today()],
            Run = async (c, i) =>
            {
                IGiftCardServicing servicing = c.Get<IGiftCardServicing>();
                IVoucherService cards = c.Get<IVoucherService>();
                string card = (i.Optional("Card") ?? string.Empty).Trim(), liability = i.Optional("Liability") ?? string.Empty;
                static string Shown(GiftCardResult r) => $"Balance {M(r.Balance)} ({r.Status}).";
                switch (i.Text("Step"))
                {
                    case "Balance and history":
                        GiftCardResult balance = Check(await cards.GetBalanceAsync(c.CompanyId, card).ConfigureAwait(true));
                        IReadOnlyList<GiftCardHistoryLine> history = Check(await servicing.HistoryAsync(c.CompanyId, card).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, history.Select(h => $"{D(h.OccurredOn)} {h.Event,-12} {M(h.Amount),12} → {M(h.BalanceAfter),12} {h.Note}")) + Environment.NewLine + Shown(balance);
                    case "Top up":
                        return Shown(Check(await servicing.TopUpAsync(c.CompanyId, card, i.Number("Amount"), i.Date("Date"), i.Optional("Cash") ?? string.Empty, liability).ConfigureAwait(true)));
                    case "Block":
                        return Shown(Check(await servicing.BlockAsync(c.CompanyId, card, i.Date("Date"), i.Optional("Reason") ?? string.Empty).ConfigureAwait(true)));
                    case "Replace":
                        return "Replacement card: " + Shown(Check(await servicing.ReplaceAsync(c.CompanyId, card, (i.Optional("NewCard") ?? string.Empty).Trim(), i.Date("Date")).ConfigureAwait(true)));
                    case "Breakage (one card)":
                        return Shown(Check(await cards.RecogniseBreakageAsync(c.CompanyId, card, i.Date("Date"), liability, i.Optional("Breakage") ?? string.Empty).ConfigureAwait(true)));
                    default:
                        BreakageRunResult run = Check(await cards.RecogniseExpiredBreakageAsync(c.CompanyId, i.Date("Date"), liability, i.Optional("Breakage") ?? string.Empty).ConfigureAwait(true));
                        return $"{run.CardsExpired} expired card(s); breakage {M(run.TotalBreakage)} recognised.";
                }
            },
            List = async c => Table(await c.Db.Set<GiftCard>().AsNoTracking().Where(g => g.CompanyId == c.CompanyId && g.Balance != 0m).OrderBy(g => g.CardNumber).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Card", g => g.CardNumber), ("Balance", g => M(g.Balance)), ("Issued", g => D(g.IssuedDate)), ("Expires", g => g.ExpiryDate is DateOnly e ? D(e) : string.Empty), ("Status", g => g.Status)),
        };
    }

    // Changes an existing promo campaign from the form: its name, window, minimum spend, uses, cap and limits; its discount only while no code has been redeemed;
    // new codes typed are added (each unique across the company's campaigns); a retired campaign is brought back into use.
    private static async Task<string> ChangeCampaignAsync(OperationContext c, OperationInput i, PromoCampaign campaign, IReadOnlyList<string> newCodes)
    {
        PromoDiscountType type = Enum.Parse<PromoDiscountType>(i.Text("DiscountType"));
        decimal value = i.Number("Discount");
        int uses = i.Whole("Uses");
        int? max = i.OptionalNumber("Max") is decimal cap ? (int)cap : null;
        if (i.Date("To") < i.Date("From") || value <= 0m || (type == PromoDiscountType.Percent && value > 100m) || uses < 1 || max < 1)
        {
            throw new OperationException("A campaign needs a validity window, a positive discount (at most 100%), at least one use per code and a cap of at least 1 (or none).");
        }

        if (campaign.Redemptions > 0 && (type != campaign.DiscountType || value != campaign.DiscountValue))
        {
            throw new OperationException($"Campaign {campaign.Code} has been redeemed {campaign.Redemptions} time(s); its discount cannot change. Start a new campaign for a different discount.");
        }

        List<string> adding = [.. newCodes.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal)];
        if (adding.Count > 0)
        {
            List<string> taken = await c.Db.Set<PromoCampaign>().Where(x => x.CompanyId == c.CompanyId).SelectMany(x => x.Codes).Select(x => x.Code).ToListAsync().ConfigureAwait(true);
            adding = [.. adding.Where(x => !taken.Contains(x, StringComparer.Ordinal))];
        }

        campaign.Name = i.Optional("Name") ?? campaign.Name;
        campaign.ChangeTerms(type, value, i.Date("From"), i.Date("To"), i.Number("MinSpend"), uses, max);
        campaign.CustomerTypes = i.Optional("CustomerTypes");
        campaign.ItemCategories = i.Optional("Categories");
        campaign.IsActive = true;
        foreach (string code in adding)
        {
            campaign.AddCode(code);
        }

        await c.Db.SaveChangesAsync().ConfigureAwait(true);
        return $"Campaign {campaign.Code} changed" + (adding.Count == 0 ? "." : $"; {adding.Count} code(s) added.");
    }
}
