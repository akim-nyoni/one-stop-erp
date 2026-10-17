using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Restaurant: the menu and its recipes, the floor (tables, sections, combined tables), orders and the kitchen, billing (split, seats, tenders, vouchers, house accounts), reservations, the waitlist, tips and wastage.</content>
public static partial class OperationCatalogue
{
    // Prints a fired order's tickets at their stations and says where each went.
    private static async Task<string> KitchenPrintsAsync(OperationContext c, string ticketNumber)
    {
        One4All.Core.Common.Result<IReadOnlyList<KitchenPrintOutcome>> printed = await c.Get<IKitchenPrintService>().PrintAsync(c.CompanyId, ticketNumber).ConfigureAwait(true);
        return printed.IsFailure ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, printed.Value.Select(o =>
            $"{o.TicketNumber} → {o.Station}: " + (o.Printer is null ? "kitchen display only" : o.Error is null ? $"printed on {o.Printer}" : $"NOT printed on {o.Printer} ({o.Error})")));
    }

    private static IEnumerable<Operation> Restaurant()
    {
        const string module = "Restaurant";
        yield return new Operation
        {
            Module = "Restaurant", Title = "Floor plan and tab warnings", ListTitle = "Floor plan",
            Description = "Place each table on the floor plan grid (across, down) with its shape; the plan shows every table, its open tab, the tab's total and how long it has "
                + "been open. Tab warnings flag a tab open longer than so many minutes or running over a total (0 turns either off).",
            Fields = [Pick("Step", "Step", ["Place table", "Tab warnings", "Warnings now"]), Opt("Table", "Table number"), Num("X", "Across (0–99)", "0"), Num("Y", "Down (0–99)", "0"),
                Pick("Shape", "Shape", Enum.GetNames<TableShape>()), Num("Minutes", "Warn after (minutes)", "90"), Num("Amount", "Warn over (total)", "0")],
            Run = async (c, i) =>
            {
                IFloorPlanService floor = c.Get<IFloorPlanService>();
                switch (i.Text("Step"))
                {
                    case "Tab warnings":
                        return Check(await floor.SetTabWarningsAsync(c.CompanyId, i.Whole("Minutes"), i.Number("Amount")).ConfigureAwait(true), "Tab warnings set.");
                    case "Warnings now":
                        IReadOnlyList<FloorTable> flagged = await floor.WarningsAsync(c.CompanyId, DateTimeOffset.UtcNow).ConfigureAwait(true);
                        return flagged.Count == 0 ? "No tab needs attention." : string.Join(Environment.NewLine, flagged.Select(t =>
                            $"Table {t.TableNumber,-6} {t.OrderNumber,-12} {M(t.TabTotal),12}  {t.Warning}"));
                    default:
                        return Check(await floor.PlaceTableAsync(c.CompanyId, i.Optional("Table") ?? string.Empty, i.Whole("X"), i.Whole("Y"), Enum.Parse<TableShape>(i.Text("Shape"))).ConfigureAwait(true),
                            "Table placed.");
                }
            },
            List = async c => Table(await c.Get<IFloorPlanService>().PlanAsync(c.CompanyId, DateTimeOffset.UtcNow).ConfigureAwait(true),
                ("Table", t => t.TableNumber), ("Seats", t => t.Seats), ("At", t => t.X is int x ? $"{x},{t.Y}" : "-"), ("Shape", t => t.Shape), ("Status", t => t.Status),
                ("Tab", t => t.OrderNumber ?? string.Empty), ("Total", t => t.OrderNumber is null ? string.Empty : M(t.TabTotal)), ("Open (min)", t => t.OrderNumber is null ? string.Empty : t.MinutesOpen.ToString(CultureInfo.InvariantCulture)),
                ("Warning", t => t.Warning ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = "Restaurant", Title = "Kitchen station printers", ListTitle = "Station printers",
            Description = "Which printer each preparation station's tickets print on (KITCHEN for dishes with no station): a Windows printer by name, or a network printer by host "
                + "and port. A station with no printer works from its kitchen display. Reprint sends a ticket (and its per-station tickets) again, marked REPRINT.",
            Fields = [Pick("Step", "Step", ["Set", "Remove", "Reprint"]), Opt("Station", "Station"), Opt("Printer", "Windows printer name"), Opt("Host", "Network printer host"),
                Num("Port", "Port", "9100"), Num("Copies", "Copies", "1"), Opt("Ticket", "Ticket number (reprint)")],
            Run = async (c, i) =>
            {
                IKitchenPrintService kitchen = c.Get<IKitchenPrintService>();
                StationPrinterSetting setting = new(i.Optional("Station") ?? string.Empty, i.Optional("Printer"), i.Optional("Host"), (int)i.Number("Port"), (int)i.Number("Copies"));
                return i.Text("Step") switch
                {
                    "Remove" => Check(await kitchen.SetStationPrinterAsync(c.CompanyId, setting, remove: true).ConfigureAwait(true), "Station printer removed."),
                    "Reprint" => "Reprinted." + await KitchenPrintsAsync(c, i.Optional("Ticket") ?? string.Empty).ConfigureAwait(true),
                    _ => Check(await kitchen.SetStationPrinterAsync(c.CompanyId, setting).ConfigureAwait(true), "Station printer set."),
                };
            },
            List = async c => Table(await c.Get<IKitchenPrintService>().StationPrintersAsync(c.CompanyId).ConfigureAwait(true),
                ("Station", p => p.Station), ("Printer", p => p.PrinterName ?? $"{p.Host}:{p.Port}"), ("Copies", p => p.Copies)),
        };
        yield return new Operation
        {
            Module = module, Title = "Menu items", ListTitle = "Menu",
            Description = "Creates a menu item with its recipe (ingredients ITEM:quantity; … issued from stock when billed), sets a price for a price level (happy hour, staff…), "
                + "marks an item available or not, refreshes availability from the stock of a warehouse, sends an item to a preparation station (grill, bar…) - fired orders "
                + "give each station its own ticket - and shows a station's queue.",
            Fields = [Pick("Step", "Step", ["Create", "Price level", "Available", "Unavailable", "Refresh availability", "Prep station", "Station queue"]), Opt("Station", "Preparation station"), Opt("Code", "Menu item code"), Opt("Name", "Name (create)"),
                Num("Price", "Price", "0"), Opt("Revenue", "Revenue account (create)"), Opt("Cost", "Cost of sales account (create)"), Opt("Tax", "Tax code (create)"),
                Opt("Recipe", "Recipe: ITEM:quantity; … (create)"), Opt("Level", "Price level"), Opt("Warehouse", "Warehouse (refresh)")],
            Run = async (c, i) =>
            {
                IRestaurantService restaurant = c.Get<IRestaurantService>();
                string code = (i.Optional("Code") ?? string.Empty).Trim();
                switch (i.Text("Step"))
                {
                    case "Create":
                        List<RecipeIngredient> recipe = [];
                        foreach (string part in (i.Optional("Recipe") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                            recipe.Add(bits.Length == 2 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity)
                                ? new RecipeIngredient(await ItemAsync(c, bits[0]).ConfigureAwait(true), quantity)
                                : throw new OperationException($"Write each ingredient as ITEM:quantity (\"{part}\")."));
                        }

                        return Check(await restaurant.CreateMenuItemAsync(c.CompanyId, code, i.Optional("Name") ?? code, i.Number("Price"), i.Optional("Revenue") ?? string.Empty,
                            i.Optional("Cost") ?? string.Empty, recipe, i.Optional("Tax")).ConfigureAwait(true), _ => $"Menu item {code} created.");
                    case "Station queue":
                        IReadOnlyList<StationTicket> queue = await restaurant.StationQueueAsync(c.CompanyId, i.Optional("Station")).ConfigureAwait(true);
                        return queue.Count == 0 ? "Nothing waiting." : string.Join(Environment.NewLine, queue.Select(t =>
                            $"{t.TicketNumber,-16} {t.Status,-10} {string.Join(", ", t.Lines.Select(l => $"{Q(l.Quantity)} × {l.Description}"))}"));
                    case "Prep station":
                        MenuItem menu = await c.Db.Set<MenuItem>().FirstOrDefaultAsync(m => m.CompanyId == c.CompanyId && m.Code == code.ToUpper()).ConfigureAwait(true)
                            ?? throw new OperationException($"No menu item {code}.");
                        menu.PrepStation = i.Optional("Station")?.Trim();
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return menu.PrepStation is null ? $"{code} is made in the kitchen." : $"{code} is made at {menu.PrepStation}.";
                    case "Refresh availability":
                        int changed = Check(await restaurant.RefreshMenuAvailabilityAsync(c.CompanyId, await WarehouseAsync(c, i.Optional("Warehouse") ?? string.Empty).ConfigureAwait(true)).ConfigureAwait(true));
                        return $"{changed} menu item(s) changed availability.";
                }

                long item = await MenuItemAsync(c, code).ConfigureAwait(true);
                return i.Text("Step") switch
                {
                    "Price level" => Check(await restaurant.SetMenuItemPriceLevelAsync(c.CompanyId, item, i.Optional("Level") ?? string.Empty, i.Number("Price")).ConfigureAwait(true), "Price set."),
                    "Available" => Check(await restaurant.SetMenuAvailabilityAsync(c.CompanyId, item, true).ConfigureAwait(true), $"{code} is available."),
                    _ => Check(await restaurant.SetMenuAvailabilityAsync(c.CompanyId, item, false).ConfigureAwait(true), $"{code} is off the menu."),
                };
            },
            List = async c => Table(await c.Db.Set<MenuItem>().AsNoTracking().Where(m => m.CompanyId == c.CompanyId).OrderBy(m => m.Code).ToListAsync().ConfigureAwait(true),
                ("Item", m => m.Code), ("Name", m => m.Name), ("Price", m => M(m.Price)), ("Tax", m => m.TaxCode ?? string.Empty), ("Available", m => m.IsAvailable),
                ("Station", m => m.PrepStation ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Tables and sections", ListTitle = "Tables",
            Description = "Sets up the floor: tables and their seats, sections (with a server; on or off), combined tables for a large party, and the automatic gratuity "
                + "added for parties of a given size.",
            Fields = [Pick("Step", "Step", ["Create table", "Create section", "Table to section", "Section server", "Section on", "Section off", "Combine tables", "Uncombine", "Auto-gratuity policy"]),
                Opt("Table", "Table number(s): T1, T2 …"), Num("Seats", "Seats / smallest party (gratuity)", "4"), Opt("Section", "Section code"), Opt("Name", "Section name"),
                Opt("Server", "Server user name (blank: none)"), Opt("Group", "Combined group label (uncombine)"), Num("Percent", "Gratuity %", "0"), Opt("Account", "Service charge account (gratuity)")],
            Run = async (c, i) =>
            {
                IRestaurantService restaurant = c.Get<IRestaurantService>();
                string table = (i.Optional("Table") ?? string.Empty).Trim(), section = (i.Optional("Section") ?? string.Empty).Trim();
                switch (i.Text("Step"))
                {
                    case "Create table":
                        return Check(await restaurant.CreateTableAsync(c.CompanyId, table, i.Whole("Seats")).ConfigureAwait(true), _ => $"Table {table} created.");
                    case "Create section":
                        return Check(await restaurant.CreateSectionAsync(c.CompanyId, section, i.Optional("Name") ?? section).ConfigureAwait(true), _ => $"Section {section} created.");
                    case "Table to section":
                        long? into = section.Length == 0 ? null : await SectionAsync(c, section).ConfigureAwait(true);
                        return Check(await restaurant.AssignTableToSectionAsync(c.CompanyId, await TableAsync(c, table).ConfigureAwait(true), into).ConfigureAwait(true), "Table assigned.");
                    case "Section server":
                        long? server = i.Optional("Server") is string name ? await UserAsync(c, name).ConfigureAwait(true) : null;
                        return Check(await restaurant.AssignSectionServerAsync(c.CompanyId, await SectionAsync(c, section).ConfigureAwait(true), server).ConfigureAwait(true), "Server assigned.");
                    case "Section on":
                    case "Section off":
                        return Check(await restaurant.SetSectionActiveAsync(c.CompanyId, await SectionAsync(c, section).ConfigureAwait(true), i.Text("Step") == "Section on").ConfigureAwait(true), "Section updated.");
                    case "Combine tables":
                        List<long> tables = [];
                        foreach (string number in table.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            tables.Add(await TableAsync(c, number).ConfigureAwait(true));
                        }

                        return Check(await restaurant.CombineTablesAsync(c.CompanyId, tables).ConfigureAwait(true), _ => $"{tables.Count} tables combined.");
                    case "Uncombine":
                        string label = (i.Optional("Group") ?? string.Empty).Trim();
                        long group = await FindAsync(c.Db.Set<TableGroup>(), g => g.CompanyId == c.CompanyId && g.Label.ToUpper() == label.ToUpper() && g.Status == TableGroupStatus.Active, g => g.Id,
                            "combined group", label).ConfigureAwait(true);
                        return Check(await restaurant.UncombineTablesAsync(c.CompanyId, group).ConfigureAwait(true), "Tables released.");
                    default:
                        return Check(await restaurant.SetAutoGratuityPolicyAsync(c.CompanyId, i.Whole("Seats"), i.Number("Percent"), i.Optional("Account") ?? string.Empty).ConfigureAwait(true),
                            "Auto-gratuity policy saved.");
                }
            },
            List = async c => Table(await c.Db.Set<RestaurantTable>().AsNoTracking().Where(t => t.CompanyId == c.CompanyId).OrderBy(t => t.TableNumber).ToListAsync().ConfigureAwait(true),
                ("Table", t => t.TableNumber), ("Seats", t => t.Seats), ("Status", t => t.Status), ("Combined", t => t.GroupId is null ? string.Empty : "yes")),
        };
        yield return new Operation
        {
            Module = module, Title = "Restaurant orders", ListTitle = "Open orders",
            Description = "Opens an order at a table or for takeaway / delivery, adds menu items (by course, seat and price level) and modifiers, voids or comps a line "
                + "(with an authorising user), moves the order, sends it or a course to the kitchen, tracks a delivery, or shows its lines (with the line ids the other steps use).",
            Fields = [Pick("Step", "Step", ["Show order", "Open at table", "Open takeaway / delivery", "Add item", "Modifier", "Seat a line", "Void line", "Comp line", "Spend limit", "Move to table",
                    "Move line to tab", "Merge into tab", "Change waiter", "Fire order", "Fire course", "Assign driver", "Delivery status", "Cancel order"]),
                Req("Order", "Order number"), Opt("Table", "Table number"), Num("Party", "Party size", "2"), Pick("Type", "Order type", Enum.GetNames<OrderType>()), Opt("Customer", "Customer name"),
                Opt("Phone", "Phone"), Opt("Address", "Delivery address"), Opt("Item", "Menu item code"), Num("Quantity", "Quantity", "1"), Pick("Course", "Course", Enum.GetNames<MenuCourse>()),
                OptNum("Seat", "Seat number"), Opt("Level", "Price level"), OptNum("Line", "Line id"), Opt("Modifier", "Modifier description"), Num("Delta", "Modifier price change", "0"),
                Opt("Ingredient", "Modifier ingredient item"), Num("IngredientQty", "Ingredient quantity", "0"), Opt("User", "Waiter / driver / authorised by (user name)"),
                OptNum("Limit", "Spend limit (blank: none)"), Opt("ToOrder", "Other tab's order number (move line / merge)"), Opt("Ticket", "Kitchen ticket number"), Pick("Delivery", "Delivery status", Enum.GetNames<DeliveryStatus>()), Opt("Reason", "Reason"), Today()],
            Run = async (c, i) =>
            {
                IRestaurantService restaurant = c.Get<IRestaurantService>();
                string number = i.Text("Order").Trim();
                long line = (long)(i.OptionalNumber("Line") ?? 0m);
                async Task<long> UserOrMe() => i.Optional("User") is string name ? await UserAsync(c, name).ConfigureAwait(true) : c.UserId;
                async Task<long?> Authoriser() => i.Optional("User") is string name ? await UserAsync(c, name).ConfigureAwait(true) : null;
                switch (i.Text("Step"))
                {
                    case "Open at table":
                        return Check(await restaurant.OpenOrderAsync(c.CompanyId, await TableAsync(c, i.Optional("Table") ?? string.Empty).ConfigureAwait(true), number, await UserOrMe().ConfigureAwait(true),
                            i.Date("Date"), i.Whole("Party")).ConfigureAwait(true), _ => $"Order {number} opened.");
                    case "Open takeaway / delivery":
                        return Check(await restaurant.OpenOffPremiseOrderAsync(c.CompanyId, number, Enum.Parse<OrderType>(i.Text("Type")), await UserOrMe().ConfigureAwait(true), i.Date("Date"),
                            i.Optional("Customer"), i.Optional("Phone"), i.Optional("Address")).ConfigureAwait(true), _ => $"Order {number} opened.");
                }

                long order = await OrderAsync(c, number).ConfigureAwait(true);
                switch (i.Text("Step"))
                {
                    case "Show order":
                        TableOrder shown = await c.Db.Set<TableOrder>().AsNoTracking().Include(o => o.Lines).FirstAsync(o => o.Id == order).ConfigureAwait(true);
                        return string.Join(Environment.NewLine, shown.Lines.Select(l => $"[{l.Id}] {l.Course,-8} seat {l.SeatNumber?.ToString(CultureInfo.InvariantCulture) ?? "-",-3} {l.Quantity,6:0.##} x "
                                + $"{l.Description,-24} {M(l.LineNet),12}{(l.Fired ? " fired" : string.Empty)}{(l.IsVoided ? " VOID" : string.Empty)}{(l.IsComped ? " COMP" : string.Empty)}"
                                + (l.BilledQuantity > 0m ? $" billed {l.BilledQuantity:0.##}" : string.Empty)))
                            + Environment.NewLine + $"{shown.OrderNumber} {shown.OrderType} {shown.Status}, party of {shown.PartySize}.";
                    case "Add item":
                        decimal added = Check(await restaurant.AddItemAsync(c.CompanyId, order, await MenuItemAsync(c, i.Optional("Item") ?? string.Empty).ConfigureAwait(true), i.Number("Quantity"), 0m,
                            Enum.Parse<MenuCourse>(i.Text("Course")), i.OptionalNumber("Seat") is decimal seat ? (int)seat : null, i.Optional("Level")).ConfigureAwait(true));
                        return $"Added ({M(added)}).";
                    case "Modifier":
                        long? ingredient = i.Optional("Ingredient") is string code ? await ItemAsync(c, code).ConfigureAwait(true) : null;
                        decimal modified = Check(await restaurant.AddItemModifierAsync(c.CompanyId, order, line, i.Optional("Modifier") ?? string.Empty, i.Number("Delta"), ingredient, i.Number("IngredientQty"))
                            .ConfigureAwait(true));
                        return $"Modifier added; line now {M(modified)}.";
                    case "Seat a line":
                        return Check(await restaurant.AssignLineToSeatAsync(c.CompanyId, order, line, i.OptionalNumber("Seat") is decimal s ? (int)s : null).ConfigureAwait(true), "Seat set.");
                    case "Void line":
                        return Check(await restaurant.VoidLineAsync(c.CompanyId, order, line, i.Optional("Reason") ?? string.Empty, await Authoriser().ConfigureAwait(true)).ConfigureAwait(true), "Line voided.");
                    case "Comp line":
                        return Check(await restaurant.CompLineAsync(c.CompanyId, order, line, i.Optional("Reason") ?? string.Empty, await Authoriser().ConfigureAwait(true)).ConfigureAwait(true), "Line comped.");
                    case "Spend limit":
                        return Check(await restaurant.SetOrderSpendLimitAsync(c.CompanyId, order, i.OptionalNumber("Limit")).ConfigureAwait(true), "Spend limit set.");
                    case "Move to table":
                        return Check(await restaurant.TransferOrderAsync(c.CompanyId, order, await TableAsync(c, i.Optional("Table") ?? string.Empty).ConfigureAwait(true)).ConfigureAwait(true), "Order moved.");
                    case "Move line to tab":
                        return Check(await restaurant.TransferLineAsync(c.CompanyId, order, line, await OrderAsync(c, i.Optional("ToOrder") ?? string.Empty).ConfigureAwait(true),
                            i.OptionalNumber("Quantity")).ConfigureAwait(true), id => $"Moved to {i.Optional("ToOrder")} (line {id}).");
                    case "Merge into tab":
                        return Check(await restaurant.MergeOrdersAsync(c.CompanyId, order, await OrderAsync(c, i.Optional("ToOrder") ?? string.Empty).ConfigureAwait(true)).ConfigureAwait(true),
                            n => $"{n} line(s) moved into {i.Optional("ToOrder")}.");
                    case "Change waiter":
                        return Check(await restaurant.ReassignWaiterAsync(c.CompanyId, order, await UserOrMe().ConfigureAwait(true)).ConfigureAwait(true), "Waiter changed.");
                    case "Fire order":
                        _ = Check(await restaurant.FireOrderAsync(c.CompanyId, order, i.Optional("Ticket") ?? string.Empty, i.Date("Date")).ConfigureAwait(true));
                        return "Sent to the kitchen." + await KitchenPrintsAsync(c, i.Optional("Ticket") ?? string.Empty).ConfigureAwait(true);
                    case "Fire course":
                        _ = Check(await restaurant.FireCourseAsync(c.CompanyId, order, Enum.Parse<MenuCourse>(i.Text("Course")), i.Optional("Ticket") ?? string.Empty, i.Date("Date")).ConfigureAwait(true));
                        return $"{i.Text("Course")} sent to the kitchen." + await KitchenPrintsAsync(c, i.Optional("Ticket") ?? string.Empty).ConfigureAwait(true);
                    case "Assign driver":
                        return Check(await restaurant.AssignDeliveryDriverAsync(c.CompanyId, order, await UserOrMe().ConfigureAwait(true)).ConfigureAwait(true), "Driver assigned.");
                    case "Delivery status":
                        return Check(await restaurant.AdvanceDeliveryAsync(c.CompanyId, order, Enum.Parse<DeliveryStatus>(i.Text("Delivery"))).ConfigureAwait(true), "Delivery updated.");
                    default:
                        return Check(await restaurant.CancelOrderAsync(c.CompanyId, order, i.Optional("Reason") ?? string.Empty).ConfigureAwait(true), $"Order {number} cancelled.");
                }
            },
            List = async c => Table(await c.Db.Set<TableOrder>().AsNoTracking().Where(o => o.CompanyId == c.CompanyId && o.Status == TableOrderStatus.Open).OrderBy(o => o.Id).ToListAsync().ConfigureAwait(true),
                ("Order", o => o.OrderNumber), ("Type", o => o.OrderType), ("Party", o => o.PartySize), ("Opened", o => D(o.OpenedOn)), ("Customer", o => o.CustomerName ?? string.Empty),
                ("Delivery", o => o.DeliveryStatus?.ToString() ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Bill an order", ListTitle = "Recent bills",
            Description = "Bills an order in full (cash or card), by seat, in part (LINE:quantity; …), across several tenders (ACCOUNT:amount; …), to a gift card or to a customer's "
                + "house account - with service charge, tip and discount - issuing the recipe ingredients from stock; quotes the automatic gratuity; or reverses a bill.",
            Fields = [Pick("Step", "Step", ["Full", "Seat", "Split", "Tenders", "Gift card", "House account", "Quote gratuity", "Reverse bill"]), Opt("Order", "Order number"),
                Opt("Warehouse", "Warehouse (stock issued)"), Opt("Cash", "Cash / card account"), Opt("Tax", "Tax account"), Num("Service", "Service charge", "0"), Opt("ServiceAccount", "Service charge account"),
                Num("Tip", "Tip", "0"), Opt("Tips", "Tips payable account"), Num("Discount", "Discount", "0"), Opt("DiscountAccount", "Discount account"), OptNum("Seat", "Seat number"),
                Opt("Portions", "Split: LINE:quantity; …"), Opt("Tenders", "Tenders: ACCOUNT:amount; …"), Opt("Card", "Gift card number"), Opt("Liability", "Gift card liability account"),
                Opt("Customer", "House account customer"), Opt("Sales", "Sales account (house account)"), OptNum("Bill", "Bill id (reverse)"), Opt("Reason", "Reason (reverse)"), Today()],
            Run = async (c, i) =>
            {
                IRestaurantService restaurant = c.Get<IRestaurantService>();
                if (i.Text("Step") == "Reverse bill")
                {
                    return Check(await restaurant.ReverseBillAsync(c.CompanyId, (long)(i.OptionalNumber("Bill") ?? 0m), i.Date("Date"), i.Optional("Reason") ?? string.Empty).ConfigureAwait(true),
                        j => $"Bill reversed ({j}); the ingredients are back in stock.");
                }

                long order = await OrderAsync(c, i.Optional("Order") ?? string.Empty).ConfigureAwait(true);
                if (i.Text("Step") == "Quote gratuity")
                {
                    AutoGratuityQuote quote = Check(await restaurant.QuoteAutoGratuityAsync(c.CompanyId, order).ConfigureAwait(true));
                    return quote.Applies ? $"Add {M(quote.Amount)} service charge ({quote.ServiceChargeAccountCode})." : "No automatic gratuity for this party.";
                }

                long warehouse = await WarehouseAsync(c, i.Optional("Warehouse") ?? string.Empty).ConfigureAwait(true);
                string cash = i.Optional("Cash") ?? string.Empty;
                string? tax = i.Optional("Tax"), serviceAccount = i.Optional("ServiceAccount"), tips = i.Optional("Tips"), discountAccount = i.Optional("DiscountAccount");
                decimal service = i.Number("Service"), tip = i.Number("Tip"), discount = i.Number("Discount");
                BillResult bill;
                switch (i.Text("Step"))
                {
                    case "Full":
                        bill = Check(await restaurant.BillOrderAsync(c.CompanyId, order, warehouse, i.Date("Date"), cash, tax, service, serviceAccount, tip, tips, discount, discountAccount).ConfigureAwait(true));
                        break;
                    case "Seat":
                        bill = Check(await restaurant.BillSeatAsync(c.CompanyId, order, (int)(i.OptionalNumber("Seat") ?? 0m), warehouse, i.Date("Date"), cash, tax, service, serviceAccount, tip, tips, discount,
                            discountAccount).ConfigureAwait(true));
                        break;
                    case "Split":
                        List<SplitPortion> portions = [.. Pairs(i.Optional("Portions"), "LINE:quantity").Select(p => new SplitPortion(long.Parse(p.Key, CultureInfo.InvariantCulture), p.Value))];
                        bill = Check(await restaurant.BillPartialAsync(c.CompanyId, order, warehouse, i.Date("Date"), portions, cash, tax, service, serviceAccount, tip, tips, discount, discountAccount)
                            .ConfigureAwait(true));
                        break;
                    case "Tenders":
                        List<TenderLine> tenders = [.. Pairs(i.Optional("Tenders"), "ACCOUNT:amount").Select(p => new TenderLine(p.Key, p.Value))];
                        bill = Check(await restaurant.BillWithTendersAsync(c.CompanyId, order, warehouse, i.Date("Date"), tenders, tax, service, serviceAccount, tip, tips, discount, discountAccount)
                            .ConfigureAwait(true));
                        break;
                    case "Gift card":
                        bill = Check(await restaurant.BillToVoucherAsync(c.CompanyId, order, (i.Optional("Card") ?? string.Empty).Trim(), warehouse, i.Date("Date"), i.Optional("Liability") ?? string.Empty, tax)
                            .ConfigureAwait(true));
                        break;
                    default:
                        bill = Check(await restaurant.BillToHouseAccountAsync(c.CompanyId, order, await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true), warehouse, i.Date("Date"),
                            i.Optional("Sales") ?? string.Empty, tax).ConfigureAwait(true));
                        break;
                }

                return $"Billed {M(bill.Gross)} (net {M(bill.Net)}, tax {M(bill.Tax)}, service {M(bill.ServiceCharge)}, tip {M(bill.Tip)}, discount {M(bill.Discount)}); cost of sales {M(bill.CostOfSales)} "
                    + $"({bill.RevenueJournalNumber}).{(bill.FullyBilled ? " The order is closed." : " Some of the order is still to bill.")}";
            },
            List = async c => Table(await c.Db.Set<RestaurantBill>().AsNoTracking().Where(b => b.CompanyId == c.CompanyId).OrderByDescending(b => b.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
                ("Bill", b => b.Id), ("Date", b => D(b.Date)), ("Gross", b => M(b.Gross)), ("Tip", b => M(b.Tip)), ("Journal", b => b.RevenueJournalNumber), ("Reversed", b => b.IsReversed)),
        };
        yield return new Operation
        {
            Module = module, Title = "Kitchen tickets", ListTitle = "Tickets in the kitchen",
            Description = "Moves a kitchen ticket along: preparing, ready, served.",
            Fields = [Req("Ticket", "Ticket number"), Pick("Status", "Now", Enum.GetNames<KitchenTicketStatus>())],
            Run = async (c, i) =>
            {
                string number = i.Text("Ticket").Trim();
                long ticket = await FindAsync(c.Db.Set<KitchenTicket>(), t => t.CompanyId == c.CompanyId && t.TicketNumber.ToUpper() == number.ToUpper(), t => t.Id, "kitchen ticket", number).ConfigureAwait(true);
                return Check(await c.Get<IRestaurantService>().AdvanceTicketAsync(c.CompanyId, ticket, Enum.Parse<KitchenTicketStatus>(i.Text("Status"))).ConfigureAwait(true), $"Ticket {number}: {i.Text("Status")}.");
            },
            List = async c => Table(await c.Db.Set<KitchenTicket>().AsNoTracking().Where(t => t.CompanyId == c.CompanyId && t.Status != KitchenTicketStatus.Served).OrderBy(t => t.Id).ToListAsync().ConfigureAwait(true),
                ("Ticket", t => t.TicketNumber), ("Course", t => t.Course?.ToString() ?? "All"), ("Sent", t => D(t.SentOn)), ("Status", t => t.Status)),
        };
        yield return new Operation
        {
            Module = module, Title = "Reservations", ListTitle = "Upcoming reservations",
            Description = "Books a table for a guest at a time (yyyy-mm-dd hh:mm), seats the guest when they arrive (opening the order), cancels a booking or marks a no-show.",
            Fields = [Pick("Step", "Step", ["Reserve", "Seat", "Cancel", "No-show"]), Opt("Table", "Table number (reserve)"), Opt("Guest", "Guest name"), Num("Party", "Party size", "2"),
                Opt("When", "When: yyyy-mm-dd hh:mm (reserve)"), Opt("Phone", "Phone"), Opt("Notes", "Notes"), OptNum("Reservation", "Reservation id"), Opt("Order", "Order number (seat)"),
                Opt("Waiter", "Waiter user name (blank: me)"), Opt("Reason", "Reason (cancel)"), Today()],
            Run = async (c, i) =>
            {
                IRestaurantService restaurant = c.Get<IRestaurantService>();
                long reservation = (long)(i.OptionalNumber("Reservation") ?? 0m);
                switch (i.Text("Step"))
                {
                    case "Reserve":
                        DateTime when = DateTime.TryParse(i.Optional("When"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed)
                            ? parsed.ToUniversalTime() : throw new OperationException("Write when as yyyy-mm-dd hh:mm.");
                        return Check(await restaurant.ReserveTableAsync(c.CompanyId, await TableAsync(c, i.Optional("Table") ?? string.Empty).ConfigureAwait(true), i.Optional("Guest") ?? string.Empty,
                            i.Whole("Party"), when, i.Optional("Phone"), i.Optional("Notes")).ConfigureAwait(true), id => $"Reservation {id} booked.");
                    case "Seat":
                        long waiter = i.Optional("Waiter") is string name ? await UserAsync(c, name).ConfigureAwait(true) : c.UserId;
                        return Check(await restaurant.SeatReservationAsync(c.CompanyId, reservation, i.Optional("Order") ?? string.Empty, waiter, i.Date("Date")).ConfigureAwait(true), _ => "Guest seated; order opened.");
                    case "Cancel":
                        return Check(await restaurant.CancelReservationAsync(c.CompanyId, reservation, i.Optional("Reason") ?? string.Empty).ConfigureAwait(true), "Reservation cancelled.");
                    default:
                        return Check(await restaurant.MarkReservationNoShowAsync(c.CompanyId, reservation).ConfigureAwait(true), "Marked as a no-show.");
                }
            },
            List = async c => Table(await c.Db.Set<TableReservation>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId && r.Status == ReservationStatus.Booked).OrderBy(r => r.ReservedFor)
                    .ToListAsync().ConfigureAwait(true),
                ("Id", r => r.Id), ("When", r => r.ReservedFor.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Guest", r => r.GuestName), ("Party", r => r.PartySize),
                ("Table", r => r.TableId), ("Phone", r => r.GuestPhone ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Waitlist", ListTitle = "Waiting",
            Description = "Walk-ins waiting for a table: add a party (with the wait quoted), seat it at a table (opening the order), or take it off the list (left, or a no-show).",
            Fields = [Pick("Step", "Step", ["Add", "Seat", "Left", "No-show"]), Opt("Guest", "Guest name"), Num("Party", "Party size", "2"), Num("Minutes", "Quoted wait (minutes)", "15"),
                Opt("Phone", "Phone"), Opt("Notes", "Notes"), OptNum("Entry", "Waitlist id"), Opt("Table", "Table number (seat)"), Opt("Order", "Order number (seat)"),
                Opt("Waiter", "Waiter user name (blank: me)"), Today()],
            Run = async (c, i) =>
            {
                IRestaurantService restaurant = c.Get<IRestaurantService>();
                long entry = (long)(i.OptionalNumber("Entry") ?? 0m);
                switch (i.Text("Step"))
                {
                    case "Add":
                        return Check(await restaurant.AddToWaitlistAsync(c.CompanyId, i.Optional("Guest") ?? string.Empty, i.Whole("Party"), i.Whole("Minutes"), DateTime.UtcNow, i.Optional("Phone"), i.Optional("Notes"))
                            .ConfigureAwait(true), id => $"On the waitlist ({id}).");
                    case "Seat":
                        long waiter = i.Optional("Waiter") is string name ? await UserAsync(c, name).ConfigureAwait(true) : c.UserId;
                        return Check(await restaurant.SeatFromWaitlistAsync(c.CompanyId, entry, await TableAsync(c, i.Optional("Table") ?? string.Empty).ConfigureAwait(true), i.Optional("Order") ?? string.Empty,
                            waiter, i.Date("Date")).ConfigureAwait(true), _ => "Party seated; order opened.");
                    case "Left":
                        return Check(await restaurant.RemoveFromWaitlistAsync(c.CompanyId, entry).ConfigureAwait(true), "Taken off the list.");
                    default:
                        return Check(await restaurant.MarkWaitlistNoShowAsync(c.CompanyId, entry).ConfigureAwait(true), "Marked as a no-show.");
                }
            },
            List = async c => Table(await c.Db.Set<WaitlistEntry>().AsNoTracking().Where(w => w.CompanyId == c.CompanyId && w.Status == WaitlistStatus.Waiting).OrderBy(w => w.AddedOn).ToListAsync()
                    .ConfigureAwait(true),
                ("Id", w => w.Id), ("Guest", w => w.GuestName), ("Party", w => w.PartySize), ("Added", w => w.AddedOn.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)),
                ("Quoted (min)", w => w.QuotedMinutes), ("Phone", w => w.GuestPhone ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Tips", ListTitle = "Tips owed to staff",
            Description = "Shares a tip pool among staff by weight (USER:weight; …, e.g. hours or points), pays one person's share, or pays tips out of the tips payable account in one sum.",
            Fields = [Pick("Step", "Step", ["Distribute", "Pay a share", "Pay out"]), Num("Amount", "Amount", "0"), Opt("Shares", "Shares: USER:weight; … (distribute)"), Opt("Tips", "Tips payable account"),
                Opt("Funding", "Cash / bank account (pay)"), OptNum("Allocation", "Share id (pay a share)"), Today()],
            Run = async (c, i) =>
            {
                IRestaurantService restaurant = c.Get<IRestaurantService>();
                switch (i.Text("Step"))
                {
                    case "Distribute":
                        List<TipShare> shares = [];
                        foreach (KeyValuePair<string, decimal> share in Pairs(i.Optional("Shares"), "USER:weight"))
                        {
                            shares.Add(new TipShare(await UserAsync(c, share.Key).ConfigureAwait(true), share.Value));
                        }

                        int count = Check(await restaurant.DistributeTipsAsync(c.CompanyId, i.Date("Date"), i.Number("Amount"), i.Optional("Tips") ?? string.Empty, shares).ConfigureAwait(true));
                        return $"{M(i.Number("Amount"))} shared among {count} staff.";
                    case "Pay a share":
                        return Check(await restaurant.PayTipAllocationAsync(c.CompanyId, (long)(i.OptionalNumber("Allocation") ?? 0m), i.Date("Date"), i.Optional("Funding") ?? string.Empty).ConfigureAwait(true),
                            j => $"Share paid ({j}).");
                    default:
                        return Check(await restaurant.PayTipsAsync(c.CompanyId, i.Number("Amount"), i.Date("Date"), i.Optional("Tips") ?? string.Empty, i.Optional("Funding") ?? string.Empty).ConfigureAwait(true),
                            j => $"Tips paid out ({j}).");
                }
            },
            List = async c => Table(await c.Db.Set<TipAllocation>().AsNoTracking().Where(t => t.CompanyId == c.CompanyId && t.Status == TipAllocationStatus.Owed).OrderBy(t => t.Date).ToListAsync().ConfigureAwait(true),
                ("Share", t => t.Id), ("Staff", t => t.StaffUserId), ("Amount", t => M(t.Amount)), ("Date", t => D(t.Date))),
        };
        yield return new Operation
        {
            Module = module, Title = "Kitchen wastage", ListTitle = "Recent wastage",
            Description = "Writes off spoilt, dropped or expired ingredients from a warehouse at cost, with a reason, to the wastage account.",
            Fields = [Req("Item", "Stock item"), WarehousePick("Warehouse", "Warehouse"), Num("Quantity", "Quantity"), Req("Account", "Wastage account"), Req("Reason", "Reason"), Today()],
            Run = async (c, i) =>
            {
                decimal value = Check(await c.Get<IRestaurantService>().RecordWastageAsync(c.CompanyId, await ItemAsync(c, i.Text("Item")).ConfigureAwait(true),
                    await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), i.Date("Date"), i.Number("Quantity"), i.Text("Account"), i.Text("Reason")).ConfigureAwait(true));
                return $"Wastage of {M(value)} written off.";
            },
            List = async c => Table(await c.Db.Set<WastageRecord>().AsNoTracking().Where(w => w.CompanyId == c.CompanyId).OrderByDescending(w => w.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
                ("Date", w => D(w.Date)), ("Item", w => w.ItemId), ("Quantity", w => w.Quantity), ("Value", w => M(w.Value)), ("Reason", w => w.Reason)),
        };
    }

    // KEY:number pairs separated by semicolons.
    private static IEnumerable<KeyValuePair<string, decimal>> Pairs(string? text, string shape)
    {
        foreach (string part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = part.LastIndexOf(':');
            yield return colon > 0 && decimal.TryParse(part[(colon + 1)..], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
                ? new KeyValuePair<string, decimal>(part[..colon].Trim(), value)
                : throw new OperationException($"Write each entry as {shape} (\"{part}\").");
        }
    }

    private static Task<long> MenuItemAsync(OperationContext c, string code)
        => FindAsync(c.Db.Set<MenuItem>(), m => m.CompanyId == c.CompanyId && m.Code.ToUpper() == code.Trim().ToUpper(), m => m.Id, "menu item", code);

    private static Task<long> TableAsync(OperationContext c, string number)
        => FindAsync(c.Db.Set<RestaurantTable>(), t => t.CompanyId == c.CompanyId && t.TableNumber.ToUpper() == number.Trim().ToUpper(), t => t.Id, "table", number);

    private static Task<long> SectionAsync(OperationContext c, string code)
        => FindAsync(c.Db.Set<FloorSection>(), s => s.CompanyId == c.CompanyId && s.Code.ToUpper() == code.Trim().ToUpper(), s => s.Id, "section", code);

    private static Task<long> OrderAsync(OperationContext c, string number)
        => FindAsync(c.Db.Set<TableOrder>(), o => o.CompanyId == c.CompanyId && o.OrderNumber.ToUpper() == number.Trim().ToUpper(), o => o.Id, "order", number);
}
