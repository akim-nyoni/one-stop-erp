using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Api;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Server;

/// <summary>
/// The REST API's master-data and document endpoints (brief §11): customers, suppliers, items and prices - list, read, create,
/// change, retire - and posting sales invoices, credit notes, receipts, supplier invoices, credit notes, payments and till sales,
/// each through the same application service the desktop uses, so every rule and posting is the same.
/// </summary>
internal static class TradingApiEndpoints
{
    private const int PageSize = 200;

    /// <summary>Maps the endpoints onto the API group.</summary>
    /// <param name="api">The API route group (authorisation is required on every route).</param>
    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        // ---- customers
        api.MapGet("/api/master/customers", async (long companyId, string? search, int? page, One4AllDbContext db, CancellationToken ct) =>
        {
            IQueryable<Customer> query = db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim().ToUpperInvariant();
                query = query.Where(c => c.Code.ToUpper().Contains(term) || c.Name.ToUpper().Contains(term));
            }

            return Results.Ok(await query.OrderBy(c => c.Code).Skip(Math.Max(0, (page ?? 1) - 1) * PageSize).Take(PageSize).Select(c => CustomerView(c)).ToListAsync(ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ListCustomers");

        api.MapGet("/api/master/customers/{customerId:long}", async (long customerId, long companyId, One4AllDbContext db, CancellationToken ct) =>
        {
            Customer? customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
            return customer is null ? NotFound($"Customer {customerId} not found.") : Results.Ok(CustomerView(customer));
        }).RequireAuthorization().WithName("GetCustomer");

        api.MapPost("/api/master/customers", async (long companyId, CustomerApi request, IMasterDataService master, One4AllDbContext db, CancellationToken ct) =>
        {
            Result<CurrencyCode> currency = TradingApiMapping.Currency(request.Currency);
            if (currency.IsFailure)
            {
                return Value(currency);
            }

            Result<long> created = await master.CreateCustomerAsync(companyId, request.Code, request.Name, currency.Value, request.CreditLimit, ct).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return Value(created);
            }

            Customer customer = await db.Customers.FirstAsync(c => c.Id == created.Value, ct).ConfigureAwait(false);
            ApplyCustomer(customer, request);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Created($"/api/master/customers/{customer.Id}", CustomerView(customer));
        }).RequireAuthorization().WithName("CreateCustomer");

        api.MapPut("/api/master/customers/{customerId:long}", async (long customerId, long companyId, CustomerApi request, One4AllDbContext db, CancellationToken ct) =>
        {
            Customer? customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
            if (customer is null)
            {
                return NotFound($"Customer {customerId} not found.");
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                customer.Name = request.Name.Trim();
            }

            ApplyCustomer(customer, request);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(CustomerView(customer));
        }).RequireAuthorization().WithName("UpdateCustomer");

        api.MapDelete("/api/master/customers/{customerId:long}", async (long customerId, long companyId, One4AllDbContext db, CancellationToken ct) =>
            await RetireAsync(db.Customers, c => c.Id == customerId && c.CompanyId == companyId, $"Customer {customerId}", db, ct).ConfigureAwait(false))
        .RequireAuthorization().WithName("RetireCustomer");

        // ---- suppliers
        api.MapGet("/api/master/suppliers", async (long companyId, string? search, int? page, One4AllDbContext db, CancellationToken ct) =>
        {
            IQueryable<Supplier> query = db.Suppliers.AsNoTracking().Where(s => s.CompanyId == companyId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim().ToUpperInvariant();
                query = query.Where(s => s.Code.ToUpper().Contains(term) || s.Name.ToUpper().Contains(term));
            }

            return Results.Ok(await query.OrderBy(s => s.Code).Skip(Math.Max(0, (page ?? 1) - 1) * PageSize).Take(PageSize).Select(s => SupplierView(s)).ToListAsync(ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ListSuppliers");

        api.MapGet("/api/master/suppliers/{supplierId:long}", async (long supplierId, long companyId, One4AllDbContext db, CancellationToken ct) =>
        {
            Supplier? supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);
            return supplier is null ? NotFound($"Supplier {supplierId} not found.") : Results.Ok(SupplierView(supplier));
        }).RequireAuthorization().WithName("GetSupplier");

        api.MapPost("/api/master/suppliers", async (long companyId, SupplierApi request, IMasterDataService master, One4AllDbContext db, CancellationToken ct) =>
        {
            Result<CurrencyCode> currency = TradingApiMapping.Currency(request.Currency);
            if (currency.IsFailure)
            {
                return Value(currency);
            }

            Result<long> created = await master.CreateSupplierAsync(companyId, request.Code, request.Name, currency.Value, ct).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return Value(created);
            }

            Supplier supplier = await db.Suppliers.FirstAsync(s => s.Id == created.Value, ct).ConfigureAwait(false);
            ApplySupplier(supplier, request);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Created($"/api/master/suppliers/{supplier.Id}", SupplierView(supplier));
        }).RequireAuthorization().WithName("CreateSupplier");

        api.MapPut("/api/master/suppliers/{supplierId:long}", async (long supplierId, long companyId, SupplierApi request, One4AllDbContext db, CancellationToken ct) =>
        {
            Supplier? supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);
            if (supplier is null)
            {
                return NotFound($"Supplier {supplierId} not found.");
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                supplier.Name = request.Name.Trim();
            }

            ApplySupplier(supplier, request);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(SupplierView(supplier));
        }).RequireAuthorization().WithName("UpdateSupplier");

        api.MapDelete("/api/master/suppliers/{supplierId:long}", async (long supplierId, long companyId, One4AllDbContext db, CancellationToken ct) =>
            await RetireAsync(db.Suppliers, s => s.Id == supplierId && s.CompanyId == companyId, $"Supplier {supplierId}", db, ct).ConfigureAwait(false))
        .RequireAuthorization().WithName("RetireSupplier");

        // ---- items
        api.MapGet("/api/master/items", async (long companyId, string? search, int? page, One4AllDbContext db, CancellationToken ct) =>
        {
            IQueryable<Item> query = db.Items.AsNoTracking().Where(i => i.CompanyId == companyId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim().ToUpperInvariant();
                query = query.Where(i => i.Code.ToUpper().Contains(term) || i.Description.ToUpper().Contains(term));
            }

            return Results.Ok(await query.OrderBy(i => i.Code).Skip(Math.Max(0, (page ?? 1) - 1) * PageSize).Take(PageSize).Select(i => ItemView(i)).ToListAsync(ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("ListItems");

        api.MapGet("/api/master/items/{itemId:long}", async (long itemId, long companyId, One4AllDbContext db, CancellationToken ct) =>
        {
            Item? item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
            return item is null ? NotFound($"Item {itemId} not found.") : Results.Ok(ItemView(item));
        }).RequireAuthorization().WithName("GetItem");

        api.MapPost("/api/master/items", async (long companyId, ItemApi request, IMasterDataService master, One4AllDbContext db, CancellationToken ct) =>
        {
            if (!Enum.TryParse(request.Type, ignoreCase: true, out ItemType type))
            {
                return BadRequest($"'{request.Type}' is not an item type ({string.Join(", ", Enum.GetNames<ItemType>())}).");
            }

            CostingMethod costing = CostingMethod.WeightedAverage;
            if (!string.IsNullOrWhiteSpace(request.Costing) && !Enum.TryParse(request.Costing, ignoreCase: true, out costing))
            {
                return BadRequest($"'{request.Costing}' is not a costing method ({string.Join(", ", Enum.GetNames<CostingMethod>())}).");
            }

            Result<long> created = await master.CreateItemAsync(new NewItem(companyId, request.Code, request.Description, type, request.RevenueAccountCode, request.CostOfSalesAccountCode, request.StandardCost, costing,
                PriceVarianceAccountCode: request.PriceVarianceAccountCode, Category: request.Category), ct).ConfigureAwait(false);
            if (created.IsFailure)
            {
                return Value(created);
            }

            Item item = await db.Items.FirstAsync(i => i.Id == created.Value, ct).ConfigureAwait(false);
            item.TaxCode = Clean(request.TaxCode);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Created($"/api/master/items/{item.Id}", ItemView(item));
        }).RequireAuthorization().WithName("CreateItem");

        api.MapPut("/api/master/items/{itemId:long}", async (long itemId, long companyId, ItemApi request, One4AllDbContext db, CancellationToken ct) =>
        {
            Item? item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
            if (item is null)
            {
                return NotFound($"Item {itemId} not found.");
            }

            if (!string.IsNullOrWhiteSpace(request.Description))
            {
                item.Description = request.Description.Trim();
            }

            item.RevenueAccountCode = Clean(request.RevenueAccountCode) ?? item.RevenueAccountCode;
            item.CostOfSalesAccountCode = Clean(request.CostOfSalesAccountCode) ?? item.CostOfSalesAccountCode;
            item.Category = Clean(request.Category) ?? item.Category;
            item.TaxCode = Clean(request.TaxCode) ?? item.TaxCode;
            item.StandardCost = request.StandardCost ?? item.StandardCost;
            item.IsActive = request.IsActive;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(ItemView(item));
        }).RequireAuthorization().WithName("UpdateItem");

        api.MapDelete("/api/master/items/{itemId:long}", async (long itemId, long companyId, One4AllDbContext db, CancellationToken ct) =>
            await RetireAsync(db.Items, i => i.Id == itemId && i.CompanyId == companyId, $"Item {itemId}", db, ct).ConfigureAwait(false))
        .RequireAuthorization().WithName("RetireItem");

        // ---- prices
        api.MapGet("/api/pricing/lists/{priceListId:long}/items/{itemId:long}", async (long priceListId, long itemId, long companyId, IPricingService pricing, CancellationToken ct) =>
            Results.Ok((await pricing.PriceHistoryAsync(companyId, priceListId, itemId, ct).ConfigureAwait(false)).Select(p => new { p.MinimumQuantity, p.UnitPrice, p.EffectiveFrom })))
        .RequireAuthorization().WithName("GetItemPrices");

        api.MapPut("/api/pricing/lists/{priceListId:long}/items/{itemId:long}", async (long priceListId, long itemId, long companyId, PriceApi request, IPricingService pricing, CancellationToken ct) =>
            Action(request.EffectiveFrom is DateOnly from
                ? await pricing.SchedulePriceAsync(companyId, priceListId, itemId, request.MinimumQuantity, request.UnitPrice, from, DateOnly.FromDateTime(DateTime.Today), ct).ConfigureAwait(false)
                : await pricing.SetPriceAsync(companyId, priceListId, itemId, request.MinimumQuantity, request.UnitPrice, ct).ConfigureAwait(false)))
        .RequireAuthorization().WithName("SetItemPrice");

        // ---- documents
        api.MapPost("/api/trading/sales-invoices", async (long companyId, SalesDocumentApi request, ISalesLedgerService sales, CancellationToken ct) =>
        {
            Result<SalesInvoiceRequest> mapped = TradingApiMapping.SalesInvoice(companyId, request);
            return mapped.IsFailure ? Value(mapped) : Value(await sales.PostInvoiceAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("PostSalesInvoice");

        api.MapPost("/api/trading/sales-credit-notes", async (long companyId, SalesDocumentApi request, ISalesLedgerService sales, Application.Abstractions.Accounting.IFiscalDeviceProvider fiscal, CancellationToken ct) =>
        {
            Result<SalesCreditNoteRequest> mapped = TradingApiMapping.SalesCreditNote(companyId, request);
            if (mapped.IsSuccess && fiscal.IsEnabled && string.IsNullOrWhiteSpace(mapped.Value.OriginalInvoiceNumber))
            {
                // The fiscal device signs a credit note against the invoice it reverses (FDMS needs the reference).
                mapped = Error.Validation("Ar.OriginalInvoiceRequired", "Give the original invoice number: the fiscal device needs the invoice a credit note reverses.");
            }

            return mapped.IsFailure ? Value(mapped) : Value(await sales.PostCreditNoteAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("PostSalesCreditNote");

        api.MapPost("/api/trading/receipts", async (long companyId, ReceiptApi request, ISalesLedgerService sales, CancellationToken ct) =>
        {
            Result<ReceiptRequest> mapped = TradingApiMapping.Receipt(companyId, request);
            return mapped.IsFailure ? Value(mapped) : Value(await sales.PostReceiptAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("PostReceipt");

        api.MapPost("/api/trading/purchase-invoices", async (long companyId, PurchaseDocumentApi request, IPurchaseLedgerService purchases, CancellationToken ct) =>
        {
            Result<PurchaseInvoiceRequest> mapped = TradingApiMapping.PurchaseInvoice(companyId, request);
            return mapped.IsFailure ? Value(mapped) : Value(await purchases.PostInvoiceAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("PostPurchaseInvoice");

        api.MapPost("/api/trading/purchase-credit-notes", async (long companyId, PurchaseDocumentApi request, IPurchaseLedgerService purchases, CancellationToken ct) =>
        {
            Result<PurchaseCreditNoteRequest> mapped = TradingApiMapping.PurchaseCreditNote(companyId, request);
            return mapped.IsFailure ? Value(mapped) : Value(await purchases.PostCreditNoteAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("PostPurchaseCreditNote");

        // The payer is whoever the token belongs to; the payment is held to the payment run's controls (supplier held or bank details changing; a second
        // person's approval, named by user name, where the company requires it).
        api.MapPost("/api/trading/supplier-payments", async (long companyId, long userId, SupplierPaymentApi request, IPurchaseLedgerService purchases, One4AllDbContext db,
            CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            long? approver = null;
            if (!string.IsNullOrWhiteSpace(request.ApprovedBy))
            {
                string name = request.ApprovedBy.Trim().ToUpperInvariant();
                approver = await db.Users.AsNoTracking().Where(u => u.NormalizedUsername == name).Select(u => (long?)u.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (approver is null)
                {
                    return Value(Result<bool>.Failure(Error.Validation("Ap.ApproverNotFound", $"No user {request.ApprovedBy.Trim()} to have approved the payment.")));
                }
            }

            Result<PaymentRequest> mapped = TradingApiMapping.SupplierPayment(companyId, request, userId, approver);
            return mapped.IsFailure ? Value(mapped) : Value(await purchases.PostPaymentAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("PostSupplierPayment");

        // The cashier is whoever the token belongs to, never the user the body names.
        api.MapPost("/api/pos/sales", async (long companyId, long userId, PosSaleApi request, IPosService pos, CancellationToken ct) =>
        {
            Result<CompleteSaleRequest> mapped = TradingApiMapping.PosSale(companyId, request, userId);
            return mapped.IsFailure ? Value(mapped) : Value(await pos.CompleteSaleAsync(mapped.Value, ct).ConfigureAwait(false));
        }).RequireAuthorization().WithName("PostTillSale");
    }

    private static object CustomerView(Customer c) => new { c.Id, c.Code, c.Name, Currency = c.Currency.Value, c.CreditLimit, c.OnHold, c.Email, c.MobileNumber, c.TaxRegistration, c.DefaultTaxCode, c.IsDeleted };

    private static object SupplierView(Supplier s) => new { s.Id, s.Code, s.Name, Currency = s.Currency.Value, s.OnHold, s.IsBlacklisted, s.TaxRegistration, s.DefaultTaxCode, s.IsDeleted };

    private static object ItemView(Item i) => new { i.Id, i.Code, i.Description, Type = i.ItemType.ToString(), Costing = i.CostingMethod.ToString(), i.RevenueAccountCode, i.CostOfSalesAccountCode, i.Category, i.TaxCode, i.StandardCost, i.IsActive, i.IsDeleted };

    private static void ApplyCustomer(Customer customer, CustomerApi request)
    {
        customer.Email = Clean(request.Email, upper: false) ?? customer.Email;
        customer.MobileNumber = Clean(request.MobileNumber, upper: false) ?? customer.MobileNumber;
        customer.TaxRegistration = Clean(request.TaxRegistration, upper: false) ?? customer.TaxRegistration;
        customer.DefaultTaxCode = Clean(request.DefaultTaxCode) ?? customer.DefaultTaxCode;
        customer.OnHold = request.OnHold;
    }

    private static void ApplySupplier(Supplier supplier, SupplierApi request)
    {
        supplier.TaxRegistration = Clean(request.TaxRegistration, upper: false) ?? supplier.TaxRegistration;
        supplier.DefaultTaxCode = Clean(request.DefaultTaxCode) ?? supplier.DefaultTaxCode;
        supplier.OnHold = request.OnHold;
    }

    // Retires (soft-deletes) a master record: it stays on history and reports but is gone from lists and lookups.
    private static async Task<IResult> RetireAsync<T>(DbSet<T> set, System.Linq.Expressions.Expression<Func<T, bool>> match, string what, One4AllDbContext db, CancellationToken ct)
        where T : SoftDeletableEntity
    {
        T? record = await set.FirstOrDefaultAsync(match, ct).ConfigureAwait(false);
        if (record is null)
        {
            return NotFound($"{what} not found.");
        }

        record.IsDeleted = true;
        record.DeletedOnUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { ok = true });
    }

    private static string? Clean(string? value, bool upper = true) => string.IsNullOrWhiteSpace(value) ? null : upper ? value.Trim().ToUpperInvariant() : value.Trim();

    /// <summary>A 404 with the message.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The response.</returns>
    internal static IResult NotFound(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status404NotFound);

    /// <summary>A 400 with the message.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The response.</returns>
    internal static IResult BadRequest(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>The value (200), or the error as 404 / 409 / 403 / 400 by its type with a body of error and code.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>The response.</returns>
    internal static IResult Value<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        int status = result.Error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Rule or ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Json(new { error = result.Error.Message, code = result.Error.Code }, statusCode: status);
    }

    /// <summary>{ ok: true } (200), or the error as <see cref="Value{T}"/> answers it.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The response.</returns>
    internal static IResult Action(Result result) => result.IsSuccess ? Results.Ok(new { ok = true }) : Value(Result<bool>.Failure(result.Error));
}
