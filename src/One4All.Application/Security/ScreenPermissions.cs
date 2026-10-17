using System.Text.RegularExpressions;
using One4All.Core.Security;

namespace One4All.Application.Security;

/// <summary>
/// Which <c>Module.Feature</c> permission a desktop screen needs, by the console module it sits in and its title, and which operation
/// flags running it needs (brief §18.3: every screen is gated by a feature in the <see cref="PermissionCatalogue"/>, not only by its module).
/// Rules are tried in order within a console module; the module's default feature covers the rest. Pure.
/// </summary>
public static partial class ScreenPermissions
{
    private const OperationFlags AnyWrite = OperationFlags.Create | OperationFlags.Edit | OperationFlags.Delete | OperationFlags.Post | OperationFlags.Approve | OperationFlags.VoidReverse
        | PermissionDefinition.AllAuthoriseLevels | OperationFlags.OverridePrice | OperationFlags.OverrideDiscount | OperationFlags.OverrideCreditLimit | OperationFlags.BackDate;

    private const OperationFlags Approving = OperationFlags.Approve | PermissionDefinition.AllAuthoriseLevels;

    private const OperationFlags Reversing = OperationFlags.VoidReverse | OperationFlags.Delete | OperationFlags.Post;

    // (console module, title pattern, feature code); the first match wins, "*" is the module's default.
    private static readonly (string Module, string Pattern, string Code)[] Rules =
    [
        ("Approvals", @"segregation", "Security.SodMatrix"),
        ("Approvals", @"manager", "Security.User"),
        ("Approvals", @"*", "System.Workflow"),

        ("Banking", @"cheque", "CashBook.Cheque"),
        ("Banking", @"standing order|direct debit|mandate", "CashBook.StandingOrder"),
        ("Banking", @"bank statement", "CashBook.Reconciliation"),
        ("Banking", @"supplier statement", "Purchasing.Statement"),
        ("Banking", @"forecast", "CashBook.Forecast"),
        ("Banking", @"file layout", "CashBook.BankFileLayout"),
        ("Banking", @"payroll bank", "Payroll.BankFile"),
        ("Banking", @"transfer", "CashBook.Transfer"),
        ("Banking", @"*", "CashBook.Transaction"),

        ("Budgeting", @"vs actual|holder statement|negotiation trail|vote book", "Budget.Reports"),
        ("Budgeting", @"commentary", "Budget.Commentary"),
        ("Budgeting", @"virement", "Budget.Virement"),
        ("Budgeting", @"re-phase", "Budget.Rephase"),
        ("Budgeting", @"assumption|driver|actual volumes", "Budget.Assumptions"),
        ("Budgeting", @"methods and forecasts", "Budget.Forecast"),
        ("Budgeting", @"through preparation|activate", "Budget.Review"),
        ("Budgeting", @"archive", "Budget.Archive"),
        ("Budgeting", @"carry|year-end commitments", "Budget.Reservation"),
        ("Budgeting", @"deadlines|alerts", "Budget.Target"),
        ("Budgeting", @"*", "Budget.Prepare"),

        ("CRM", @"activity", "Crm.Activity"),
        ("CRM", @"whatsapp", "Crm.WhatsApp"),
        ("CRM", @"*", "Crm.Lead"),

        ("Contracts", @"boq|measure", "JobCosting.Budget"),
        ("Contracts", @"subcontract", "JobCosting.Subcontractor"),
        ("Contracts", @"variation", "JobCosting.VariationOrder"),
        ("Contracts", @"certificate", "JobCosting.Certificate"),
        ("Contracts", @"timesheet|plant hours", "JobCosting.Timesheet"),
        ("Contracts", @"issue materials", "Inventory.Issue"),
        ("Contracts", @"*", "JobCosting.Contract"),

        ("Customers", @"invoice|debit note", "Sales.Invoice"),
        ("Customers", @"receipt", "Sales.Receipt"),
        ("Customers", @"statement", "Sales.Statement"),
        ("Customers", @"allocate", "Sales.Allocation"),
        ("Customers", @"credit limit|debt collection|dunning|promises|credit loss|finance charges", "Sales.CreditControl"),
        ("Customers", @"price list", "Sales.PriceList"),
        ("Customers", @"commission", "Sales.Commission"),
        ("Customers", @"lay-by", "Sales.Layby"),
        ("Customers", @"*", "Sales.Customer"),

        ("Documents", @"*", "Reporting.Report"),

        ("Fixed Assets", @"capital project|cwip|capitalise", "FixedAssets.CapitalProject"),
        ("Fixed Assets", @"count|sighting", "FixedAssets.Verification"),
        ("Fixed Assets", @"maintenance|usage", "FixedAssets.Maintenance"),
        ("Fixed Assets", @"*", "FixedAssets.Asset"),

        ("General Ledger", @"close or reopen", "System.Period"),
        ("General Ledger", @"year-end", "GeneralLedger.YearEnd"),
        ("General Ledger", @"revalue|exchange-rate", "GeneralLedger.Revaluation"),
        ("General Ledger", @"vat|tax", "GeneralLedger.Tax"),
        ("General Ledger", @"recurring", "GeneralLedger.RecurringJournal"),
        ("General Ledger", @"accrual|deferred income", "GeneralLedger.Accrual"),
        ("General Ledger", @"funds|reporting trees|hyperinflation|overhead", "GeneralLedger.Account"),
        ("General Ledger", @"petty cash", "CashBook.PettyCash"),
        ("General Ledger", @"vouchers", "Voucher.PaymentVoucher"),
        ("General Ledger", @"*", "GeneralLedger.Journal"),

        ("HR & Attendance", @"approve payroll", "Payroll.Approve"),
        ("HR & Attendance", @"commit approved|pay and close", "Payroll.Commit"),
        ("HR & Attendance", @"reverse a payroll", "Payroll.Reverse"),
        ("HR & Attendance", @"calculate payroll|run payroll|supplementary payroll|legacy payroll", "Payroll.Calculate"),
        ("HR & Attendance", @"payslip", "Payroll.Payslip"),
        ("HR & Attendance", @"pay element|tax year|tax credits|net-pay protection|benefits in kind", "Payroll.PayElement"),
        ("HR & Attendance", @"pay variable|split net pay", "Payroll.Variables"),
        ("HR & Attendance", @"bank detail", "Payroll.BankDetails"),
        ("HR & Attendance", @"backpay|salary scale|increment|pay group|appoint to act", "Payroll.SalaryChange"),
        ("HR & Attendance", @"establishment", "Payroll.Establishment"),
        ("HR & Attendance", @"approve overtime|self-service", "Payroll.SelfService"),
        ("HR & Attendance", @"leave", "Payroll.Leave"),
        ("HR & Attendance", @"loan|garnishee", "Payroll.Loan"),
        ("HR & Attendance", @"termination", "Payroll.Termination"),
        ("HR & Attendance", @"expense", "Payroll.Expense"),
        ("HR & Attendance", @"medical", "Payroll.Medical"),
        ("HR & Attendance", @"attendance|roster|time-clock", "Payroll.Attendance"),
        ("HR & Attendance", @"grievance|disciplinary|appraisal|training|document|contacts|expiry", "Payroll.HrRecord"),
        ("HR & Attendance", @"accrue|provision|capitalise|third-party", "Payroll.Commit"),
        ("HR & Attendance", @"*", "Payroll.Employee"),

        ("Housing", @"waiting-list|waiting list|applicant", "Housing.WaitingList"),
        ("Housing", @"allocation appeals|allocation quotas", "Housing.AllocationApprove"),
        ("Housing", @"allocation", "Housing.AllocationRun"),
        ("Housing", @"offer", "Housing.Offer"),
        ("Housing", @"developer", "Housing.Scheme"),
        ("Housing", @"instalment", "Housing.InstalmentSchedule"),
        ("Housing", @"agreement|sale, lease", "Housing.Agreement"),
        ("Housing", @"ownership", "Housing.OwnershipChange"),
        ("Housing", @"title deed", "Housing.TitleDeed"),
        ("Housing", @"repossess", "Housing.Repossession"),
        ("Housing", @"eviction", "Housing.Eviction"),
        ("Housing", @"rent tariff", "Housing.RentRun"),
        ("Housing", @"rental|tenanc|occupant", "Housing.Tenancy"),
        ("Housing", @"scheme|developer|covenant|levies|layout plan|servicing cost|sectional", "Housing.Scheme"),
        ("Housing", @"acquisition|compulsory", "Housing.Acquisition"),
        ("Housing", @"land|map stands", "Housing.LandParcel"),
        ("Housing", @"caveat|blocks", "Housing.Caveat"),
        ("Housing", @"pricing|deposit", "Housing.Pricing"),
        ("Housing", @"cooperative", "Housing.Cooperative"),
        ("Housing", @"informal", "Housing.InformalSettlement"),
        ("Housing", @"*", "Housing.Stand"),

        ("Municipality", @"cemeter|facility", "Municipal.Facility"),
        ("Municipality", @"meter reading|routes", "Municipal.Reading"),
        ("Municipality", @"meter", "Municipal.Meter"),
        ("Municipality", @"billing", "Municipal.BillingRun"),
        ("Municipality", @"tariff", "Municipal.Tariff"),
        ("Municipality", @"licence", "Municipal.Licence"),
        ("Municipality", @"clearance", "Municipal.Clearance"),
        ("Municipality", @"valuation roll", "Municipal.ValuationRoll"),
        ("Municipality", @"fault", "Municipal.ServiceTicket"),
        ("Municipality", @"service", "Municipal.Service"),
        ("Municipality", @"disconnection|indigent", "Municipal.DebtManagement"),
        ("Municipality", @"prepaid", "Municipal.Prepaid"),
        ("Municipality", @"*", "Municipal.Property"),

        ("Point of Sale", @"gift card", "Voucher.GiftCard"),
        ("Point of Sale", @"loyalty", "Pos.Loyalty"),
        ("Point of Sale", @"promotion|price a basket", "Pos.Promotion"),
        ("Point of Sale", @"refund|exchange goods", "Pos.Refund"),
        ("Point of Sale", @"discard", "Pos.Void"),
        ("Point of Sale", @"till", "Pos.TillSession"),
        ("Point of Sale", @"category|currency|rounding", "Pos.TillSetting"),
        ("Point of Sale", @"*", "Pos.Sale"),

        ("Purchasing", @"requisition", "Procurement.Requisition"),
        ("Purchasing", @"quotation|framework", "Procurement.Rfq"),
        ("Purchasing", @"receive|quality|return stock", "Procurement.Grn"),
        ("Purchasing", @"match|dispute", "Procurement.InvoiceMatch"),
        ("Purchasing", @"budget", "Budget.Reservation"),
        ("Purchasing", @"rebate|scorecard", "Procurement.SupplierPerformance"),
        ("Purchasing", @"*", "Procurement.PurchaseOrder"),

        ("Receipting", @"stations|collection agents|receipt books|matching rules|cashiers' own", "CashBook.CashOffice"),
        ("Receipting", @"reverse|arrangements|dishonour", "Sales.Receipt"),
        ("Receipting", @"^banking|drawer", "CashBook.Transaction"),
        ("Receipting", @"*", "CashBook.Teller"),

        ("Restaurant", @"kitchen", "Pos.Kitchen"),
        ("Restaurant", @"bill|tips", "Pos.Sale"),
        ("Restaurant", @"menu|tables|floor plan|printers", "Pos.RestaurantSetup"),
        ("Restaurant", @"*", "Pos.Restaurant"),

        ("Sales & Schools", @"academic|term dates|classes", "School.AcademicYear"),
        ("Sales & Schools", @"bursar|sibling", "School.Bursary"),
        ("Sales & Schools", @"fee structure", "School.FeeStructure"),
        ("Sales & Schools", @"fee reminders", "School.Statement"),
        ("Sales & Schools", @"restricted", "School.Restricted"),
        ("Sales & Schools", @"wallet", "School.Wallet"),
        ("Sales & Schools", @"student", "School.Student"),
        ("Sales & Schools", @"contract|call off", "Sales.Contract"),
        ("Sales & Schools", @"quotation", "SalesOrders.Quotation"),
        ("Sales & Schools", @"delivery|pick list|drop-ship", "SalesOrders.Despatch"),
        ("Sales & Schools", @"*", "SalesOrders.Order"),

        ("Stock", @"stocktake|count", "Inventory.StockTake"),
        ("Stock", @"adjust|write-down|revalue", "Inventory.Adjustment"),
        ("Stock", @"transfer", "Inventory.Transfer"),
        ("Stock", @"assembly|disassemble", "Inventory.Assembly"),
        ("Stock", @"bins|picks|availability", "Inventory.Warehouse"),
        ("Stock", @"price", "Inventory.Price"),
        ("Stock", @"landed cost", "Inventory.GoodsReceipt"),
        ("Stock", @"store requisition", "Inventory.Issue"),
        ("Stock", @"reorder|replenishment|classify", "Inventory.Replenishment"),
        ("Stock", @"owned stock", "Inventory.Consignment"),
        ("Stock", @"*", "Inventory.Item"),

        ("Suppliers", @"invoice|credit note|recurring|allowance|allocate", "Purchasing.Invoice"),
        ("Suppliers", @"payment|remittance|advance", "Purchasing.PaymentRun"),
        ("Suppliers", @"bank details", "Purchasing.SupplierBankDetail"),
        ("Suppliers", @"withholding", "Purchasing.WithholdingTax"),
        ("Suppliers", @"statement", "Purchasing.Statement"),
        ("Suppliers", @"*", "Purchasing.Supplier"),

        ("System Manager", @"add a customer", "Sales.Customer"),
        ("System Manager", @"add a supplier", "Purchasing.Supplier"),
        ("System Manager", @"add an item", "Inventory.Item"),
        ("System Manager", @"warehouse", "Inventory.Warehouse"),
        ("System Manager", @"ledger account|cost centre", "GeneralLedger.Account"),
        ("System Manager", @"tax code", "System.TaxCode"),
        ("System Manager", @"fiscal year", "System.Period"),
        ("System Manager", @"exchange rate", "System.ExchangeRate"),
        ("System Manager", @"currency", "System.Currency"),
        ("System Manager", @"branch", "System.Branch"),
        ("System Manager", @"industry|sample data", "System.Industry"),
        ("System Manager", @"message template|message batch", "System.Company"),
        ("System Manager", @"company|logo|language", "System.Company"),
        ("System Manager", @"role", "Security.Role"),
        ("System Manager", @"group", "Security.Group"),
        ("System Manager", @"user|password|cashier card", "Security.User"),
        ("System Manager", @"payroll encryption", "System.Encryption"),
        ("System Manager", @"api keys|webhook|offline", "System.Integration"),
        ("System Manager", @"back up", "System.Backup"),
        ("System Manager", @"audit|direct database", "Security.AuditTrail"),
        ("System Manager", @"house style", "System.Company"),
        ("System Manager", @"subscription|burst", "Reporting.Schedule"),
        ("System Manager", @"report|snapshot", "Reporting.Report"),
        ("System Manager", @"import", "System.Import"),
        ("System Manager", @"printer", "System.Printer"),
        ("System Manager", @"attachment", "System.Attachment"),
        ("System Manager", @"licence", "System.Licence"),
        ("System Manager", @"quick search", "System.DataHealth"),
        ("System Manager", @"*", "System.Company"),

        ("Weighbridge", @"invoice", "Sales.Invoice"),
        ("Weighbridge", @"*", "Inventory.Weighbridge"),
    ];

