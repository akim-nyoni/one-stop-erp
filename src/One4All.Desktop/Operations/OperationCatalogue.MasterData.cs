using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.MasterData;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.System;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>System Manager operations: creating companies, the chart of accounts and the other master data.</content>
public static partial class OperationCatalogue
{
    private const string NoControl = "(not a control account)";

    private static IEnumerable<Operation> MasterData()
    {
        const string module = "System Manager";
        yield return new Operation
        {
            Module = module, Title = "Create a company", ListTitle = "Companies", Description = "A new company with a head-office branch, held to the licence's company limit.",
            Fields = [Req("Code", "Company code"), Req("Name", "Name"), Req("Currency", "Base currency", "USD"), Req("TimeZone", "Time zone", "UTC")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateCompanyAsync(i.Text("Code"), i.Text("Name"), CurrencyCode.Parse(i.Text("Currency")), i.Text("TimeZone"))
                .ConfigureAwait(true), id => $"Company {id} created."),
            List = async c => Table(await c.Db.Companies.AsNoTracking().OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Id", x => x.Id), ("Code", x => x.Code), ("Name", x => x.Name), ("Base currency", x => x.BaseCurrency.Value), ("Time zone", x => x.TimeZoneId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a branch", ListTitle = "Branches", Description = "A branch or site of the current company.",
            Fields = [Req("Code", "Code"), Req("Name", "Name")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateBranchAsync(c.CompanyId, i.Text("Code"), i.Text("Name")).ConfigureAwait(true), id => "Branch added."),
            List = async c => Table(await c.Db.Branches.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Head office", x => x.IsHeadOffice)),
        };
        yield return new Operation
        {
            Module = module, Title = "Create a fiscal year", ListTitle = "Fiscal years", Description = "Monthly periods from the start date; tick to open them for every ledger at once.",
            Fields = [Req("Code", "Year code"), Today("Start", "First day"), Num("Months", "Periods", "12"), Tick("Open", "Open all periods now")],
            Run = async (c, i) =>
            {
                long year = Check(await c.Get<IMasterDataService>().CreateFiscalYearAsync(c.CompanyId, i.Text("Code"), i.Date("Start"), i.Whole("Months")).ConfigureAwait(true));
                if (i.Flag("Open"))
                {
                    _ = Check(await c.Get<IPeriodService>().OpenYearAsync(c.CompanyId, year, ["GL", "AR", "AP", "CB", "INV", "POP", "SOP", "POS", "VOU", "FA", "PAY", "JOB", "MUNI", "HSE", "SCH", "BUD"])
                        .ConfigureAwait(true), string.Empty);
                }

                return $"Fiscal year {i.Text("Code").ToUpperInvariant()} created{(i.Flag("Open") ? " and opened" : string.Empty)}.";
            },
            List = async c => Table(await c.Db.FiscalYears.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.StartDate).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("From", x => D(x.StartDate)), ("To", x => D(x.EndDate)), ("Closed", x => x.IsClosed)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a ledger account", ListTitle = "Chart of accounts", Description = "A ledger account; a control account is posted only by its own ledger.",
            Fields = [Req("Code", "Account code (up to 8)"), Req("Name", "Name"), Pick("Type", "Type", Enum.GetNames<AccountType>()),
                Pick("Control", "Control account", [NoControl, .. Enum.GetNames<ControlAccountType>()]), Opt("Category", "Report category")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateAccountAsync(new NewAccount(c.CompanyId, i.Text("Code"), i.Text("Name"), Enum.Parse<AccountType>(i.Text("Type")),
                i.Text("Control") == NoControl ? null : Enum.Parse<ControlAccountType>(i.Text("Control")), i.Optional("Category"))).ConfigureAwait(true), id => "Account added."),
            List = async c => Table(await c.Db.Accounts.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.AccountCode).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.AccountCode), ("Name", x => x.Name), ("Type", x => x.Type), ("Control", x => x.ControlAccountType), ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a tax code", ListTitle = "Tax codes", Description = "A tax code with its first rate and the accounts it posts to.",
            Fields = [Req("Code", "Code"), Req("Name", "Name"), Pick("Type", "Type", Enum.GetNames<TaxType>()), Num("Rate", "Rate %"), Today("From", "Rate from"),
                Opt("Output", "Output / payable account"), Opt("Input", "Input account")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateTaxCodeAsync(new NewTaxCode(c.CompanyId, i.Text("Code"), i.Text("Name"), Enum.Parse<TaxType>(i.Text("Type")),
                i.Number("Rate"), i.Date("From"), i.Optional("Output"), i.Optional("Input"))).ConfigureAwait(true), id => "Tax code added."),
            List = async c => Table(await c.Db.TaxCodes.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Type", x => x.Type), ("Output", x => x.OutputAccountCode), ("Input", x => x.InputAccountCode), ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a currency", ListTitle = "Currencies", Description = "An ISO currency the system may trade or hold balances in.",
            Fields = [Req("Code", "ISO code"), Req("Name", "Name"), Opt("Symbol", "Symbol"), Num("Places", "Decimal places", "2")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateCurrencyAsync(CurrencyCode.Parse(i.Text("Code")), i.Text("Name"), i.Optional("Symbol") ?? string.Empty, i.Whole("Places"))
                .ConfigureAwait(true), "Currency added."),
            List = async c => Table(await c.Db.Currencies.AsNoTracking().ToListAsync().ConfigureAwait(true), ("Code", x => x.Code.Value), ("Name", x => x.Name), ("Symbol", x => x.Symbol),
                ("Places", x => x.DecimalPlaces)),
        };
        yield return new Operation
        {
            Module = module, Title = "Set an exchange rate", ListTitle = "Exchange rates (latest 200)", Description = "A rate from a date; one already set for that day and type is replaced.",
            Fields = [Req("From", "From currency"), Req("To", "To currency"), Pick("Type", "Rate type", Enum.GetNames<ExchangeRateType>()), Today(), Num("Rate", "Rate")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().SetExchangeRateAsync(CurrencyCode.Parse(i.Text("From")), CurrencyCode.Parse(i.Text("To")),
                Enum.Parse<ExchangeRateType>(i.Text("Type")), i.Date("Date"), i.Number("Rate")).ConfigureAwait(true), "Rate set."),
            List = async c => Table(await c.Db.ExchangeRates.AsNoTracking().OrderByDescending(x => x.EffectiveFrom).Take(200).ToListAsync().ConfigureAwait(true),
                ("From", x => x.FromCurrency.Value), ("To", x => x.ToCurrency.Value), ("Type", x => x.RateType), ("Effective", x => D(x.EffectiveFrom)), ("Rate", x => x.Rate.ToString("0.######", CultureInfo.InvariantCulture))),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a warehouse", ListTitle = "Warehouses", Description = "A stock location of the current company.",
            Fields = [Req("Code", "Code"), Req("Name", "Name")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateWarehouseAsync(c.CompanyId, i.Text("Code"), i.Text("Name")).ConfigureAwait(true), id => "Warehouse added."),
            List = async c => Table(await c.Db.Warehouses.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a cost centre or department", ListTitle = "Cost centres and departments", Description = "GL analysis codes of up to three characters.",
            Fields = [Pick("Kind", "Kind", ["Cost centre", "Department"]), Req("Code", "Code (up to 3)"), Req("Name", "Name")],
            Run = async (c, i) => Check(i.Text("Kind") == "Department"
                ? await c.Get<IMasterDataService>().CreateDepartmentAsync(c.CompanyId, i.Text("Code"), i.Text("Name")).ConfigureAwait(true)
                : await c.Get<IMasterDataService>().CreateCostCentreAsync(c.CompanyId, i.Text("Code"), i.Text("Name")).ConfigureAwait(true), id => $"{i.Text("Kind")} added."),
            List = async c =>
            {
                var centres = (await c.Db.CostCentres.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true)).Select(x => (Kind: "Cost centre", x.Code, x.Name));
                var departments = (await c.Db.Departments.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true)).Select(x => (Kind: "Department", x.Code, x.Name));
                return Table(centres.Concat(departments).OrderBy(x => x.Kind).ThenBy(x => x.Code), ("Kind", x => x.Kind), ("Code", x => x.Code), ("Name", x => x.Name));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Add a customer", ListTitle = "Customers", Description = "A customer account; raise the credit limit later through approval.",
            Fields = [Req("Code", "Account code"), Req("Name", "Name"), Req("Currency", "Currency", "USD"), Num("Limit", "Credit limit", "0")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateCustomerAsync(c.CompanyId, i.Text("Code"), i.Text("Name"), CurrencyCode.Parse(i.Text("Currency")), i.Number("Limit"))
                .ConfigureAwait(true), id => "Customer added."),
            List = async c => Table(await c.Db.Customers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Currency", x => x.Currency.Value), ("Credit limit", x => M(x.CreditLimit))),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a supplier", ListTitle = "Suppliers", Description = "A supplier account.",
            Fields = [Req("Code", "Account code"), Req("Name", "Name"), Req("Currency", "Currency", "USD")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateSupplierAsync(c.CompanyId, i.Text("Code"), i.Text("Name"), CurrencyCode.Parse(i.Text("Currency"))).ConfigureAwait(true),
                id => "Supplier added."),
            List = async c => Table(await c.Db.Suppliers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Currency", x => x.Currency.Value)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add an item", ListTitle = "Items", Description = "A stock, service or other item with the accounts its sales and cost post to.",
            Fields = [Req("Code", "Item code"), Req("Description", "Description"), Pick("Type", "Type", Enum.GetNames<ItemType>()), Opt("Revenue", "Revenue account"), Opt("Cost", "Cost of sales account"),
                OptNum("Standard", "Standard cost")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateItemAsync(new NewItem(c.CompanyId, i.Text("Code"), i.Text("Description"), Enum.Parse<ItemType>(i.Text("Type")),
                i.Optional("Revenue"), i.Optional("Cost"), i.OptionalNumber("Standard"))).ConfigureAwait(true), id => "Item added."),
            List = async c => Table(await c.Db.Items.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).Take(500).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Description", x => x.Description), ("Type", x => x.ItemType), ("Revenue", x => x.RevenueAccountCode), ("Cost of sales", x => x.CostOfSalesAccountCode)),
        };
    }
}
