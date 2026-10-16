using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Core.FixedAssets;
using One4All.Core.Housing;
using One4All.Core.Inventory;
using One4All.Core.Municipal;
using One4All.Core.Payroll;
using One4All.Core.Receipting;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.MasterData;

/// <summary>
/// Loads the sample records (see <see cref="ISampleDataService"/>). Everything that posts goes through the real services, so the sample books
/// balance and every rule holds; plain registers (properties, meters, stands, applicants) are written directly. Dates fall inside the
/// company's latest fiscal year, ending at today, so the records look current whenever they are loaded.
/// </summary>
public sealed class SampleDataService : ISampleDataService
{
    private const string Prefix = "SMP-";

    private readonly One4AllDbContext _db;
    private readonly IMasterDataService _master;
    private readonly IInventoryService _inventory;
    private readonly ISalesLedgerService _sales;
    private readonly IPurchaseLedgerService _purchases;
    private readonly IPayrollSetupService _payroll;
    private readonly IFixedAssetService _assets;
    private readonly IPricingService _pricing;

    /// <summary>Creates the loader.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="master">Creates the master records.</param>
    /// <param name="inventory">Receives the stock.</param>
    /// <param name="sales">Posts the invoices and receipts.</param>
    /// <param name="purchases">Posts the supplier invoices and the payment.</param>
    /// <param name="payroll">Sets up the payroll and the employees.</param>
    /// <param name="assets">Registers the assets.</param>
    public SampleDataService(One4AllDbContext db, IMasterDataService master, IInventoryService inventory, ISalesLedgerService sales, IPurchaseLedgerService purchases,
        IPayrollSetupService payroll, IFixedAssetService assets, IPricingService pricing)
    {
        _pricing = pricing;
        _db = db;
        _master = master;
        _inventory = inventory;
        _sales = sales;
        _purchases = purchases;
        _payroll = payroll;
        _assets = assets;
    }

    /// <inheritdoc />
    public Task<Result<SampleDataSummary>> LoadAsync(long companyId, CancellationToken ct = default)
        => _db.AtomicAsync(() => LoadCoreAsync(companyId, ct), ct); // all or nothing: a refused record leaves no half-loaded company behind

    private async Task<Result<SampleDataSummary>> LoadCoreAsync(long companyId, CancellationToken ct)
    {
        Company? company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Sample.CompanyNotFound", $"Company {companyId} not found.");
        }

        if (await _db.Customers.AnyAsync(c => c.CompanyId == companyId && c.Code.StartsWith(Prefix), ct).ConfigureAwait(false))
        {
            return Error.Rule("Sample.AlreadyLoaded", "This company already has the sample data (customers SMP-…). Load it into a fresh company instead.");
        }

        FiscalYear? year = await _db.FiscalYears.Where(y => y.CompanyId == companyId).OrderByDescending(y => y.StartDate).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (year is null)
        {
            return Error.Rule("Sample.NoFiscalYear", "The company has no fiscal year yet: set it up for an industry first (Administration › Company).");
        }