    /// <summary>The permission code a screen needs.</summary>
    /// <param name="consoleModule">The console module the screen sits in (e.g. "Customers").</param>
    /// <param name="title">The screen's title.</param>
    /// <returns>The <c>Module.Feature</c> code, or null when the console module has no rules (any signed-in user).</returns>
    public static string? CodeFor(string consoleModule, string title)
    {
        ArgumentNullException.ThrowIfNull(consoleModule);
        ArgumentNullException.ThrowIfNull(title);
        string? fallback = null;
        foreach ((string module, string pattern, string code) in Rules)
        {
            if (!string.Equals(module, consoleModule, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pattern == "*")
            {
                fallback ??= code;
            }
            else if (Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return code;
            }
        }

        return fallback;
    }

    /// <summary>The console modules the rules know.</summary>
    public static IEnumerable<string> ConsoleModules => Rules.Select(r => r.Module).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The operation flags running a screen needs - any one of them: printing a document needs Print; approving or deciding needs an approval
    /// flag; reversing, voiding or cancelling needs a reversal flag; an enquiry needs View; anything else needs a flag that changes data. Only flags
    /// the feature offers count, so what the permission grid can grant is always enough.
    /// </summary>
    /// <param name="title">The screen's title.</param>
    /// <param name="isDocument">Whether the screen prints a document.</param>
    /// <param name="isEnquiry">Whether the screen only looks things up.</param>
    /// <param name="available">The flags the feature offers (<see cref="PermissionDefinition.Available"/>), or null for an unknown feature.</param>
    /// <returns>The flags, any of which lets the screen run.</returns>
    public static OperationFlags NeedsToRun(string title, bool isDocument, bool isEnquiry, OperationFlags? available)
    {
        ArgumentNullException.ThrowIfNull(title);
        OperationFlags offered = available ?? ~OperationFlags.None;
        OperationFlags wanted = isDocument ? OperationFlags.Print | OperationFlags.Reprint
            : ApprovingTitle().IsMatch(title) ? Approving
            : ReversingTitle().IsMatch(title) ? Reversing
            : isEnquiry ? OperationFlags.View
            : AnyWrite;
        OperationFlags needs = wanted & offered;
        if (needs == OperationFlags.None)
        {
            needs = AnyWrite & offered;
        }

        return needs == OperationFlags.None ? OperationFlags.View : needs;
    }

    /// <summary>Whether a user may do something that needs any one of some flags on a feature.</summary>
    /// <param name="permissions">The user's permissions.</param>
    /// <param name="code">The feature code (null: anyone).</param>
    /// <param name="anyOf">The flags, any of which is enough.</param>
    /// <returns>True when allowed.</returns>
    public static bool Allows(EffectivePermissions permissions, string? code, OperationFlags anyOf)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return code is null || permissions.IsSuperAdministrator || (permissions.OperationsFor(code) & anyOf) != OperationFlags.None;
    }

