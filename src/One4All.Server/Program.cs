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
