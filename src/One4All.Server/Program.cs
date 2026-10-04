using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using One4All.Application.Abstractions.Security;
using One4All.Core.Common;
using One4All.Infrastructure;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Infrastructure.Security;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --- Infrastructure (embedded services, same code path as the desktop) ------------------------------------
string connectionString = builder.Configuration.GetConnectionString("One4All")
    ?? Environment.GetEnvironmentVariable("ONE4ALL_CONNECTION")
    ?? throw new InvalidOperationException("No database connection string (ConnectionStrings:One4All or ONE4ALL_CONNECTION).");
builder.Services.AddOne4AllInfrastructure(connectionString);

// --- JWT options + token service --------------------------------------------------------------------------
JwtOptions jwt = new();
builder.Configuration.GetSection("Jwt").Bind(jwt);
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
{
    // Development fallback so the server runs out of the box; production must configure Jwt:SigningKey.
    jwt.SigningKey = "one4all-development-signing-key-change-me-please";
}

builder.Services.AddSingleton(jwt);
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep claim types verbatim (sub, unique_name, name)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Name,
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// Apply migrations and seed the permission catalogue + default administrator on startup.
await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync();
    await scope.ServiceProvider.GetRequiredService<AdministratorSeeder>().SeedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// --- Endpoints --------------------------------------------------------------------------------------------
app.MapPost("/api/auth/login", async (LoginRequest request, IAuthenticationService auth, IJwtTokenService tokens, CancellationToken ct) =>
{
    AuthenticationResult result = await auth.AuthenticateAsync(request.Username, request.Password, "api", ct).ConfigureAwait(false);
    if (!result.Succeeded)
    {
        return Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status401Unauthorized);
    }

    TokenResult token = tokens.Issue(result);
    return Results.Ok(new
    {
        accessToken = token.AccessToken,
        tokenType = token.TokenType,
        expiresAtUtc = token.ExpiresAtUtc,
        displayName = result.DisplayName,
        mustChangePassword = result.MustChangePassword,
    });
})
.AllowAnonymous()
.WithName("Login");

app.MapGet("/api/me", (ClaimsPrincipal user) => Results.Ok(new
{
    userId = user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub),
    userName = user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName),
    displayName = user.Identity?.Name,
    isAdmin = user.FindFirstValue(JwtTokenService.AdminClaim) == "true",
}))
.RequireAuthorization()
.WithName("Me");

app.MapGet("/api/reports", async (One4AllDbContext db, CancellationToken ct) =>
{
    var reports = await db.ReportDefinitions
        .Where(r => r.Scope == One4All.Core.Reporting.ReportScope.System)
        .OrderBy(r => r.Module).ThenBy(r => r.Name)
        .Select(r => new { r.Code, r.Name, r.Module })
        .ToListAsync(ct).ConfigureAwait(false);
    return Results.Ok(reports);
})
.RequireAuthorization()
.WithName("Reports");

// --- Offline outbox (drain + dead-letter administration) --------------------------------------------------
app.MapPost("/api/offline/sync", async (One4All.Application.Abstractions.Offline.IOfflineSyncService sync, CancellationToken ct) =>
{
    One4All.Application.Abstractions.Offline.OfflineSyncReport report = await sync.SyncPendingAsync(ct).ConfigureAwait(false);
    return Results.Ok(new { report.Synced, report.DeadLettered, report.Retryable, report.Processed });
})
.RequireAuthorization()
.WithName("OfflineSync");

app.MapGet("/api/offline/failed", async (One4All.Application.Abstractions.Offline.IOfflineAdminService admin, CancellationToken ct) =>
{
    IReadOnlyList<One4All.Core.Offline.OfflineOperation> failed = await admin.ListFailedAsync(ct: ct).ConfigureAwait(false);
    return Results.Ok(failed.Select(o => new
    {
        o.OperationId,
        o.Kind,
        o.CompanyId,
        o.Attempts,
        o.LastError,
        o.CreatedUtc,
    }));
})
.RequireAuthorization()
.WithName("OfflineFailed");

app.MapPost("/api/offline/{operationId:guid}/requeue", async (Guid operationId, One4All.Application.Abstractions.Offline.IOfflineAdminService admin, CancellationToken ct) =>
{
    Result result = await admin.RequeueAsync(operationId, ct).ConfigureAwait(false);
    return OfflineActionResult(result);
})
.RequireAuthorization()
.WithName("OfflineRequeue");

app.MapPost("/api/offline/{operationId:guid}/discard", async (Guid operationId, One4All.Application.Abstractions.Offline.IOfflineAdminService admin, CancellationToken ct) =>
{
    Result result = await admin.DiscardAsync(operationId, ct).ConfigureAwait(false);
    return OfflineActionResult(result);
})
.RequireAuthorization()
.WithName("OfflineDiscard");

