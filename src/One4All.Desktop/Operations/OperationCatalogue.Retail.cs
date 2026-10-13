using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Application.Inventory;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Point-of-sale and stock operations.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Retail()
    {
        yield return new Operation
        {
            Module = "Point of Sale", Title = "Create gift card stock", ListTitle = "Gift card stock",
            Description = "A range of printed cards (prefix plus zero-padded serials), inactive with no value or liability until activated at sale.",
            Fields = [Req("Prefix", "Number prefix", "GC"), Num("First", "First serial", "1"), Num("Last", "Last serial"), Num("Digits", "Serial digits", "6")],
            Run = async (c, i) =>
            {
                Core.Accounting.CurrencyCode currency = await c.Db.Companies.Where(x => x.Id == c.CompanyId).Select(x => x.BaseCurrency).SingleAsync().ConfigureAwait(true);
                return $"{Check(await c.Get<Application.Abstractions.Vouchers.IGiftCardServicing>().CreateRangeAsync(c.CompanyId, i.Text("Prefix"), (long)i.Number("First"), (long)i.Number("Last"),
                    (int)i.Number("Digits"), currency, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true))} card(s) created.";
            },
            List = GiftCardStockAsync,
        };
        yield return new Operation
        {
            Module = "Point of Sale", Title = "Change currency and cash rounding",
            Description = "The currency the till gives change in (blank: base), converted at the day's rate and rounded to its smallest note or coin; the base cash rounding "
                + "increment (e.g. 0.05); and the account rounding differences go to.",
            Fields = [Opt("Currency", "Change currency (blank: base)"), OptNum("Increment", "Base cash rounding increment"), Opt("Account", "Rounding difference account")],
            Run = async (c, i) => Check(await c.Get<IPosService>().ConfigureChangeAsync(c.CompanyId, i.Optional("Currency"), i.OptionalNumber("Increment"), i.Optional("Account")).ConfigureAwait(true),
                "Change settings saved."),
        };
        yield return new Operation
        {
            Module = "Point of Sale", Title = "Activate a gift card", ListTitle = "Gift card stock", Description = "Loads a card of printed stock with its value when sold (Dr cash, Cr gift card liability).",
            Fields = [Req("Card", "Card number"), Num("Value", "Value"), Today(), Opt("Expiry", "Expires (yyyy-MM-dd, optional)"), Req("Cash", "Cash account", "1000"), Req("Liability", "Gift card liability account")],
            Run = async (c, i) =>
            {
                Application.Abstractions.Vouchers.GiftCardResult card = Check(await c.Get<Application.Abstractions.Vouchers.IGiftCardServicing>().ActivateAsync(c.CompanyId, i.Text("Card"), i.Number("Value"),
                    i.Date("Date"), i.Optional("Expiry") is string e ? DateOnly.ParseExact(e, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : null, i.Text("Cash"), i.Text("Liability"))
                    .ConfigureAwait(true));
                return $"Card active with {M(card.Balance)}.";
            },
            List = GiftCardStockAsync,
        };
        const string module = "Point of Sale";
        string[] kinds = [.. Enum.GetNames<PosEventKind>().Where(k => k != nameof(PosEventKind.Refund))];
        yield return new Operation
        {
            Module = module, Title = "Till favourites", ListTitle = "Favourites",
            Description = "Items pinned to the till's ★ Favourites tabs — for one terminal or every till, and optionally for one cashier (their own come first, then the till's, "
                + "then every till's), on a page (★ Favourites, ★ Favourites 2…) in a colour (#RRGGBB or a name). Profile: what kind of counter the terminal is. "
                + "Function keys: the terminal's F1–F12 (F2=TenderCash; F3=TenderCard; …) over its profile's defaults.",
            Fields = [Pick("Step", "Step", ["Favourite", "Profile", "Function keys"]), Opt("Item", "Item code"), Opt("Terminal", "Terminal code (blank: every till)"), Opt("Cashier", "Cashier user name (blank: anyone)"),
                Num("Order", "Position", "10"), Num("Page", "Page", "1"), Opt("Colour", "Tile colour"), Tick("Remove", "Remove it"),
                Pick("Profile", "Terminal profile", Enum.GetNames<Core.Pos.TerminalProfile>()), Opt("Keys", "Function keys F2=TenderCash; … (blank: the profile's)")],
            Run = async (c, i) =>
            {
                long? terminal = null;
                if (i.Optional("Terminal") is string code)
                {
                    string key = code.ToUpperInvariant();
                    terminal = await FindAsync(c.Db.PosTerminals, t => t.CompanyId == c.CompanyId && t.Code == key, t => t.Id, "terminal", code).ConfigureAwait(true);
                }

                if (i.Text("Step") == "Profile")
                {
                    Core.Pos.PosTerminal till = await c.Db.PosTerminals.FirstOrDefaultAsync(t => t.Id == terminal).ConfigureAwait(true)
                        ?? throw new OperationException("Name the terminal whose profile to set.");
                    till.Profile = Enum.Parse<Core.Pos.TerminalProfile>(i.Text("Profile"));
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Terminal {till.Code} is a {till.Profile} till.";
                }

                if (i.Text("Step") == "Function keys")
                {
                    Core.Pos.PosTerminal till = await c.Db.PosTerminals.FirstOrDefaultAsync(t => t.Id == terminal).ConfigureAwait(true)
                        ?? throw new OperationException("Name the terminal whose function keys to set.");
                    IReadOnlyDictionary<string, string> own = Check(Application.Pos.TillFunctionKeys.Parse(i.Optional("Keys")));
                    till.FunctionKeys = own.Count == 0 ? null : string.Join("; ", own.Select(k => $"{k.Key}={k.Value}"));
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Terminal {till.Code}: {Application.Pos.TillFunctionKeys.Describe(Application.Pos.TillFunctionKeys.For(till.Profile, till.FunctionKeys))}";
                }

                long? cashier = null;
                if (i.Optional("Cashier") is string name)
                {
                    string user = name.ToUpperInvariant();
                    cashier = await FindAsync(c.Db.Users, u => u.NormalizedUsername == user, u => u.Id, "user", name).ConfigureAwait(true);
                }

                return Check(await c.Get<IPosCatalogueService>().SetFavouriteAsync(c.CompanyId, terminal, cashier, i.Optional("Item") ?? string.Empty, i.Whole("Order"), i.Whole("Page"),
                    i.Optional("Colour"), i.Flag("Remove")).ConfigureAwait(true), i.Flag("Remove") ? "Favourite removed." : "Favourite saved.");
            },
            List = async c => Table(await (from f in c.Db.PosFavourites.AsNoTracking()
                                           join it in c.Db.Items.AsNoTracking() on f.ItemId equals it.Id
                                           join t in c.Db.PosTerminals.AsNoTracking() on f.TerminalId equals (long?)t.Id into terminals
                                           from t in terminals.DefaultIfEmpty()
                                           where f.CompanyId == c.CompanyId
                                           orderby f.TerminalId, f.SortOrder
                                           select new { Till = t == null ? "every till" : t.Code, it.Code, it.Description, f.SortOrder }).ToListAsync().ConfigureAwait(true),
                ("Till", r => r.Till), ("Item", r => r.Code), ("Description", r => r.Description), ("Position", r => r.SortOrder)),
        };
        yield return new Operation
        {
            Module = module, Title = "Refund window", Description = "How many days after a sale an ordinary supervisor may authorise its refund (blank: no limit). Past it, the authoriser needs the late-refund authority (Pos.Refund, AuthoriseLevel2).",
            Fields = [OptNum("Days", "Days")],
            Run = async (c, i) =>
            {
                Core.System.Company company = await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                company.RefundWindowDays = i.OptionalNumber("Days") is decimal days ? (int)days : null;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return company.RefundWindowDays is int d ? $"Refunds after {d} days need late-refund authority." : "Refunds have no time limit.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Category under a category", ListTitle = "Item categories",
            Description = "Places a category under another so the till can drill down (blank parent: top level).",
            Fields = [Req("Category", "Category code"), Opt("Parent", "Parent category code")],
            Run = async (c, i) =>
            {
                string code = i.Text("Category").ToUpperInvariant(), parent = (i.Optional("Parent") ?? string.Empty).ToUpperInvariant();
                ItemCategory category = await c.Db.ItemCategories.FirstOrDefaultAsync(x => x.CompanyId == c.CompanyId && x.Code == code).ConfigureAwait(true)
                    ?? throw new OperationException($"Category {code} not found.");
                if (parent.Length > 0)
                {
                    List<ItemCategory> all = await c.Db.ItemCategories.Where(x => x.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true);
                    for (string? up = parent; up is not null; up = all.FirstOrDefault(x => x.Code == up)?.ParentCode)
                    {
                        if (up == code || all.All(x => x.Code != up))
                        {
                            throw new OperationException(up == code ? "A category cannot sit under itself or its own sub-category." : $"Category {up} not found.");
                        }
                    }
                }

                category.ParentCode = parent.Length == 0 ? null : parent;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return parent.Length == 0 ? $"{code} is a top-level category." : $"{code} now sits under {parent}.";
            },
            List = async c => Table(await c.Db.ItemCategories.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.ParentCode).ThenBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Under", x => x.ParentCode ?? "—"), ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Save a promotion", ListTitle = "Promotions",
            Description = "NForPrice (N items for a price), BuyXGetY (no reward items = lowest price free), PercentOff (items or a category), MealDeal (courses separated by '|'). Items as codes separated by ','.",
            Fields = [Req("Code", "Code"), Req("Name", "Name on receipts"), Pick("Kind", "Kind", Enum.GetNames<PromotionKind>()), Req("Account", "Discount account"), Num("Quantity", "N / X", "0"),
                Num("Reward", "Y", "0"), OptNum("Price", "Set price"), OptNum("Percent", "Percent off"), Opt("Category", "Category"), Opt("Items", "Items"), Opt("RewardItems", "Reward items"),
                Opt("Courses", "Meal courses: A,B|C|D"), Num("Priority", "Priority (lower first)", "10"), Tick("Compounding", "Stacks on other promotions"), Tick("Member", "Members only"),
                Opt("Types", "For customer types (',', blank: all)"), Opt("Coupon", "Coupon code"), Today("From", "Starts"), new("To", "Ends", FieldKind.Date, Required: false), Opt("DailyFrom", "Happy hour from (HH:mm)"), Opt("DailyTo", "Happy hour to (HH:mm)")],
            Run = async (c, i) =>
            {
                static IReadOnlyList<string> Codes(string? text) => [.. (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                static TimeOnly? Clock(string? text) => text is null ? null : TimeOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                return Check(await c.Get<IPromotionService>().SaveAsync(new PromotionRequest
                {
                    CompanyId = c.CompanyId, Code = i.Text("Code"), Name = i.Text("Name"), Kind = Enum.Parse<PromotionKind>(i.Text("Kind")), DiscountAccountCode = i.Text("Account"),
                    Quantity = i.Whole("Quantity"), RewardQuantity = i.Whole("Reward"), Price = i.OptionalNumber("Price"), Percent = i.OptionalNumber("Percent"), Category = i.Optional("Category"),
                    Items = Codes(i.Optional("Items")), RewardItems = Codes(i.Optional("RewardItems")),
                    Courses = [.. (i.Optional("Courses") ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Codes)],
                    Priority = i.Whole("Priority"), Compounding = i.Flag("Compounding"), MemberOnly = i.Flag("Member"), CustomerTypes = Codes(i.Optional("Types")), CouponCode = i.Optional("Coupon"),
                    StartsOn = i.Date("From"),
                    EndsOn = i.OptionalDate("To"), DailyFrom = Clock(i.Optional("DailyFrom")), DailyTo = Clock(i.Optional("DailyTo")),
                }).ConfigureAwait(true), id => $"Promotion {i.Text("Code").ToUpperInvariant()} saved.");
            },
            List = async c => Table(await c.Db.Promotions.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.Priority).ThenBy(p => p.Code).ToListAsync().ConfigureAwait(true),
                ("Code", p => p.Code), ("Name", p => p.Name), ("Kind", p => p.Kind), ("Priority", p => p.Priority), ("Runs", p => $"{D(p.StartsOn)} – {(p.EndsOn is DateOnly e ? D(e) : "open")}"),
                ("Hours", p => p.DailyFrom is TimeOnly f ? $"{f:HH\\:mm}–{p.DailyTo:HH\\:mm}" : "all day"), ("Members", p => p.MemberOnly ? "Yes" : string.Empty), ("Coupon", p => p.CouponCode)),
        };
        yield return new Operation
        {
            Module = module, Title = "Loyalty: enrol, earn, redeem, statement", ListTitle = "Loyalty members",
            Description = "Pick an action. Earning books the points' value as a liability; a redemption's value is tendered at the till on the scheme's liability account.",
            Fields = [Pick("Action", "Action", "Statement", "Enrol", "Earn", "Redeem"), Req("Member", "Member number"), Opt("Name", "Name (enrol)"), OptNum("Amount", "Spend (earn) / points (redeem)"),
                Today(), Opt("Reference", "Sale reference")],
            Run = async (c, i) =>
            {
                ILoyaltyService loyalty = c.Get<ILoyaltyService>();
                switch (i.Text("Action"))
                {
                    case "Enrol":
                        return Check(await loyalty.EnrolAsync(c.CompanyId, i.Text("Member"), i.Text("Name"), null, i.Date("Date")).ConfigureAwait(true), id => $"Member {i.Text("Member").ToUpperInvariant()} enrolled.");
                    case "Earn":
                        LoyaltyEarned earned = Check(await loyalty.EarnAsync(c.CompanyId, i.Text("Member"), i.Number("Amount"), i.Date("Date"), i.Optional("Reference") ?? "MANUAL").ConfigureAwait(true));
                        return $"{Q(earned.Points)} points earned{(earned.Tier is null ? string.Empty : $" ({earned.Tier})")}; balance {Q(earned.Balance)}.";
                    case "Redeem":
                        return $"Redeemed: tender {M(Check(await loyalty.RedeemAsync(c.CompanyId, i.Text("Member"), i.Number("Amount"), i.Date("Date"), i.Optional("Reference") ?? "MANUAL").ConfigureAwait(true)))} on the loyalty liability account.";
                    default:
                        LoyaltyStatement s = Check(await loyalty.StatementAsync(c.CompanyId, i.Text("Member"), i.Date("Date")).ConfigureAwait(true));
                        return $"{s.MemberNumber} {s.Name} — {s.Tier ?? "no tier"}, {Q(s.Balance)} points worth {M(s.BalanceValue)}{Environment.NewLine}"
                            + string.Join(Environment.NewLine, s.Movements.Select(m => $"{D(m.Date)}  {m.Kind,-7} {Q(m.Points),10}  {m.Reference}"));
                }
            },
            List = async c => Table(await c.Db.LoyaltyMembers.AsNoTracking().Where(m => m.CompanyId == c.CompanyId).OrderBy(m => m.MemberNumber).ToListAsync().ConfigureAwait(true),
                ("Member", m => m.MemberNumber), ("Name", m => m.Name), ("Joined", m => D(m.JoinedOn))),
        };
        yield return new Operation
        {
            Module = module, Title = "Till pickup / paid-out / float top-up", ListTitle = "Open till sessions",
            Description = "Cash in or out of a drawer other than by sales; posted, and counted in the cash expected at cash-up. A paid-out needs a different supervisor.",
            Fields = [Num("Session", "Till session id (from the list)"), Pick("Kind", "Kind", [.. Enum.GetNames<TillCashKind>().Where(k => k != nameof(TillCashKind.Refund))]), Num("Amount", "Amount"), Req("Cash", "Till cash account", "1000"),
                Req("Other", "Safe / bank, expense or float source account"), Req("Reason", "Reason"), Opt("Supervisor", "Supervisor (user name)"), Today()],
            Run = async (c, i) => $"Posted: {Check(await c.Get<ITillCashService>().RecordAsync(new TillCashRequest((long)i.Number("Session"), Enum.Parse<TillCashKind>(i.Text("Kind")), i.Number("Amount"),
                i.Text("Cash"), i.Text("Other"), i.Text("Reason"), c.UserId, await OptionalUserAsync(c, i.Optional("Supervisor")).ConfigureAwait(true), i.Date("Date"))).ConfigureAwait(true))}.",
            List = async c => Table(await c.Db.Set<TillSession>().AsNoTracking().Where(s => s.CompanyId == c.CompanyId && s.Status == TillSessionStatus.Open).OrderBy(s => s.Id).ToListAsync().ConfigureAwait(true),
                ("Session", s => s.Id), ("Terminal", s => s.TerminalId), ("Opened", s => s.OpenedOnUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)), ("Float", s => M(s.OpeningFloat))),
        };
        yield return new Operation
        {
            Module = module, Title = "Till cash-up position", ListTitle = "Tills",
            Description = "A blind till's cashier declares the count (\"120; ZAR 500; EUR 20\") before the cash expected is shown; the position shows expected, counted and the "
                + "variance per currency (a blind till's only after its count is declared). Blind on / off is per till.",
            Fields = [Pick("Step", "Step", ["Position", "Blind on", "Blind off"]), OptNum("Session", "Till session id (position)"), Opt("Terminal", "Till code (blind on / off)")],
            Run = async (c, i) =>
            {
                if (i.Text("Step") == "Position")
                {
                    IReadOnlyList<CurrencyCashUp> position = Check(await c.Get<IPosService>().ExpectedCashAsync((long)i.Number("Session")).ConfigureAwait(true));
                    return string.Join(Environment.NewLine, position.Select(p => $"{p.Currency}  expected {M(p.Expected),14}  counted {M(p.Counted),14}  variance {M(p.Variance),12}  ({M(p.VarianceBase)} base)"));
                }

                string code = (i.Optional("Terminal") ?? throw new OperationException("Type the till code.")).ToUpperInvariant();
                PosTerminal terminal = await c.Db.Set<PosTerminal>().FirstOrDefaultAsync(t => t.CompanyId == c.CompanyId && t.Code.ToUpper() == code).ConfigureAwait(true)
                    ?? throw new OperationException($"Till {code} not found.");
                terminal.BlindCashUp = i.Text("Step") == "Blind on";
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"Till {terminal.Code} is {(terminal.BlindCashUp ? "now cashed up blind" : "no longer blind")}.";
            },
            List = async c => Table(await c.Db.Set<PosTerminal>().AsNoTracking().Where(t => t.CompanyId == c.CompanyId).OrderBy(t => t.Code).ToListAsync().ConfigureAwait(true),
                ("Till", t => t.Code), ("Name", t => t.Name), ("Active", t => t.IsActive), ("Blind cash-up", t => t.BlindCashUp)),
        };
        yield return new Operation
        {
            Module = module, Title = "Discard a parked sale", ListTitle = "Parked sales", Description = "Sales held at the tills under a name, table or customer tag; resumed at any till, or discarded here with a reason.",
            Fields = [Num("Parked", "Parked sale id (from the list)"), Req("Reason", "Reason")],
            Run = async (c, i) => Check(await c.Get<IParkedSaleService>().DiscardAsync(c.CompanyId, (long)i.Number("Parked"), c.UserId, i.Text("Reason")).ConfigureAwait(true), "Parked sale discarded."),
            List = async c => Table(await c.Get<IParkedSaleService>().ParkedAsync(c.CompanyId).ConfigureAwait(true),
                ("Parked", p => p.Id), ("Tag", p => p.Tag), ("Till", p => p.TerminalId), ("Lines", p => p.Lines), ("Total", p => M(p.Total)),
                ("Since", p => p.ParkedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture))),
        };
        yield return new Operation
        {
            Module = module, Title = "Refund without a receipt", Description = "Goods brought back with no receipt: a different supervisor authorises it, within the company's limit; optionally restocked.",
            Fields = [Num("Terminal", "Till id"), Req("Item", "Item code"), Num("Quantity", "Quantity", "1"), Num("Price", "Unit price refunded (excl. tax)"), Num("Tax", "Tax refunded", "0"),
                Opt("TaxAccount", "Output tax account"), Req("RefundAccount", "Paid from (cash) account", "1000"), Req("Reason", "Reason"), Req("Supervisor", "Supervisor (user name)"),
                Opt("Warehouse", "Restock into warehouse"), OptNum("Session", "Paid from till session (cash)"), Today()],
            Run = async (c, i) =>
            {
                PosRefundResult refund = Check(await c.Get<IPosEventService>().RefundWithoutReceiptAsync(new UnreceiptedRefundRequest
                {
                    CompanyId = c.CompanyId, TerminalId = (long)i.Number("Terminal"), CashierUserId = c.UserId, AuthorisedByUserId = await UserAsync(c, i.Text("Supervisor")).ConfigureAwait(true), Date = i.Date("Date"),
                    ItemId = await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), Quantity = i.Number("Quantity"), UnitPrice = i.Number("Price"), TaxAmount = i.Number("Tax"), TaxAccountCode = i.Optional("TaxAccount"),
                    RefundAccountCode = i.Text("RefundAccount"), Reason = i.Text("Reason"),
                    RestockWarehouseId = i.Optional("Warehouse") is string w ? await WarehouseAsync(c, w).ConfigureAwait(true) : null,
                    TillSessionId = i.OptionalNumber("Session") is decimal session ? (long)session : null,
                }).ConfigureAwait(true));
                return $"Refunded {M(refund.Gross)} ({refund.JournalNumber}).";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Expire loyalty points", Description = "Expires every member's points past their expiry (oldest first) and releases the liability.",
            Fields = [Today("AsAt", "As at"), Req("Document", "Document number")],
            Run = async (c, i) =>
            {
                (int members, decimal points, decimal value) = Check(await c.Get<ILoyaltyService>().ExpireAsync(c.CompanyId, i.Date("AsAt"), i.Text("Document")).ConfigureAwait(true));
                return $"{Q(points)} points of {members} member(s) expired, releasing {M(value)}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Price a basket with promotions", Description = "Shows what the running promotions take off a basket at a date and time.",
            Fields = [Req("Lines", "Lines: ITEM:quantity; … (at the default price list)"), Req("When", "When (yyyy-MM-dd HH:mm)", DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)),
                Tick("Member", "Loyalty member"), Opt("Type", "Customer type"), Opt("Coupons", "Coupon codes (',')"), Req("Prices", "Unit prices: ITEM:price; …")],
            Run = async (c, i) =>
            {
                Dictionary<string, decimal> prices = i.Pairs("Prices").ToDictionary(p => p.Code.ToUpperInvariant(), p => p.Quantity);
                List<BasketItem> lines = [];
                foreach ((string code, decimal quantity, DateOnly? _) in i.Pairs("Lines"))
                {
                    lines.Add(new BasketItem(await ItemAsync(c, code).ConfigureAwait(true), quantity, prices.TryGetValue(code.ToUpperInvariant(), out decimal p) ? p : throw new OperationException($"No price for {code}.")));
                }

                BasketPricing priced = await c.Get<IPromotionService>().PriceBasketAsync(new BasketRequest(c.CompanyId, null, DateTime.Parse(i.Text("When"), System.Globalization.CultureInfo.InvariantCulture),
                    i.Flag("Member"), [.. (i.Optional("Coupons") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)], lines, i.Optional("Type")))
                    .ConfigureAwait(true);
                return string.Join(Environment.NewLine, priced.Lines.Select(l => $"{Q(l.Quantity),6} × item {l.ItemId,-6} gross {M(l.Gross),10}  less {M(l.Discount),8}  net {M(l.Net),10}  {string.Join(",", l.PromotionCodes)}"))
                    + Environment.NewLine + $"Savings: {string.Join("; ", priced.Applied.Select(a => $"{a.Name} {M(a.Saving)}"))}";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Refund sale lines", ListTitle = "Recent till events",
            Description = "Refunds units of a completed sale's line (by item code): revenue and tax back, money out of the refund account, goods back on the shelf if ticked. Needs a supervisor other than you.",
            Fields = [Req("Sale", "Sale number"), Req("Item", "Item code"), Num("Quantity", "Quantity"), Req("Terminal", "Till code"), Today(), Req("RefundAccount", "Refund paid from (account)", "1000"),
                Opt("TaxAccount", "Output tax account", "2200"), Req("Reason", "Reason"), Req("AuthorisedBy", "Authorised by (user name)"), Tick("Restock", "Put the goods back in stock", true),
                OptNum("Session", "Paid from till session (cash out of the drawer)")],
            Run = async (c, i) =>
            {
                Sale sale = await c.Db.Sales.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.CompanyId == c.CompanyId && s.SaleNumber == i.Text("Sale")).ConfigureAwait(true)
                    ?? throw new OperationException($"No sale {i.Text("Sale")}.");
                long itemId = await ItemAsync(c, i.Text("Item")).ConfigureAwait(true);
                SaleLine line = sale.Lines.FirstOrDefault(l => l.ItemId == itemId) ?? throw new OperationException($"{i.Text("Item")} is not on sale {sale.SaleNumber}.");
                long terminal = await FindAsync(c.Db.Set<PosTerminal>(), t => t.CompanyId == c.CompanyId && t.Code.ToUpper() == i.Text("Terminal").ToUpper(), t => t.Id, "till", i.Text("Terminal")).ConfigureAwait(true);
                PosRefundResult refund = Check(await c.Get<IPosEventService>().RefundAsync(new PosRefundRequest
                {
                    CompanyId = c.CompanyId, SaleId = sale.Id, TerminalId = terminal, CashierUserId = c.UserId, AuthorisedByUserId = await UserAsync(c, i.Text("AuthorisedBy")).ConfigureAwait(true),
                    Date = i.Date("Date"), RefundAccountCode = i.Text("RefundAccount"), TaxAccountCode = i.Optional("TaxAccount"), Reason = i.Text("Reason"),
                    Lines = [new PosRefundLine(line.Id, i.Number("Quantity"))], Restock = i.Flag("Restock"),
                    TillSessionId = i.OptionalNumber("Session") is decimal session ? (long)session : null,
                }).ConfigureAwait(true));
                return $"Refunded {M(refund.Gross)} on {sale.SaleNumber} — journal {refund.JournalNumber}.";
            },
            List = RecentTillEventsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Exchange goods", ListTitle = "Recent till events",
            Description = "Takes back units of a sale's line and sells new goods in one go: the returned value pays towards the new goods and only the difference changes hands "
                + "(the customer pays it in cash, or is given the excess back). Returns are checked like a refund and need a supervisor other than you.",
            Fields = [Req("Sale", "Original sale number"), Req("Item", "Item coming back"), Num("Quantity", "Quantity coming back", "1"), Req("NewItem", "Item going out"),
                Num("NewQuantity", "Quantity going out", "1"), Num("NewPrice", "Unit price going out (excl. tax)"), Num("NewTax", "Tax on the new goods", "0"), Req("NewSale", "New sale number"),
                Req("Terminal", "Till code"), Num("Session", "Till session"), Req("Warehouse", "Warehouse"), Req("Cash", "Till cash account", "1000"), Opt("TaxAccount", "Output tax account", "2200"),
                Req("Reason", "Reason"), Req("AuthorisedBy", "Authorised by (user name)"), Today()],
            Run = async (c, i) =>
            {
                Sale sale = await c.Db.Sales.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.CompanyId == c.CompanyId && s.SaleNumber == i.Text("Sale")).ConfigureAwait(true)
                    ?? throw new OperationException($"No sale {i.Text("Sale")}.");
                long itemId = await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), newItem = await ItemAsync(c, i.Text("NewItem")).ConfigureAwait(true);
                SaleLine line = sale.Lines.FirstOrDefault(l => l.ItemId == itemId) ?? throw new OperationException($"{i.Text("Item")} is not on sale {sale.SaleNumber}.");
                Core.Inventory.Item goods = await c.Db.Items.AsNoTracking().SingleAsync(x => x.Id == newItem).ConfigureAwait(true);
                long terminal = await FindAsync(c.Db.Set<PosTerminal>(), t => t.CompanyId == c.CompanyId && t.Code.ToUpper() == i.Text("Terminal").ToUpper(), t => t.Id, "till", i.Text("Terminal")).ConfigureAwait(true);
                decimal net = decimal.Round(i.Number("NewQuantity") * i.Number("NewPrice"), 2, MidpointRounding.AwayFromZero), tax = i.Number("NewTax");
                PosExchangeResult done = Check(await c.Get<IPosEventService>().ExchangeAsync(new PosExchangeRequest
                {
                    CompanyId = c.CompanyId, OriginalSaleId = sale.Id, TerminalId = terminal, TillSessionId = (long)i.Number("Session"), CashierUserId = c.UserId,
                    AuthorisedByUserId = await UserAsync(c, i.Text("AuthorisedBy")).ConfigureAwait(true), Date = i.Date("Date"), Reason = i.Text("Reason"),
                    Returns = [new PosRefundLine(line.Id, i.Number("Quantity"))], CashAccountCode = i.Text("Cash"), TaxAccountCode = i.Optional("TaxAccount"), NewSaleNumber = i.Text("NewSale"),
                    WarehouseId = await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true),
                    NewLines = [new PosSaleLineRequest(newItem, goods.Description, i.Number("NewQuantity"), i.Number("NewPrice"), net, tax, goods.RevenueAccountCode ?? "4000")],
                }).ConfigureAwait(true));
                return done.Difference >= 0m
                    ? $"Exchanged: {M(done.Returned)} returned towards {M(done.NewSaleGross)}; customer paid {M(done.Difference)}."
                    : $"Exchanged: {M(done.Returned)} returned towards {M(done.NewSaleGross)}; {M(-done.Difference)} given back.";
            },
            List = RecentTillEventsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Record a till event", ListTitle = "Recent till events",
            Description = "Logs a void, discount, price override, no-sale, parked / resumed sale, reprint, fiscal submission or loyalty points. Overrides and no-sales need a supervisor; voids, discounts and overrides need a reason.",
            Fields = [Pick("Kind", "Event", kinds), Req("Terminal", "Till code"), Today(), Req("Description", "Item / description"), Num("Amount", "Amount (value, keyed price, points…)"),
                OptNum("Original", "List amount (discounts / overrides)"), Opt("Sale", "Sale number"), Opt("Reason", "Reason"), Opt("AuthorisedBy", "Authorised by (user name)"),
                Opt("Customer", "Loyalty card / customer"), Opt("Reference", "Reference (fiscal receipt no.)"), Opt("Outcome", "Outcome (fiscal)")],
            Run = async (c, i) =>
            {
                long terminal = await FindAsync(c.Db.Set<PosTerminal>(), t => t.CompanyId == c.CompanyId && t.Code.ToUpper() == i.Text("Terminal").ToUpper(), t => t.Id, "till", i.Text("Terminal")).ConfigureAwait(true);
                long id = Check(await c.Get<IPosEventService>().RecordAsync(new PosEventRequest
                {
                    CompanyId = c.CompanyId, TerminalId = terminal, CashierUserId = c.UserId, Kind = Enum.Parse<PosEventKind>(i.Text("Kind")), Date = i.Date("Date"), Description = i.Text("Description"),
                    Amount = i.Number("Amount"), OriginalAmount = i.OptionalNumber("Original"), SaleNumber = i.Optional("Sale"), Reason = i.Optional("Reason"),
                    AuthorisedByUserId = await OptionalUserAsync(c, i.Optional("AuthorisedBy")).ConfigureAwait(true), CustomerReference = i.Optional("Customer"), Reference = i.Optional("Reference"), Outcome = i.Optional("Outcome"),
                }).ConfigureAwait(true));
                return $"{i.Text("Kind")} recorded (event {id}).";
            },
            List = RecentTillEventsAsync,
        };
    }

    private static async Task<System.Data.DataTable> RecentTillEventsAsync(OperationContext c)
        => Table(await c.Db.PosEvents.AsNoTracking().Where(e => e.CompanyId == c.CompanyId).OrderByDescending(e => e.Id).Take(50).ToListAsync().ConfigureAwait(true),
            ("When", e => e.OccurredOnUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)), ("Event", e => e.Kind), ("Sale", e => e.SaleNumber),
            ("Description", e => e.Description), ("Amount", e => M(e.Amount)), ("Reason", e => e.Reason), ("Reference", e => e.Reference));

    private static IEnumerable<Operation> Stock()
    {
        const string module = "Stock";
        yield return new Operation
        {
            Module = module, Title = "Item picture, description and dimensions", ListTitle = "Items",
            Description = "An item's long description, its length, width and height in centimetres (the volume follows) and weight, and its picture (a JPEG, PNG, GIF or WebP "
                + "file of at most 1 MB; Remove picture drops it).",
            Fields = [Pick("Step", "Step", ["Details", "Picture", "Remove picture"]), Req("Item", "Item code"), Opt("LongDescription", "Long description"), OptNum("Length", "Length (cm)"),
                OptNum("Width", "Width (cm)"), OptNum("Height", "Height (cm)"), OptNum("Weight", "Weight (kg)"), Opt("File", "Picture file")],
            Run = async (c, i) =>
            {
                IItemDetailsService details = c.Get<IItemDetailsService>();
                switch (i.Text("Step"))
                {
                    case "Picture":
                        string file = i.Optional("File") ?? throw new OperationException("Choose the picture file.");
                        string type = System.IO.Path.GetExtension(file).ToLowerInvariant() switch { ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp", _ => "image/jpeg" };
                        return Check(await details.SetImageAsync(c.CompanyId, i.Text("Item"), await System.IO.File.ReadAllBytesAsync(file).ConfigureAwait(true), type).ConfigureAwait(true), "Picture saved.");
                    case "Remove picture":
                        return Check(await details.SetImageAsync(c.CompanyId, i.Text("Item"), null, null).ConfigureAwait(true), "Picture removed.");
                    default:
                        return Check(await details.SetDetailsAsync(c.CompanyId, i.Text("Item"), new ItemDetails
                        {
                            LongDescription = i.Optional("LongDescription"), LengthCm = i.OptionalNumber("Length"), WidthCm = i.OptionalNumber("Width"), HeightCm = i.OptionalNumber("Height"),
                            WeightKg = i.OptionalNumber("Weight"),
                        }).ConfigureAwait(true), "Item details saved.");
                }
            },
            List = async c => Table(await c.Db.Items.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).Take(500).ToListAsync().ConfigureAwait(true),
                ("Item", x => x.Code), ("Description", x => x.Description), ("L × W × H (cm)", x => x.LengthCm is null ? string.Empty : $"{x.LengthCm:0.#} × {x.WidthCm:0.#} × {x.HeightCm:0.#}"),
                ("Weight (kg)", x => x.UnitWeightKg?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Supplier- and customer-owned stock", ListTitle = "Goods held for others",
            Description = "Goods we hold that are not ours: a supplier's consignment stock (with the agreed price) or a customer's goods in our keeping. They stay out of our stock "
                + "value. Return hands them back; Use (supplier-owned only) takes them into our stock at the agreed price and raises the supplier's invoice through the clearing account.",
            Fields = [Pick("Step", "Step", ["Take in", "Return", "Use"]), Pick("Owner", "Owner is a", Enum.GetNames<HeldStockOwner>()), Opt("OwnerCode", "Supplier or customer code"),
                Opt("Item", "Item code"), Opt("Warehouse", "Location code"), Num("Quantity", "Quantity", "1"), OptNum("Price", "Agreed price (supplier-owned)"), OptNum("Held", "Holding id"),
                Opt("Reference", "Reference"), Opt("Clearing", "Consignment clearing account (use)"), Today()],
            Run = async (c, i) =>
            {
                IHeldStockService held = c.Get<IHeldStockService>();
                long holding = (long)(i.OptionalNumber("Held") ?? 0m);
                switch (i.Text("Step"))
                {
                    case "Return":
                        return Check(await held.ReturnAsync(c.CompanyId, holding, i.Number("Quantity"), i.Date("Date"), i.Optional("Reference")).ConfigureAwait(true), "Handed back.");
                    case "Use":
                        string journal = Check(await held.UseAsync(c.CompanyId, holding, i.Number("Quantity"), i.Date("Date"), i.Optional("Reference") ?? string.Empty, i.Optional("Clearing") ?? string.Empty)
                            .ConfigureAwait(true));
                        return $"Taken into stock and invoiced by the supplier ({journal}).";
                    default:
                        long id = Check(await held.TakeInAsync(c.CompanyId, new HeldStockIn(Enum.Parse<HeldStockOwner>(i.Text("Owner")), i.Optional("OwnerCode") ?? string.Empty, i.Optional("Item") ?? string.Empty,
                            i.Optional("Warehouse") ?? string.Empty, i.Number("Quantity"), i.Date("Date"), i.Optional("Reference")) { UnitPrice = i.OptionalNumber("Price") }).ConfigureAwait(true));
                        return $"Taken in (holding {id}).";
                }
            },
            List = async c => Table(await c.Get<IHeldStockService>().HoldingsAsync(c.CompanyId, null).ConfigureAwait(true),
                ("Holding", r => r.Id), ("Owner", r => r.Owner), ("Code", r => r.OwnerCode), ("Name", r => r.OwnerName), ("Item", r => r.ItemCode), ("Location", r => r.WarehouseCode),
                ("Quantity", r => r.Quantity), ("Agreed price", r => r.Owner == HeldStockOwner.Supplier ? r.UnitPrice.ToString("0.00##", System.Globalization.CultureInfo.InvariantCulture) : string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Price a list from cost", ListTitle = "Price lists",
            Description = "Items with no price on the list are priced from cost (average, else standard): a markup on cost or a margin on price, rounded to a price point. Basis None turns it off.",
            Fields = [Req("List", "Price list code"), Pick("Basis", "Basis", Enum.GetNames<CostPlusBasis>()), Num("Percent", "Markup or margin %", "0"), Num("RoundTo", "Price point", "0.05"),
                Pick("Rounding", "Rounding", Enum.GetNames<PriceRounding>())],
            Run = async (c, i) =>
            {
                long list = await FindAsync(c.Db.PriceLists, p => p.CompanyId == c.CompanyId && p.Code.ToUpper() == i.Text("List").ToUpper(), p => p.Id, "price list", i.Text("List")).ConfigureAwait(true);
                return Check(await c.Get<IPricingService>().SetCostPlusAsync(c.CompanyId, list, Enum.Parse<CostPlusBasis>(i.Text("Basis")), i.Number("Percent"), i.Number("RoundTo"),
                    Enum.Parse<PriceRounding>(i.Text("Rounding"))).ConfigureAwait(true), "Cost-plus pricing updated.");
            },
            List = async c => Table(await c.Db.PriceLists.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.Code).ToListAsync().ConfigureAwait(true),
                ("List", p => p.Code), ("Name", p => p.Name), ("Currency", p => p.Currency.Value), ("Default", p => p.IsDefault), ("From cost", p => p.CostPlusBasis), ("Percent", p => Q(p.CostPlusPercent)),
                ("Price point", p => Q(p.CostPlusRoundTo))),
        };
        yield return new Operation
        {
            Module = module, Title = "Future prices", ListTitle = "Price lists",
            Description = "Schedules a price from a date, or a percentage change of the whole list from a date (e.g. 1 July); orders and sales dated before it keep the earlier price. "
                + "History lists an item's standing and dated prices.",
            Fields = [Pick("Step", "Step", ["Schedule price", "Schedule list change", "History"]), Req("List", "Price list code"), Opt("Item", "Item code"), Num("MinQty", "From quantity", "1"),
                OptNum("Price", "Unit price"), OptNum("Percent", "Change %"), Num("RoundTo", "Price point", "0.05"), Pick("Rounding", "Rounding", Enum.GetNames<PriceRounding>()), Today("From", "Effective from")],
            Run = async (c, i) =>
            {
                IPricingService pricing = c.Get<IPricingService>();
                long list = await FindAsync(c.Db.PriceLists, p => p.CompanyId == c.CompanyId && p.Code.ToUpper() == i.Text("List").ToUpper(), p => p.Id, "price list", i.Text("List")).ConfigureAwait(true);
                DateOnly today = DateOnly.FromDateTime(DateTime.Today);
                switch (i.Text("Step"))
                {
                    case "Schedule price":
                        return Check(await pricing.SchedulePriceAsync(c.CompanyId, list, await ItemAsync(c, i.Optional("Item") ?? string.Empty).ConfigureAwait(true), i.Number("MinQty"), i.Number("Price"),
                            i.Date("From"), today).ConfigureAwait(true), $"Price scheduled from {D(i.Date("From"))}.");
                    case "Schedule list change":
                        int count = Check(await pricing.ScheduleAdjustmentAsync(c.CompanyId, list, i.Number("Percent"), i.Number("RoundTo"), Enum.Parse<PriceRounding>(i.Text("Rounding")), i.Date("From"), today)
                            .ConfigureAwait(true));
                        return $"{count} price(s) scheduled from {D(i.Date("From"))}.";
                    default:
                        var history = await pricing.PriceHistoryAsync(c.CompanyId, list, await ItemAsync(c, i.Optional("Item") ?? string.Empty).ConfigureAwait(true)).ConfigureAwait(true);
                        return history.Count == 0 ? "No prices." : string.Join(Environment.NewLine, history.Select(h => $"from qty {Q(h.MinimumQuantity),8}  {M(h.UnitPrice),12}  {(h.EffectiveFrom is DateOnly d ? "from " + D(d) : "standing")}"));
                }
            },
            List = async c => Table(await c.Db.PriceLists.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.Code).ToListAsync().ConfigureAwait(true),
                ("List", p => p.Code), ("Name", p => p.Name), ("Currency", p => p.Currency.Value), ("Default", p => p.IsDefault)),
        };
        yield return new Operation
        {
            Module = module, Title = "Set item sale limits", ListTitle = "Items with sale limits",
            Description = "Age-restricted items need the till's age check, a per-sale limit caps the quantity in one sale, and prescription-only items need a prescription reference.",
            Fields = [Req("Item", "Item code"), OptNum("Age", "Minimum age"), OptNum("Max", "Most per sale"), Tick("Rx", "Prescription only")],
            Run = async (c, i) => Check(await c.Get<IItemCatalogueService>().SetSaleLimitsAsync(c.CompanyId, await ItemAsync(c, i.Text("Item")).ConfigureAwait(true),
                i.OptionalNumber("Age") is decimal age ? (int)age : null, i.OptionalNumber("Max"), i.Flag("Rx")).ConfigureAwait(true), "Sale limits updated."),
            List = async c => Table(await c.Db.Items.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && (x.MinimumAge != null || x.MaxQuantityPerSale != null || x.RequiresPrescription))
                .OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Item", x => x.Code), ("Description", x => x.Description), ("Minimum age", x => x.MinimumAge), ("Most per sale", x => x.MaxQuantityPerSale is decimal m ? Q(m) : null),
                ("Prescription only", x => x.RequiresPrescription)),
        };
        yield return new Operation
        {
            Module = module, Title = "Find an item", Description = "Searches codes, PLUs, barcodes (item, pack, alias), synonyms, brands and descriptions.",
            Fields = [Req("Term", "Search for")],
            Run = async (c, i) =>
            {
                IReadOnlyList<ItemSearchHit> hits = await c.Get<IItemCatalogueService>().SearchAsync(c.CompanyId, i.Text("Term"), 30).ConfigureAwait(true);
                return hits.Count == 0 ? "Nothing found." : string.Join(Environment.NewLine, hits.Select(h => $"{h.Code,-16} {h.Description,-40} {h.Brand,-14} ({h.MatchedOn})"));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Add a barcode, synonym, pack or supplier to an item",
            Description = "Barcode / Synonym: the value. Pack: unit code, factor and its barcode. Supplier: supplier code, their item code, lead days, minimum order, last cost, preferred.",
            Fields = [Req("Item", "Item code"), Pick("What", "Add", "Barcode", "Synonym", "Pack", "Supplier"), Opt("Value", "Barcode / synonym / pack unit / supplier code"), OptNum("Factor", "Pack factor"),
                Opt("Code", "Pack barcode / supplier's item code"), OptNum("Lead", "Lead time (days)"), OptNum("Minimum", "Minimum order"), OptNum("Cost", "Last cost"), Tick("Preferred", "Preferred supplier")],
            Run = async (c, i) =>
            {
                IItemCatalogueService catalogue = c.Get<IItemCatalogueService>();
                long item = await ItemAsync(c, i.Text("Item")).ConfigureAwait(true);
                return i.Text("What") switch
                {
                    "Barcode" => Check(await catalogue.AddAliasAsync(c.CompanyId, item, ItemAliasKind.Barcode, i.Text("Value")).ConfigureAwait(true), "Barcode added."),
                    "Synonym" => Check(await catalogue.AddAliasAsync(c.CompanyId, item, ItemAliasKind.Synonym, i.Text("Value")).ConfigureAwait(true), "Synonym added."),
                    "Pack" => Check(await catalogue.AddUnitAsync(c.CompanyId, item, i.Text("Value"), i.Number("Factor"), i.Optional("Code")).ConfigureAwait(true), "Pack unit added."),
                    _ => Check(await catalogue.LinkSupplierAsync(new ItemSupplierRequest(c.CompanyId, item, await SupplierAsync(c, i.Text("Value")).ConfigureAwait(true), i.Optional("Code"),
                        (int)(i.OptionalNumber("Lead") ?? 0m), i.OptionalNumber("Minimum") ?? 0m, i.OptionalNumber("Cost"), i.Flag("Preferred"))).ConfigureAwait(true), "Supplier linked."),
                };
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Bins: add, put away, move, take, pick walk", ListTitle = "Bin contents",
            Description = "Add a bin (sequence, capacity), put stock away (never more than is unbinned), move between bins, take picked stock, or plan a pick walk (Lines: ITEM:qty; …).",
            Fields = [Req("Warehouse", "Warehouse code"), Pick("Action", "Action", "Pick walk", "Add bin", "Put away", "Move", "Take"), Opt("Bin", "Bin (from)"), Opt("ToBin", "To bin"),
                Opt("Item", "Item code"), OptNum("Quantity", "Quantity"), OptNum("Sequence", "Pick sequence (add)"), OptNum("Capacity", "Capacity (add)"), Opt("Lines", "Pick lines: ITEM:qty; …")],
            Run = async (c, i) =>
            {
                IBinService bins = c.Get<IBinService>();
                long warehouse = await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true);
                switch (i.Text("Action"))
                {
                    case "Add bin":
                        return Check(await bins.AddBinAsync(c.CompanyId, warehouse, i.Text("Bin"), (int)(i.OptionalNumber("Sequence") ?? 0m), i.OptionalNumber("Capacity")).ConfigureAwait(true), id => "Bin added.");
                    case "Put away":
                        return Check(await bins.PutAwayAsync(c.CompanyId, warehouse, i.Text("Bin"), await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), i.Number("Quantity")).ConfigureAwait(true), "Put away.");
                    case "Move":
                        return Check(await bins.MoveAsync(c.CompanyId, warehouse, i.Text("Bin"), i.Text("ToBin"), await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), i.Number("Quantity")).ConfigureAwait(true), "Moved.");
                    case "Take":
                        return Check(await bins.TakeAsync(c.CompanyId, warehouse, i.Text("Bin"), await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), i.Number("Quantity")).ConfigureAwait(true), "Taken.");
                    default:
                        List<(long, decimal)> wanted = [];
                        foreach ((string code, decimal quantity, DateOnly? _) in i.Pairs("Lines"))
                        {
                            wanted.Add((await ItemAsync(c, code).ConfigureAwait(true), quantity));
                        }

                        return string.Join(Environment.NewLine, (await bins.PickRouteAsync(c.CompanyId, warehouse, wanted).ConfigureAwait(true)).Select(s => $"{s.BinCode,-10} item {s.ItemId,-6} {Q(s.Quantity),10}"));
                }
            },
            List = async c =>
            {
                long? warehouse = await c.Db.Warehouses.Where(w => w.CompanyId == c.CompanyId && c.Db.WarehouseBins.Any(b => b.WarehouseId == w.Id)).Select(w => (long?)w.Id).FirstOrDefaultAsync().ConfigureAwait(true);
                return Table(warehouse is long id ? await c.Get<IBinService>().ContentsAsync(c.CompanyId, id).ConfigureAwait(true) : [],
                    ("Bin", b => b.BinCode), ("Sequence", b => b.PickSequence), ("Item", b => b.ItemCode), ("Quantity", b => Q(b.Quantity)));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Set a warehouse reorder level", Description = "An item's reorder level and quantity at one warehouse; replenishment for that warehouse uses them.",
            Fields = [Req("Item", "Item code"), Req("Warehouse", "Warehouse code"), Num("Level", "Reorder level"), Num("Quantity", "Reorder quantity", "0")],
            Run = async (c, i) => Check(await c.Get<IBinService>().SetWarehouseLevelAsync(c.CompanyId, await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true),
                i.Number("Level"), i.Number("Quantity")).ConfigureAwait(true), "Reorder level saved."),
        };
        yield return new Operation
        {
            Module = module, Title = "Set an item as catch-weight",
            Description = "Counted in pieces and weighed: receipts need their weight (and may be costed per weight unit), the average weight per piece must be within the tolerance "
                + "of the nominal weight, and issues are costed by the weight that leaves. Leave the unit blank to make the item ordinary again (only with no stock on hand).",
            Fields = [Req("Item", "Item code"), Opt("Unit", "Weight unit (kg, t…)", "kg"), OptNum("Nominal", "Nominal weight per piece"), OptNum("Tolerance", "Tolerance %")],
            Run = async (c, i) => Check(await c.Get<IInventoryService>().SetCatchWeightAsync(c.CompanyId, await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), i.Optional("Unit"),
                i.OptionalNumber("Nominal"), i.OptionalNumber("Tolerance")).ConfigureAwait(true), "Catch weight saved."),
        };
        yield return new Operation
        {
            Module = module, Title = "Set an item's replenishment policy", ListTitle = "Replenishment policies",
            Description = "Min/max and forecast: at or below the reorder level (on hand plus on order) a reorder is suggested, up to the maximum if set, else the reorder quantity. "
                + "With a forecast window, the reorder level rises to the average daily issues over that window times the lead time — scaled, with seasonal factors, by the "
                + "factor for the month the lead time ends in (twelve numbers, January first).",
            Fields = [Req("Item", "Item code"), Num("Level", "Reorder level (minimum)", "0"), Num("Quantity", "Reorder quantity", "0"), Num("Maximum", "Maximum stock", "0"),
                Num("LeadTime", "Lead time (days)", "0"), Num("Window", "Forecast window (days, 0 = none)", "0"), Opt("Seasonal", "Seasonal factors Jan…Dec (e.g. 0.8,0.8,1,…)")],
            Run = async (c, i) => Check(await c.Get<IReplenishmentService>().SetPolicyAsync(c.CompanyId, await ItemAsync(c, i.Text("Item")).ConfigureAwait(true),
                new ReplenishmentPolicy(i.Number("Level"), i.Number("Quantity"), i.Number("Maximum"), (int)i.Number("LeadTime"), (int)i.Number("Window"))
                {
                    SeasonalFactors = i.Optional("Seasonal"),
                }).ConfigureAwait(true), "Replenishment policy saved."),
            List = async c => Table(await c.Db.Items.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && (x.ReorderLevel > 0m || x.MaximumStock > 0m || x.ForecastWindowDays > 0))
                    .OrderBy(x => x.Code).Take(200).ToListAsync().ConfigureAwait(true),
                ("Item", x => x.Code), ("Minimum", x => Q(x.ReorderLevel)), ("Reorder qty", x => Q(x.ReorderQuantity)), ("Maximum", x => Q(x.MaximumStock)),
                ("Lead time", x => x.LeadTimeDays), ("Window", x => x.ForecastWindowDays), ("Seasonal factors", x => x.SeasonalFactors ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Classify items A / B / C", Description = "A: the items making the first 80% of the value issued in the period, B the next 15%, C the rest and unmoved stock.",
            Fields = [Today("From", "From"), Today("To", "To")],
            Run = async (c, i) =>
            {
                (int a, int b, int cc) = await c.Get<IItemCatalogueService>().ClassifyAbcAsync(c.CompanyId, i.Date("From"), i.Date("To")).ConfigureAwait(true);
                return $"A: {a} item(s), B: {b}, C: {cc}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Record batches / serials on a movement", ListTitle = "Recent movements of batch / serial items (without lots)",
            Description = "Records which batches or serial numbers a receipt, issue or transfer moved. Lots: BATCH:qty[:expiry yyyy-MM-dd]; separated by semicolons (serials are one each).",
            Fields = [Num("Movement", "Movement id (from the list)"), Req("Lots", "Lots", "B1:10")],
            Run = async (c, i) =>
            {
                List<LotQuantity> lots = [.. i.Pairs("Lots").Select(p => new LotQuantity(p.Code, p.Quantity, p.Date))];
                return Check(await c.Get<ILotTrackingService>().RecordAsync(c.CompanyId, (long)i.Number("Movement"), lots).ConfigureAwait(true), $"{lots.Count} lot(s) recorded on movement {i.Number("Movement")}.");
            },
            List = async c =>
            {
                List<long> tracked = await c.Db.Items.Where(x => x.CompanyId == c.CompanyId && x.Tracking != ItemTracking.None).Select(x => x.Id).ToListAsync().ConfigureAwait(true);
                List<long> done = await c.Db.StockLotMovements.Where(l => l.CompanyId == c.CompanyId).Select(l => l.StockMovementId).Distinct().ToListAsync().ConfigureAwait(true);
                Dictionary<long, string> items = await c.Db.Items.Where(x => tracked.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code).ConfigureAwait(true);
                return Table(await c.Db.StockMovements.AsNoTracking().Where(m => m.CompanyId == c.CompanyId && tracked.Contains(m.ItemId) && !done.Contains(m.Id) && m.Quantity != 0m)
                        .OrderByDescending(m => m.Id).Take(50).ToListAsync().ConfigureAwait(true),
                    ("Movement", m => m.Id), ("Date", m => D(m.Date)), ("Type", m => m.Type), ("Item", m => items.GetValueOrDefault(m.ItemId)), ("Quantity", m => Q(m.Quantity)), ("Reference", m => m.Reference));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Suggest picks (first expiry, first out)", Description = "Which batches to pick for a quantity, earliest expiry first, never expired stock.",
            Fields = [Req("Item", "Item code"), Req("Warehouse", "Warehouse code"), Num("Quantity", "Quantity"), Today()],
            Run = async (c, i) =>
            {
                var (picks, shortfall) = await c.Get<ILotTrackingService>().SuggestPicksAsync(c.CompanyId, await ItemAsync(c, i.Text("Item")).ConfigureAwait(true),
                    await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), i.Number("Quantity"), i.Date("Date")).ConfigureAwait(true);
                string list = picks.Count == 0 ? "nothing available" : string.Join(", ", picks.Select(p => $"{p.LotNumber} × {Q(p.Quantity)}{(p.ExpiryDate is DateOnly e ? $" (exp {D(e)})" : string.Empty)}"));
                return $"Pick: {list}." + (shortfall > 0m ? $" Short by {Q(shortfall)}." : string.Empty);
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Adjust stock with a reason", Description = "Sets the counted quantity on hand; the difference posts to the adjustment account under the reason code (DAMAGED, EXPIRED, THEFT…).",
            Fields = [Req("Item", "Item code"), Req("Warehouse", "Warehouse code"), Num("Counted", "Counted quantity"), Today(), Req("Account", "Adjustment account", "5000"), Opt("Reason", "Reason code"), Opt("Reference", "Reference")],
            Run = async (c, i) =>
            {
                decimal value = Check(await c.Get<IInventoryService>().AdjustAsync(c.CompanyId, await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true),
                    i.Date("Date"), i.Number("Counted"), i.Text("Account"), i.Optional("Reference"), i.Optional("Reason")).ConfigureAwait(true));
                return value == 0m ? "No difference to adjust." : $"Adjusted: {(value > 0m ? "surplus" : "shortage")} of {M(Math.Abs(value))}.";
            },
            List = async c => Table(await c.Db.StockReasonCodes.AsNoTracking().Where(r => r.CompanyId == c.CompanyId && r.IsActive).OrderBy(r => r.Code).ToListAsync().ConfigureAwait(true),
                ("Reason code", r => r.Code), ("Meaning", r => r.Name)),
            ListTitle = "Reason codes",
        };
        yield return new Operation
        {
            Module = module, Title = "Dispatch a transfer", ListTitle = "Transfers in transit",
            Description = "Sends stock from one warehouse towards another through a transit location; receive it at the other end when it arrives.",
            Fields = [Req("Item", "Item code"), Req("From", "From warehouse"), Req("To", "To warehouse"), Req("Transit", "Transit location"), Num("Quantity", "Quantity"), Today()],
            Run = async (c, i) =>
            {
                DispatchTransferResult sent = Check(await c.Get<IStockTransferService>().DispatchAsync(new DispatchTransferRequest
                {
                    CompanyId = c.CompanyId, ItemId = await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), FromWarehouseId = await WarehouseAsync(c, i.Text("From")).ConfigureAwait(true),
                    ToWarehouseId = await WarehouseAsync(c, i.Text("To")).ConfigureAwait(true), TransitWarehouseId = await WarehouseAsync(c, i.Text("Transit")).ConfigureAwait(true), Date = i.Date("Date"), Quantity = i.Number("Quantity"),
                }).ConfigureAwait(true));
                return $"Transfer {sent.TransferNumber} dispatched ({M(sent.Value)} in transit).";
            },
            List = InTransitAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Receive a transfer", ListTitle = "Transfers in transit",
            Description = "Books what arrived into the destination; any shortfall is written off the transit location to the loss account.",
            Fields = [Req("Transfer", "Transfer number"), Num("Received", "Quantity received"), Today(), Opt("LossAccount", "Loss account (if short)", "5000")],
            Run = async (c, i) =>
            {
                long id = await FindAsync(c.Db.StockTransfers, t => t.CompanyId == c.CompanyId && t.TransferNumber == i.Text("Transfer"), t => t.Id, "transfer", i.Text("Transfer")).ConfigureAwait(true);
                decimal lost = Check(await c.Get<IStockTransferService>().ReceiveAsync(c.CompanyId, id, i.Date("Date"), i.Number("Received"), i.Optional("LossAccount")).ConfigureAwait(true));
                return lost == 0m ? $"Transfer {i.Text("Transfer")} received in full." : $"Transfer {i.Text("Transfer")} received short; {M(lost)} written off.";
            },
            List = InTransitAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Run an assembly / production batch", ListTitle = "Bills of materials",
            Description = "Consumes the BOM's components (or the usage keyed, ITEM:qty;…) and books the output into stock at their cost.",
            Fields = [Req("Bom", "BOM code"), Req("Warehouse", "Warehouse code"), Num("Planned", "Planned output"), OptNum("Actual", "Actual output (if different)"), Today(),
                Req("Clearing", "Assembly clearing account"), Opt("Usage", "Actual usage ITEM:qty;… (optional)")],
            Run = async (c, i) =>
            {
                long bom = await FindAsync(c.Db.BillsOfMaterials, b => b.CompanyId == c.CompanyId && b.Code.ToUpper() == i.Text("Bom").ToUpper(), b => b.Id, "BOM", i.Text("Bom")).ConfigureAwait(true);
                List<ComponentUsage> usage = [];
                foreach (var (code, quantity, _) in i.Pairs("Usage"))
                {
                    usage.Add(new ComponentUsage(await ItemAsync(c, code).ConfigureAwait(true), quantity));
                }

                AssemblyResult run = Check(await c.Get<IAssemblyService>().AssembleAsync(new AssemblyRequest
                {
                    CompanyId = c.CompanyId, BillOfMaterialsId = bom, WarehouseId = await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), Date = i.Date("Date"),
                    PlannedOutput = i.Number("Planned"), ActualOutput = i.OptionalNumber("Actual"), Usage = usage, ClearingAccountCode = i.Text("Clearing"),
                }).ConfigureAwait(true));
                return $"Run {run.RunNumber}: cost {M(run.TotalCost)}, {M(run.UnitCost)} a unit.";
            },
            List = async c =>
            {
                Dictionary<long, string> items = await c.Db.Items.Where(x => x.CompanyId == c.CompanyId).ToDictionaryAsync(x => x.Id, x => x.Code).ConfigureAwait(true);
                return Table(await c.Db.BillsOfMaterials.AsNoTracking().Include(b => b.Lines).Where(b => b.CompanyId == c.CompanyId && b.IsActive).OrderBy(b => b.Code).ToListAsync().ConfigureAwait(true),
                    ("BOM", b => b.Code), ("Makes", b => items.GetValueOrDefault(b.ParentItemId)), ("Per batch", b => Q(b.OutputQuantity)),
                    ("Components", b => string.Join(", ", b.Lines.Select(l => $"{items.GetValueOrDefault(l.ComponentItemId)} × {Q(l.Quantity)}"))));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Disassemble finished goods", ListTitle = "Bills of materials",
            Description = "Takes finished units apart into their components; their cost is split over the components recovered at each one's average cost, or by their "
                + "relative sales value on the default price list (joint products: a carcass into cuts).",
            Fields = [Req("Bom", "BOM code"), Req("Warehouse", "Warehouse code"), Num("Quantity", "Units taken apart"), Today(), Req("Clearing", "Assembly clearing account"),
                Opt("Recovered", "Recovered ITEM:qty;… (if not the BOM quantities)"), Tick("BySales", "Split by sales value")],
            Run = async (c, i) =>
            {
                long bom = await FindAsync(c.Db.BillsOfMaterials, b => b.CompanyId == c.CompanyId && b.Code.ToUpper() == i.Text("Bom").ToUpper(), b => b.Id, "BOM", i.Text("Bom")).ConfigureAwait(true);
                List<ComponentUsage> recovered = [];
                foreach (var (code, quantity, _) in i.Pairs("Recovered"))
                {
                    recovered.Add(new ComponentUsage(await ItemAsync(c, code).ConfigureAwait(true), quantity));
                }

                AssemblyResult run = Check(await c.Get<IAssemblyService>().DisassembleAsync(new DisassemblyRequest
                {
                    CompanyId = c.CompanyId, BillOfMaterialsId = bom, WarehouseId = await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), Date = i.Date("Date"),
                    Quantity = i.Number("Quantity"), Recovered = recovered, ClearingAccountCode = i.Text("Clearing"), BySalesValue = i.Flag("BySales"),
                }).ConfigureAwait(true));
                return $"Run {run.RunNumber}: {M(run.TotalCost)} moved into components.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Start a stocktake", ListTitle = "Blind count sheets (draft stocktakes)",
            Description = "Freezes the warehouse's quantities. Counters see only the items, never the system quantity. Counts outside the tolerance must be recounted by someone else.",
            Fields = [Req("Warehouse", "Warehouse code"), Today(), Req("Reference", "Stocktake reference"), OptNum("Tolerance", "Recount tolerance % (blank: none)")],
            Run = async (c, i) => Check(await c.Get<IStocktakeService>().StartAsync(c.CompanyId, await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), i.Date("Date"), i.Text("Reference"),
                recountTolerancePercent: i.OptionalNumber("Tolerance")).ConfigureAwait(true), id => $"Stocktake {id} started."),
            List = CountSheetsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Record a count", ListTitle = "Blind count sheets (draft stocktakes)", Description = "The first count of an item, by the signed-in user.",
            Fields = [Num("Stocktake", "Stocktake id"), Req("Item", "Item code"), Num("Counted", "Counted quantity")],
            Run = async (c, i) => Check(await c.Get<IStocktakeService>().RecordCountAsync(c.CompanyId, (long)i.Number("Stocktake"), await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), i.Number("Counted"),
                countedByUserId: c.UserId).ConfigureAwait(true), "Count recorded."),
            List = CountSheetsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Record a recount", ListTitle = "Blind count sheets (draft stocktakes)", Description = "A recount of an item flagged as outside the tolerance, by someone other than the first counter.",
            Fields = [Num("Stocktake", "Stocktake id"), Req("Item", "Item code"), Num("Counted", "Recounted quantity")],
            Run = async (c, i) => Check(await c.Get<IStocktakeService>().RecordRecountAsync(c.CompanyId, (long)i.Number("Stocktake"), await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), i.Number("Counted"),
                c.UserId).ConfigureAwait(true), "Recount recorded."),
            List = CountSheetsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Post a stocktake", ListTitle = "Blind count sheets (draft stocktakes)", Description = "Adjusts stock to the counts at average cost. Refused while recounts are due.",
            Fields = [Num("Stocktake", "Stocktake id"), Today(), Req("Account", "Stock adjustment account")],
            Run = async (c, i) =>
            {
                StocktakePostResult result = Check(await c.Get<IStocktakeService>().PostAsync(c.CompanyId, (long)i.Number("Stocktake"), i.Date("Date"), i.Text("Account"), postedByUserId: c.UserId)
                    .ConfigureAwait(true));
                return result.AwaitingApproval ? "The variances are held for approval; they post when the stocktake workflow approves them."
                    : $"{result.AdjustmentsPosted} adjustment(s), net variance {M(result.TotalVarianceValue)}.";
            },
            List = CountSheetsAsync,
        };
    }

    private static async Task<System.Data.DataTable> GiftCardStockAsync(OperationContext c)
        => Table((await c.Db.Set<Core.Vouchers.GiftCard>().AsNoTracking().Where(g => g.CompanyId == c.CompanyId).GroupBy(g => g.Status)
                .Select(g => new { Status = g.Key, Cards = g.Count(), Balance = g.Sum(x => x.Balance) }).ToListAsync().ConfigureAwait(true)).OrderBy(g => g.Status),
            ("Status", g => g.Status), ("Cards", g => g.Cards), ("Balance", g => M(g.Balance)));

    private static async Task<System.Data.DataTable> CountSheetsAsync(OperationContext c)
    {
        IStocktakeService stocktakes = c.Get<IStocktakeService>();
        List<(long Id, string Reference)> drafts = [.. (await c.Db.Set<Core.Inventory.StocktakeSession>().AsNoTracking().Where(s => s.CompanyId == c.CompanyId && s.Status == Core.Inventory.StocktakeStatus.Draft)
            .OrderBy(s => s.Id).Select(s => new { s.Id, s.Reference }).ToListAsync().ConfigureAwait(true)).Select(s => (s.Id, s.Reference))];
        List<(long Id, string Reference, CountSheetLine Line)> rows = [];
        foreach ((long id, string reference) in drafts)
        {
            Core.Common.Result<IReadOnlyList<CountSheetLine>> sheet = await stocktakes.CountSheetAsync(c.CompanyId, id).ConfigureAwait(true);
            rows.AddRange(sheet.IsSuccess ? sheet.Value.Select(l => (id, reference, l)) : []);
        }

        return Table(rows, ("Stocktake", r => r.Id), ("Reference", r => r.Reference), ("Item", r => r.Line.ItemCode), ("Description", r => r.Line.Description), ("Counted", r => r.Line.Counted),
            ("Recount due", r => r.Line.RecountDue), ("Recounted", r => r.Line.Recounted));
    }

    private static async Task<System.Data.DataTable> InTransitAsync(OperationContext c)
    {
        Dictionary<long, string> items = await c.Db.Items.Where(x => x.CompanyId == c.CompanyId).ToDictionaryAsync(x => x.Id, x => x.Code).ConfigureAwait(true);
        Dictionary<long, string> warehouses = await c.Db.Warehouses.Where(x => x.CompanyId == c.CompanyId).ToDictionaryAsync(x => x.Id, x => x.Code).ConfigureAwait(true);
        return Table(await c.Db.StockTransfers.AsNoTracking().Where(t => t.CompanyId == c.CompanyId && t.Status == StockTransferStatus.InTransit).OrderBy(t => t.DispatchedOn).ToListAsync().ConfigureAwait(true),
            ("Transfer", t => t.TransferNumber), ("Dispatched", t => D(t.DispatchedOn)), ("Item", t => items.GetValueOrDefault(t.ItemId)), ("From", t => warehouses.GetValueOrDefault(t.FromWarehouseId)),
            ("To", t => warehouses.GetValueOrDefault(t.ToWarehouseId)), ("Quantity", t => Q(t.Quantity)), ("Value", t => M(t.Value)));
    }
}
