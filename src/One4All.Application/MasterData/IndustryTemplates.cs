using One4All.Core.Accounting;
using One4All.Core.Inventory;

namespace One4All.Application.MasterData;

/// <summary>The industries a company can be set up for; they add to one another (brief §12).</summary>
public enum Vertical
{
    /// <summary>The accounts and taxes every company needs (always applied).</summary>
    Core = 0,

    /// <summary>Shops and point of sale.</summary>
    Retail = 1,

    /// <summary>A council: rates, water, refuse, sewerage.</summary>
    Municipal = 2,

    /// <summary>Land, stands and housing.</summary>
    Housing = 3,

    /// <summary>A school: fees, boarding, transport.</summary>
    School = 4,

    /// <summary>Contracting: work in progress, retentions, subcontractors.</summary>
    Construction = 5,

    /// <summary>Hospitality: bars, restaurants and lodges — food, beverage and liquor sales and costs, tabs, tips and functions.</summary>
    Hospitality = 6,
}

/// <summary>How much of a permission a starter role gets.</summary>
public enum GrantLevel
{
    /// <summary>View only.</summary>
    Read = 1,

    /// <summary>View, create, post and print.</summary>
    Use = 2,

    /// <summary>Everything the permission offers.</summary>
    Full = 3,
}

/// <summary>A ledger account to seed.</summary>
/// <param name="Code">The code.</param>
/// <param name="Name">The name.</param>
/// <param name="Type">The type.</param>
/// <param name="Control">The control type, if a control account.</param>
/// <param name="ReportCategory">Where it shows on the financial statements (null: its type).</param>
/// <param name="BudgetEnabled">Whether it is a budget vote.</param>
public sealed record AccountSeed(string Code, string Name, AccountType Type, ControlAccountType? Control = null, string? ReportCategory = null, bool BudgetEnabled = false);

/// <summary>A tax code to seed; a null rate takes the standard rate given at setup.</summary>
/// <param name="Code">The code.</param>
/// <param name="Name">The name.</param>
/// <param name="Type">The type.</param>
/// <param name="Rate">The rate, or null for the standard rate.</param>
/// <param name="OutputAccount">The output account.</param>
/// <param name="InputAccount">The input account.</param>
public sealed record TaxSeed(string Code, string Name, TaxType Type, decimal? Rate, string? OutputAccount, string? InputAccount);

/// <summary>A starter role: grants by permission module (e.g. Pos) or exact code (e.g. Pos.Sale).</summary>
/// <param name="Name">The role name.</param>
/// <param name="Grants">What it may do.</param>
public sealed record RoleSeed(string Name, IReadOnlyList<(string Scope, GrantLevel Level)> Grants);

/// <summary>A demonstration item.</summary>
/// <param name="Code">The code.</param>
/// <param name="Description">The description.</param>
/// <param name="Type">The type.</param>
/// <param name="Category">The category.</param>
/// <param name="RevenueAccount">Its sales account.</param>
/// <param name="CostAccount">Its cost of sales account.</param>
public sealed record ItemSeed(string Code, string Description, ItemType Type, string Category, string RevenueAccount, string? CostAccount);

/// <summary>What an industry seeds.</summary>
/// <param name="Vertical">The industry.</param>
/// <param name="Accounts">Ledger accounts.</param>
/// <param name="TaxCodes">Tax codes.</param>
/// <param name="Warehouses">Warehouses (code, name).</param>
/// <param name="Roles">Starter roles.</param>
/// <param name="DemoCustomers">Demonstration customers (code, name).</param>
/// <param name="DemoSuppliers">Demonstration suppliers (code, name).</param>
/// <param name="DemoItems">Demonstration items.</param>
public sealed record VerticalTemplate(Vertical Vertical, IReadOnlyList<AccountSeed> Accounts, IReadOnlyList<TaxSeed> TaxCodes, IReadOnlyList<(string Code, string Name)> Warehouses,
    IReadOnlyList<RoleSeed> Roles, IReadOnlyList<(string Code, string Name)> DemoCustomers, IReadOnlyList<(string Code, string Name)> DemoSuppliers, IReadOnlyList<ItemSeed> DemoItems);