// --- Analytics / treasury / CRM read models (dashboards and mobile clients) --------------------------------
app.MapGet("/api/analytics/working-capital", async (long companyId, DateOnly from, DateOnly to, One4All.Application.Abstractions.Reporting.IWorkingCapitalService wc, CancellationToken ct) =>
    ValueResponse(await wc.ComputeAsync(companyId, from, to, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("WorkingCapital");

app.MapGet("/api/treasury/cash-position", async (long companyId, DateOnly asAt, One4All.Application.Abstractions.Banking.IBankReconciliationService banking, CancellationToken ct) =>
    ValueResponse(await banking.CashPositionAsync(companyId, asAt, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CashPosition");

app.MapGet("/api/crm/pipeline-forecast", async (long companyId, One4All.Application.Abstractions.Crm.IOpportunityService crm, CancellationToken ct) =>
    ValueResponse(await crm.PipelineForecastAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PipelineForecast");

app.MapGet("/api/crm/conversion", async (long companyId, One4All.Application.Abstractions.Crm.IOpportunityService crm, CancellationToken ct) =>
    ValueResponse(await crm.ConversionMetricsAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CrmConversion");

// --- Workflow approvals (the approver's inbox, and acting on an item) ---------------------------------------
app.MapGet("/api/workflow/inbox", async (long companyId, long userId, One4All.Application.Abstractions.Workflow.IWorkflowService workflow, CancellationToken ct) =>
    Results.Ok(await workflow.PendingForUserAsync(companyId, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("WorkflowInbox");

app.MapPost("/api/workflow/{instanceId:long}/act", async (long instanceId, long userId, One4All.Core.Workflow.WorkflowActionType action, string? comment, One4All.Application.Abstractions.Workflow.IWorkflowService workflow, CancellationToken ct) =>
    ValueResponse(await workflow.ActAsync(instanceId, userId, action, comment, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("WorkflowAct");

// --- Credit control -----------------------------------------------------------------------------------------
app.MapPost("/api/credit/review", async (long companyId, DateOnly asAt, int overdueToleranceDays, One4All.Application.Abstractions.Trading.ICreditControlService credit, CancellationToken ct) =>
    ValueResponse(await credit.ReviewAsync(companyId, asAt, overdueToleranceDays, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreditReview");

app.MapPost("/api/credit/{customerId:long}/hold", async (long companyId, long customerId, string reason, One4All.Application.Abstractions.Trading.ICreditControlService credit, CancellationToken ct) =>
    ActionResponse(await credit.HoldAsync(companyId, customerId, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreditHold");

app.MapPost("/api/credit/{customerId:long}/release", async (long companyId, long customerId, string reason, One4All.Application.Abstractions.Trading.ICreditControlService credit, CancellationToken ct) =>
    ActionResponse(await credit.ReleaseAsync(companyId, customerId, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreditRelease");

// --- Stock: replenishment, availability, pricing ----------------------------------------------------------
app.MapGet("/api/stock/provision", async (long companyId, DateOnly asAtDate, string provisionAccount, decimal? costToSellPercent, int? slowMovingDays, decimal? slowMovingPercent, int? obsoleteDays, decimal? obsoletePercent, One4All.Application.Abstractions.Inventory.IStockProvisionService provisions, CancellationToken ct) =>
    ValueResponse(await provisions.AssessAsync(companyId, asAtDate, ProvisionPolicy(costToSellPercent, slowMovingDays, slowMovingPercent, obsoleteDays, obsoletePercent), provisionAccount, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("StockProvision");

app.MapPost("/api/stock/provision", async (long companyId, DateOnly asAtDate, string provisionAccount, string expenseAccount, decimal? costToSellPercent, int? slowMovingDays, decimal? slowMovingPercent, int? obsoleteDays, decimal? obsoletePercent, One4All.Application.Abstractions.Inventory.IStockProvisionService provisions, CancellationToken ct) =>
    ValueResponse(await provisions.PostAsync(companyId, asAtDate, ProvisionPolicy(costToSellPercent, slowMovingDays, slowMovingPercent, obsoleteDays, obsoletePercent), provisionAccount, expenseAccount, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PostStockProvision");

app.MapPost("/api/stock/landed-cost", async (One4All.Application.Abstractions.Inventory.LandedCostRequest request, One4All.Application.Abstractions.Inventory.ILandedCostService landed, CancellationToken ct) =>
    ValueResponse(await landed.ApplyAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApplyLandedCost");

app.MapPost("/api/stock/items/{itemId:long}/standard-cost", async (long itemId, long companyId, decimal newStandardCost, DateOnly date, string revaluationAccountCode, One4All.Application.Abstractions.Inventory.IInventoryService inventory, CancellationToken ct) =>
    ValueResponse(await inventory.RevalueStandardCostAsync(companyId, itemId, newStandardCost, date, revaluationAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RevalueStandardCost");

app.MapGet("/api/stock/replenishment", async (long companyId, long? warehouseId, One4All.Application.Abstractions.Inventory.IReplenishmentService replenish, CancellationToken ct) =>
    Results.Ok(await replenish.SuggestAsync(companyId, warehouseId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("Replenishment");

app.MapGet("/api/stock/{itemId:long}/availability", async (long companyId, long itemId, long? warehouseId, One4All.Application.Abstractions.Inventory.IStockAvailabilityService availability, CancellationToken ct) =>
    ValueResponse(await availability.AvailabilityAsync(companyId, itemId, warehouseId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("StockAvailability");

app.MapGet("/api/stock/back-orders", async (long companyId, One4All.Application.Abstractions.Inventory.IStockAvailabilityService availability, CancellationToken ct) =>
    Results.Ok(await availability.BackOrdersAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("BackOrders");

app.MapPost("/api/pricing/lists/{priceListId:long}/adjust", async (long companyId, long priceListId, decimal percent, decimal? roundTo, One4All.Application.Inventory.PriceRounding? rounding, long[]? itemIds, One4All.Application.Abstractions.Inventory.IPricingService pricing, CancellationToken ct) =>
    ValueResponse(await pricing.AdjustPricesAsync(companyId, priceListId, percent, roundTo ?? 0m, rounding ?? One4All.Application.Inventory.PriceRounding.Nearest, itemIds is { Length: > 0 } ? itemIds : null, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AdjustPrices");

app.MapPost("/api/pricing/lists/{priceListId:long}/copy", async (long companyId, long priceListId, string code, string name, decimal percent, decimal? roundTo, One4All.Application.Inventory.PriceRounding? rounding, One4All.Application.Abstractions.Inventory.IPricingService pricing, CancellationToken ct) =>
    ValueResponse(await pricing.CopyPriceListAsync(companyId, priceListId, code, name, percent, roundTo ?? 0m, rounding ?? One4All.Application.Inventory.PriceRounding.Nearest, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CopyPriceList");

app.MapGet("/api/pricing/price", async (long companyId, long? customerId, long itemId, decimal quantity, One4All.Application.Abstractions.Inventory.IPricingService pricing, CancellationToken ct) =>
    ValueResponse(await pricing.PriceAsync(companyId, customerId, itemId, quantity, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("Price");

// --- Documents (plain text) and order cancellation ---------------------------------------------------------
app.MapGet("/api/trading/customers/{customerId:long}/statement", async (long companyId, long customerId, DateOnly asAt, int? width, One4All.Application.Abstractions.Trading.IStatementService statements, CancellationToken ct) =>
    TextResponse(await statements.RenderCustomerStatementTextAsync(companyId, customerId, asAt, width ?? 64, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CustomerStatement");

app.MapPost("/api/trading/auto-allocate", async (long companyId, One4All.Core.Trading.TradingLedger ledger, long? traderId, DateOnly date, One4All.Application.Abstractions.Trading.IAutoAllocationService auto, CancellationToken ct) =>
    traderId is { } id
        ? ValueResponse(await auto.AllocateTraderAsync(companyId, ledger, id, date, ct).ConfigureAwait(false))
        : Results.Ok(await auto.AllocateLedgerAsync(companyId, ledger, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AutoAllocate");

app.MapPost("/api/trading/payment-run/propose", async (One4All.Application.Abstractions.Trading.PaymentRunRequest request, One4All.Application.Abstractions.Trading.IPaymentRunService run, CancellationToken ct) =>
    ValueResponse(await run.ProposeAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ProposePaymentRun");

app.MapPost("/api/trading/payment-run", async (One4All.Application.Abstractions.Trading.PaymentRunRequest request, One4All.Application.Abstractions.Trading.IPaymentRunService run, CancellationToken ct) =>
    ValueResponse(await run.RunAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PaymentRun");

app.MapGet("/api/ar/expected-credit-loss", async (long companyId, DateOnly asAtDate, string allowanceAccount, One4All.Application.Abstractions.Trading.IExpectedCreditLossService ecl, CancellationToken ct) =>
    ValueResponse(await ecl.AssessAsync(companyId, asAtDate, One4All.Application.Trading.EclMatrix.Default, allowanceAccount, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ExpectedCreditLoss");

app.MapPost("/api/ar/expected-credit-loss", async (long companyId, DateOnly asAtDate, string allowanceAccount, string expenseAccount, One4All.Application.Abstractions.Trading.IExpectedCreditLossService ecl, CancellationToken ct) =>
    ValueResponse(await ecl.PostAsync(companyId, asAtDate, One4All.Application.Trading.EclMatrix.Default, allowanceAccount, expenseAccount, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PostExpectedCreditLoss");

app.MapGet("/api/ap/withholding/return", async (long companyId, DateOnly fromDate, DateOnly toDate, One4All.Application.Abstractions.Trading.IWithholdingTaxService wht, CancellationToken ct) =>
    ValueResponse(await wht.ReturnAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("WithholdingReturn");

app.MapPost("/api/ap/withholding/remit", async (long companyId, DateOnly fromDate, DateOnly toDate, DateOnly remittanceDate, string bankAccount, One4All.Application.Abstractions.Trading.IWithholdingTaxService wht, CancellationToken ct) =>
    ValueResponse(await wht.RemitAsync(companyId, fromDate, toDate, remittanceDate, bankAccount, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RemitWithholding");

app.MapGet("/api/ap/withholding/{certificateId:long}/certificate", async (long companyId, long certificateId, One4All.Application.Abstractions.Trading.IWithholdingTaxService wht, CancellationToken ct) =>
{
    Result<string> text = await wht.CertificateTextAsync(companyId, certificateId, ct).ConfigureAwait(false);
    return text.IsSuccess ? Results.Text(text.Value, "text/plain") : ValueResponse(text);
})
.RequireAuthorization()
.WithName("WithholdingCertificate");

app.MapPost("/api/trading/revalue", async (long companyId, One4All.Core.Trading.TradingLedger ledger, DateOnly asAtDate, bool? autoReverse, One4All.Application.Abstractions.Trading.ITradingRevaluationService reval, CancellationToken ct) =>
    ValueResponse(await reval.RevalueAsync(companyId, ledger, asAtDate, autoReverse ?? true, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RevalueTradingLedger");

app.MapPost("/api/trading/suppliers/{supplierId:long}/reconcile-statement", async (long companyId, long supplierId, int? dateToleranceDays, IReadOnlyList<One4All.Application.Trading.SupplierStatementLine> statement, One4All.Application.Abstractions.Trading.ISupplierStatementService service, CancellationToken ct) =>
    ValueResponse(await service.ReconcileAsync(companyId, supplierId, statement, dateToleranceDays ?? One4All.Application.Trading.SupplierStatementReconciler.DefaultDateToleranceDays, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReconcileSupplierStatement");

app.MapGet("/api/trading/suppliers/{supplierId:long}/statement", async (long companyId, long supplierId, DateOnly asAt, int? width, One4All.Application.Abstractions.Trading.IStatementService statements, CancellationToken ct) =>
    TextResponse(await statements.RenderSupplierStatementTextAsync(companyId, supplierId, asAt, width ?? 64, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SupplierStatement");

app.MapGet("/api/trading/items/{openItemId:long}/document", async (long companyId, long openItemId, int? width, One4All.Application.Abstractions.Trading.IInvoiceDocumentService documents, CancellationToken ct) =>
    TextResponse(await documents.RenderTextAsync(companyId, openItemId, width ?? 64, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("InvoiceDocument");

app.MapGet("/api/pop/{purchaseOrderId:long}/document", async (long companyId, long purchaseOrderId, int? width, One4All.Application.Abstractions.Procurement.IPurchaseOrderDocumentService documents, CancellationToken ct) =>
    TextResponse(await documents.RenderTextAsync(companyId, purchaseOrderId, width ?? 72, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PurchaseOrderDocument");

app.MapGet("/api/sop/{salesOrderId:long}/pick-list", async (long companyId, long salesOrderId, long warehouseId, int? width, One4All.Application.Abstractions.SalesOrders.IPickListService picks, CancellationToken ct) =>
    TextResponse(await picks.RenderTextAsync(companyId, salesOrderId, warehouseId, width ?? 64, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PickList");

app.MapPost("/api/sop/{salesOrderId:long}/cancel", async (long companyId, long salesOrderId, string reason, One4All.Application.Abstractions.SalesOrders.ISalesOrderService sop, CancellationToken ct) =>
    ActionResponse(await sop.CancelOrderAsync(companyId, salesOrderId, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CancelSalesOrder");

app.MapPost("/api/pop/{purchaseOrderId:long}/cancel", async (long companyId, long purchaseOrderId, string reason, One4All.Application.Abstractions.Procurement.IProcurementService pop, CancellationToken ct) =>
    ActionResponse(await pop.CancelPurchaseOrderAsync(companyId, purchaseOrderId, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CancelPurchaseOrder");

// --- Banking: reconciliation work list and auto-match ------------------------------------------------------
app.MapGet("/api/banking/{bankAccountId:long}/work-list", async (long companyId, long bankAccountId, DateOnly asAt, One4All.Application.Abstractions.Banking.IBankReconciliationService banking, CancellationToken ct) =>
    ValueResponse(await banking.WorkListAsync(companyId, bankAccountId, asAt, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("BankWorkList");

app.MapPost("/api/banking/{bankAccountId:long}/auto-match", async (long companyId, long bankAccountId, int? dateToleranceDays, One4All.Application.Abstractions.Banking.IBankReconciliationService banking, CancellationToken ct) =>
    ValueResponse(await banking.AutoMatchAsync(companyId, bankAccountId, dateToleranceDays ?? One4All.Application.Banking.BankAutoMatcher.DefaultDateToleranceDays, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("BankAutoMatch");

app.MapGet("/api/banking/rules", async (long companyId, One4All.Application.Abstractions.Banking.IBankRuleService rules, CancellationToken ct) =>
    Results.Ok((await rules.ListAsync(companyId, ct).ConfigureAwait(false)).Select(r => new { r.Id, r.Name, r.BankAccountId, r.DescriptionContains, r.Direction, r.MinAmount, r.MaxAmount, r.AccountCode, r.Narrative, r.Priority, r.IsActive })))
.RequireAuthorization()
.WithName("BankRules");

app.MapPost("/api/banking/rules", async (One4All.Application.Abstractions.Banking.CreateBankRuleRequest request, One4All.Application.Abstractions.Banking.IBankRuleService rules, CancellationToken ct) =>
    ValueResponse(await rules.CreateRuleAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreateBankRule");

app.MapPost("/api/banking/rules/{ruleId:long}/active", async (long companyId, long ruleId, bool isActive, One4All.Application.Abstractions.Banking.IBankRuleService rules, CancellationToken ct) =>
    ActionResponse(await rules.SetActiveAsync(companyId, ruleId, isActive, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SetBankRuleActive");

app.MapPost("/api/banking/{bankAccountId:long}/apply-rules", async (long companyId, long bankAccountId, DateOnly upToDate, One4All.Application.Abstractions.Banking.IBankRuleService rules, CancellationToken ct) =>
    ValueResponse(await rules.ApplyAsync(companyId, bankAccountId, upToDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApplyBankRules");

app.MapPost("/api/banking/{bankAccountId:long}/lines/{lineId:long}/post-receipt", async (long companyId, long bankAccountId, long lineId, long customerId, string? documentNumber, One4All.Application.Abstractions.Banking.IBankStatementPostingService posting, CancellationToken ct) =>
    ValueResponse(await posting.PostCustomerReceiptAsync(companyId, bankAccountId, lineId, customerId, documentNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PostStatementReceipt");

app.MapPost("/api/banking/{bankAccountId:long}/lines/{lineId:long}/post-payment", async (long companyId, long bankAccountId, long lineId, long supplierId, string? documentNumber, One4All.Application.Abstractions.Banking.IBankStatementPostingService posting, CancellationToken ct) =>
    ValueResponse(await posting.PostSupplierPaymentAsync(companyId, bankAccountId, lineId, supplierId, documentNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PostStatementPayment");

app.MapPost("/api/banking/{bankAccountId:long}/lines/{lineId:long}/post-to-account", async (long companyId, long bankAccountId, long lineId, string accountCode, string? narrative, One4All.Application.Abstractions.Banking.IBankStatementPostingService posting, CancellationToken ct) =>
    ValueResponse(await posting.PostToAccountAsync(companyId, bankAccountId, lineId, accountCode, narrative, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PostStatementToAccount");

// --- Banking: statement CSV import (body is the raw CSV; the layout comes from the query) --------------------
app.MapPost("/api/banking/{bankAccountId:long}/statement-csv", async (HttpRequest http, long companyId, long bankAccountId,
    int dateColumn, int descriptionColumn, int? amountColumn, int? debitColumn, int? creditColumn, int? referenceColumn, string? dateFormat, string? delimiter, bool? hasHeader,
    One4All.Application.Abstractions.Banking.IBankReconciliationService banking, CancellationToken ct) =>
{
    using StreamReader reader = new(http.Body, Encoding.UTF8);
    string csv = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    One4All.Application.Banking.BankCsvLayout layout = new()
    {
        DateColumn = dateColumn,
        DescriptionColumn = descriptionColumn,
        AmountColumn = amountColumn,
        DebitColumn = debitColumn,
        CreditColumn = creditColumn,
        ReferenceColumn = referenceColumn,
        DateFormat = string.IsNullOrWhiteSpace(dateFormat) ? "yyyy-MM-dd" : dateFormat,
        Delimiter = string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0],
        HasHeader = hasHeader ?? true,
    };
    return ValueResponse(await banking.ImportStatementCsvAsync(companyId, bankAccountId, csv, layout, ct).ConfigureAwait(false));
})
.RequireAuthorization()
.Accepts<string>("text/csv", "text/plain")
.WithName("ImportStatementCsv");

app.MapPost("/api/banking/{bankAccountId:long}/statement/file", async (long companyId, long bankAccountId, One4All.Application.Banking.StatementFileFormat format, HttpRequest http,
    One4All.Application.Abstractions.Banking.IBankReconciliationService banking, CancellationToken ct) =>
{
    using StreamReader reader = new(http.Body, Encoding.UTF8);
    string content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    return ValueResponse(await banking.ImportStatementFileAsync(companyId, bankAccountId, content, format, null, ct).ConfigureAwait(false));
})
.RequireAuthorization()
.Accepts<string>("text/plain", "application/xml")
.WithName("ImportStatementFile");

app.MapPost("/api/banking/{bankAccountId:long}/reconciliation/sign-off", async (long bankAccountId, long companyId, DateOnly asAtDate, long userId, One4All.Application.Abstractions.Banking.IBankReconciliationService banking, CancellationToken ct) =>
    ValueResponse(await banking.SignOffAsync(companyId, bankAccountId, asAtDate, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SignOffBankReconciliation");

app.MapPost("/api/banking/transfers", async (One4All.Application.Abstractions.Banking.BankTransferRequest request, One4All.Application.Abstractions.Banking.ITreasuryService treasury, CancellationToken ct) =>
    ValueResponse(await treasury.TransferAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TransferBetweenBankAccounts");

app.MapPost("/api/vouchers/promo-campaigns", async (long companyId, One4All.Application.Abstractions.Vouchers.PromoCampaignRequest request, One4All.Application.Abstractions.Vouchers.IPromoService promos, CancellationToken ct) =>
    ValueResponse(await promos.CreateCampaignAsync(companyId, request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreatePromoCampaign");

app.MapGet("/api/vouchers/promo-codes/{code}/quote", async (string code, long companyId, DateOnly date, decimal spend, One4All.Application.Abstractions.Vouchers.IPromoService promos, CancellationToken ct) =>
    ValueResponse(await promos.QuoteAsync(companyId, code, date, spend, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("QuotePromoCode");

app.MapPost("/api/vouchers/promo-codes/{code}/redeem", async (string code, long companyId, DateOnly date, decimal spend, string reference, One4All.Application.Abstractions.Vouchers.IPromoService promos, CancellationToken ct) =>
    ValueResponse(await promos.RedeemAsync(companyId, code, date, spend, reference, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RedeemPromoCode");

app.MapGet("/api/vouchers/promo-campaigns/{campaignId:long}/analysis", async (long campaignId, long companyId, One4All.Application.Abstractions.Vouchers.IPromoService promos, CancellationToken ct) =>
    ValueResponse(await promos.AnalysisAsync(companyId, campaignId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PromoCampaignAnalysis");

app.MapPost("/api/vouchers/allowances/issue", async (long companyId, One4All.Application.Abstractions.Vouchers.AllowanceIssueRequest request, One4All.Application.Abstractions.Vouchers.IAllowanceVoucherService vouchers, CancellationToken ct) =>
    ValueResponse(await vouchers.IssueAsync(companyId, request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("IssueAllowanceVouchers");

app.MapPost("/api/vouchers/allowances/reconcile", async (long companyId, long supplierId, string invoiceNumber, decimal invoicedAmount, One4All.Application.Abstractions.Vouchers.BilledVoucher[] vouchers, One4All.Application.Abstractions.Vouchers.IAllowanceVoucherService service, CancellationToken ct) =>
    ValueResponse(await service.ReconcileInvoiceAsync(companyId, supplierId, invoiceNumber, invoicedAmount, vouchers, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReconcileAllowanceVouchers");

app.MapGet("/api/vouchers/allowances/outstanding", async (long companyId, DateOnly asAtDate, One4All.Application.Abstractions.Vouchers.IAllowanceVoucherService vouchers, CancellationToken ct) =>
    Results.Ok(await vouchers.OutstandingAsync(companyId, asAtDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("OutstandingAllowanceVouchers");

app.MapPost("/api/vouchers/gift-cards/{cardNumber}/top-up", async (string cardNumber, long companyId, decimal amount, DateOnly date, string cashAccountCode, string liabilityAccountCode, One4All.Application.Abstractions.Vouchers.IGiftCardServicing cards, CancellationToken ct) =>
    ValueResponse(await cards.TopUpAsync(companyId, cardNumber, amount, date, cashAccountCode, liabilityAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TopUpGiftCard");

app.MapPost("/api/vouchers/gift-cards/{cardNumber}/block", async (string cardNumber, long companyId, DateOnly date, string reason, One4All.Application.Abstractions.Vouchers.IGiftCardServicing cards, CancellationToken ct) =>
    ValueResponse(await cards.BlockAsync(companyId, cardNumber, date, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("BlockGiftCard");

app.MapPost("/api/vouchers/gift-cards/{cardNumber}/replace", async (string cardNumber, long companyId, string newCardNumber, DateOnly date, One4All.Application.Abstractions.Vouchers.IGiftCardServicing cards, CancellationToken ct) =>
    ValueResponse(await cards.ReplaceAsync(companyId, cardNumber, newCardNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReplaceGiftCard");

app.MapGet("/api/vouchers/gift-cards/{cardNumber}/history", async (string cardNumber, long companyId, One4All.Application.Abstractions.Vouchers.IGiftCardServicing cards, CancellationToken ct) =>
    ValueResponse(await cards.HistoryAsync(companyId, cardNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("GiftCardHistory");

app.MapPost("/api/vouchers/accounting", async (long companyId, One4All.Application.Abstractions.Vouchers.VoucherRequest request, One4All.Application.Abstractions.Vouchers.IAccountingVoucherService vouchers, CancellationToken ct) =>
    ValueResponse(await vouchers.PrepareAsync(companyId, request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PrepareAccountingVoucher");

app.MapPost("/api/vouchers/accounting/{voucherId:long}/approve", async (long voucherId, long companyId, long userId, One4All.Application.Abstractions.Vouchers.IAccountingVoucherService vouchers, CancellationToken ct) =>
    ValueResponse(await vouchers.ApproveAsync(companyId, voucherId, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveAccountingVoucher");

app.MapPost("/api/vouchers/accounting/{voucherId:long}/cancel", async (long voucherId, long companyId, One4All.Application.Abstractions.Vouchers.IAccountingVoucherService vouchers, CancellationToken ct) =>
    ActionResponse(await vouchers.CancelAsync(companyId, voucherId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CancelAccountingVoucher");

app.MapGet("/api/vouchers/accounting/{voucherId:long}/print", async (long voucherId, long companyId, One4All.Application.Abstractions.Vouchers.IAccountingVoucherService vouchers, CancellationToken ct) =>
    TextResponse(await vouchers.RenderAsync(companyId, voucherId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PrintAccountingVoucher");

app.MapPost("/api/stock/requisitions", async (long companyId, One4All.Application.Abstractions.Inventory.StoreRequisitionRequest request, One4All.Application.Abstractions.Inventory.IStoreRequisitionService store, CancellationToken ct) =>
{
    var raised = await store.RaiseAsync(companyId, request, ct).ConfigureAwait(false);
    return raised.IsSuccess ? Results.Ok(new { raised.Value.Id, raised.Value.Number }) : Results.BadRequest(new { error = raised.Error.Message, code = raised.Error.Code });
})
.RequireAuthorization()
.WithName("RaiseStoreRequisition");

app.MapPost("/api/stock/requisitions/{requisitionId:long}/approve", async (long requisitionId, long companyId, long userId, One4All.Application.Abstractions.Inventory.IStoreRequisitionService store, CancellationToken ct) =>
    ActionResponse(await store.ApproveAsync(companyId, requisitionId, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveStoreRequisition");

app.MapPost("/api/stock/requisitions/{requisitionId:long}/issue", async (long requisitionId, long companyId, DateOnly date, One4All.Application.Abstractions.Inventory.StoreRequisitionLineRequest[] lines, One4All.Application.Abstractions.Inventory.IStoreRequisitionService store, CancellationToken ct) =>
    ValueResponse(await store.IssueAsync(companyId, requisitionId, date, lines, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("IssueStoreRequisition");

app.MapPost("/api/municipal/indigent", async (long companyId, decimal maximumIncome, One4All.Application.Abstractions.Municipal.IndigentApplication application, One4All.Application.Abstractions.Municipal.IIndigentRegisterService register, CancellationToken ct) =>
    ValueResponse(await register.RegisterAsync(companyId, application, maximumIncome, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RegisterIndigent");

app.MapPost("/api/municipal/indigent/{registrationId:long}/review", async (long registrationId, long companyId, decimal householdIncome, decimal subsidyPercent, DateOnly reviewDue, decimal maximumIncome, long userId, One4All.Application.Abstractions.Municipal.IIndigentRegisterService register, CancellationToken ct) =>
    ValueResponse(await register.ReviewAsync(companyId, registrationId, householdIncome, subsidyPercent, reviewDue, maximumIncome, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReviewIndigent");

app.MapPost("/api/municipal/indigent/subsidies", async (long companyId, DateOnly monthEnd, string subsidyAccountCode, One4All.Application.Abstractions.Municipal.IIndigentRegisterService register, CancellationToken ct) =>
    ValueResponse(await register.ApplySubsidiesAsync(companyId, monthEnd, subsidyAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApplyIndigentSubsidies");

app.MapGet("/api/municipal/indigent/reviews-due", async (long companyId, DateOnly asAtDate, int withinDays, One4All.Application.Abstractions.Municipal.IIndigentRegisterService register, CancellationToken ct) =>
    Results.Ok(await register.ReviewsDueAsync(companyId, asAtDate, withinDays, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("IndigentReviewsDue");

app.MapGet("/api/municipal/disconnections/candidates", async (long companyId, One4All.Core.Municipal.ServiceType serviceType, DateOnly asAtDate, decimal threshold, One4All.Application.Abstractions.Municipal.IServiceOrderService orders, CancellationToken ct) =>
    Results.Ok(await orders.CandidatesAsync(companyId, serviceType, asAtDate, threshold, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DisconnectionCandidates");

app.MapPost("/api/municipal/service-orders", async (long companyId, long propertyId, One4All.Core.Municipal.ServiceType serviceType, One4All.Core.Municipal.ServiceOrderKind kind, DateOnly date, decimal threshold, One4All.Application.Abstractions.Municipal.IServiceOrderService orders, CancellationToken ct) =>
    ValueResponse(await orders.RaiseAsync(companyId, propertyId, serviceType, kind, date, threshold, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RaiseServiceOrder");

app.MapPost("/api/municipal/service-orders/{orderNumber}/execute", async (string orderNumber, long companyId, DateOnly date, decimal? feeAmount, string? feeRevenueAccountCode, One4All.Application.Abstractions.Municipal.IServiceOrderService orders, CancellationToken ct) =>
    ActionResponse(await orders.ExecuteAsync(companyId, orderNumber, date,
        feeAmount is decimal amount && !string.IsNullOrWhiteSpace(feeRevenueAccountCode) ? new One4All.Application.Abstractions.Municipal.ReconnectionFee(amount, feeRevenueAccountCode) : null, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ExecuteServiceOrder");

app.MapPost("/api/municipal/service-orders/{orderNumber}/cancel", async (string orderNumber, long companyId, One4All.Application.Abstractions.Municipal.IServiceOrderService orders, CancellationToken ct) =>
    ActionResponse(await orders.CancelAsync(companyId, orderNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CancelServiceOrder");

app.MapGet("/api/municipal/clearance/quote", async (long companyId, long propertyId, DateOnly date, int monthsInAdvance, One4All.Application.Abstractions.Municipal.IClearanceService clearance, CancellationToken ct) =>
    ValueResponse(await clearance.QuoteAsync(companyId, propertyId, date, monthsInAdvance, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("QuoteRatesClearance");

app.MapPost("/api/municipal/clearance", async (long companyId, long propertyId, DateOnly date, int monthsInAdvance, int validDays, long userId, One4All.Application.Abstractions.Municipal.IClearanceService clearance, CancellationToken ct) =>
    ValueResponse(await clearance.IssueAsync(companyId, propertyId, date, monthsInAdvance, validDays, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("IssueRatesClearance");

app.MapPost("/api/municipal/licences", async (One4All.Application.Abstractions.Municipal.IssueLicenceRequest request, One4All.Application.Abstractions.Municipal.ICouncilLicensingService licensing, CancellationToken ct) =>
    ValueResponse(await licensing.IssueAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("IssueCouncilLicence");

app.MapPost("/api/municipal/licences/{licenceNumber}/inspections", async (string licenceNumber, long companyId, DateOnly date, string inspector, bool passed, string? notes, One4All.Application.Abstractions.Municipal.ICouncilLicensingService licensing, CancellationToken ct) =>
    ValueResponse(await licensing.RecordInspectionAsync(companyId, licenceNumber, date, inspector, passed, notes, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordLicenceInspection");

app.MapPost("/api/municipal/licences/{licenceNumber}/renew", async (string licenceNumber, long companyId, DateOnly date, decimal penaltyPercent, string? penaltyAccountCode, One4All.Application.Abstractions.Municipal.ICouncilLicensingService licensing, CancellationToken ct) =>
    ValueResponse(await licensing.RenewAsync(companyId, licenceNumber, date, penaltyPercent, penaltyAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RenewCouncilLicence");

app.MapPost("/api/municipal/licences/{licenceNumber}/revoke", async (string licenceNumber, long companyId, string reason, One4All.Application.Abstractions.Municipal.ICouncilLicensingService licensing, CancellationToken ct) =>
    ActionResponse(await licensing.RevokeAsync(companyId, licenceNumber, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RevokeCouncilLicence");

app.MapGet("/api/municipal/licences/renewals-due", async (long companyId, DateOnly asAtDate, int withinDays, One4All.Application.Abstractions.Municipal.ICouncilLicensingService licensing, CancellationToken ct) =>
    Results.Ok(await licensing.RenewalsDueAsync(companyId, asAtDate, withinDays, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LicenceRenewalsDue");

app.MapPost("/api/municipal/valuation-rolls", async (long companyId, string rollName, DateOnly effectiveFrom, IReadOnlyList<One4All.Application.Abstractions.Municipal.RollEntry> entries, One4All.Application.Abstractions.Municipal.IValuationRollService roll, CancellationToken ct) =>
    ValueResponse(await roll.ImportRollAsync(companyId, rollName, effectiveFrom, entries, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ImportValuationRoll");

app.MapPost("/api/municipal/properties/{propertyId:long}/supplementary-valuations", async (long propertyId, long companyId, DateOnly effectiveFrom, decimal value, string reason, DateOnly adjustmentDate, One4All.Application.Abstractions.Municipal.IValuationRollService roll, CancellationToken ct) =>
    ValueResponse(await roll.RecordSupplementaryAsync(companyId, propertyId, effectiveFrom, value, reason, adjustmentDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordSupplementaryValuation");

app.MapGet("/api/municipal/properties/{propertyId:long}/valuations", async (long propertyId, long companyId, One4All.Application.Abstractions.Municipal.IValuationRollService roll, CancellationToken ct) =>
    Results.Ok(await roll.HistoryAsync(companyId, propertyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ValuationHistory");

app.MapPost("/api/municipal/objections", async (long companyId, long propertyId, DateOnly lodgedOn, decimal claimedValue, string grounds, One4All.Application.Abstractions.Municipal.IValuationRollService roll, CancellationToken ct) =>
    ValueResponse(await roll.LodgeObjectionAsync(companyId, propertyId, lodgedOn, claimedValue, grounds, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LodgeValuationObjection");

app.MapPost("/api/municipal/objections/{objectionNumber}/decide", async (string objectionNumber, long companyId, DateOnly decidedOn, decimal? revisedValue, string notes, One4All.Application.Abstractions.Municipal.IValuationRollService roll, CancellationToken ct) =>
    ValueResponse(await roll.DecideObjectionAsync(companyId, objectionNumber, decidedOn, revisedValue, notes, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DecideValuationObjection");

app.MapGet("/api/municipal/objections", async (long companyId, One4All.Core.Municipal.ObjectionStatus? status, One4All.Application.Abstractions.Municipal.IValuationRollService roll, CancellationToken ct) =>
    Results.Ok(await roll.ObjectionsAsync(companyId, status, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ValuationObjectionsRegister");

app.MapPost("/api/municipal/meters", async (long companyId, string serial, int digits, decimal multiplier, decimal startReading, DateOnly date, One4All.Application.Abstractions.Municipal.IMeterReadingService meters, CancellationToken ct) =>
    ValueResponse(await meters.InstallAsync(companyId, serial, digits, multiplier, startReading, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("InstallMeter");

app.MapPost("/api/municipal/meters/{meterId:long}/readings", async (long meterId, long companyId, DateOnly date, decimal reading, bool? tamperSuspected, One4All.Application.Abstractions.Municipal.IMeterReadingService meters, CancellationToken ct) =>
    ValueResponse(await meters.CaptureAsync(companyId, meterId, date, reading, tamperSuspected ?? false, null, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CaptureMeterReading");

app.MapPost("/api/municipal/meters/{meterId:long}/estimate", async (long meterId, long companyId, DateOnly date, One4All.Application.Abstractions.Municipal.IMeterReadingService meters, CancellationToken ct) =>
    ValueResponse(await meters.EstimateAsync(companyId, meterId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("EstimateMeterReading");

app.MapPost("/api/municipal/meters/{meterId:long}/replace", async (long meterId, long companyId, DateOnly date, decimal finalReading, string newSerial, int newDigits, decimal newMultiplier, decimal newStartReading, One4All.Application.Abstractions.Municipal.IMeterReadingService meters, CancellationToken ct) =>
    ValueResponse(await meters.ReplaceAsync(companyId, meterId, date, finalReading, newSerial, newDigits, newMultiplier, newStartReading, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReplaceMeter");

app.MapGet("/api/municipal/meters/{meterId:long}/readings", async (long meterId, long companyId, One4All.Application.Abstractions.Municipal.IMeterReadingService meters, CancellationToken ct) =>
    Results.Ok(await meters.HistoryAsync(companyId, meterId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("MeterConsumptionHistory");

app.MapGet("/api/municipal/reading-exceptions", async (long companyId, DateOnly start, DateOnly end, One4All.Application.Abstractions.Municipal.IMeterReadingService meters, CancellationToken ct) =>
    Results.Ok(await meters.ExceptionsAsync(companyId, start, end, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("MeterReadingExceptions");

app.MapGet("/api/receipting/arrangements/{arrangementId:long}/schedule", async (long arrangementId, long companyId, One4All.Application.Abstractions.Receipting.IArrangementMonitor monitor, CancellationToken ct) =>
    ValueResponse(await monitor.ScheduleAsync(companyId, arrangementId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ArrangementSchedule");

app.MapPost("/api/receipting/arrangements/review", async (long companyId, DateOnly asAtDate, int graceDays, int missedToDefault, One4All.Application.Abstractions.Receipting.IArrangementMonitor monitor, CancellationToken ct) =>
    Results.Ok(await monitor.ReviewAsync(companyId, asAtDate, graceDays, missedToDefault, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReviewArrangements");

app.MapPost("/api/municipal/prepaid/vend", async (One4All.Application.Abstractions.Municipal.PrepaidVendRequest request, One4All.Application.Abstractions.Municipal.IPrepaidVendingService prepaid, CancellationToken ct) =>
    ValueResponse(await prepaid.VendAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("VendPrepaid");

app.MapGet("/api/municipal/prepaid/sales", async (long companyId, DateOnly start, DateOnly end, One4All.Application.Abstractions.Municipal.IPrepaidVendingService prepaid, CancellationToken ct) =>
    Results.Ok(await prepaid.SalesAsync(companyId, start, end, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PrepaidSales");

app.MapPost("/api/municipal/properties/{propertyId:long}/owners", async (long propertyId, long companyId, DateOnly ownedFrom, string? deedsReference, IReadOnlyList<One4All.Application.Municipal.OwnerShare> owners, One4All.Application.Abstractions.Municipal.IOwnershipService ownership, CancellationToken ct) =>
    ActionResponse(await ownership.RegisterOwnersAsync(companyId, propertyId, owners, ownedFrom, deedsReference, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RegisterPropertyOwners");

app.MapPost("/api/municipal/properties/{propertyId:long}/transfer", async (long propertyId, long companyId, DateOnly transferDate, string clearanceNumber, string? deedsReference, IReadOnlyList<One4All.Application.Municipal.OwnerShare> buyers, One4All.Application.Abstractions.Municipal.IOwnershipService ownership, CancellationToken ct) =>
    ActionResponse(await ownership.TransferAsync(companyId, propertyId, buyers, transferDate, clearanceNumber, deedsReference, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TransferProperty");

app.MapGet("/api/municipal/properties/{propertyId:long}/owners", async (long propertyId, long companyId, DateOnly? asAtDate, One4All.Application.Abstractions.Municipal.IOwnershipService ownership, CancellationToken ct) =>
    Results.Ok(asAtDate is DateOnly date
        ? await ownership.OwnersAsync(companyId, propertyId, date, ct).ConfigureAwait(false)
        : await ownership.HistoryAsync(companyId, propertyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PropertyOwners");

app.MapPost("/api/municipal/billing-runs", async (One4All.Application.Abstractions.Municipal.BillingRunProposal proposal, One4All.Application.Abstractions.Municipal.IBillingRunService runs, CancellationToken ct) =>
    ValueResponse(await runs.ProposeAsync(proposal, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ProposeBillingRun");

app.MapGet("/api/municipal/billing-runs/{runNumber}/lines", async (string runNumber, long companyId, bool? exceptionsOnly, One4All.Application.Abstractions.Municipal.IBillingRunService runs, CancellationToken ct) =>
    ValueResponse(await runs.LinesAsync(companyId, runNumber, exceptionsOnly ?? false, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("BillingRunLines");

app.MapPost("/api/municipal/billing-runs/{runNumber}/lines/{propertyId:long}", async (string runNumber, long propertyId, long companyId, bool included, One4All.Application.Abstractions.Municipal.IBillingRunService runs, CancellationToken ct) =>
    ValueResponse(await runs.SetIncludedAsync(companyId, runNumber, propertyId, included, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SetBillingRunLine");

app.MapPost("/api/municipal/billing-runs/{runNumber}/approve", async (string runNumber, long companyId, long userId, One4All.Application.Abstractions.Municipal.IBillingRunService runs, CancellationToken ct) =>
    ValueResponse(await runs.ApproveAsync(companyId, runNumber, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveBillingRun");

app.MapPost("/api/municipal/billing-runs/{runNumber}/cancel", async (string runNumber, long companyId, One4All.Application.Abstractions.Municipal.IBillingRunService runs, CancellationToken ct) =>
    ActionResponse(await runs.CancelAsync(companyId, runNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CancelBillingRun");

app.MapGet("/api/municipal/properties/{propertyId:long}/enquiry", async (long propertyId, long companyId, DateOnly asAtDate, int? recentTransactions, One4All.Application.Abstractions.Municipal.IPropertyEnquiryService enquiry, CancellationToken ct) =>
    ValueResponse(await enquiry.EnquireAsync(companyId, propertyId, asAtDate, recentTransactions ?? 10, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PropertyEnquiry");

app.MapPost("/api/municipal/reading-routes", async (long companyId, string code, string name, string readerName, IReadOnlyList<long> meterIds, One4All.Application.Abstractions.Municipal.IReadingRouteService routes, CancellationToken ct) =>
    ValueResponse(await routes.CreateRouteAsync(companyId, code, name, readerName, meterIds, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreateReadingRoute");

app.MapGet("/api/municipal/reading-routes/{code}/sheet", async (string code, long companyId, One4All.Application.Abstractions.Municipal.IReadingRouteService routes, CancellationToken ct) =>
    ValueResponse(await routes.RouteSheetAsync(companyId, code, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReadingRouteSheet");

app.MapPost("/api/municipal/readings/import", async (long companyId, HttpRequest http, One4All.Application.Abstractions.Municipal.IReadingRouteService routes, CancellationToken ct) =>
{
    using StreamReader reader = new(http.Body);
    string csv = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    return Results.Ok(await routes.ImportReadingsAsync(companyId, csv, ct).ConfigureAwait(false));
})
.RequireAuthorization()
.WithName("ImportMeterReadings");

app.MapPost("/api/municipal/deposits", async (One4All.Application.Abstractions.Municipal.TakeDepositRequest request, One4All.Application.Abstractions.Municipal.IServiceDepositService deposits, CancellationToken ct) =>
    ValueResponse(await deposits.TakeAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TakeServiceDeposit");

app.MapPost("/api/municipal/deposits/{reference}/refund", async (string reference, long companyId, DateOnly date, string bankAccountCode, One4All.Application.Abstractions.Municipal.IServiceDepositService deposits, CancellationToken ct) =>
    ValueResponse(await deposits.RefundAsync(companyId, reference, date, bankAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RefundServiceDeposit");

app.MapPost("/api/municipal/deposits/{reference}/apply", async (string reference, long companyId, DateOnly date, One4All.Application.Abstractions.Municipal.IServiceDepositService deposits, CancellationToken ct) =>
    ValueResponse(await deposits.ApplyToArrearsAsync(companyId, reference, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApplyServiceDeposit");

app.MapGet("/api/municipal/properties/{propertyId:long}/deposits", async (long propertyId, long companyId, One4All.Application.Abstractions.Municipal.IServiceDepositService deposits, CancellationToken ct) =>
    Results.Ok(await deposits.HeldAsync(companyId, propertyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("HeldServiceDeposits");

app.MapGet("/api/municipal/tariffs", async (long companyId, One4All.Core.Municipal.ServiceType serviceType, DateOnly date, One4All.Application.Abstractions.Municipal.ITariffCatalogueService catalogue, CancellationToken ct) =>
    Results.Ok(await catalogue.ListAsync(companyId, serviceType, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TariffCatalogue");

app.MapGet("/api/municipal/tariffs/resolve", async (long companyId, One4All.Core.Municipal.ServiceType serviceType, One4All.Core.Municipal.ConsumerCategory category, string? ward, DateOnly date, One4All.Application.Abstractions.Municipal.ITariffCatalogueService catalogue, CancellationToken ct) =>
    ValueResponse(await catalogue.ResolveAsync(companyId, serviceType, category, ward, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ResolveTariff");

app.MapPost("/api/municipal/facilities", async (long companyId, string code, string name, decimal dailyRate, string revenueAccountCode, One4All.Application.Abstractions.Municipal.IFacilityBookingService facilities, CancellationToken ct) =>
    ValueResponse(await facilities.CreateFacilityAsync(companyId, code, name, dailyRate, revenueAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreateFacility");

app.MapPost("/api/municipal/facility-bookings", async (One4All.Application.Abstractions.Municipal.FacilityBookingRequest request, One4All.Application.Abstractions.Municipal.IFacilityBookingService facilities, CancellationToken ct) =>
    ValueResponse(await facilities.BookAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("BookFacility");

app.MapPost("/api/municipal/facility-bookings/{bookingNumber}/cancel", async (string bookingNumber, long companyId, DateOnly date, decimal refundPercent, One4All.Application.Abstractions.Municipal.IFacilityBookingService facilities, CancellationToken ct) =>
    ValueResponse(await facilities.CancelAsync(companyId, bookingNumber, date, refundPercent, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CancelFacilityBooking");

app.MapGet("/api/municipal/facilities/{code}/calendar", async (string code, long companyId, DateOnly start, DateOnly end, One4All.Application.Abstractions.Municipal.IFacilityBookingService facilities, CancellationToken ct) =>
    ValueResponse(await facilities.CalendarAsync(companyId, code, start, end, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("FacilityCalendar");

app.MapPost("/api/municipal/cemeteries/{cemetery}/plots", async (string cemetery, long companyId, string section, decimal price, int capacity, IReadOnlyList<string> plotNumbers, One4All.Application.Abstractions.Municipal.ICemeteryService cemeteries, CancellationToken ct) =>
    ValueResponse(await cemeteries.AddPlotsAsync(companyId, cemetery, section, plotNumbers, price, capacity, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AddCemeteryPlots");

app.MapGet("/api/municipal/cemeteries/{cemetery}/plots", async (string cemetery, long companyId, One4All.Core.Municipal.CemeteryPlotStatus? status, One4All.Application.Abstractions.Municipal.ICemeteryService cemeteries, CancellationToken ct) =>
    Results.Ok(await cemeteries.PlotsAsync(companyId, cemetery, status, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CemeteryPlots");

app.MapPost("/api/municipal/cemetery-plots/{plotReference}/sell", async (string plotReference, long companyId, long holderCustomerId, DateOnly date, string revenueAccountCode, One4All.Application.Abstractions.Municipal.ICemeteryService cemeteries, CancellationToken ct) =>
    ValueResponse(await cemeteries.SellAsync(companyId, plotReference, holderCustomerId, date, revenueAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SellCemeteryPlot");

app.MapPost("/api/municipal/interments", async (One4All.Application.Abstractions.Municipal.IntermentRequest request, One4All.Application.Abstractions.Municipal.ICemeteryService cemeteries, CancellationToken ct) =>
    ValueResponse(await cemeteries.RecordIntermentAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordInterment");

app.MapGet("/api/municipal/burial-register", async (long companyId, DateOnly start, DateOnly end, One4All.Application.Abstractions.Municipal.ICemeteryService cemeteries, CancellationToken ct) =>
    Results.Ok(await cemeteries.BurialRegisterAsync(companyId, start, end, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("BurialRegister");

app.MapPost("/api/ar/debt-cases", async (long companyId, long customerId, string handedOverTo, DateOnly date, One4All.Application.Abstractions.Trading.IDebtCollectionService debts, CancellationToken ct) =>
    ValueResponse(await debts.HandOverAsync(companyId, customerId, handedOverTo, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("HandOverDebt");

app.MapPost("/api/ar/debt-cases/{caseNumber}/summons", async (string caseNumber, long companyId, string summonsNumber, string court, DateOnly issuedOn, DateOnly? hearingDate, One4All.Application.Abstractions.Trading.IDebtCollectionService debts, CancellationToken ct) =>
    ActionResponse(await debts.RecordSummonsAsync(companyId, caseNumber, summonsNumber, court, issuedOn, hearingDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordSummons");

app.MapGet("/api/ar/summons-register", async (long companyId, One4All.Application.Abstractions.Trading.IDebtCollectionService debts, CancellationToken ct) =>
    Results.Ok(await debts.SummonsRegisterAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SummonsRegister");

app.MapPost("/api/ar/debt-cases/{caseNumber}/write-off/request", async (string caseNumber, long companyId, long userId, string reason, One4All.Application.Abstractions.Trading.IDebtCollectionService debts, CancellationToken ct) =>
    ActionResponse(await debts.RequestWriteOffAsync(companyId, caseNumber, userId, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RequestDebtWriteOff");

app.MapPost("/api/ar/debt-cases/{caseNumber}/write-off/approve", async (string caseNumber, long companyId, long userId, DateOnly date, string badDebtAccountCode, One4All.Application.Abstractions.Trading.IDebtCollectionService debts, CancellationToken ct) =>
    ValueResponse(await debts.ApproveWriteOffAsync(companyId, caseNumber, userId, date, badDebtAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveDebtWriteOff");

app.MapPost("/api/ar/debt-cases/{caseNumber}/close", async (string caseNumber, long companyId, One4All.Core.Trading.DebtCaseStage outcome, DateOnly date, One4All.Application.Abstractions.Trading.IDebtCollectionService debts, CancellationToken ct) =>
    ActionResponse(await debts.CloseAsync(companyId, caseNumber, outcome, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CloseDebtCase");

app.MapGet("/api/gl/accounts/{accountCode}/activity", async (long companyId, string accountCode, DateOnly from, DateOnly to, One4All.Application.Abstractions.Reporting.IGeneralLedgerReports gl, CancellationToken ct) =>
    Results.Ok(await gl.NominalActivityAsync(companyId, accountCode, from, to, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AccountActivity");

app.MapGet("/api/gl/journals/{journalNumber}", async (long companyId, string journalNumber, One4All.Application.Abstractions.Reporting.IGeneralLedgerReports gl, CancellationToken ct) =>
    await gl.JournalDetailAsync(companyId, journalNumber, ct).ConfigureAwait(false) is { } journal
        ? Results.Ok(journal)
        : Results.Json(new { error = $"Journal {journalNumber} not found." }, statusCode: StatusCodes.Status404NotFound))
.RequireAuthorization()
.WithName("JournalDetail");

app.MapGet("/api/audit", async (long companyId, DateOnly? from, DateOnly? to, string? entityType, string? entityId, string? userName, One4All.Core.Audit.AuditAction? action, int? take, IAuditService audit, CancellationToken ct) =>
    Results.Ok(await audit.QueryAsync(new AuditQuery { CompanyId = companyId, From = from, To = to, EntityType = entityType, EntityId = entityId, UserName = userName, Action = action, Take = take ?? 500 }, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AuditTrail");

app.MapGet("/api/audit/verify", async (long? companyId, IAuditService audit, CancellationToken ct) =>
    Results.Ok(await audit.VerifyChainAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("VerifyAuditChain");

app.MapGet("/api/accounting/period-close", async (long companyId, DateOnly periodEnd, One4All.Application.Abstractions.Accounting.IPeriodCloseService close, CancellationToken ct) =>
    Results.Ok(await close.ChecklistAsync(companyId, periodEnd, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PeriodCloseChecklist");

app.MapPost("/api/accounting/release-deferred", async (long companyId, DateOnly upToDate, One4All.Application.Abstractions.Accounting.IPostingEngine engine, CancellationToken ct) =>
    Results.Ok(await engine.ReleaseAllDeferredAsync(companyId, upToDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReleaseDeferredJournals");

app.MapPost("/api/gl/journals/submit", async (long userId, One4All.Application.Abstractions.Accounting.JournalRequest request, One4All.Application.Abstractions.Accounting.IJournalApprovalService approvals, CancellationToken ct) =>
    ValueResponse(await approvals.SubmitAsync(request, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SubmitManualJournal");

app.MapGet("/api/gl/journals/awaiting-approval", async (long companyId, One4All.Application.Abstractions.Accounting.IJournalApprovalService approvals, CancellationToken ct) =>
    Results.Ok(await approvals.AwaitingApprovalAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("JournalsAwaitingApproval");

app.MapPost("/api/gl/journals/{journalId:long}/approve", async (long journalId, long companyId, long userId, One4All.Application.Abstractions.Accounting.IJournalApprovalService approvals, CancellationToken ct) =>
    ValueResponse(await approvals.ApproveAsync(companyId, journalId, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveJournal");

app.MapPost("/api/gl/journals/{journalId:long}/reject", async (long journalId, long companyId, string reason, One4All.Application.Abstractions.Accounting.IJournalApprovalService approvals, CancellationToken ct) =>
    ActionResponse(await approvals.RejectAsync(companyId, journalId, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RejectJournal");

app.MapPost("/api/gl/journals/import", async (long companyId, long userId, HttpRequest http, One4All.Application.Abstractions.Accounting.IJournalImportService import, CancellationToken ct) =>
{
    using StreamReader reader = new(http.Body, Encoding.UTF8);
    string csv = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    return ValueResponse(await import.ImportAsync(companyId, csv, userId, ct).ConfigureAwait(false));
})
.RequireAuthorization()
.Accepts<string>("text/csv", "text/plain")
.WithName("ImportJournals");

app.MapPost("/api/gl/allocation-rules", async (One4All.Application.Abstractions.Accounting.AllocationRuleRequest request, One4All.Application.Abstractions.Accounting.IOverheadAllocationService allocation, CancellationToken ct) =>
    ValueResponse(await allocation.CreateRuleAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreateAllocationRule");

app.MapPut("/api/gl/allocation-rules/{ruleId:long}/drivers", async (long ruleId, long companyId, One4All.Application.Abstractions.Accounting.AllocationTargetRequest[] targets, One4All.Application.Abstractions.Accounting.IOverheadAllocationService allocation, CancellationToken ct) =>
    ActionResponse(await allocation.SetDriversAsync(companyId, ruleId, targets, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SetAllocationDrivers");

app.MapGet("/api/gl/allocations/preview", async (long companyId, DateOnly periodDate, One4All.Application.Abstractions.Accounting.IOverheadAllocationService allocation, CancellationToken ct) =>
    ValueResponse(await allocation.PreviewAsync(companyId, periodDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PreviewAllocations");

app.MapPost("/api/gl/allocations/run", async (long companyId, DateOnly periodDate, One4All.Application.Abstractions.Accounting.IOverheadAllocationService allocation, CancellationToken ct) =>
    ValueResponse(await allocation.RunAsync(companyId, periodDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RunAllocations");

app.MapPut("/api/accounting/ias29/index", async (long companyId, string indexCode, DateOnly month, decimal value, One4All.Application.Abstractions.Accounting.IHyperinflationService ias29, CancellationToken ct) =>
    ActionResponse(await ias29.SetIndexAsync(companyId, indexCode, month, value, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SetPriceIndex");

app.MapGet("/api/accounting/ias29/trial-balance", async (long companyId, DateOnly asAtDate, string? indexCode, One4All.Application.Abstractions.Accounting.IHyperinflationService ias29, CancellationToken ct) =>
    ValueResponse(await ias29.RestatedTrialBalanceAsync(companyId, asAtDate, indexCode ?? "CPI", ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RestatedTrialBalance");

app.MapPost("/api/ap/suppliers/{supplierId:long}/bank-change", async (long supplierId, long companyId, long userId, One4All.Application.Abstractions.Trading.SupplierBankDetails details, One4All.Application.Abstractions.Trading.ISupplierBankDetailsService bank, CancellationToken ct) =>
    ValueResponse(await bank.RequestChangeAsync(companyId, supplierId, details, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RequestSupplierBankChange");

app.MapGet("/api/ap/bank-changes", async (long companyId, One4All.Application.Abstractions.Trading.ISupplierBankDetailsService bank, CancellationToken ct) =>
    Results.Ok(await bank.PendingAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PendingSupplierBankChanges");

app.MapPost("/api/ap/bank-changes/{changeId:long}/approve", async (long changeId, long companyId, long userId, One4All.Application.Abstractions.Trading.ISupplierBankDetailsService bank, CancellationToken ct) =>
    ActionResponse(await bank.ApproveAsync(companyId, changeId, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveSupplierBankChange");

app.MapPost("/api/ap/bank-changes/{changeId:long}/reject", async (long changeId, long companyId, long userId, string reason, One4All.Application.Abstractions.Trading.ISupplierBankDetailsService bank, CancellationToken ct) =>
    ActionResponse(await bank.RejectAsync(companyId, changeId, userId, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RejectSupplierBankChange");

app.MapPost("/api/budgets/{budgetId:long}/virements", async (long budgetId, long companyId, long userId, One4All.Application.Abstractions.Budgeting.VirementRequest request, One4All.Application.Abstractions.Budgeting.IBudgetControlService control, CancellationToken ct) =>
    ValueResponse(await control.RequestVirementAsync(companyId, budgetId, request, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RequestVirement");

app.MapPost("/api/budgets/virements/{virementId:long}/approve", async (long virementId, long companyId, long userId, One4All.Application.Abstractions.Budgeting.IBudgetControlService control, CancellationToken ct) =>
    ActionResponse(await control.ApproveVirementAsync(companyId, virementId, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveVirement");

app.MapPost("/api/budgets/virements/{virementId:long}/reject", async (long virementId, long companyId, long userId, string reason, One4All.Application.Abstractions.Budgeting.IBudgetControlService control, CancellationToken ct) =>
    ActionResponse(await control.RejectVirementAsync(companyId, virementId, userId, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RejectVirement");

app.MapGet("/api/budgets/{budgetId:long}/virement-register", async (long budgetId, long companyId, One4All.Application.Abstractions.Budgeting.IBudgetControlService control, CancellationToken ct) =>
    ValueResponse(await control.VirementRegisterAsync(companyId, budgetId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("VirementRegister");

app.MapGet("/api/budgets/vote-book", async (long companyId, long fiscalYearId, int asAtPeriod, One4All.Application.Abstractions.Budgeting.IBudgetControlService control, CancellationToken ct) =>
    Results.Ok(await control.VoteBookAsync(companyId, fiscalYearId, asAtPeriod, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("VoteBook");

app.MapGet("/api/budgets/{budgetId:long}/verify-seal", async (long budgetId, long companyId, One4All.Application.Abstractions.Budgeting.IBudgetControlService control, CancellationToken ct) =>
    ValueResponse(await control.VerifySealAsync(companyId, budgetId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("VerifyBudgetSeal");

app.MapPost("/api/budgets/commitments/year-end", async (long companyId, long fiscalYearId, long nextFiscalYearId, long userId, long[] carryForwardIds, One4All.Application.Abstractions.Budgeting.IBudgetControlService control, CancellationToken ct) =>
    ValueResponse(await control.CloseYearCommitmentsAsync(companyId, fiscalYearId, nextFiscalYearId, carryForwardIds, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CloseYearCommitments");

app.MapGet("/api/budgets/utilisation-alerts", async (long companyId, long fiscalYearId, int asAtPeriod, decimal[]? thresholds, One4All.Application.Abstractions.Budgeting.IBudgetControlService control, CancellationToken ct) =>
    Results.Ok(await control.UtilisationAlertsAsync(companyId, fiscalYearId, asAtPeriod,
        thresholds is { Length: > 0 } ? thresholds : One4All.Application.Budgeting.BudgetUtilisation.DefaultThresholds, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("BudgetUtilisationAlerts");

app.MapPost("/api/ar/laybys", async (One4All.Application.Abstractions.Trading.LaybyRequest request, One4All.Application.Abstractions.Trading.ILaybyService laybys, CancellationToken ct) =>
    ValueResponse(await laybys.OpenAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("OpenLayby");

app.MapPost("/api/ar/laybys/{planId:long}/pay", async (long planId, long companyId, DateOnly date, decimal amount, string bankAccountCode, One4All.Application.Abstractions.Trading.ILaybyService laybys, CancellationToken ct) =>
    ValueResponse(await laybys.PayAsync(companyId, planId, date, amount, bankAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PayLayby");

app.MapPost("/api/ar/laybys/{planId:long}/cancel", async (long planId, long companyId, DateOnly date, string refundAccountCode, One4All.Application.Abstractions.Trading.ILaybyService laybys, CancellationToken ct) =>
    ValueResponse(await laybys.CancelAsync(companyId, planId, date, refundAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CancelLayby");

app.MapPost("/api/ar/laybys/default-overdue", async (long companyId, DateOnly asAtDate, string refundAccountCode, One4All.Application.Abstractions.Trading.ILaybyService laybys, CancellationToken ct) =>
    ValueResponse(await laybys.DefaultOverdueAsync(companyId, asAtDate, refundAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DefaultOverdueLaybys");

app.MapPost("/api/ar/customers/{customerId:long}/credit-limit/lower", async (long customerId, long companyId, decimal limit, One4All.Application.Abstractions.Trading.ICreditLimitService limits, CancellationToken ct) =>
    ActionResponse(await limits.LowerAsync(companyId, customerId, limit, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LowerCreditLimit");

app.MapPost("/api/ar/customers/{customerId:long}/credit-limit/request", async (long customerId, long companyId, decimal limit, string reason, long userId, One4All.Application.Abstractions.Trading.ICreditLimitService limits, CancellationToken ct) =>
    ValueResponse(await limits.RequestIncreaseAsync(companyId, customerId, limit, reason, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RequestCreditLimitIncrease");

app.MapGet("/api/ar/credit-limit-requests", async (long companyId, One4All.Application.Abstractions.Trading.ICreditLimitService limits, CancellationToken ct) =>
    Results.Ok(await limits.PendingAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PendingCreditLimitRequests");

app.MapPost("/api/ar/credit-limit-requests/{requestId:long}/approve", async (long requestId, long companyId, long userId, One4All.Application.Abstractions.Trading.ICreditLimitService limits, CancellationToken ct) =>
    ActionResponse(await limits.ApproveAsync(companyId, requestId, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveCreditLimitRequest");

app.MapPost("/api/ar/credit-limit-requests/{requestId:long}/reject", async (long requestId, long companyId, long userId, string reason, One4All.Application.Abstractions.Trading.ICreditLimitService limits, CancellationToken ct) =>
    ActionResponse(await limits.RejectAsync(companyId, requestId, userId, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RejectCreditLimitRequest");

app.MapGet("/api/ar/sales-reps/{salesRepId:long}/commission-statement", async (long salesRepId, long companyId, DateOnly from, DateOnly to, One4All.Application.Abstractions.Trading.ICommissionService commission, CancellationToken ct) =>
    ValueResponse(await commission.StatementAsync(companyId, salesRepId, from, to, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CommissionStatement");

app.MapPost("/api/ar/commission/accrue", async (long companyId, DateOnly monthEnd, string expenseAccountCode, string payableAccountCode, One4All.Application.Abstractions.Trading.ICommissionService commission, CancellationToken ct) =>
    ValueResponse(await commission.AccrueAsync(companyId, monthEnd, expenseAccountCode, payableAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AccrueCommission");

app.MapGet("/api/pop/supplier-scorecard", async (long companyId, DateOnly from, DateOnly to, One4All.Application.Abstractions.Procurement.ISupplierScorecardService scorecard, CancellationToken ct) =>
    Results.Ok(await scorecard.ScorecardAsync(companyId, from, to, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SupplierScorecard");

app.MapPost("/api/pop/rfqs", async (long companyId, One4All.Application.Abstractions.Procurement.RfqRequest request, One4All.Application.Abstractions.Procurement.IRfqService rfqs, CancellationToken ct) =>
    ValueResponse(await rfqs.IssueAsync(companyId, request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("IssueRfq");

app.MapPost("/api/pop/rfqs/{rfqId:long}/quotes", async (long rfqId, long companyId, One4All.Application.Abstractions.Procurement.QuoteRequest quote, One4All.Application.Abstractions.Procurement.IRfqService rfqs, CancellationToken ct) =>
    ActionResponse(await rfqs.RecordQuoteAsync(companyId, rfqId, quote, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordRfqQuote");

app.MapGet("/api/pop/rfqs/{rfqId:long}/comparison", async (long rfqId, long companyId, decimal? priceWeight, One4All.Application.Abstractions.Procurement.IRfqService rfqs, CancellationToken ct) =>
    ValueResponse(await rfqs.CompareAsync(companyId, rfqId, priceWeight ?? One4All.Application.Procurement.TenderEvaluation.DefaultPriceWeight, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CompareRfqQuotes");

app.MapPost("/api/pop/rfqs/{rfqId:long}/award", async (long rfqId, long companyId, long supplierId, DateOnly orderDate, long userId, string? reason, One4All.Application.Abstractions.Procurement.IRfqService rfqs, CancellationToken ct) =>
    ValueResponse(await rfqs.AwardAsync(companyId, rfqId, supplierId, orderDate, userId, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AwardRfq");

app.MapPost("/api/pop/grns/{grnId:long}/inspection", async (long grnId, long companyId, One4All.Application.Abstractions.Procurement.InspectionRequest request, One4All.Application.Abstractions.Procurement.IQualityInspectionService qc, CancellationToken ct) =>
    ValueResponse(await qc.InspectAsync(companyId, grnId, request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("InspectGoodsReceived");

app.MapPost("/api/pop/invoices/submit", async (One4All.Application.Abstractions.Procurement.MatchInvoiceRequest request, One4All.Application.Abstractions.Procurement.IInvoiceDisputeService disputes, CancellationToken ct) =>
    ValueResponse(await disputes.SubmitAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SubmitSupplierInvoice");

app.MapGet("/api/pop/invoice-disputes", async (long companyId, One4All.Application.Abstractions.Procurement.IInvoiceDisputeService disputes, CancellationToken ct) =>
    Results.Ok(await disputes.OpenAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("OpenInvoiceDisputes");

app.MapPost("/api/pop/invoice-disputes/{disputeId:long}/accept", async (long disputeId, long companyId, long userId, string note, string priceVarianceAccountCode, One4All.Application.Abstractions.Procurement.IInvoiceDisputeService disputes, CancellationToken ct) =>
    ValueResponse(await disputes.AcceptAsync(companyId, disputeId, userId, note, priceVarianceAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AcceptInvoiceDispute");

app.MapPost("/api/pop/invoice-disputes/{disputeId:long}/reject", async (long disputeId, long companyId, long userId, string note, One4All.Application.Abstractions.Procurement.IInvoiceDisputeService disputes, CancellationToken ct) =>
    ActionResponse(await disputes.RejectAsync(companyId, disputeId, userId, note, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RejectInvoiceDispute");

app.MapPost("/api/pop/{purchaseOrderId:long}/amend", async (long purchaseOrderId, long companyId, One4All.Application.Abstractions.Procurement.AmendmentRequest request, One4All.Application.Abstractions.Procurement.IPurchaseOrderAmendmentService amendments, CancellationToken ct) =>
    ValueResponse(await amendments.AmendAsync(companyId, purchaseOrderId, request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AmendPurchaseOrder");

app.MapPost("/api/pop/{purchaseOrderId:long}/reapprove", async (long purchaseOrderId, long companyId, long userId, One4All.Application.Abstractions.Procurement.IPurchaseOrderAmendmentService amendments, CancellationToken ct) =>
    ActionResponse(await amendments.ReapproveAsync(companyId, purchaseOrderId, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReapprovePurchaseOrder");

app.MapGet("/api/pop/{purchaseOrderId:long}/history", async (long purchaseOrderId, long companyId, One4All.Application.Abstractions.Procurement.IPurchaseOrderAmendmentService amendments, CancellationToken ct) =>
    Results.Ok(await amendments.HistoryAsync(companyId, purchaseOrderId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PurchaseOrderHistory");

app.MapPost("/api/pop/rebate-agreements", async (long companyId, One4All.Application.Abstractions.Procurement.RebateAgreementRequest request, One4All.Application.Abstractions.Procurement.IRebateService rebates, CancellationToken ct) =>
    ValueResponse(await rebates.CreateAsync(companyId, request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreateRebateAgreement");

app.MapPost("/api/pop/rebates/accrue", async (long companyId, DateOnly asAtDate, string receivableAccountCode, string incomeAccountCode, One4All.Application.Abstractions.Procurement.IRebateService rebates, CancellationToken ct) =>
    ValueResponse(await rebates.AccrueAsync(companyId, asAtDate, receivableAccountCode, incomeAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AccrueRebates");

app.MapPost("/api/ar/items/{openItemId:long}/write-off-with-vat-relief", async (long openItemId, long companyId, DateOnly date, string badDebtAccountCode, string reason, int minimumDaysOverdue, One4All.Application.Abstractions.Trading.ISalesLedgerService ar, CancellationToken ct) =>
    ValueResponse(await ar.WriteOffWithVatReliefAsync(companyId, openItemId, date, badDebtAccountCode, reason, minimumDaysOverdue, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("WriteOffWithVatRelief");

app.MapGet("/api/budgets/material-variances", async (long companyId, long fiscalYearId, One4All.Core.Budgeting.BudgetType type, string? department, int asAtPeriod, decimal? thresholdValue, decimal? thresholdPercent, One4All.Application.Abstractions.Budgeting.IVarianceCommentaryService commentary, CancellationToken ct) =>
{
    One4All.Application.Budgeting.VarianceThreshold threshold = new(
        thresholdValue ?? One4All.Application.Budgeting.VarianceThreshold.Default.Value, thresholdPercent ?? One4All.Application.Budgeting.VarianceThreshold.Default.Percent);
    return ValueResponse(await commentary.MaterialVariancesAsync(companyId, fiscalYearId, type, department, asAtPeriod, threshold, ct).ConfigureAwait(false));
})
.RequireAuthorization()
.WithName("MaterialVariances");

app.MapPut("/api/budgets/variance-comments", async (long companyId, long fiscalYearId, int asAtPeriod, string accountCode, long userId, string comment, One4All.Application.Abstractions.Budgeting.IVarianceCommentaryService commentary, CancellationToken ct) =>
    ActionResponse(await commentary.CommentAsync(companyId, fiscalYearId, asAtPeriod, accountCode, comment, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CommentVariance");

app.MapPost("/api/ar/customers/{customerId:long}/promises", async (long customerId, long companyId, long userId, DateOnly madeOn, DateOnly payBy, decimal amount, string? note, One4All.Application.Abstractions.Trading.IPromiseToPayService promises, CancellationToken ct) =>
    ValueResponse(await promises.RecordAsync(companyId, customerId, madeOn, payBy, amount, note, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordPromiseToPay");

app.MapGet("/api/ar/promises", async (long companyId, One4All.Core.Trading.PromiseStatus? status, One4All.Application.Abstractions.Trading.IPromiseToPayService promises, CancellationToken ct) =>
    Results.Ok(await promises.DiaryAsync(companyId, status, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PromiseDiary");

app.MapPost("/api/ar/promises/review", async (long companyId, DateOnly asAtDate, One4All.Application.Abstractions.Trading.IPromiseToPayService promises, CancellationToken ct) =>
    Results.Ok(await promises.ReviewAsync(companyId, asAtDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReviewPromises");

app.MapGet("/api/consolidation/trial-balance", async (long[] companyIds, DateOnly asAtDate, string? presentationCurrency, string[]? intercompanyAccounts, One4All.Application.Abstractions.Reporting.IConsolidationService consolidation, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(presentationCurrency))
    {
        return ValueResponse(await consolidation.ConsolidatedTrialBalanceAsync(companyIds, asAtDate, intercompanyAccounts ?? [], ct).ConfigureAwait(false));
    }

    if (!One4All.Core.Accounting.CurrencyCode.TryParse(presentationCurrency, out One4All.Core.Accounting.CurrencyCode currency))
    {
        return Results.BadRequest(new { error = $"'{presentationCurrency}' is not a currency code." });
    }

    return ValueResponse(await consolidation.TranslatedTrialBalanceAsync(companyIds, currency, asAtDate, intercompanyAccounts, ct).ConfigureAwait(false));
})
.RequireAuthorization()
.WithName("ConsolidatedTrialBalance");

app.MapPost("/api/accounting/deferred-income/recognise", async (long companyId, DateOnly asAtDate, One4All.Application.Abstractions.Accounting.IDeferredIncomeService deferred, CancellationToken ct) =>
    ValueResponse(await deferred.RecogniseAsync(companyId, asAtDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecogniseDeferredIncome");

app.MapGet("/api/accounting/check-data", async (long companyId, One4All.Application.Abstractions.Accounting.ILedgerIntegrityService integrity, CancellationToken ct) =>
    ValueResponse(await integrity.CheckAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CheckData");

app.MapPost("/api/accounting/check-data/rebuild-period-balances", async (long companyId, One4All.Application.Abstractions.Accounting.ILedgerIntegrityService integrity, CancellationToken ct) =>
    ValueResponse(await integrity.RebuildPeriodBalancesAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RebuildPeriodBalances");

// --- Intercompany and job-costing period-end runs ----------------------------------------------------------
app.MapPost("/api/interco/post", async (One4All.Application.Abstractions.Accounting.IntercompanyJournalRequest request, One4All.Application.Abstractions.Accounting.IIntercompanyService interco, CancellationToken ct) =>
    ValueResponse(await interco.PostAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("IntercompanyPost");

app.MapPost("/api/jobs/{contractId:long}/recognise-revenue", async (long companyId, long contractId, DateOnly date, string contractAssetAccount, string contractLiabilityAccount, string documentNumber, One4All.Application.Abstractions.JobCosting.IJobCostingService jobs, CancellationToken ct) =>
    ValueResponse(await jobs.RecogniseRevenueAsync(companyId, contractId, date, contractAssetAccount, contractLiabilityAccount, documentNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("JobRecogniseRevenue");

app.MapPost("/api/jobs/{contractId:long}/expected-loss", async (long companyId, long contractId, DateOnly date, string lossExpenseAccount, string provisionAccount, string documentNumber, One4All.Application.Abstractions.JobCosting.IJobCostingService jobs, CancellationToken ct) =>
    ValueResponse(await jobs.ProvideForExpectedLossAsync(companyId, contractId, date, lossExpenseAccount, provisionAccount, documentNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("JobExpectedLoss");

app.MapPost("/api/jobs/{contractId:long}/revise-budget", async (long companyId, long contractId, decimal newBudgetCost, string reason, One4All.Application.Abstractions.JobCosting.IJobCostingService jobs, CancellationToken ct) =>
    ActionResponse(await jobs.ReviseBudgetAsync(companyId, contractId, newBudgetCost, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("JobReviseBudget");

app.Run();

// Maps a text-bearing Result to a plain-text response (200), or 404 / 409 / 400 by error type.
static IResult TextResponse(Result<string> result)
{
    if (result.IsSuccess)
    {
        return Results.Text(result.Value, "text/plain", Encoding.UTF8);
    }

    int status = result.Error.Type switch
    {
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Rule => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };
    return Results.Json(new { error = result.Error.Message }, statusCode: status);
}

// Maps a non-value Result to an HTTP response: 200 ok, 404 not-found, 409 rule violation, else 400.
static IResult ActionResponse(Result result)
{
    if (result.IsSuccess)
    {
        return Results.Ok(new { ok = true });
    }

    int status = result.Error.Type switch
    {
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Rule => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };
    return Results.Json(new { error = result.Error.Message }, statusCode: status);
}

// Maps a value-bearing Result to an HTTP response: 200 with the value, 404 for not-found, else 400.
// The stock provision policy from query parameters, each defaulting to the standard policy.
static One4All.Application.Inventory.StockProvisionPolicy ProvisionPolicy(decimal? costToSellPercent, int? slowMovingDays, decimal? slowMovingPercent, int? obsoleteDays, decimal? obsoletePercent)
{
    One4All.Application.Inventory.StockProvisionPolicy standard = One4All.Application.Inventory.StockProvisionPolicy.Default;
    return new One4All.Application.Inventory.StockProvisionPolicy(
        costToSellPercent ?? standard.CostToSellPercent,
        slowMovingDays ?? standard.SlowMovingDays,
        slowMovingPercent ?? standard.SlowMovingPercent,
        obsoleteDays ?? standard.ObsoleteDays,
        obsoletePercent ?? standard.ObsoletePercent);
}

static IResult ValueResponse<T>(Result<T> result)
{
    if (result.IsSuccess)
    {
        return Results.Ok(result.Value);
    }

    int status = result.Error.Type == ErrorType.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
    return Results.Json(new { error = result.Error.Message }, statusCode: status);
}

// Maps an offline admin Result to an HTTP response (404 not-found, 409 rule violation, 200 success).
static IResult OfflineActionResult(Result result)
{
    if (result.IsSuccess)
    {
        return Results.Ok(new { ok = true });
    }

    int status = result.Error.Type == ErrorType.NotFound
        ? StatusCodes.Status404NotFound
        : StatusCodes.Status409Conflict;
    return Results.Json(new { error = result.Error.Message }, statusCode: status);
}

/// <summary>The login request body.</summary>
/// <param name="Username">The login name.</param>
/// <param name="Password">The plaintext password.</param>
internal sealed record LoginRequest(string Username, string Password);