        HashSet<string> accounts = [.. await _db.Accounts.Where(a => a.CompanyId == companyId).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)];
        foreach (string needed in new[] { "1000", "1100", "1200", "2100", "4000", "5000", "6000", "6200" })
        {
            if (!accounts.Contains(needed))
            {
                return Error.Rule("Sample.ChartIncomplete", $"The chart of accounts has no account {needed}: set the company up for an industry first so the standard chart is there.");
            }
        }

        string Account(string wanted, string fallback) => accounts.Contains(wanted) ? wanted : fallback;
        CurrencyCode currency = company.BaseCurrency;
        Dates dates = new(year.StartDate, year.EndDate);
        List<string> loaded = [];

        // ---- Reference: a store, a bank account, a cashier station.
        long warehouseId = await _db.Warehouses.Where(w => w.CompanyId == companyId).OrderBy(w => w.Id).Select(w => (long?)w.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? 0;
        if (warehouseId == 0)
        {
            Result<long> store = await _master.CreateWarehouseAsync(companyId, "MAIN", "Main Store", ct).ConfigureAwait(false);
            if (store.IsFailure)
            {
                return store.Error;
            }

            warehouseId = store.Value;
            loaded.Add("1 warehouse (MAIN)");
        }

        if (!await _db.BankAccounts.AnyAsync(b => b.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            _db.BankAccounts.Add(new BankAccount(companyId, "MAIN", "Main Bank Account", currency, "1100"));
            loaded.Add("1 bank account (MAIN, GL 1100)");
        }

        if (!await _db.CashierStations.AnyAsync(s => s.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            _db.CashierStations.Add(new CashierStation(companyId, "CASH1", "Counter 1", "RC"));
            _db.CashierStations.Add(new CashierStation(companyId, "FLD1", "Field collector — markets", "FC") { IsFieldCollector = true, RouteCode = "R-NORTH", ReceiptRangeFrom = 1, ReceiptRangeTo = 500 });
            loaded.Add("1 cashier station (CASH1), 1 field collector (FLD1, route R-NORTH, receipts 1–500)");
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // ---- Products, with stock on hand.
        // Categories are the catalogue's own codes where it has them (the till filters by them); the selling price goes on a default price list.
        HashSet<string> categories = [.. await _db.ItemCategories.Where(x => x.CompanyId == companyId).Select(x => x.Code).ToListAsync(ct).ConfigureAwait(false)];
        string Cat(string wanted, string fallback) => categories.Contains(wanted) ? wanted : categories.Contains(fallback) ? fallback : wanted;
        (string Code, string Name, decimal Cost, decimal Price, int Qty, string Category, string Barcode)[] products =
        [
            ("SMP-BREAD", "Bread, white loaf", 0.85m, 1.20m, 120, Cat("GROC", "GEN"), "6001001"), ("SMP-MILK", "Milk 1 litre", 1.10m, 1.50m, 80, Cat("GROC", "GEN"), "6001002"),
            ("SMP-OIL", "Cooking oil 2 litre", 3.60m, 4.75m, 40, Cat("GROC", "GEN"), "6001003"), ("SMP-SOAP", "Bath soap 250g", 0.70m, 1.00m, 150, Cat("PERS", "GEN"), "6001004"),
            ("SMP-CEMENT", "Cement 50 kg", 9.50m, 12.50m, 60, Cat("BLDG", "GEN"), "6002001"), ("SMP-BRICK", "Common brick", 0.12m, 0.18m, 5000, Cat("BLDG", "GEN"), "6002002"),
            ("SMP-PIPE", "PVC pipe 110 mm, 6 m", 11.00m, 15.00m, 25, Cat("BLDG", "GEN"), "6002003"), ("SMP-BOOK", "Exercise book 72 pages", 0.45m, 0.65m, 300, Cat("STAT", "GEN"), "6003001"),
        ];
        long priceListId = await _db.PriceLists.Where(p => p.CompanyId == companyId && p.IsDefault).Select(p => (long?)p.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? 0;
        if (priceListId == 0)
        {
            Result<long> list = await _pricing.CreatePriceListAsync(companyId, "RETAIL", "Retail selling prices", currency, true, ct).ConfigureAwait(false);
            if (list.IsFailure)
            {
                return Step("price list", list.Error);
            }

            priceListId = list.Value;
            loaded.Add("1 default price list (RETAIL)");
        }

        foreach ((string code, string name, decimal cost, decimal price, int qty, string category, string barcode) in products)
        {
            Result<long> item = await _master.CreateItemAsync(new NewItem(companyId, code, name, ItemType.Stock, "4000", "5000", cost, Category: category), ct).ConfigureAwait(false);
            if (item.IsFailure)
            {
                return Step("product " + code, item.Error);
            }

            Item record = await _db.Items.SingleAsync(i => i.Id == item.Value, ct).ConfigureAwait(false);
            record.Barcode = barcode;
            record.ReorderLevel = qty / 4;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            Result priced = await _pricing.SetPriceAsync(companyId, priceListId, item.Value, 1m, price, ct).ConfigureAwait(false);
            if (priced.IsFailure)
            {
                return Step("price of " + code, priced.Error);
            }

            Result<StockMovementResult> received = await _inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = companyId, ItemId = item.Value, WarehouseId = warehouseId, Date = dates.DaysAgo(60), Quantity = qty, UnitCost = cost, CreditAccountCode = "1100",
            }, ct).ConfigureAwait(false);
            if (received.IsFailure)
            {
                return Step("stock of " + code, received.Error);
            }
        }

        loaded.Add($"{products.Length} products with selling prices and stock on hand");

        // ---- Customers, with invoices (some overdue) and receipts.
        (string Code, string Name, string Email, string Mobile, decimal Limit)[] customers =
        [
            // Limits sit above each customer's invoices (the oldest-first invoices add up to at most ~1,000); C005 is kept close to its limit for credit control to show.
            ("SMP-C001", "Tendai Moyo", "tendai.moyo@example.com", "+263771000001", 1_000m), ("SMP-C002", "Rudo Chikwanda", "rudo.c@example.com", "+263771000002", 1_200m),
            ("SMP-C003", "Blessing Ncube", "blessing.n@example.com", "+263771000003", 1_500m), ("SMP-C004", "Farai Dube", "", "+263771000004", 1_000m),
            ("SMP-C005", "Chipo Mutasa", "chipo.m@example.com", "", 300m), ("SMP-C006", "Nyasha Sibanda", "nyasha.s@example.com", "+263771000006", 2_000m),
            ("SMP-C007", "Tatenda Mhlanga", "", "", 1_200m), ("SMP-C008", "Vimbai Gumbo", "vimbai.g@example.com", "+263771000008", 1_500m),
            ("SMP-C009", "Greenfield Hardware (Pvt) Ltd", "accounts@greenfield.example.com", "+263242000009", 5_000m), ("SMP-C010", "Ministry of Health", "payments@health.example.gov", "", 20_000m),
        ];
        List<long> customerIds = [];
        int invoices = 0, receipts = 0;
        for (int n = 0; n < customers.Length; n++)
        {
            (string code, string name, string email, string mobile, decimal limit) = customers[n];
            Result<long> customer = await _master.CreateCustomerAsync(companyId, code, name, currency, limit, ct).ConfigureAwait(false);
            if (customer.IsFailure)
            {
                return Step("customer " + code, customer.Error);
            }

            customerIds.Add(customer.Value);
            Customer record = await _db.Customers.SingleAsync(c => c.Id == customer.Value, ct).ConfigureAwait(false);
            record.Email = email.Length == 0 ? null : email;
            record.MobileNumber = mobile.Length == 0 ? null : mobile;
            record.CustomerType = n >= 8 ? "Business" : "Household";
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            // One to three invoices, the oldest well overdue; receipts settle some in full and some in part.
            decimal[] amounts = [45m + (n * 12.5m), 120m + (n * 20m), 300m + (n * 55m)];
            int count = 1 + (n % 3);
            for (int k = 0; k < count; k++)
            {
                invoices++;
                Result<TradingPostingResult> invoice = await _sales.PostInvoiceAsync(new SalesInvoiceRequest
                {
                    CompanyId = companyId, CustomerId = customer.Value, DocumentNumber = $"SMP-INV-{invoices:000}", DocumentDate = dates.DaysAgo(110 - (k * 35) - n),
                    Currency = currency, NetAmount = amounts[k], SalesAccountCode = "4000", Narrative = k == 0 ? "Goods sold on account" : "Monthly account",
                }, ct).ConfigureAwait(false);
                if (invoice.IsFailure)
                {
                    return Step($"invoice {invoices} for {code}", invoice.Error);
                }
            }

            if (n % 2 == 0)
            {
                receipts++;
                decimal paid = n % 4 == 0 ? amounts[0] : Math.Round(amounts[0] / 2m, 2);
                Result<TradingPostingResult> receipt = await _sales.PostReceiptAsync(new ReceiptRequest
                {
                    CompanyId = companyId, CustomerId = customer.Value, DocumentNumber = $"SMP-RCT-{receipts:000}", ReceiptDate = dates.DaysAgo(20 - n), Currency = currency,
                    Amount = paid, BankAccountCode = "1100", Narrative = "Payment received, thank you",
                }, ct).ConfigureAwait(false);
                if (receipt.IsFailure)
                {
                    return Step($"receipt for {code}", receipt.Error);
                }
            }
        }

        loaded.Add($"{customers.Length} customers, {invoices} invoices, {receipts} receipts");

        // ---- Suppliers, with invoices and one payment.
        (string Code, string Name, string Account, decimal Amount)[] suppliers =
        [
            ("SMP-S001", "Harare Wholesalers", Account("1400", "5000"), 1_850m), ("SMP-S002", "ZESA Holdings (electricity)", "6200", 420m),
            ("SMP-S003", "City Stationers", "6200", 96.50m), ("SMP-S004", "Mashonaland Transport", "6200", 310m), ("SMP-S005", "Office Rentals (Pvt) Ltd", Account("6100", "6200"), 1_200m),
        ];
        for (int n = 0; n < suppliers.Length; n++)
        {
            (string code, string name, string account, decimal amount) = suppliers[n];
            Result<long> supplier = await _master.CreateSupplierAsync(companyId, code, name, currency, ct).ConfigureAwait(false);
            if (supplier.IsFailure)
            {
                return Step("supplier " + code, supplier.Error);
            }

            Result<TradingPostingResult> invoice = await _purchases.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplier.Value, DocumentNumber = $"SMP-PINV-{n + 1:000}", DocumentDate = dates.DaysAgo(45 - (n * 7)), Currency = currency,
                NetAmount = amount, ExpenseAccountCode = account, Narrative = name,
            }, ct).ConfigureAwait(false);
            if (invoice.IsFailure)
            {
                return Step("supplier invoice for " + code, invoice.Error);
            }

            if (n == 1)
            {
                Result<TradingPostingResult> payment = await _purchases.PostPaymentAsync(new PaymentRequest
                {
                    CompanyId = companyId, SupplierId = supplier.Value, DocumentNumber = "SMP-PAY-001", PaymentDate = dates.DaysAgo(10), Currency = currency, Amount = amount, BankAccountCode = "1100",
                }, ct).ConfigureAwait(false);
                if (payment.IsFailure)
                {
                    return Step("payment to " + code, payment.Error);
                }
            }
        }

        loaded.Add($"{suppliers.Length} suppliers with invoices, 1 payment");

        // ---- Payroll: a tax table, the standard pay elements and the employees.
        if (!await _db.Set<TaxTable>().AnyAsync(t => t.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            Result<long> table = await _payroll.CreateTaxTableAsync(companyId, "PAYE", year.StartDate, null,
                [new TaxBandInput(0m, 300m, 0m), new TaxBandInput(300m, 1_000m, 20m), new TaxBandInput(1_000m, 3_000m, 30m), new TaxBandInput(3_000m, null, 40m)], ct).ConfigureAwait(false);
            if (table.IsFailure)
            {
                return Step("tax table", table.Error);
            }

            loaded.Add("1 PAYE tax table");
        }

        string taxTable = await _db.Set<TaxTable>().Where(t => t.CompanyId == companyId).OrderByDescending(t => t.EffectiveFrom).Select(t => t.Code).FirstAsync(ct).ConfigureAwait(false);
        if (!await _db.PayElements.AnyAsync(e => e.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            Result<IReadOnlyList<string>> elements = await _payroll.SeedStandardElementsAsync(companyId, "6000", Account("2400", "2300"), taxTable, ct).ConfigureAwait(false);
            if (elements.IsFailure)
            {
                return Step("pay elements", elements.Error);
            }

            loaded.Add($"{elements.Value.Count} standard pay elements");
        }

        // Departments are the ledger's three-letter analysis codes.
        foreach ((string code, string name) in new[] { ("FIN", "Finance"), ("STO", "Stores"), ("ADM", "Administration"), ("WKS", "Works") })
        {
            if (!await _db.Set<Department>().AnyAsync(d => d.CompanyId == companyId && d.Code == code, ct).ConfigureAwait(false))
            {
                Result<long> department = await _master.CreateDepartmentAsync(companyId, code, name, ct).ConfigureAwait(false);
                if (department.IsFailure)
                {
                    return Step("department " + code, department.Error);
                }
            }
        }

        (string Number, string Name, decimal Basic, string Department, PayMethod Method, Gender Gender, int BirthYear)[] employees =
        [
            ("SMP-E001", "Grace Mapfumo", 2_400m, "FIN", PayMethod.BankTransfer, Gender.Female, 1984), ("SMP-E002", "Kudzai Banda", 1_650m, "FIN", PayMethod.BankTransfer, Gender.Male, 1991),
            ("SMP-E003", "Precious Zulu", 950m, "STO", PayMethod.Cash, Gender.Female, 1996), ("SMP-E004", "Simba Chirwa", 1_100m, "STO", PayMethod.MobileMoney, Gender.Male, 1989),
            ("SMP-E005", "Lindiwe Nkomo", 3_200m, "ADM", PayMethod.BankTransfer, Gender.Female, 1979), ("SMP-E006", "Tapiwa Shoko", 780m, "WKS", PayMethod.Cash, Gender.Male, 1999),
            ("SMP-E007", "Anesu Makoni", 820m, "WKS", PayMethod.Cash, Gender.Male, 1994), ("SMP-E008", "Rufaro Chigumba", 1_900m, "ADM", PayMethod.BankTransfer, Gender.Female, 1987),
        ];
        for (int n = 0; n < employees.Length; n++)
        {
            (string number, string name, decimal basic, string department, PayMethod method, Gender gender, int birthYear) = employees[n];
            Result<long> employee = await _payroll.CreateEmployeeAsync(companyId, new NewEmployee(number, name, basic, year.StartDate.AddMonths(-(n * 7) - 3))
            {
                Department = department, PaymentMethod = method, Gender = gender, DateOfBirth = new DateOnly(birthYear, 1 + (n % 12), 1 + n), NationalIdNumber = $"63-{100000 + (n * 731)}-A-{10 + n}",
                EmploymentType = n == 6 ? EmploymentType.Contract : EmploymentType.Permanent, ContractEndDate = n == 6 ? year.EndDate : null, BankName = method == PayMethod.BankTransfer ? "CBZ Bank" : null,
                BankAccountNumber = method == PayMethod.BankTransfer ? $"0100{200000 + (n * 917)}" : null, MobileMoneyNumber = method == PayMethod.MobileMoney ? "+263771000104" : null,
            }, ct).ConfigureAwait(false);
            if (employee.IsFailure)
            {
                return Step("employee " + number, employee.Error);
            }
        }

        loaded.Add($"{employees.Length} employees");

        // ---- Rated properties with services, water meters and three months of readings, for the first eight customers.
        Tariff rates = new(companyId, "SMP-RATES", "Property rates (domestic)", ServiceType.Rates, TariffType.RatePerValuation, year.StartDate, Account("4300", "4000")) { RatePerValuationCents = 0.08m, MinimumCharge = 5m };
        Tariff water = new(companyId, "SMP-WATER", "Water (metered)", ServiceType.Water, TariffType.PerUnit, year.StartDate, Account("4310", "4000")) { PerUnitRate = 0.85m, FixedCharge = 4m };
        Tariff refuse = new(companyId, "SMP-REFUSE", "Refuse removal (weekly)", ServiceType.Refuse, TariffType.Flat, year.StartDate, Account("4320", "4000")) { FixedCharge = 12m };
        _db.Tariffs.AddRange(rates, water, refuse);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        string[] wards = ["Ward 3", "Ward 3", "Ward 5", "Ward 5", "Ward 5", "Ward 7", "Ward 7", "Ward 9"];
        string[] suburbs = ["Highfield", "Highfield", "Mabvuku", "Mabvuku", "Mabvuku", "Avondale", "Avondale", "Borrowdale"];
        int readings = 0;
        for (int n = 0; n < 8; n++)
        {
            Property property = new(companyId, $"{1000 + (n * 37)}", wards[n], customerIds[n])
            {
                Address = $"{12 + (n * 3)} {suburbs[n]} Road", Suburb = suburbs[n], SizeSquareMetres = 300m + (n * 150m), Valuation = 18_000m + (n * 9_500m), LandUse = n == 7 ? "Commercial" : "Residential",
            };
            property.AddService(ServiceType.Rates, rates.Id, year.StartDate);
            property.AddService(ServiceType.Refuse, refuse.Id, year.StartDate);
            Meter meter = new(companyId, $"SMP-W{n + 1:000}") { IsActive = true, MeterType = "Water", SizeMm = 15, Location = "Front boundary", InstalledOn = year.StartDate };
            meter.Start(1_000m + (n * 250m), year.StartDate, 0m);
            _db.Meters.Add(meter);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            property.AddService(ServiceType.Water, water.Id, year.StartDate).MeterId = meter.Id;
            _db.Properties.Add(property);
            decimal reading = 1_000m + (n * 250m);
            for (int month = 3; month >= 1; month--)
            {
                decimal previous = reading;
                reading += 12m + (n * 2m) + (month * 1.5m);
                DateOnly on = dates.DaysAgo(month * 30);
                decimal consumption = meter.RecordReading(reading, on);
                _db.MeterReadings.Add(new MeterReading(companyId, meter.Id, on, reading, previous, consumption));
                readings++;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        loaded.Add($"3 tariffs, 8 properties with rates, refuse and metered water, {readings} meter readings");

        // ---- A housing scheme: the land, twelve stands and eight applicants on the waiting list.
        HousingScheme scheme = new(companyId, "SMP-SUNRISE", "Sunrise Park housing scheme", SchemeType.CouncilDelivered) { Ward = "Ward 5", TargetBeneficiaries = 120, StartDate = year.StartDate };
        LandParcel parcel = new(companyId, "SMP-LP-001", "Sunrise Park, Ward 5 (remainder of Lot 12)", 48_000m, 96_000m);
        _db.HousingSchemes.Add(scheme);
        _db.LandParcels.Add(parcel);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        for (int n = 1; n <= 12; n++)
        {
            _db.HousingStands.Add(new HousingStand(companyId, parcel.Id, scheme.Id, $"SR-{n:000}", n <= 8 ? 300m : 450m, n <= 8 ? 2_400m : 3_600m)
            {
                Ward = "Ward 5", Suburb = "Sunrise Park", StreetAddress = $"{n} Dawn Crescent", IntendedUse = "Residential", Zoning = n <= 8 ? "High density" : "Medium density",
                HasWater = n <= 6, HasSewer = n <= 6, HasRoad = true, ServicingPhase = n <= 6 ? "Phase 1" : "Phase 2",
            });
        }

        (string Id, string Name, Gender Gender, string Employer, int YearsAgo)[] applicants =
        [
            ("63-221144-B-21", "Memory Chidziva", Gender.Female, "Harare City Council", 6), ("63-301922-C-42", "Takudzwa Mujuru", Gender.Male, "Self-employed", 5),
            ("63-118877-A-07", "Fungai Mawire", Gender.Female, "Ministry of Education", 4), ("63-442190-D-15", "Tonderai Hove", Gender.Male, "ZESA Holdings", 3),
            ("63-556701-A-33", "Patience Nyoni", Gender.Female, "Unemployed", 3), ("63-609318-B-18", "Munyaradzi Phiri", Gender.Male, "Delta Beverages", 2),
            ("63-702255-C-09", "Sekai Marufu", Gender.Female, "Nurse, Parirenyatwa", 1), ("63-815433-A-26", "Tawanda Gono", Gender.Male, "Informal trader", 1),
        ];
        foreach ((string id, string name, Gender gender, string employer, int yearsAgo) in applicants)
        {
            _db.HousingApplicants.Add(new HousingApplicant(companyId, id, name, dates.Clamp(DateOnly.FromDateTime(DateTime.Today).AddYears(-yearsAgo)))
            {
                Gender = gender, Employer = employer, Phone = $"+26377{Random.Shared.Next(1_000_000, 9_999_999)}", PhysicalAddress = "Lodging, Mbare",
            });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        loaded.Add("1 housing scheme, 1 land parcel, 12 stands, 8 waiting-list applicants");

        // ---- Fixed assets: a class and three vehicles / machines.
        if (accounts.Contains("1600") && accounts.Contains("1650") && accounts.Contains("6800"))
        {
            Result saved = await _assets.SaveClassAsync(new AssetClassRequest(companyId, "VEH", "Vehicles and plant", DepreciationMethod.StraightLine, 60, 10m, 0m, "1600", "1650", "6800", null), ct).ConfigureAwait(false);
            if (saved.IsFailure)
            {
                return Step("asset class", saved.Error);
            }

            (string Number, string Name, decimal Cost)[] vehicles = [("SMP-A001", "Toyota Hilux bakkie (ABC 1234)", 28_000m), ("SMP-A002", "Refuse compactor truck", 95_000m), ("SMP-A003", "Honda generator 7.5 kVA", 2_600m)];
            foreach ((string number, string name, decimal cost) in vehicles)
            {
                Result<long> asset = await _assets.RegisterAsync(new RegisterAssetRequest
                {
                    CompanyId = companyId, AssetNumber = number, Description = name, AssetClass = "VEH", Cost = cost, ResidualValue = Math.Round(cost / 10m, 2), AcquisitionDate = dates.DaysAgo(90),
                    Method = DepreciationMethod.StraightLine, UsefulLifeMonths = 60, CostAccountCode = "1600", AccumulatedDepreciationAccountCode = "1650", DepreciationExpenseAccountCode = "6800",
                    FundingAccountCode = "1100", DocumentNumber = number,
                }, ct).ConfigureAwait(false);
                if (asset.IsFailure)
                {
                    return Step("asset " + number, asset.Error);
                }
            }

            loaded.Add("1 asset class, 3 assets");
        }

        return new SampleDataSummary(companyId, loaded);
    }

    private static Error Step(string what, Error error) => Error.Rule("Sample.Failed", $"Loading the {what} failed: {error.Message} ({error.Code})");

    // Dates for the sample: so many days before today, kept inside the fiscal year.
    private sealed class Dates(DateOnly start, DateOnly end)
    {
        private readonly DateOnly _anchor = DateOnly.FromDateTime(DateTime.Today) < start ? start : DateOnly.FromDateTime(DateTime.Today) > end ? end : DateOnly.FromDateTime(DateTime.Today);

        public DateOnly DaysAgo(int days) => Clamp(_anchor.AddDays(-days));

        public DateOnly Clamp(DateOnly date) => date < start ? start : date > end ? end : date;
    }
}