/// <summary>The industry templates: charts of accounts, taxes, warehouses, roles and demo data per vertical (brief §12). Pure.</summary>
public static class IndustryTemplates
{
    private static readonly Dictionary<Vertical, VerticalTemplate> Templates = new()
    {
        [Vertical.Core] = new(Vertical.Core,
            [
                new("1000", "Cash on Hand", AccountType.Asset), new("1100", "Bank", AccountType.Asset),
                new("1200", "Debtors Control", AccountType.Asset, ControlAccountType.AccountsReceivable), new("1300", "Prepayments", AccountType.Asset),
                new("1400", "Inventory", AccountType.Asset, ControlAccountType.Stock), new("1600", "Property, Plant & Equipment", AccountType.Asset),
                new("1650", "Accumulated Depreciation", AccountType.Asset),
                new("2100", "Creditors Control", AccountType.Liability, ControlAccountType.AccountsPayable),
                new("2150", "Goods Received Not Invoiced", AccountType.Liability, ControlAccountType.GoodsReceivedNotInvoiced),
                new("2200", "VAT Output", AccountType.Liability, ControlAccountType.VatOutput), new("2210", "VAT Input", AccountType.Asset, ControlAccountType.VatInput),
                new("2300", "Accruals", AccountType.Liability), new("2400", "Payroll Control", AccountType.Liability, ControlAccountType.PayrollControl),
                new("2900", "Suspense", AccountType.Liability, ControlAccountType.Suspense),
                new("3000", "Share Capital", AccountType.Equity), new("3200", "Retained Earnings", AccountType.Equity, ControlAccountType.RetainedEarnings),
                new("4000", "Sales", AccountType.Income), new("4900", "Other Income", AccountType.Income),
                new("5000", "Cost of Sales", AccountType.Expense), new("6000", "Salaries and Wages", AccountType.Expense), new("6100", "Rent", AccountType.Expense),
                new("6200", "Utilities", AccountType.Expense), new("6800", "Depreciation", AccountType.Expense), new("6900", "Bank Charges", AccountType.Expense),
                new("7900", "Rounding", AccountType.Expense, ControlAccountType.Rounding),
                new("8000", "Exchange Gains", AccountType.Income, ControlAccountType.FxGain), new("8100", "Exchange Losses", AccountType.Expense, ControlAccountType.FxLoss),
            ],
            [
                new("VAT", "VAT standard rate", TaxType.Standard, null, "2200", "2210"), new("ZERO", "Zero-rated", TaxType.Zero, 0m, "2200", "2210"),
                new("EXEMPT", "Exempt", TaxType.Exempt, 0m, null, null),
            ],
            [("MAIN", "Main Store")],
            [new("Accountant", [("GeneralLedger", GrantLevel.Full), ("CashBook", GrantLevel.Full), ("Sales", GrantLevel.Use), ("Purchasing", GrantLevel.Use), ("Reporting.Report", GrantLevel.Full)])],
            [("CASH", "Cash Customer")], [("GENERAL", "General Supplier")], []),
        [Vertical.Retail] = new(Vertical.Retail,
            [
                new("1010", "Till Floats", AccountType.Asset), new("2310", "Gift Card Liability", AccountType.Liability), new("4010", "Retail Sales", AccountType.Income),
                new("4020", "Promotional Discounts", AccountType.Income), new("4910", "Gift Card Breakage", AccountType.Income), new("5010", "Stock Wastage", AccountType.Expense),
                new("5020", "Cash Over / Short", AccountType.Expense),
            ],
            [],
            [("SHOP", "Shop Floor"), ("BACK", "Back Store")],
            [
                new("Retail Cashier", [("Pos.Sale", GrantLevel.Use), ("Pos.TillSession", GrantLevel.Use), ("Inventory.Item", GrantLevel.Read)]),
                new("Store Manager", [("Pos", GrantLevel.Full), ("Inventory", GrantLevel.Full), ("Procurement", GrantLevel.Use), ("Voucher", GrantLevel.Full)]),
            ],
            [], [("WHOLESALE", "Wholesale Foods")],
            [
                new("BREAD", "White bread 700g", ItemType.Stock, "Bakery", "4010", "5000"), new("MILK", "Fresh milk 1L", ItemType.Stock, "Dairy", "4010", "5000"),
                new("SODA", "Soft drink 500ml", ItemType.Stock, "Beverages", "4010", "5000"),
            ]),
        [Vertical.Municipal] = new(Vertical.Municipal,
            [
                new("4300", "Property Rates", AccountType.Income), new("4310", "Water Sales", AccountType.Income), new("4320", "Refuse Removal", AccountType.Income),
                new("4330", "Sewerage", AccountType.Income), new("4340", "Interest on Arrears", AccountType.Income), new("2320", "Consumer Deposits", AccountType.Liability),
                new("6500", "Indigent Subsidy", AccountType.Expense), new("6510", "Bad Debts", AccountType.Expense),
            ],
            [], [],
            [
                new("Revenue Clerk", [("Municipal", GrantLevel.Use), ("Sales.Receipt", GrantLevel.Use)]),
                new("Billing Officer", [("Municipal", GrantLevel.Full)]),
            ],
            [("RP-DEMO1", "Demo Ratepayer One"), ("RP-DEMO2", "Demo Ratepayer Two")], [("CHEMSUP", "Water Treatment Chemicals Supplier")],
            [new("WMETER15", "Water meter 15mm", ItemType.Stock, "Meters", "4310", "5000")]),
        [Vertical.Housing] = new(Vertical.Housing,
            [
                new("1450", "Land & Housing Stock (WIP)", AccountType.Asset), new("1460", "Stands Available for Sale", AccountType.Asset),
                new("2330", "Stand Deposits", AccountType.Liability), new("4100", "Land & Housing Sales", AccountType.Income), new("4110", "Rental Income", AccountType.Income),
                new("5100", "Cost of Land Sales", AccountType.Expense),
            ],
            [], [],
            [new("Housing Officer", [("Housing", GrantLevel.Use)]), new("Housing Manager", [("Housing", GrantLevel.Full)])],
            [("BEN-DEMO1", "Demo Beneficiary")], [("SURVEYOR", "Land Surveyors")], []),
        [Vertical.School] = new(Vertical.School,
            [
                new("4200", "Tuition Fees", AccountType.Income), new("4210", "Boarding Fees", AccountType.Income), new("4220", "Transport Fees", AccountType.Income),
                new("2340", "Fees Received in Advance", AccountType.Liability), new("2341", "Student Wallets", AccountType.Liability), new("6400", "Bursaries Granted", AccountType.Expense),
            ],
            [], [("TUCK", "Tuck Shop")],
            [new("Bursar", [("School", GrantLevel.Full), ("Sales.Receipt", GrantLevel.Use)])],
            [("GDN-DEMO1", "Demo Guardian")], [("UNIFORMS", "School Uniform Supplier")],
            [new("UNIFORM", "School uniform set", ItemType.Stock, "Uniforms", "4000", "5000")]),
        [Vertical.Construction] = new(Vertical.Construction,
            [
                new("1470", "Contract Work in Progress", AccountType.Asset, ControlAccountType.WorkInProgress), new("1480", "Retention Receivable", AccountType.Asset),
                new("1490", "Contract Assets", AccountType.Asset), new("1495", "Advances to Subcontractors", AccountType.Asset),
                new("2350", "Retention Payable", AccountType.Liability, ControlAccountType.Retention), new("2360", "Contract Liabilities", AccountType.Liability),
                new("2370", "Withholding Tax Payable", AccountType.Liability), new("4400", "Contract Revenue", AccountType.Income), new("5200", "Contract Costs", AccountType.Expense),
            ],
            [new("WHT", "Withholding tax on contractors", TaxType.Withholding, 10m, "2370", null)],
            [("YARD", "Site Yard")],
            [new("Contracts Manager", [("JobCosting", GrantLevel.Full), ("Procurement", GrantLevel.Use)])],
            [("CLIENT-DEMO", "Demo Contract Client")], [("SUBCON", "Demo Subcontractor"), ("BUILDMAT", "Building Materials Supplier")],
            [new("CEMENT", "Cement 50kg", ItemType.Stock, "Materials", "4400", "5200")]),
        [Vertical.Hospitality] = new(Vertical.Hospitality,
            [
                new("1413", "Inventory - Liquor", AccountType.Asset), new("2380", "Tips & Service Charge Payable", AccountType.Liability),
                new("2382", "Open Tabs Clearing", AccountType.Liability), new("4500", "Food Sales", AccountType.Income), new("4501", "Beverage Sales", AccountType.Income),
                new("4502", "Liquor Sales", AccountType.Income), new("5500", "Cost of Food", AccountType.Expense), new("5501", "Cost of Beverages", AccountType.Expense),
                new("5502", "Cost of Liquor", AccountType.Expense), new("5503", "Spillage & Kitchen Wastage", AccountType.Expense),
            ],
            [],
            [("KITCHEN", "Kitchen"), ("BAR", "Bar"), ("CELLAR", "Cellar Store")],
            [
                new("Waiter", [("Pos.Sale", GrantLevel.Use), ("Pos.TillSession", GrantLevel.Use), ("Inventory.Item", GrantLevel.Read)]),
                new("Restaurant Manager", [("Pos", GrantLevel.Full), ("Inventory", GrantLevel.Full), ("Procurement", GrantLevel.Use)]),
            ],
            [], [("BEVDIST", "Beverage Distributors"), ("FRESHMKT", "Fresh Market Produce")],
            [
                new("LAGER", "Lager 340ml", ItemType.Stock, "Liquor", "4502", "5502"), new("COLA", "Cola 330ml", ItemType.Stock, "Beverages", "4501", "5501"),
                new("BURGER", "Beef burger and chips", ItemType.Stock, "Food", "4500", "5500"),
            ]),
    };

