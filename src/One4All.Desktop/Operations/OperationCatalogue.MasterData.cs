using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Security;
using One4All.Application.MasterData;
using One4All.Core.Security;
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
            Module = module, Title = "Set up a company for an industry", ListTitle = "Companies",
            Description = "The Industry Setup Wizard: a new company with the chart of accounts, tax codes, warehouses, an open fiscal year and starter roles for the industries chosen (they add up).",
            Fields = [Req("Code", "Company code"), Req("Name", "Name"), Req("Currency", "Base currency", "USD"), Req("Industries", "Industries (" + string.Join(", ", Enum.GetNames<Vertical>()) + ")", "Retail"),
                Today("Start", "Fiscal year starts"), Num("Vat", "VAT rate %", "15"), Tick("Demo", "Add demonstration data")],
            Run = async (c, i) =>
            {
                List<Vertical> verticals = [];
                foreach (string name in i.Text("Industries").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    verticals.Add(Enum.TryParse(name, true, out Vertical v) ? v : throw new OperationException($"Unknown industry {name}."));
                }

                IndustrySetupResult done = Check(await c.Get<IIndustrySetupService>().SetUpCompanyAsync(new IndustrySetupRequest
                {
                    Code = i.Text("Code"), Name = i.Text("Name"), BaseCurrency = CurrencyCode.Parse(i.Text("Currency")), Verticals = verticals, FiscalYearStart = i.Date("Start"),
                    VatRatePercent = i.Number("Vat"), WithDemoData = i.Flag("Demo"),
                }).ConfigureAwait(true));
                return $"Company {done.CompanyId} set up: {done.Accounts} accounts, {done.TaxCodes} tax codes, {done.Warehouses} warehouses, {done.Roles} new roles, {done.DemoRecords} demo records.";
            },
            List = async c => Table(await c.Db.Companies.AsNoTracking().OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Id", x => x.Id), ("Code", x => x.Code), ("Name", x => x.Name), ("Base currency", x => x.BaseCurrency.Value)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add an industry to this company", ListTitle = "Chart of accounts",
            Description = "Adds an industry's accounts, taxes, warehouses and roles to the current company, keeping everything already there.",
            Fields = [Pick("Industry", "Industry", Enum.GetNames<Vertical>()), Num("Vat", "VAT rate %", "15"), Tick("Demo", "Add demonstration data")],
            Run = async (c, i) =>
            {
                IndustrySetupResult done = Check(await c.Get<IIndustrySetupService>().AddVerticalAsync(c.CompanyId, Enum.Parse<Vertical>(i.Text("Industry")), i.Number("Vat"), i.Flag("Demo"))
                    .ConfigureAwait(true));
                return $"Added {done.Accounts} accounts, {done.TaxCodes} tax codes, {done.Warehouses} warehouses, {done.Roles} roles, {done.DemoRecords} demo records.";
            },
            List = async c => Table(await c.Db.Accounts.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.AccountCode).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.AccountCode), ("Name", x => x.Name), ("Type", x => x.Type), ("Control", x => x.ControlAccountType)),
        };
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
        yield return new Operation
        {
            Module = module, Title = "Create a user", ListTitle = "Users and their roles",
            Description = "A login who must change the initial password at first sign-in (8+ characters, letters and digits, not the user name).",
            Fields = [Req("Username", "Login name"), Req("Name", "Display name"), Opt("Email", "E-mail"), Req("Password", "Initial password")],
            Run = async (c, i) => Check(await c.Get<IUserAdminService>().CreateUserAsync(i.Text("Username"), i.Text("Name"), i.Optional("Email"), i.Text("Password")).ConfigureAwait(true),
                id => $"User {i.Text("Username")} created."),
            List = UsersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Reset a password", ListTitle = "Users and their roles", Description = "Sets a new password the user must change at next sign-in, and clears any lockout.",
            Fields = [Req("Username", "Login name"), Req("Password", "New password")],
            Run = async (c, i) => Check(await c.Get<IUserAdminService>().ResetPasswordAsync(await UserAsync(c, i.Text("Username")).ConfigureAwait(true), i.Text("Password")).ConfigureAwait(true), "Password reset."),
            List = UsersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Activate or deactivate a user", ListTitle = "Users and their roles", Description = "A deactivated user cannot sign in; the last system administrator stays active.",
            Fields = [Req("Username", "Login name"), Tick("Active", "Active")],
            Run = async (c, i) => Check(await c.Get<IUserAdminService>().SetActiveAsync(await UserAsync(c, i.Text("Username")).ConfigureAwait(true), i.Flag("Active")).ConfigureAwait(true), "Updated."),
            List = UsersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Create a role", ListTitle = "Roles", Description = "A role, optionally starting from a copy of another role's permissions (edit grants in the permission grid).",
            Fields = [Req("Name", "Role name"), Opt("Description", "Description"), Opt("Copy", "Copy permissions from role")],
            Run = async (c, i) => Check(await c.Get<IUserAdminService>().CreateRoleAsync(i.Text("Name"), i.Optional("Description"),
                i.Optional("Copy") is string copy ? await RoleAsync(c, copy).ConfigureAwait(true) : null).ConfigureAwait(true), id => $"Role {i.Text("Name")} created."),
            List = async c => Table(await c.Db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync().ConfigureAwait(true),
                ("Role", r => r.Name), ("Description", r => r.Description), ("Permissions", r => r.Permissions.Count)),
        };
        yield return new Operation
        {
            Module = module, Title = "Assign a role", ListTitle = "Users and their roles",
            Description = "Gives a user a role, limited by scopes such as Company:ACME;Branch:HO,NORTH (blank: unrestricted). Warns of segregation-of-duties conflicts.",
            Fields = [Req("Username", "Login name"), Req("Role", "Role name"), Opt("Scopes", "Scopes (Dimension:values;...)"), Tick("Remove", "Remove the role instead")],
            Run = async (c, i) =>
            {
                long user = await UserAsync(c, i.Text("Username")).ConfigureAwait(true);
                long role = await RoleAsync(c, i.Text("Role")).ConfigureAwait(true);
                if (i.Flag("Remove"))
                {
                    return Check(await c.Get<IUserAdminService>().RemoveRoleAsync(user, role).ConfigureAwait(true), "Role removed.");
                }

                Dictionary<ScopeDimension, IReadOnlyList<string>> scopes = [];
                foreach (string part in (i.Optional("Scopes") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = part.Split(':', 2, StringSplitOptions.TrimEntries);
                    if (bits.Length != 2 || !Enum.TryParse(bits[0], true, out ScopeDimension dimension))
                    {
                        throw new OperationException($"'{part}' should look like Company:CODE1,CODE2 (dimensions: {string.Join(", ", Enum.GetNames<ScopeDimension>())}).");
                    }

                    scopes[dimension] = [.. bits[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                }

                IReadOnlyList<string> conflicts = Check(await c.Get<IUserAdminService>().AssignRoleAsync(user, role, scopes).ConfigureAwait(true));
                return conflicts.Count == 0 ? "Role assigned." : $"Role assigned. Segregation-of-duties conflicts: {string.Join("; ", conflicts)}.";
            },
            List = UsersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Create a group or add a role to it", ListTitle = "Groups",
            Description = "Groups bundle roles; users who join inherit them. Give a role to add it to the group (creating the group if new).",
            Fields = [Req("Group", "Group name"), Opt("Role", "Role to add")],
            Run = async (c, i) =>
            {
                IUserAdminService admin = c.Get<IUserAdminService>();
                string name = i.Text("Group").Trim();
                string normalized = name.ToUpperInvariant();
                long? existing = await c.Db.Groups.Where(g => g.NormalizedName == normalized).Select(g => (long?)g.Id).FirstOrDefaultAsync().ConfigureAwait(true);
                long group = existing ?? Check(await admin.CreateGroupAsync(name, null).ConfigureAwait(true));
                if (i.Optional("Role") is string roleName)
                {
                    _ = Check(await admin.AddRoleToGroupAsync(group, await RoleAsync(c, roleName).ConfigureAwait(true)).ConfigureAwait(true), string.Empty);
                }

                return existing is null ? $"Group {name} created." : $"Group {name} updated.";
            },
            List = async c =>
            {
                Dictionary<long, string> roles = await c.Db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name).ConfigureAwait(true);
                return Table(await c.Db.Groups.AsNoTracking().Include(g => g.Roles).OrderBy(g => g.Name).ToListAsync().ConfigureAwait(true),
                    ("Group", g => g.Name), ("Roles", g => string.Join(", ", g.Roles.Select(r => roles.GetValueOrDefault(r.RoleId)))));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Join or leave a group", ListTitle = "Users and their roles", Description = "Puts a user in a group (inheriting its roles) or takes them out.",
            Fields = [Req("Username", "Login name"), Req("Group", "Group name"), Tick("Member", "Member")],
            Run = async (c, i) =>
            {
                string normalized = i.Text("Group").Trim().ToUpperInvariant();
                long group = await FindAsync(c.Db.Groups, g => g.NormalizedName == normalized, g => g.Id, "group", i.Text("Group")).ConfigureAwait(true);
                IReadOnlyList<string> conflicts = Check(await c.Get<IUserAdminService>().SetGroupMembershipAsync(await UserAsync(c, i.Text("Username")).ConfigureAwait(true), group, i.Flag("Member"))
                    .ConfigureAwait(true));
                return conflicts.Count == 0 ? "Membership updated." : $"Membership updated. Segregation-of-duties conflicts: {string.Join("; ", conflicts)}.";
            },
            List = UsersAsync,
        };
    }

    private static Task<long> RoleAsync(OperationContext c, string name)
    {
        string normalized = name.Trim().ToUpperInvariant();
        return FindAsync(c.Db.Roles, r => r.NormalizedName == normalized, r => r.Id, "role", name);
    }

    private static async Task<DataTable> UsersAsync(OperationContext c)
    {
        Dictionary<long, string> roles = await c.Db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name).ConfigureAwait(true);
        return Table(await c.Db.Users.AsNoTracking().Include(u => u.Roles).ThenInclude(r => r.Scopes).OrderBy(u => u.Username).ToListAsync().ConfigureAwait(true),
            ("User", u => u.Username), ("Name", u => u.DisplayName), ("Active", u => u.IsActive), ("Administrator", u => u.IsSystemAdministrator),
            ("Roles", u => string.Join("; ", u.Roles.Select(r => roles.GetValueOrDefault(r.RoleId) + (r.Scopes.Count == 0 ? string.Empty
                : " (" + string.Join(", ", r.Scopes.Select(s => $"{s.Dimension}: {string.Join("/", s.Values)}")) + ")")))));
    }
}