    /// <summary>Whether a user may see a screen at all: its feature's module is licensed and they have any flag on the feature (the grid's View is the usual one).</summary>
    /// <param name="permissions">The user's permissions.</param>
    /// <param name="code">The feature code (null: anyone).</param>
    /// <param name="isLicensed">Whether a licence module is usable (null: do not check the licence).</param>
    /// <returns>True when the screen may be shown.</returns>
    public static bool CanSee(EffectivePermissions permissions, string? code, Func<string, bool>? isLicensed = null)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return code is null || (Unlicensed(code, isLicensed) is null && (permissions.IsSuperAdministrator || permissions.OperationsFor(code) != OperationFlags.None));
    }

    /// <summary>Why a feature cannot be used under the licence, or null when it can (or the feature is unknown).</summary>
    /// <param name="code">The feature code.</param>
    /// <param name="isLicensed">Whether a licence module is usable (null: do not check).</param>
    /// <returns>The refusal, or null.</returns>
    public static string? Unlicensed(string code, Func<string, bool>? isLicensed)
    {
        ArgumentNullException.ThrowIfNull(code);
        PermissionDefinition? feature = PermissionCatalogue.Find(code);
        return isLicensed is null || feature is null || isLicensed(feature.LicenceModule) ? null : $"\"{feature.Name}\" is not licensed ({feature.LicenceModule}).";
    }

    /// <summary>Why a user may not open, run or delete on a screen, or null when they may.</summary>
    /// <param name="permissions">The user's permissions.</param>
    /// <param name="code">The feature code.</param>
    /// <param name="anyOf">The flags needed (any one).</param>
    /// <param name="screen">The screen's title, for the message.</param>
    /// <returns>The refusal, or null.</returns>
    public static string? Refusal(EffectivePermissions permissions, string? code, OperationFlags anyOf, string screen)
    {
        if (Allows(permissions, code, anyOf))
        {
            return null;
        }

        string feature = PermissionCatalogue.Find(code!)?.Name ?? code!;
        string what = anyOf.HasFlag(OperationFlags.View) && (anyOf & AnyWrite) == OperationFlags.None ? "use"
            : (anyOf & Approving) == anyOf ? "approve on"
            : (anyOf & Reversing) == anyOf ? "reverse on"
            : anyOf == OperationFlags.Delete ? "delete on"
            : (anyOf & (OperationFlags.Print | OperationFlags.Reprint)) == anyOf ? "print from"
            : "change data on";
        return $"You have no permission to {what} \"{feature}\" ({screen}). Ask your administrator for the {code} permission.";
    }

    [GeneratedRegex(@"^(approve|decide|authori[sz]e|accept or decline|certify|payment run approval)\b|\bapproval$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApprovingTitle();

    [GeneratedRegex(@"^(reverse|void|cancel|discard|stop|repossess|write off|write-off)\b|\bdishonours\b|\bevictions?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReversingTitle();
}