    /// <summary>The template for an industry.</summary>
    /// <param name="vertical">The industry.</param>
    /// <returns>Its template.</returns>
    public static VerticalTemplate For(Vertical vertical) => Charted[vertical];

    // Each template with the full seed chart of its industry (docs/coa/{vertical}.csv, embedded): the accounts above keep their names and control
    // types, take the chart's report category and budget flag, and the chart's further accounts follow.
    private static readonly Dictionary<Vertical, VerticalTemplate> Charted = Templates.ToDictionary(t => t.Key, t => t.Value with
    {
        Accounts = ChartOfAccounts.Merge(t.Value.Accounts, ChartOfAccounts.Load(t.Key)),
    });

    /// <summary>The templates to apply for a set of industries: Core first, then each industry once, in order.</summary>
    /// <param name="verticals">The industries chosen.</param>
    /// <returns>The templates.</returns>
    public static IReadOnlyList<VerticalTemplate> Plan(IEnumerable<Vertical> verticals)
        => [Charted[Vertical.Core], .. verticals.Where(v => v != Vertical.Core).Distinct().Order().Select(v => Charted[v])];

    /// <summary>The operations a grant level gives, within what a permission offers.</summary>
    /// <param name="available">What the permission offers.</param>
    /// <param name="level">The level.</param>
    /// <returns>The operations.</returns>
    public static Core.Security.OperationFlags Operations(Core.Security.OperationFlags available, GrantLevel level) => level switch
    {
        GrantLevel.Full => available,
        GrantLevel.Use => available & (Core.Security.OperationFlags.View | Core.Security.OperationFlags.Create | Core.Security.OperationFlags.Post | Core.Security.OperationFlags.Print),
        _ => available & Core.Security.OperationFlags.View,
    };
}
