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
// Licensing (brief §11.5): trusted vendor keys from Licensing:TrustedKeys, enforced unless Licensing:Enforce is false. Development
// also trusts the development key and falls back to the development licence.
One4All.Infrastructure.Licensing.LicensingOptions licensing = new();
builder.Configuration.GetSection(One4All.Infrastructure.Licensing.LicensingOptions.SectionName).Bind(licensing);
licensing.Enforce = builder.Configuration.GetValue("Licensing:Enforce", true);
if (builder.Environment.IsDevelopment())
{
    licensing.TrustedKeys[One4All.Infrastructure.Licensing.DevLicensing.KeyId] = One4All.Infrastructure.Licensing.DevLicensing.PublicKeyBase64;
}

// Messaging (brief §8): SMTP and SMS provider settings from the Messaging section; secrets belong in user secrets or environment variables.
One4All.Infrastructure.Communications.MessagingOptions messaging = new();
builder.Configuration.GetSection(One4All.Infrastructure.Communications.MessagingOptions.SectionName).Bind(messaging);

// Backups (brief §9): folder, off-site copy folder, pg tools folder and retention from the Backup section.
One4All.Infrastructure.Backup.BackupOptions backup = new();
builder.Configuration.GetSection(One4All.Infrastructure.Backup.BackupOptions.SectionName).Bind(backup);

One4All.Infrastructure.Security.FieldEncryptionOptions fieldEncryption = new();
builder.Configuration.GetSection(One4All.Infrastructure.Security.FieldEncryptionOptions.SectionName).Bind(fieldEncryption);
builder.Services.AddOne4AllInfrastructure(connectionString, licensing, messaging, backup, fieldEncryption);
builder.Services.AddHostedService<One4All.Server.LicenceHost>();
builder.Services.AddHostedService<One4All.Server.WebhookHost>();

// Sign-in policy (brief §5.2): lockout, password history and expiry, idle timeout, concurrent sessions, from the Authentication section.
One4All.Infrastructure.Security.AuthenticationOptions authentication = new();
builder.Configuration.GetSection("Authentication").Bind(authentication);
builder.Services.AddSingleton(authentication);

// Scheduled jobs (brief §4.17): due jobs run once a minute; set Scheduler:Enabled=false to keep a node from running them.
if (builder.Configuration.GetValue("Scheduler:Enabled", true))
{
    builder.Services.AddHostedService<One4All.Server.SchedulerHost>();
}

// --- JWT options + token service --------------------------------------------------------------------------
JwtOptions jwt = new();
builder.Configuration.GetSection("Jwt").Bind(jwt);
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
{
    // The built-in key is public (it is in the source): only a development server may use it. Anywhere else, anyone who read it could
    // forge tokens, so the server refuses to start until Jwt:SigningKey is configured.
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Jwt:SigningKey must be configured (at least 32 bytes) outside Development.");
    }

    jwt.SigningKey = "one4all-development-signing-key-change-me-please";
}

builder.Services.AddSingleton(jwt);
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep claim types verbatim (sub, unique_name, name)

        // A token is only as good as its sign-in session: ended (logout, the session limit, an administrator) or idle past the timeout,
        // the token stops working before it expires (brief §5.2).
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (context.Principal?.FindFirst(One4All.Infrastructure.Security.JwtTokenService.SessionClaim)?.Value is string session
                    && !await context.HttpContext.RequestServices.GetRequiredService<IAuthenticationService>().TouchSessionAsync(session, context.HttpContext.RequestAborted).ConfigureAwait(false))
                {
                    context.Fail("The session has ended; sign in again.");
                }
            },
        };
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

    One4All.Application.Abstractions.Licensing.ILicenceService licence = scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Licensing.ILicenceService>();
    await licence.ReloadAsync();
    if (app.Environment.IsDevelopment() && !licence.Current.IsUsable && !licence.Current.IsReadOnly)
    {
        await licence.InstallAsync(One4All.Infrastructure.Licensing.DevLicensing.LicenceToken);
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// A save refused by a licence limit is the caller's problem (403), not a server fault.
app.Use(async (context, next) =>
{
    try
    {
        await next(context).ConfigureAwait(false);
    }
    catch (One4All.Infrastructure.Licensing.LicenceLimitException limit) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { code = "Licence.LimitReached", message = limit.Message }).ConfigureAwait(false);
    }
});
app.UseAuthentication();
app.UseAuthorization();

// Who and where: the signed-in caller (and the client's address, also for sign-in attempts) for the audit and login trails (brief §5.2, §6).
app.Use(async (context, next) =>
{
    One4All.Infrastructure.Security.CurrentUserContext caller = context.RequestServices.GetRequiredService<One4All.Infrastructure.Security.CurrentUserContext>();
    caller.SetClientAddress(context.Connection.RemoteIpAddress?.ToString());
    if (SignedInUser(context.User) is long callerId)
    {
        caller.SignIn(callerId, context.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName) ?? context.User.Identity?.Name ?? $"user {callerId}",
            One4All.Core.Security.EffectivePermissions.None, terminal: "api");
    }

    await next(context).ConfigureAwait(false);
});

// Every signed-in API request is checked against the user's permissions for its area, the company it names and the user it says is
// acting (brief §5.1); an acting user left out is filled in from the token.
app.Use(async (context, next) =>
{
    Endpoint? endpoint = context.GetEndpoint();
    if (endpoint is null || endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null
        || !context.Request.Path.StartsWithSegments("/api") || SignedInUser(context.User) is not long userId)
    {
        await next(context).ConfigureAwait(false);
        return;
    }

    One4All.Application.Abstractions.Security.IApiAccessService access = context.RequestServices.GetRequiredService<One4All.Application.Abstractions.Security.IApiAccessService>();
    Dictionary<string, string> query = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    foreach ((string key, object? value) in context.Request.RouteValues)
    {
        if (value is not null && !query.ContainsKey(key))
        {
            query[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    Dictionary<string, string>? body = await JsonBodyScalarsAsync(context.Request, context.RequestAborted).ConfigureAwait(false);
    One4All.Application.Abstractions.Security.ApiAccessDecision decision = await access.AuthoriseAsync(
        new One4All.Application.Abstractions.Security.ApiAccessRequest(userId, context.Request.Method, context.Request.Path.Value ?? string.Empty, query, body), context.RequestAborted).ConfigureAwait(false);
    if (!decision.Allowed)
    {
        await Results.Json(new { error = decision.Reason }, statusCode: decision.StatusCode).ExecuteAsync(context).ConfigureAwait(false);
        return;
    }

    foreach (string name in decision.MissingActorParameters)
    {
        context.Request.QueryString = context.Request.QueryString.Add(name, userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // The request's company, once authorised, scopes the database: company-scoped rows of any other company are invisible to it (brief §2).
    string? requestCompany = query.GetValueOrDefault("companyId") ?? body?.GetValueOrDefault("companyId");
    if (long.TryParse(requestCompany, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long scopedCompany))
    {
        context.RequestServices.GetRequiredService<One4All.Infrastructure.Persistence.One4AllDbContext>().CompanyScope = scopedCompany;
    }

    // Reads see only the rows the user's scope rules reach (branch, warehouse, till, job, cost centre, department, ward); writes are held to them
    // by the request checks above, and may need rows across scopes (a transfer between warehouses).
    if (HttpMethods.IsGet(context.Request.Method))
    {
        One4All.Application.Security.RowScope rows = await context.RequestServices.GetRequiredService<One4All.Application.Abstractions.Security.IDataScopeService>()
            .RowScopeAsync(userId, context.RequestAborted).ConfigureAwait(false);
        if (!rows.IsEmpty)
        {
            context.RequestServices.GetRequiredService<One4All.Infrastructure.Persistence.One4AllDbContext>().ApplyRowScope(rows);
        }
    }

    await next(context).ConfigureAwait(false);
});

// Field-level sensitivity (brief §5.1): cost, margin, salary and credit-limit fields are stripped from JSON responses for users who may
// not see them.
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api") || SignedInUser(context.User) is not long userId)
    {
        await next(context).ConfigureAwait(false);
        return;
    }

    One4All.Core.Security.EffectivePermissions permissions = await context.RequestServices.GetRequiredService<One4All.Application.Abstractions.Security.IPermissionService>()
        .ResolveAsync(userId, context.RequestAborted).ConfigureAwait(false);
    IReadOnlySet<One4All.Application.Security.SensitiveField> hidden = One4All.Application.Security.FieldSensitivity.HiddenFor(permissions);
    if (hidden.Count == 0)
    {
        await next(context).ConfigureAwait(false);
        return;
    }

    Stream original = context.Response.Body;
    await using MemoryStream buffer = new();
    context.Response.Body = buffer;
    try
    {
        await next(context).ConfigureAwait(false);
    }
    finally
    {
        context.Response.Body = original;
    }

    buffer.Position = 0;
    if (context.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true && buffer.Length > 0)
    {
        System.Text.Json.Nodes.JsonNode? json = await System.Text.Json.Nodes.JsonNode.ParseAsync(buffer, cancellationToken: context.RequestAborted).ConfigureAwait(false);
        if (One4All.Application.Security.FieldSensitivity.Strip(json, hidden) > 0)
        {
            byte[] stripped = Encoding.UTF8.GetBytes(json!.ToJsonString());
            context.Response.ContentLength = stripped.Length;
            await original.WriteAsync(stripped, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        buffer.Position = 0;
    }

    await buffer.CopyToAsync(original, context.RequestAborted).ConfigureAwait(false);
});

// Idempotency keys (brief §2.4): a POST/PUT/PATCH/DELETE carrying an Idempotency-Key header runs once; a retry with the same key and
// request gets the first response back (it passes through the field-stripping above again), a different request with the key is
// refused, and one still running is reported as such. A server-side failure lets the key go so the retry runs.
app.Use(async (context, next) =>
{
    string? key = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(key) || HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
        || !context.Request.Path.StartsWithSegments("/api") || SignedInUser(context.User) is not long userId)
    {
        await next(context).ConfigureAwait(false);
        return;
    }

    if (key.Length > 100)
    {
        await Results.Json(new { error = "An Idempotency-Key is at most 100 characters." }, statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context).ConfigureAwait(false);
        return;
    }

    context.Request.EnableBuffering();
    using System.Security.Cryptography.IncrementalHash hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
    hash.AppendData(Encoding.UTF8.GetBytes($"{context.Request.Method} {context.Request.Path}{context.Request.QueryString}\n"));
    await using (MemoryStream requestBody = new())
    {
        await context.Request.Body.CopyToAsync(requestBody, context.RequestAborted).ConfigureAwait(false);
        hash.AppendData(requestBody.ToArray());
    }

    context.Request.Body.Position = 0;
    string requestHash = Convert.ToHexString(hash.GetHashAndReset());
    One4All.Application.Abstractions.Integration.IIdempotencyStore store = context.RequestServices.GetRequiredService<One4All.Application.Abstractions.Integration.IIdempotencyStore>();
    One4All.Application.Abstractions.Integration.IdempotencyClaim claim = await store.BeginAsync(userId, key, context.Request.Method, context.Request.Path.Value ?? string.Empty, requestHash, context.RequestAborted)
        .ConfigureAwait(false);
    switch (claim.Outcome)
    {
        case One4All.Application.Abstractions.Integration.IdempotencyOutcome.Replay:
            context.Response.StatusCode = claim.Status;
            context.Response.Headers["Idempotent-Replayed"] = "true";
            if (claim.ContentType is not null)
            {
                context.Response.ContentType = claim.ContentType;
            }

            await context.Response.Body.WriteAsync(claim.Body ?? [], context.RequestAborted).ConfigureAwait(false);
            return;
        case One4All.Application.Abstractions.Integration.IdempotencyOutcome.InProgress:
            await Results.Json(new { error = "A request with this Idempotency-Key is still being processed." }, statusCode: StatusCodes.Status409Conflict).ExecuteAsync(context).ConfigureAwait(false);
            return;
        case One4All.Application.Abstractions.Integration.IdempotencyOutcome.Mismatch:
            await Results.Json(new { error = "This Idempotency-Key was already used for a different request." }, statusCode: StatusCodes.Status422UnprocessableEntity).ExecuteAsync(context).ConfigureAwait(false);
            return;
    }

    Stream original = context.Response.Body;
    await using MemoryStream buffer = new();
    context.Response.Body = buffer;
    bool kept = false;
    try
    {
        await next(context).ConfigureAwait(false);
        if (context.Response.StatusCode < 500)
        {
            await store.CompleteAsync(claim.ClaimId!.Value, context.Response.StatusCode, context.Response.ContentType, buffer.ToArray(), CancellationToken.None).ConfigureAwait(false);
            kept = true;
        }
    }
    finally
    {
        if (!kept)
        {
            await store.AbandonAsync(claim.ClaimId!.Value, CancellationToken.None).ConfigureAwait(false);
        }

        context.Response.Body = original;
        buffer.Position = 0;
        await buffer.CopyToAsync(original, CancellationToken.None).ConfigureAwait(false);
    }
});

// A user who must change their password can do nothing else until they have (brief §5.2).
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/api/auth")
        && context.User.FindFirst(One4All.Infrastructure.Security.JwtTokenService.PasswordChangeClaim)?.Value == "true")
    {
        await Results.Json(new { error = "Change your password first (POST /api/auth/password), then sign in again." }, statusCode: StatusCodes.Status403Forbidden)
            .ExecuteAsync(context).ConfigureAwait(false);
        return;
    }

    await next(context).ConfigureAwait(false);
});

// --- Endpoints --------------------------------------------------------------------------------------------
app.MapPost("/api/auth/logout", async (ClaimsPrincipal user, IAuthenticationService auth, CancellationToken ct) =>
{
    if (user.FindFirst(One4All.Infrastructure.Security.JwtTokenService.SessionClaim)?.Value is string session)
    {
        await auth.EndSessionAsync(session, ct).ConfigureAwait(false);
    }

    return Results.NoContent();
})
.RequireAuthorization()
.WithName("Logout");

app.MapPost("/api/auth/password", async (ChangePasswordRequest request, ClaimsPrincipal user, IAuthenticationService auth, CancellationToken ct) =>
{
    if (!long.TryParse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long userId))
    {
        return Results.Unauthorized();
    }

    if (!await auth.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, ct).ConfigureAwait(false))
    {
        return Results.Json(new { error = "The current password is wrong, or the new one does not meet the password policy." }, statusCode: StatusCodes.Status400BadRequest);
    }

    // The old session ends with the old password: sign in again with the new one.
    if (user.FindFirst(One4All.Infrastructure.Security.JwtTokenService.SessionClaim)?.Value is string session)
    {
        await auth.EndSessionAsync(session, ct).ConfigureAwait(false);
    }

    return Results.NoContent();
})
.RequireAuthorization()
.WithName("ChangePassword");

app.MapPost("/api/auth/login", async (LoginRequest request, IAuthenticationService auth, IJwtTokenService tokens, CancellationToken ct) =>
{
    AuthenticationResult result = await auth.AuthenticateWithCodeAsync(request.Username, request.Password, request.OneTimeCode, "api", ct).ConfigureAwait(false);
    if (!result.Succeeded)
    {
        return Results.Json(new { error = result.Message, twoFactorRequired = result.Outcome == AuthenticationOutcome.TwoFactorRequired }, statusCode: StatusCodes.Status401Unauthorized);
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

// WhatsApp Cloud API webhook: Meta calls these without a user; the GET is the subscription handshake and every POST is signed with the app secret.
app.MapGet("/api/webhooks/whatsapp", (HttpRequest request, One4All.Application.Abstractions.Communications.IWhatsAppWebhookService whatsApp) =>
    whatsApp.VerifySubscription(request.Query["hub.mode"], request.Query["hub.verify_token"], request.Query["hub.challenge"]) is string challenge
        ? Results.Text(challenge)
        : Results.StatusCode(StatusCodes.Status403Forbidden))
.AllowAnonymous()
.WithName("WhatsAppWebhookVerify");

app.MapPost("/api/webhooks/whatsapp", async (HttpRequest request, One4All.Application.Abstractions.Communications.IWhatsAppWebhookService whatsApp, CancellationToken ct) =>
{
    using MemoryStream buffer = new();
    await request.Body.CopyToAsync(buffer, ct).ConfigureAwait(false);
    Result<One4All.Application.Abstractions.Communications.WhatsAppWebhookResult> processed =
        await whatsApp.ProcessAsync(buffer.ToArray(), request.Headers["X-Hub-Signature-256"].FirstOrDefault(), ct).ConfigureAwait(false);
    return processed.IsSuccess ? Results.Ok(processed.Value)
        : processed.Error.Code == "WhatsApp.Signature" ? Results.StatusCode(StatusCodes.Status401Unauthorized) : Results.BadRequest(new { error = processed.Error.Message });
})
.AllowAnonymous()
.WithName("WhatsAppWebhook");

app.MapGet("/api/me", (ClaimsPrincipal user) => Results.Ok(new
{
    userId = user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub),
    userName = user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName),
    displayName = user.Identity?.Name,
    isAdmin = user.FindFirstValue(JwtTokenService.AdminClaim) == "true",
}))
.RequireAuthorization()
.WithName("Me");

// Two-factor enrolment for the signed-in user: begin returns the otpauth URI to scan; confirm switches it on with a first code.
app.MapPost("/api/me/two-factor", async (ClaimsPrincipal user, IAuthenticationService auth, CancellationToken ct) =>
{
    if (SignedInUser(user) is not long id)
    {
        return Results.Unauthorized();
    }

    Result<(string Secret, string ProvisioningUri)> begun = await auth.BeginTwoFactorAsync(id, ct).ConfigureAwait(false);
    return begun.IsSuccess ? Results.Ok(new { secret = begun.Value.Secret, provisioningUri = begun.Value.ProvisioningUri }) : ActionResponse(Result.Failure(begun.Error));
})
.RequireAuthorization()
.WithName("TwoFactorBegin");

app.MapPost("/api/me/two-factor/confirm", async (string code, ClaimsPrincipal user, IAuthenticationService auth, CancellationToken ct) =>
    SignedInUser(user) is long id ? ActionResponse(await auth.ConfirmTwoFactorAsync(id, code, ct).ConfigureAwait(false)) : Results.Unauthorized())
.RequireAuthorization()
.WithName("TwoFactorConfirm");

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

app.MapPut("/api/housing/waiting-list/policy", async (long companyId, One4All.Application.Housing.WaitingListTerms terms, One4All.Application.Abstractions.Housing.IWaitingListService waitingList, CancellationToken ct) =>
    ActionResponse(await waitingList.SetPolicyAsync(companyId, new One4All.Application.Abstractions.Housing.WaitingListPolicyInput(terms), ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SetWaitingListPolicy");

app.MapPost("/api/housing/waiting-list", async (One4All.Application.Abstractions.Housing.WaitingListApplication application, One4All.Application.Abstractions.Housing.IWaitingListService waitingList, CancellationToken ct) =>
    ValueResponse(await waitingList.RegisterAsync(application, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RegisterOnWaitingList");

app.MapGet("/api/housing/waiting-list", async (long companyId, One4All.Core.Housing.HousingCategory category, One4All.Application.Abstractions.Housing.WaitingListOrder order, DateOnly asAtDate, One4All.Application.Abstractions.Housing.IWaitingListService waitingList, CancellationToken ct) =>
    Results.Ok(await waitingList.RankingAsync(companyId, category, order, asAtDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("WaitingListRanking");

app.MapGet("/api/housing/waiting-list/{numberOrId}", async (string numberOrId, long companyId, DateOnly asAtDate, One4All.Application.Abstractions.Housing.IWaitingListService waitingList, CancellationToken ct) =>
    ValueResponse(await waitingList.EnquireAsync(companyId, numberOrId, asAtDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("WaitingListEnquiry");

app.MapPost("/api/housing/allocation-runs", async (One4All.Application.Abstractions.Housing.AllocationProposal proposal, One4All.Application.Abstractions.Housing.IAllocationService allocations, CancellationToken ct) =>
    ValueResponse(await allocations.ProposeAsync(proposal, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ProposeAllocationRun");

app.MapPost("/api/housing/allocation-runs/{runNumber}/approve", async (string runNumber, long companyId, long approverUserId, DateOnly date, int offerDays, One4All.Application.Abstractions.Housing.IAllocationService allocations, CancellationToken ct) =>
    ValueResponse(await allocations.ApproveAsync(companyId, runNumber, approverUserId, date, offerDays, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveAllocationRun");

app.MapPost("/api/housing/allocation-runs/{runNumber}/cancel", async (string runNumber, long companyId, One4All.Application.Abstractions.Housing.IAllocationService allocations, CancellationToken ct) =>
    ActionResponse(await allocations.CancelAsync(companyId, runNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CancelAllocationRun");

app.MapGet("/api/housing/allocation-runs/{runNumber}/verify", async (string runNumber, long companyId, One4All.Application.Abstractions.Housing.IAllocationService allocations, CancellationToken ct) =>
    ValueResponse(await allocations.VerifyDrawAsync(companyId, runNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("VerifyAllocationDraw");

app.MapPost("/api/housing/allocations/{certificateNumber}/accept", async (string certificateNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IAllocationService allocations, CancellationToken ct) =>
    ActionResponse(await allocations.AcceptAsync(companyId, certificateNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AcceptAllocation");

app.MapPost("/api/housing/allocations/{certificateNumber}/decline", async (string certificateNumber, long companyId, DateOnly date, string reason, One4All.Application.Abstractions.Housing.IAllocationService allocations, CancellationToken ct) =>
    ActionResponse(await allocations.DeclineAsync(companyId, certificateNumber, date, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DeclineAllocation");

app.MapPost("/api/housing/allocations/lapse-expired", async (long companyId, DateOnly asAtDate, One4All.Application.Abstractions.Housing.IAllocationService allocations, CancellationToken ct) =>
    Results.Ok(await allocations.LapseExpiredAsync(companyId, asAtDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LapseExpiredAllocations");

app.MapPost("/api/housing/instalment-sales", async (One4All.Application.Abstractions.Housing.InstalmentSaleRequest request, One4All.Application.Abstractions.Housing.IInstalmentSaleService sales, CancellationToken ct) =>
    ValueResponse(await sales.SignAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SignInstalmentSale");

app.MapPost("/api/housing/instalment-sales/raise-due", async (long companyId, DateOnly asAtDate, One4All.Application.Abstractions.Housing.IInstalmentSaleService sales, CancellationToken ct) =>
    ValueResponse(await sales.RaiseDueAsync(companyId, asAtDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RaiseDueInstalments");

app.MapGet("/api/housing/instalment-sales/{agreementNumber}/settlement", async (string agreementNumber, long companyId, DateOnly asAtDate, One4All.Application.Abstractions.Housing.IInstalmentSaleService sales, CancellationToken ct) =>
    ValueResponse(await sales.QuoteSettlementAsync(companyId, agreementNumber, asAtDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("QuoteInstalmentSettlement");

app.MapPost("/api/housing/instalment-sales/{agreementNumber}/settle", async (string agreementNumber, long companyId, DateOnly asAtDate, One4All.Application.Abstractions.Housing.IInstalmentSaleService sales, CancellationToken ct) =>
    ValueResponse(await sales.SettleEarlyAsync(companyId, agreementNumber, asAtDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SettleInstalmentSale");

app.MapPost("/api/housing/rental-units", async (long companyId, string code, string description, One4All.Core.Housing.RentalUnitType unitType, int rooms, decimal monthlyRent, string rentAccountCode, DateOnly vacantSince, One4All.Application.Abstractions.Housing.IRentalHousingService rentals, CancellationToken ct) =>
    ValueResponse(await rentals.AddUnitAsync(companyId, code, description, unitType, rooms, monthlyRent, rentAccountCode, vacantSince, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AddRentalUnit");

app.MapPost("/api/housing/tenancies", async (One4All.Application.Abstractions.Housing.LetUnitRequest request, One4All.Application.Abstractions.Housing.IRentalHousingService rentals, CancellationToken ct) =>
    ValueResponse(await rentals.LetAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LetRentalUnit");

app.MapPost("/api/housing/rent-runs", async (long companyId, DateOnly month, One4All.Application.Abstractions.Housing.IRentalHousingService rentals, CancellationToken ct) =>
    ValueResponse(await rentals.RunRentAsync(companyId, month, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RunRent");

app.MapPost("/api/housing/rental-units/{unitCode}/max-occupants", async (string unitCode, long companyId, int? maxOccupants, One4All.Application.Abstractions.Housing.IRentalOccupancyService occupancy, CancellationToken ct) =>
    ActionResponse(await occupancy.SetMaxOccupantsAsync(companyId, unitCode, maxOccupants, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SetUnitMaxOccupants");

app.MapPost("/api/housing/tenancies/{tenancyNumber}/occupants", async (string tenancyNumber, long companyId, string fullName, string? idNumber, string relationship, DateOnly movedInOn, One4All.Application.Abstractions.Housing.IRentalOccupancyService occupancy, CancellationToken ct) =>
    ValueResponse(await occupancy.AddOccupantAsync(companyId, tenancyNumber, fullName, idNumber, relationship, movedInOn, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AddTenancyOccupant");

app.MapPost("/api/housing/tenancies/{tenancyNumber}/occupants/{occupantId:long}/move-out", async (string tenancyNumber, long occupantId, long companyId, DateOnly movedOutOn, One4All.Application.Abstractions.Housing.IRentalOccupancyService occupancy, CancellationToken ct) =>
    ActionResponse(await occupancy.RemoveOccupantAsync(companyId, tenancyNumber, occupantId, movedOutOn, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("MoveOutTenancyOccupant");

app.MapGet("/api/housing/tenancies/{tenancyNumber}/occupancy", async (string tenancyNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IRentalOccupancyService occupancy, CancellationToken ct) =>
    ValueResponse(await occupancy.OccupancyAsync(companyId, tenancyNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TenancyOccupancy");

app.MapPost("/api/housing/lodger-permits", async (One4All.Application.Abstractions.Housing.LodgerPermitRequest request, One4All.Application.Abstractions.Housing.IRentalOccupancyService occupancy, CancellationToken ct) =>
    ValueResponse(await occupancy.IssueLodgerPermitAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("IssueLodgerPermit");

app.MapPost("/api/housing/lodger-permits/{permitNumber}/revoke", async (string permitNumber, long companyId, DateOnly lastDay, string reason, One4All.Application.Abstractions.Housing.IRentalOccupancyService occupancy, CancellationToken ct) =>
    ActionResponse(await occupancy.RevokeLodgerPermitAsync(companyId, permitNumber, lastDay, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RevokeLodgerPermit");

app.MapPost("/api/housing/tenancies/{tenancyNumber}/end", async (string tenancyNumber, long companyId, DateOnly endDate, string refundBankAccountCode, One4All.Application.Abstractions.Housing.IRentalHousingService rentals, CancellationToken ct) =>
    ValueResponse(await rentals.EndTenancyAsync(companyId, tenancyNumber, endDate, refundBankAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("EndTenancy");

app.MapGet("/api/housing/voids", async (long companyId, DateOnly asAtDate, One4All.Application.Abstractions.Housing.IRentalHousingService rentals, CancellationToken ct) =>
    Results.Ok(await rentals.VoidsAsync(companyId, asAtDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RentalVoids");

app.MapPost("/api/housing/rent-reviews", async (long companyId, DateOnly effectiveFrom, decimal percent, One4All.Core.Housing.RentalUnitType? unitType, One4All.Application.Abstractions.Housing.IRentalHousingService rentals, CancellationToken ct) =>
    ValueResponse(await rentals.ReviewRentsAsync(companyId, effectiveFrom, percent, unitType, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReviewRents");

app.MapPost("/api/housing/rental-units/{unitCode}/repairs", async (string unitCode, long companyId, DateOnly reportedOn, string description, One4All.Application.Abstractions.Housing.IRentalMaintenanceService maintenance, CancellationToken ct) =>
    ValueResponse(await maintenance.ReportRepairAsync(companyId, unitCode, reportedOn, description, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReportUnitRepair");

app.MapPost("/api/housing/repairs/complete", async (One4All.Application.Abstractions.Housing.RepairCompletion completion, One4All.Application.Abstractions.Housing.IRentalMaintenanceService maintenance, CancellationToken ct) =>
    ValueResponse(await maintenance.CompleteRepairAsync(completion, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CompleteUnitRepair");

app.MapPost("/api/housing/rental-units/{unitCode}/inspections", async (string unitCode, long companyId, DateOnly date, string inspector, int conditionGrade, string findings, bool followUpRequired, One4All.Application.Abstractions.Housing.IRentalMaintenanceService maintenance, CancellationToken ct) =>
    ValueResponse(await maintenance.RecordInspectionAsync(companyId, unitCode, date, inspector, conditionGrade, findings, followUpRequired, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordUnitInspection");

app.MapPost("/api/housing/inspections/{inspectionId:long}/close-follow-up", async (long inspectionId, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IRentalMaintenanceService maintenance, CancellationToken ct) =>
    ActionResponse(await maintenance.CloseFollowUpAsync(companyId, inspectionId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CloseInspectionFollowUp");

app.MapGet("/api/housing/rental-units/{unitCode}/maintenance", async (string unitCode, long companyId, One4All.Application.Abstractions.Housing.IRentalMaintenanceService maintenance, CancellationToken ct) =>
    ValueResponse(await maintenance.HistoryAsync(companyId, unitCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("UnitMaintenanceHistory");

app.MapPost("/api/housing/ecl/assess", async (One4All.Application.Abstractions.Housing.HousingEclRequest request, One4All.Application.Abstractions.Housing.IHousingEclService ecl, CancellationToken ct) =>
    ValueResponse(await ecl.AssessAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AssessHousingEcl");

app.MapPost("/api/housing/ecl/post", async (One4All.Application.Abstractions.Housing.HousingEclRequest request, One4All.Application.Abstractions.Housing.IHousingEclService ecl, CancellationToken ct) =>
    ValueResponse(await ecl.PostAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PostHousingEcl");

app.MapPost("/api/housing/waiting-list/{waitingListNumber}/date-change", async (string waitingListNumber, long companyId, DateOnly newDate, string reason, long userId, One4All.Application.Abstractions.Housing.IWaitingListControlService controls, CancellationToken ct) =>
    ValueResponse(await controls.RequestDateChangeAsync(companyId, waitingListNumber, newDate, reason, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RequestRegistrationDateChange");

app.MapPost("/api/housing/date-changes/{requestId:long}/decide", async (long requestId, long companyId, bool approve, long userId, One4All.Application.Abstractions.Housing.IWaitingListControlService controls, CancellationToken ct) =>
    ActionResponse(await controls.DecideDateChangeAsync(companyId, requestId, approve, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DecideRegistrationDateChange");

app.MapPost("/api/housing/waiting-list/{waitingListNumber}/confirm", async (string waitingListNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IWaitingListControlService controls, CancellationToken ct) =>
    ActionResponse(await controls.ConfirmAsync(companyId, waitingListNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ConfirmWaitingListRenewal");

app.MapPost("/api/housing/waiting-list/renewal-run", async (long companyId, DateOnly asAtDate, int cycleMonths, int graceDays, One4All.Application.Abstractions.Housing.IWaitingListControlService controls, CancellationToken ct) =>
    Results.Ok(await controls.RunRenewalsAsync(companyId, asAtDate, cycleMonths, graceDays, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RunWaitingListRenewals");

app.MapGet("/api/housing/waiting-list/{waitingListNumber}/notices", async (string waitingListNumber, long companyId, One4All.Application.Abstractions.Housing.IWaitingListControlService controls, CancellationToken ct) =>
    Results.Ok(await controls.NoticesAsync(companyId, waitingListNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApplicantNotices");

app.MapPost("/api/housing/appeals", async (long companyId, string waitingListNumber, string runNumber, string grounds, DateOnly lodgedOn, One4All.Application.Abstractions.Housing.IAllocationAppealService appeals, CancellationToken ct) =>
    ValueResponse(await appeals.LodgeAsync(companyId, waitingListNumber, runNumber, grounds, lodgedOn, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LodgeAllocationAppeal");

app.MapPost("/api/housing/appeals/{appealNumber}/hearing", async (string appealNumber, long companyId, DateOnly hearingDate, One4All.Application.Abstractions.Housing.IAllocationAppealService appeals, CancellationToken ct) =>
    ActionResponse(await appeals.SetHearingAsync(companyId, appealNumber, hearingDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SetAppealHearing");

app.MapPost("/api/housing/appeals/{appealNumber}/decide", async (string appealNumber, long companyId, bool upheld, DateOnly date, string reasons, One4All.Application.Abstractions.Housing.IAllocationAppealService appeals, CancellationToken ct) =>
    ActionResponse(await appeals.DecideAsync(companyId, appealNumber, upheld, date, reasons, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DecideAllocationAppeal");

app.MapPost("/api/housing/appeals/{appealNumber}/reallocation", async (string appealNumber, long companyId, string certificateNumber, One4All.Application.Abstractions.Housing.IAllocationAppealService appeals, CancellationToken ct) =>
    ActionResponse(await appeals.LinkReallocationAsync(companyId, appealNumber, certificateNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LinkAppealReallocation");

app.MapGet("/api/housing/appeals", async (long companyId, One4All.Application.Abstractions.Housing.IAllocationAppealService appeals, CancellationToken ct) =>
    Results.Ok(await appeals.RegisterAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AllocationAppealsRegister");

app.MapPost("/api/housing/covenants", async (long companyId, long standId, long holderCustomerId, DateOnly startDate, int years, One4All.Application.Abstractions.Housing.IDevelopmentCovenantService covenants, CancellationToken ct) =>
    ValueResponse(await covenants.CreateAsync(companyId, standId, holderCustomerId, startDate, years, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreateDevelopmentCovenant");

app.MapPost("/api/housing/covenants/{covenantNumber}/milestones", async (string covenantNumber, long companyId, One4All.Core.Housing.CovenantMilestone milestone, DateOnly date, string? reference, One4All.Application.Abstractions.Housing.IDevelopmentCovenantService covenants, CancellationToken ct) =>
    ActionResponse(await covenants.RecordMilestoneAsync(companyId, covenantNumber, milestone, date, reference, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordCovenantMilestone");

app.MapPost("/api/housing/covenants/{covenantNumber}/extend", async (string covenantNumber, long companyId, int months, DateOnly date, decimal fee, string? feeAccountCode, One4All.Application.Abstractions.Housing.IDevelopmentCovenantService covenants, CancellationToken ct) =>
    ValueResponse(await covenants.GrantExtensionAsync(companyId, covenantNumber, months, date, fee, feeAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ExtendDevelopmentCovenant");

app.MapPost("/api/housing/covenants/breach-run", async (long companyId, DateOnly asAtDate, decimal penaltyLevy, string levyAccountCode, One4All.Application.Abstractions.Housing.IDevelopmentCovenantService covenants, CancellationToken ct) =>
    ValueResponse(await covenants.RunBreachesAsync(companyId, asAtDate, penaltyLevy, levyAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RunCovenantBreaches");

app.MapPost("/api/housing/covenants/{covenantNumber}/notice", async (string covenantNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IDevelopmentCovenantService covenants, CancellationToken ct) =>
    ActionResponse(await covenants.IssueNoticeAsync(companyId, covenantNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("IssueCovenantNotice");

app.MapGet("/api/housing/covenants", async (long companyId, One4All.Application.Abstractions.Housing.IDevelopmentCovenantService covenants, CancellationToken ct) =>
    Results.Ok(await covenants.RegisterAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DevelopmentCovenantRegister");

app.MapPost("/api/housing/stand-leases", async (One4All.Application.Abstractions.Housing.StandLeaseRequest request, One4All.Application.Abstractions.Housing.IStandLeaseService leases, CancellationToken ct) =>
    ValueResponse(await leases.CreateAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreateStandLease");

app.MapGet("/api/housing/stand-leases/{leaseNumber}/schedule", async (string leaseNumber, long companyId, One4All.Application.Abstractions.Housing.IStandLeaseService leases, CancellationToken ct) =>
    ValueResponse(await leases.ScheduleAsync(companyId, leaseNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("StandLeaseSchedule");

app.MapPost("/api/housing/stand-leases/run", async (long companyId, DateOnly month, One4All.Application.Abstractions.Housing.IStandLeaseService leases, CancellationToken ct) =>
    ValueResponse(await leases.RunMonthAsync(companyId, month, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RunStandLeaseBilling");

app.MapPost("/api/housing/grants", async (One4All.Application.Abstractions.Housing.GrantReceipt receipt, One4All.Application.Abstractions.Housing.IHousingGrantService grants, CancellationToken ct) =>
    ValueResponse(await grants.ReceiveAsync(receipt, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReceiveHousingGrant");

app.MapPost("/api/housing/grants/{grantNumber}/assure", async (string grantNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IHousingGrantService grants, CancellationToken ct) =>
    ValueResponse(await grants.AssureConditionsAsync(companyId, grantNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AssureGrantConditions");

app.MapPost("/api/housing/grants/{grantNumber}/expenditure", async (string grantNumber, long companyId, decimal expenditureToDate, DateOnly date, One4All.Application.Abstractions.Housing.IHousingGrantService grants, CancellationToken ct) =>
    ValueResponse(await grants.RecordExpenditureAsync(companyId, grantNumber, expenditureToDate, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordGrantExpenditure");

app.MapGet("/api/housing/grants/disclosure", async (long companyId, One4All.Application.Abstractions.Housing.IHousingGrantService grants, CancellationToken ct) =>
    Results.Ok(await grants.DisclosureAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("HousingGrantDisclosure");

app.MapPost("/api/housing/compensation-claims", async (long companyId, long landParcelId, string claimant, DateOnly lodgedOn, string landCostAccountCode, string provisionAccountCode, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ValueResponse(await compensation.LodgeAsync(companyId, landParcelId, claimant, lodgedOn, landCostAccountCode, provisionAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LodgeCompensationClaim");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/provide", async (string claimNumber, long companyId, decimal bestEstimate, DateOnly date, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ValueResponse(await compensation.ProvideAsync(companyId, claimNumber, bestEstimate, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ProvideCompensation");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/settle", async (string claimNumber, long companyId, decimal paid, string bankAccountCode, DateOnly date, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ActionResponse(await compensation.SettleAsync(companyId, claimNumber, paid, bankAccountCode, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SettleCompensation");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/withdraw", async (string claimNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ActionResponse(await compensation.WithdrawAsync(companyId, claimNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("WithdrawCompensation");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/notice", async (string claimNumber, long companyId, string reference, DateOnly servedOn, int objectionDays, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ActionResponse(await compensation.ServeNoticeAsync(companyId, claimNumber, reference, servedOn, objectionDays, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ServeAcquisitionNotice");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/objections", async (string claimNumber, long companyId, string objector, string grounds, DateOnly lodgedOn, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ValueResponse(await compensation.LodgeObjectionAsync(companyId, claimNumber, objector, grounds, lodgedOn, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LodgeAcquisitionObjection");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/objections/{objectionId:long}/decide", async (string claimNumber, long objectionId, long companyId, bool upheld, DateOnly date, string? note, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ActionResponse(await compensation.DecideObjectionAsync(companyId, claimNumber, objectionId, upheld, date, note, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DecideAcquisitionObjection");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/assess", async (string claimNumber, long companyId, string assessor, DateOnly date, decimal amount, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ActionResponse(await compensation.AssessAsync(companyId, claimNumber, assessor, date, amount, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AssessCompensation");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/offers", async (string claimNumber, long companyId, decimal amount, DateOnly date, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ValueResponse(await compensation.OfferAsync(companyId, claimNumber, amount, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("OfferCompensation");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/offers/{offerId:long}/respond", async (string claimNumber, long offerId, long companyId, bool accepted, DateOnly date, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ActionResponse(await compensation.RespondToOfferAsync(companyId, claimNumber, offerId, accepted, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RespondToCompensationOffer");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/disputes", async (string claimNumber, long companyId, string forum, DateOnly date, string? note, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ValueResponse(await compensation.DisputeAsync(companyId, claimNumber, forum, date, note, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DisputeCompensation");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/disputes/{disputeId:long}/award", async (string claimNumber, long disputeId, long companyId, decimal amount, DateOnly date, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ActionResponse(await compensation.AwardAsync(companyId, claimNumber, disputeId, amount, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AwardCompensation");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/part-payments", async (string claimNumber, long companyId, decimal amount, string bankAccountCode, DateOnly date, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ActionResponse(await compensation.PayPartAsync(companyId, claimNumber, amount, bankAccountCode, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PayCompensationPart");

app.MapPost("/api/housing/compensation-claims/{claimNumber}/possession", async (string claimNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    ActionResponse(await compensation.TakePossessionAsync(companyId, claimNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TakeAcquisitionPossession");

app.MapGet("/api/housing/compensation-claims", async (long companyId, One4All.Application.Abstractions.Housing.ICompensationService compensation, CancellationToken ct) =>
    Results.Ok(await compensation.RegisterAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CompensationRegister");

app.MapPost("/api/housing/cooperatives", async (long companyId, string code, string name, string registrationNumber, One4All.Application.Abstractions.Housing.ICooperativeService cooperatives, CancellationToken ct) =>
    ValueResponse(await cooperatives.RegisterAsync(companyId, code, name, registrationNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RegisterCooperative");

app.MapPost("/api/housing/cooperatives/{code}/members", async (string code, long companyId, string idNumber, string fullName, DateOnly joinedOn, One4All.Application.Abstractions.Housing.ICooperativeService cooperatives, CancellationToken ct) =>
    ActionResponse(await cooperatives.AddMemberAsync(companyId, code, idNumber, fullName, joinedOn, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AddCooperativeMember");

app.MapDelete("/api/housing/cooperatives/{code}/members/{idNumber}", async (string code, string idNumber, long companyId, One4All.Application.Abstractions.Housing.ICooperativeService cooperatives, CancellationToken ct) =>
    ActionResponse(await cooperatives.RemoveMemberAsync(companyId, code, idNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RemoveCooperativeMember");

app.MapPost("/api/housing/cooperatives/{code}/stands", async (string code, long companyId, DateOnly date, IReadOnlyList<long> standIds, One4All.Application.Abstractions.Housing.ICooperativeService cooperatives, CancellationToken ct) =>
    ValueResponse(await cooperatives.AllocateStandsAsync(companyId, code, standIds, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AllocateStandsToCooperative");

app.MapPost("/api/housing/cooperatives/{code}/stands/{standId:long}/member", async (string code, long standId, long companyId, string memberIdNumber, DateOnly date, One4All.Application.Abstractions.Housing.ICooperativeService cooperatives, CancellationToken ct) =>
    ActionResponse(await cooperatives.SubAllocateAsync(companyId, code, standId, memberIdNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SubAllocateCooperativeStand");

app.MapGet("/api/housing/cooperatives/{code}/stands", async (string code, long companyId, One4All.Application.Abstractions.Housing.ICooperativeService cooperatives, CancellationToken ct) =>
    ValueResponse(await cooperatives.StandsAsync(companyId, code, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CooperativeStands");

app.MapPost("/api/housing/agreements", async (One4All.Application.Abstractions.Housing.AgreementDraft draft, One4All.Application.Abstractions.Housing.IAgreementRegisterService agreements, CancellationToken ct) =>
    ValueResponse(await agreements.DraftAsync(draft, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DraftHousingAgreement");

app.MapPost("/api/housing/agreements/{agreementNumber}/sign", async (string agreementNumber, long companyId, DateOnly date, IReadOnlyList<string> witnesses, One4All.Application.Abstractions.Housing.IAgreementRegisterService agreements, CancellationToken ct) =>
    ActionResponse(await agreements.SignAsync(companyId, agreementNumber, date, witnesses, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SignHousingAgreement");

app.MapPost("/api/housing/agreements/{agreementNumber}/amend", async (string agreementNumber, long companyId, decimal price, string terms, string? specialConditions, string reason, One4All.Application.Abstractions.Housing.IAgreementRegisterService agreements, CancellationToken ct) =>
    ValueResponse(await agreements.AmendAsync(companyId, agreementNumber, price, terms, specialConditions, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AmendHousingAgreement");

app.MapGet("/api/housing/agreements/{agreementNumber}/history", async (string agreementNumber, long companyId, One4All.Application.Abstractions.Housing.IAgreementRegisterService agreements, CancellationToken ct) =>
    ValueResponse(await agreements.HistoryAsync(companyId, agreementNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("HousingAgreementHistory");

app.MapGet("/api/housing/agreements/verify", async (long companyId, One4All.Application.Abstractions.Housing.IAgreementRegisterService agreements, CancellationToken ct) =>
    Results.Ok(await agreements.VerifyDocumentsAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("VerifyHousingAgreements");

app.MapPost("/api/housing/settlements", async (One4All.Application.Abstractions.Housing.SettlementRegistration registration, One4All.Application.Abstractions.Housing.IInformalSettlementService settlements, CancellationToken ct) =>
    ValueResponse(await settlements.RegisterAsync(registration, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RegisterInformalSettlement");

app.MapPost("/api/housing/settlements/{code}/plan", async (string code, long companyId, string plan, DateOnly approvedOn, One4All.Application.Abstractions.Housing.IInformalSettlementService settlements, CancellationToken ct) =>
    ActionResponse(await settlements.ApprovePlanAsync(companyId, code, plan, approvedOn, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveSettlementUpgradingPlan");

app.MapPost("/api/housing/settlements/{code}/enumerations", async (string code, long companyId, string reference, DateOnly date, One4All.Application.Abstractions.Housing.IInformalSettlementService settlements, CancellationToken ct) =>
    ValueResponse(await settlements.StartEnumerationAsync(companyId, code, reference, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("StartEnumeration");

app.MapPost("/api/housing/settlements/households", async (One4All.Application.Abstractions.Housing.HouseholdSurvey survey, One4All.Application.Abstractions.Housing.IInformalSettlementService settlements, CancellationToken ct) =>
    ValueResponse(await settlements.EnumerateAsync(survey, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("EnumerateHousehold");

app.MapPost("/api/housing/settlements/{code}/enumerations/close", async (string code, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IInformalSettlementService settlements, CancellationToken ct) =>
    ValueResponse(await settlements.CloseEnumerationAsync(companyId, code, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CloseEnumeration");

app.MapPost("/api/housing/settlements/households/{householdNumber}/waiting-list", async (string householdNumber, long companyId, One4All.Core.Housing.HousingCategory category, DateOnly date, One4All.Application.Abstractions.Housing.IInformalSettlementService settlements, CancellationToken ct) =>
    ValueResponse(await settlements.ConvertToWaitingListAsync(companyId, householdNumber, category, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ConvertHouseholdToWaitingList");

app.MapPost("/api/housing/settlements/households/{householdNumber}/regularise", async (string householdNumber, long companyId, long standId, DateOnly date, One4All.Application.Abstractions.Housing.IInformalSettlementService settlements, CancellationToken ct) =>
    ActionResponse(await settlements.RegulariseAsync(companyId, householdNumber, standId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RegulariseHousehold");

app.MapGet("/api/housing/settlements/{code}", async (string code, long companyId, One4All.Application.Abstractions.Housing.IInformalSettlementService settlements, CancellationToken ct) =>
    ValueResponse(await settlements.SummaryAsync(companyId, code, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SettlementSummary");

app.MapPost("/api/housing/developer-agreements", async (One4All.Application.Abstractions.Housing.DeveloperAgreementTerms terms, One4All.Application.Abstractions.Housing.IDeveloperAgreementService developers, CancellationToken ct) =>
    ValueResponse(await developers.CreateAsync(terms, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CreateDeveloperAgreement");

app.MapPost("/api/housing/developer-agreements/{agreementNumber}/milestones", async (string agreementNumber, long companyId, string description, DateOnly dueOn, One4All.Application.Abstractions.Housing.IDeveloperAgreementService developers, CancellationToken ct) =>
    ActionResponse(await developers.AddMilestoneAsync(companyId, agreementNumber, description, dueOn, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AddDeliveryMilestone");

app.MapPost("/api/housing/developer-agreements/{agreementNumber}/milestones/complete", async (string agreementNumber, long companyId, string description, DateOnly date, One4All.Application.Abstractions.Housing.IDeveloperAgreementService developers, CancellationToken ct) =>
    ActionResponse(await developers.CompleteMilestoneAsync(companyId, agreementNumber, description, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CompleteDeliveryMilestone");

app.MapPost("/api/housing/developer-agreements/{agreementNumber}/stands", async (string agreementNumber, long companyId, One4All.Core.Housing.StandQuota quota, DateOnly date, IReadOnlyList<long> standIds, One4All.Application.Abstractions.Housing.IDeveloperAgreementService developers, CancellationToken ct) =>
    ValueResponse(await developers.AssignStandsAsync(companyId, agreementNumber, quota, standIds, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AssignDeveloperStands");

app.MapPost("/api/housing/developer-agreements/{agreementNumber}/stands/{standId:long}/hand-over", async (string agreementNumber, long standId, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IDeveloperAgreementService developers, CancellationToken ct) =>
    ActionResponse(await developers.HandOverAsync(companyId, agreementNumber, standId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("HandOverDeveloperStand");

app.MapGet("/api/housing/developer-agreements/{agreementNumber}/reconciliation", async (string agreementNumber, long companyId, DateOnly asAt, One4All.Application.Abstractions.Housing.IDeveloperAgreementService developers, CancellationToken ct) =>
    ValueResponse(await developers.ReconcileAsync(companyId, agreementNumber, asAt, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReconcileDeveloperAgreement");

app.MapPost("/api/housing/developer-agreements/{agreementNumber}/penalties", async (string agreementNumber, long companyId, DateOnly asAt, string penaltyIncomeAccountCode, One4All.Application.Abstractions.Housing.IDeveloperAgreementService developers, CancellationToken ct) =>
    ValueResponse(await developers.ChargePenaltiesAsync(companyId, agreementNumber, asAt, penaltyIncomeAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ChargeDeveloperPenalties");

app.MapPost("/api/housing/stands/{standId:long}/block", async (long standId, long companyId, One4All.Core.Housing.BlockReason reason, string detail, string documentReference, long userId, DateOnly date, One4All.Application.Abstractions.Housing.IStandBlockService blocks, CancellationToken ct) =>
    ActionResponse(await blocks.PlaceAsync(companyId, standId, reason, detail, documentReference, userId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("BlockStand");

app.MapPost("/api/housing/stands/{standId:long}/block/lift", async (long standId, long companyId, string reason, string documentReference, long userId, DateOnly date, One4All.Application.Abstractions.Housing.IStandBlockService blocks, CancellationToken ct) =>
    ActionResponse(await blocks.LiftAsync(companyId, standId, reason, documentReference, userId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LiftStandBlock");

app.MapGet("/api/housing/stands/{standId:long}/blocks", async (long standId, long companyId, One4All.Application.Abstractions.Housing.IStandBlockService blocks, CancellationToken ct) =>
    Results.Ok(await blocks.HistoryAsync(companyId, standId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("StandBlockHistory");

app.MapGet("/api/housing/stands/blocked", async (long companyId, One4All.Application.Abstractions.Housing.IStandBlockService blocks, CancellationToken ct) =>
    Results.Ok(await blocks.InForceAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("BlockedStands");

app.MapPost("/api/housing/land/encumbrances", async (One4All.Application.Abstractions.Housing.EncumbranceRegistration registration, One4All.Application.Abstractions.Housing.ILandRegisterService land, CancellationToken ct) =>
    ValueResponse(await land.RegisterEncumbranceAsync(registration, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RegisterEncumbrance");

app.MapPost("/api/housing/land/encumbrances/{encumbranceId:long}/lift", async (long encumbranceId, long companyId, DateOnly date, string reference, One4All.Application.Abstractions.Housing.ILandRegisterService land, CancellationToken ct) =>
    ActionResponse(await land.LiftEncumbranceAsync(companyId, encumbranceId, date, reference, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LiftEncumbrance");

app.MapPost("/api/housing/land/disputes", async (One4All.Application.Abstractions.Housing.LandDisputeLodgement lodgement, One4All.Application.Abstractions.Housing.ILandRegisterService land, CancellationToken ct) =>
    ValueResponse(await land.OpenDisputeAsync(lodgement, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("OpenLandDispute");

app.MapPost("/api/housing/land/disputes/{disputeNumber}/advance", async (string disputeNumber, long companyId, One4All.Core.Housing.LandDisputeStage stage, DateOnly date, string note, One4All.Application.Abstractions.Housing.ILandRegisterService land, CancellationToken ct) =>
    ActionResponse(await land.AdvanceDisputeAsync(companyId, disputeNumber, stage, date, note, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AdvanceLandDispute");

app.MapPost("/api/housing/land/disposals", async (One4All.Application.Abstractions.Housing.LandDisposalRequest request, One4All.Application.Abstractions.Housing.ILandRegisterService land, CancellationToken ct) =>
    ValueResponse(await land.DisposeAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DisposeOfLand");

app.MapPost("/api/housing/survey", async (long companyId, string[] standNumbers, One4All.Core.Housing.SurveyStage stage, DateOnly date, string reference, One4All.Application.Abstractions.Housing.ISchemeProgressService progress, CancellationToken ct) =>
    ValueResponse(await progress.RecordSurveyAsync(companyId, standNumbers, stage, date, reference, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordSurvey");

app.MapPost("/api/housing/servicing-progress", async (One4All.Application.Abstractions.Housing.ServicingCertificate certificate, One4All.Application.Abstractions.Housing.ISchemeProgressService progress, CancellationToken ct) =>
    ValueResponse(await progress.RecordServicingAsync(certificate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordServicingProgress");

app.MapPost("/api/housing/land/{parcelNumber}/valuations", async (string parcelNumber, long companyId, DateOnly date, decimal value, string valuer, One4All.Application.Abstractions.Housing.ISchemeProgressService progress, CancellationToken ct) =>
    ValueResponse(await progress.RecordValuationAsync(companyId, parcelNumber, date, value, valuer, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordLandValuation");

app.MapPost("/api/housing/quotas", async (long companyId, string? schemeCode, One4All.Core.Housing.BeneficiaryGroups group, decimal targetPercent, One4All.Application.Abstractions.Housing.IAllocationQuotaService quotas, CancellationToken ct) =>
    ActionResponse(await quotas.SetQuotaAsync(companyId, schemeCode, group, targetPercent, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SetAllocationQuota");

app.MapPost("/api/housing/applicants/{waitingListNumber}/groups", async (string waitingListNumber, long companyId, One4All.Core.Housing.BeneficiaryGroups groups, One4All.Application.Abstractions.Housing.IAllocationQuotaService quotas, CancellationToken ct) =>
    ActionResponse(await quotas.DeclareGroupsAsync(companyId, waitingListNumber, groups, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DeclareBeneficiaryGroups");

app.MapPost("/api/housing/applicant-fees", async (One4All.Application.Abstractions.Housing.ApplicantFeeReceipt receipt, One4All.Application.Abstractions.Housing.IHousingFeeService fees, CancellationToken ct) =>
    ValueResponse(await fees.RecordApplicantFeeAsync(receipt, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordApplicantFee");

app.MapPost("/api/housing/transfers/duty", async (One4All.Application.Abstractions.Housing.TransferDutyReceipt receipt, One4All.Application.Abstractions.Housing.IHousingFeeService fees, CancellationToken ct) =>
    ValueResponse(await fees.WithholdTransferDutyAsync(receipt, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("WithholdTransferDuty");

app.MapPost("/api/housing/transfers/{applicationNumber}/duty/remit", async (string applicationNumber, long companyId, DateOnly date, string reference, string bankAccountCode, One4All.Application.Abstractions.Housing.IHousingFeeService fees, CancellationToken ct) =>
    ValueResponse(await fees.RemitTransferDutyAsync(companyId, applicationNumber, date, reference, bankAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RemitTransferDuty");

app.MapPost("/api/housing/deposits", async (One4All.Application.Abstractions.Housing.StandDepositReceipt receipt, One4All.Application.Abstractions.Housing.IHousingDepositService deposits, CancellationToken ct) =>
    ValueResponse(await deposits.TakeDepositAsync(receipt, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TakeStandDeposit");

app.MapPost("/api/housing/deposits/{certificateNumber}/apply", async (string certificateNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IHousingDepositService deposits, CancellationToken ct) =>
    ValueResponse(await deposits.ApplyDepositAsync(companyId, certificateNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApplyStandDeposit");

app.MapPost("/api/housing/deposits/{certificateNumber}/refund", async (string certificateNumber, long companyId, DateOnly date, string bankAccountCode, One4All.Application.Abstractions.Housing.IHousingDepositService deposits, CancellationToken ct) =>
    ValueResponse(await deposits.RefundDepositAsync(companyId, certificateNumber, date, bankAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RefundStandDeposit");

app.MapPost("/api/housing/agreements/cancel", async (One4All.Application.Abstractions.Housing.AgreementCancellationRequest request, One4All.Application.Abstractions.Housing.IHousingDepositService deposits, CancellationToken ct) =>
    ValueResponse(await deposits.CancelAgreementAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CancelInstalmentAgreement");

app.MapPost("/api/housing/schemes/{schemeCode}/budget", async (string schemeCode, long companyId, One4All.Core.Housing.SchemeCostElement element, decimal amount, One4All.Application.Abstractions.Housing.ISchemeBudgetService budgets, CancellationToken ct) =>
    ActionResponse(await budgets.SetBudgetAsync(companyId, schemeCode, element, amount, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SetSchemeBudget");

app.MapPost("/api/housing/schemes/costs", async (One4All.Application.Abstractions.Housing.SchemeCostRequest request, One4All.Application.Abstractions.Housing.ISchemeBudgetService budgets, CancellationToken ct) =>
    ValueResponse(await budgets.RecordCostAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CapitaliseSchemeCost");

app.MapPost("/api/housing/titles", async (long companyId, long standId, DateOnly date, One4All.Application.Abstractions.Housing.ITitleDeedService titles, CancellationToken ct) =>
    ValueResponse(await titles.ApplyAsync(companyId, standId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApplyForTitle");

app.MapPost("/api/housing/titles/{applicationNumber}/advance", async (string applicationNumber, long companyId, One4All.Application.Abstractions.Housing.TitleStepInput step, One4All.Application.Abstractions.Housing.ITitleDeedService titles, CancellationToken ct) =>
    ActionResponse(await titles.AdvanceAsync(companyId, applicationNumber, step, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AdvanceTitle");

app.MapGet("/api/housing/titles/pipeline", async (long companyId, DateOnly asAt, One4All.Application.Abstractions.Housing.ITitleDeedService titles, CancellationToken ct) =>
    Results.Ok(await titles.PipelineAsync(companyId, asAt, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TitlePipeline");

app.MapPost("/api/housing/transfers", async (One4All.Application.Abstractions.Housing.TransferLodgement lodgement, One4All.Application.Abstractions.Housing.ITransferApplicationService transfers, CancellationToken ct) =>
    ValueResponse(await transfers.LodgeAsync(lodgement, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LodgeTransfer");

app.MapPost("/api/housing/transfers/{applicationNumber}/verify", async (string applicationNumber, long companyId, One4All.Core.Housing.TransferRequirement requirement, string documentReference, long userId, DateOnly date, One4All.Application.Abstractions.Housing.ITransferApplicationService transfers, CancellationToken ct) =>
    ActionResponse(await transfers.VerifyAsync(companyId, applicationNumber, requirement, documentReference, userId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("VerifyTransferItem");

app.MapGet("/api/housing/transfers/{applicationNumber}/checklist", async (string applicationNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.ITransferApplicationService transfers, CancellationToken ct) =>
    ValueResponse(await transfers.ChecklistAsync(companyId, applicationNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TransferChecklist");

app.MapPost("/api/housing/transfers/{applicationNumber}/submit", async (string applicationNumber, long companyId, long userId, DateOnly date, One4All.Application.Abstractions.Housing.ITransferApplicationService transfers, CancellationToken ct) =>
    ActionResponse(await transfers.SubmitAsync(companyId, applicationNumber, userId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SubmitTransfer");

app.MapPost("/api/housing/transfers/{applicationNumber}/approve", async (string applicationNumber, long companyId, long userId, DateOnly date, One4All.Application.Abstractions.Housing.ITransferApplicationService transfers, CancellationToken ct) =>
    ActionResponse(await transfers.ApproveAsync(companyId, applicationNumber, userId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveTransfer");

app.MapPost("/api/housing/transfers/{applicationNumber}/complete", async (string applicationNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.ITransferApplicationService transfers, CancellationToken ct) =>
    ActionResponse(await transfers.CompleteAsync(companyId, applicationNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CompleteTransfer");

app.MapPost("/api/housing/transfers/{applicationNumber}/reject", async (string applicationNumber, long companyId, string reason, DateOnly date, One4All.Application.Abstractions.Housing.ITransferApplicationService transfers, CancellationToken ct) =>
    ActionResponse(await transfers.RejectAsync(companyId, applicationNumber, reason, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RejectTransfer");

app.MapPost("/api/housing/acquisitions", async (One4All.Application.Abstractions.Housing.AcquisitionCase acquisition, One4All.Application.Abstractions.Housing.ILandAcquisitionService acquisitions, CancellationToken ct) =>
    ValueResponse(await acquisitions.OpenAsync(acquisition, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("OpenLandAcquisition");

app.MapPost("/api/housing/acquisitions/{acquisitionNumber}/valuation", async (string acquisitionNumber, long companyId, string valuer, DateOnly valuedOn, decimal value, One4All.Application.Abstractions.Housing.ILandAcquisitionService acquisitions, CancellationToken ct) =>
    ActionResponse(await acquisitions.RecordValuationAsync(companyId, acquisitionNumber, valuer, valuedOn, value, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordAcquisitionValuation");

app.MapPost("/api/housing/acquisitions/{acquisitionNumber}/due-diligence", async (string acquisitionNumber, long companyId, One4All.Core.Housing.DueDiligenceItem item, string finding, bool acceptable, long userId, DateOnly date, One4All.Application.Abstractions.Housing.ILandAcquisitionService acquisitions, CancellationToken ct) =>
    ActionResponse(await acquisitions.SignOffAsync(companyId, acquisitionNumber, item, finding, acceptable, userId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SignOffDueDiligence");

app.MapPost("/api/housing/acquisitions/{acquisitionNumber}/costs", async (string acquisitionNumber, long companyId, One4All.Application.Abstractions.Housing.AcquisitionCostInput cost, One4All.Application.Abstractions.Housing.ILandAcquisitionService acquisitions, CancellationToken ct) =>
    ValueResponse(await acquisitions.AddCostAsync(companyId, acquisitionNumber, cost, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AddAcquisitionCost");

app.MapGet("/api/housing/acquisitions/{acquisitionNumber}", async (string acquisitionNumber, long companyId, One4All.Application.Abstractions.Housing.ILandAcquisitionService acquisitions, CancellationToken ct) =>
    ValueResponse(await acquisitions.PositionAsync(companyId, acquisitionNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AcquisitionPosition");

app.MapPost("/api/housing/acquisitions/{acquisitionNumber}/complete", async (string acquisitionNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.ILandAcquisitionService acquisitions, CancellationToken ct) =>
    ValueResponse(await acquisitions.CompleteAsync(companyId, acquisitionNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CompleteLandAcquisition");

app.MapPost("/api/housing/acquisitions/{acquisitionNumber}/abandon", async (string acquisitionNumber, long companyId, DateOnly date, string reason, string expenseAccountCode, string wipAccountCode, One4All.Application.Abstractions.Housing.ILandAcquisitionService acquisitions, CancellationToken ct) =>
    ValueResponse(await acquisitions.AbandonAsync(companyId, acquisitionNumber, date, reason, expenseAccountCode, wipAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AbandonLandAcquisition");

app.MapPost("/api/housing/layouts", async (One4All.Application.Abstractions.Housing.LayoutSubmission submission, One4All.Application.Abstractions.Housing.ILayoutPlanService layouts, CancellationToken ct) =>
    ActionResponse(await layouts.SubmitAsync(submission, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SubmitLayoutPlan");

app.MapPost("/api/housing/layouts/{planNumber}/approve", async (string planNumber, long companyId, string authority, DateOnly date, One4All.Application.Abstractions.Housing.ILayoutPlanService layouts, CancellationToken ct) =>
    ActionResponse(await layouts.ApproveAsync(companyId, planNumber, authority, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveLayoutPlan");

app.MapPost("/api/housing/layouts/amend", async (One4All.Application.Abstractions.Housing.LayoutSubmission submission, One4All.Application.Abstractions.Housing.ILayoutPlanService layouts, CancellationToken ct) =>
    ValueResponse(await layouts.AmendAsync(submission, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AmendLayoutPlan");

app.MapGet("/api/housing/layouts/{planNumber}/history", async (string planNumber, long companyId, One4All.Application.Abstractions.Housing.ILayoutPlanService layouts, CancellationToken ct) =>
    ValueResponse(await layouts.HistoryAsync(companyId, planNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LayoutPlanHistory");

app.MapGet("/api/housing/layouts/{planNumber}/reconciliation", async (string planNumber, long companyId, One4All.Application.Abstractions.Housing.ILayoutPlanService layouts, CancellationToken ct) =>
    ValueResponse(await layouts.ReconcileAsync(companyId, planNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReconcileLayoutPlan");

app.MapPost("/api/housing/pricing/rules", async (One4All.Application.Abstractions.Housing.PricingRuleInput input, One4All.Application.Abstractions.Housing.IStandPricingService pricing, CancellationToken ct) =>
    ValueResponse(await pricing.ProposeAsync(input, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ProposeStandPricingRule");

app.MapPost("/api/housing/pricing/rules/{ruleId:long}/approve", async (long ruleId, long companyId, long userId, DateOnly date, One4All.Application.Abstractions.Housing.IStandPricingService pricing, CancellationToken ct) =>
    ActionResponse(await pricing.ApproveAsync(companyId, ruleId, userId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApproveStandPricingRule");

app.MapGet("/api/housing/pricing/stands/{standId:long}", async (long standId, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IStandPricingService pricing, CancellationToken ct) =>
    ValueResponse(await pricing.QuoteAsync(companyId, standId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("QuoteStandPrice");

app.MapPost("/api/housing/levies", async (long companyId, string code, string name, One4All.Core.Housing.LevyTrigger trigger, One4All.Core.Housing.LevyBasis basis, decimal rate, string incomeAccountCode, One4All.Application.Abstractions.Housing.IDevelopmentLevyService levies, CancellationToken ct) =>
    ValueResponse(await levies.DefineAsync(companyId, code, name, trigger, basis, rate, incomeAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DefineDevelopmentLevy");

app.MapPost("/api/housing/levies/{code}/deactivate", async (string code, long companyId, One4All.Application.Abstractions.Housing.IDevelopmentLevyService levies, CancellationToken ct) =>
    ActionResponse(await levies.DeactivateAsync(companyId, code, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DeactivateDevelopmentLevy");

app.MapPost("/api/housing/levies/allocations/{certificateNumber}", async (string certificateNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IDevelopmentLevyService levies, CancellationToken ct) =>
    ValueResponse(await levies.ChargeAllocationAsync(companyId, certificateNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ChargeAllocationLevies");

app.MapPost("/api/housing/levies/transfers/{applicationNumber}", async (string applicationNumber, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IDevelopmentLevyService levies, CancellationToken ct) =>
    ValueResponse(await levies.ChargeTransferAsync(companyId, applicationNumber, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ChargeTransferLevies");

app.MapPost("/api/housing/stands/{standId:long}/holders", async (long standId, long companyId, One4All.Core.Housing.CoOwnershipRegime regime, DateOnly date, IReadOnlyList<One4All.Application.Housing.HolderShare> holders, One4All.Application.Abstractions.Housing.IJointOwnershipService joint, CancellationToken ct) =>
    ActionResponse(await joint.RegisterAsync(companyId, standId, regime, holders, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RegisterJointHolders");

app.MapPost("/api/housing/stands/{standId:long}/holders/transfer-share", async (long standId, long companyId, long fromCustomerId, long toCustomerId, decimal portion, DateOnly date, One4All.Application.Abstractions.Housing.IJointOwnershipService joint, CancellationToken ct) =>
    ActionResponse(await joint.TransferShareAsync(companyId, standId, fromCustomerId, toCustomerId, portion, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TransferStandShare");

app.MapPost("/api/housing/stands/{standId:long}/holders/{customerId:long}/death", async (long standId, long customerId, long companyId, DateOnly date, One4All.Application.Abstractions.Housing.IJointOwnershipService joint, CancellationToken ct) =>
    ValueResponse(await joint.RecordDeathAsync(companyId, standId, customerId, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RecordHolderDeath");

app.MapGet("/api/housing/stands/{standId:long}/holders", async (long standId, long companyId, DateOnly asAt, One4All.Application.Abstractions.Housing.IJointOwnershipService joint, CancellationToken ct) =>
    Results.Ok(await joint.HoldersAsync(companyId, standId, asAt, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("StandHolders");

app.MapPost("/api/housing/sectional", async (long companyId, string code, string name, long standId, IReadOnlyList<One4All.Application.Housing.UnitArea> units, One4All.Application.Abstractions.Housing.ISectionalTitleService sectional, CancellationToken ct) =>
    ValueResponse(await sectional.RegisterAsync(companyId, code, name, standId, units, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RegisterSectionalScheme");

app.MapPost("/api/housing/sectional/{code}/units/{unitNumber}/transfer", async (string code, string unitNumber, long companyId, long toCustomerId, decimal consideration, DateOnly date, One4All.Application.Abstractions.Housing.ISectionalTitleService sectional, CancellationToken ct) =>
    ActionResponse(await sectional.TransferUnitAsync(companyId, code, unitNumber, toCustomerId, consideration, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("TransferSectionalUnit");

app.MapGet("/api/housing/sectional/{code}/units", async (string code, long companyId, One4All.Application.Abstractions.Housing.ISectionalTitleService sectional, CancellationToken ct) =>
    ValueResponse(await sectional.UnitsAsync(companyId, code, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SectionalUnits");

app.MapPost("/api/housing/sectional/{code}/levies", async (string code, long companyId, int period, decimal levy, string incomeAccountCode, DateOnly date, One4All.Application.Abstractions.Housing.ISectionalTitleService sectional, CancellationToken ct) =>
    ValueResponse(await sectional.RaiseLevyAsync(companyId, code, period, levy, incomeAccountCode, date, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RaiseSectionalLevy");

app.MapPost("/api/housing/municipal-link", async (long companyId, One4All.Core.Housing.MunicipalLinkTrigger trigger, string ward, IReadOnlyList<long> tariffIds, One4All.Application.Abstractions.Housing.IHousingMunicipalLinkService link, CancellationToken ct) =>
    ActionResponse(await link.ConfigureAsync(companyId, trigger, ward, tariffIds, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ConfigureHousingMunicipalLink");

app.MapPost("/api/housing/stands/{standId:long}/boundary", async (long standId, long companyId, string wkt, One4All.Application.Abstractions.Housing.ISpatialRegisterService spatial, CancellationToken ct) =>
    ValueResponse(await spatial.MapStandAsync(companyId, standId, wkt, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("MapStandBoundary");

app.MapPost("/api/housing/parcels/{parcelId:long}/boundary", async (long parcelId, long companyId, string wkt, One4All.Application.Abstractions.Housing.ISpatialRegisterService spatial, CancellationToken ct) =>
    ValueResponse(await spatial.MapParcelAsync(companyId, parcelId, wkt, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("MapParcelBoundary");

app.MapGet("/api/housing/map/discrepancies", async (long companyId, decimal tolerancePercent, One4All.Application.Abstractions.Housing.ISpatialRegisterService spatial, CancellationToken ct) =>
    Results.Ok(await spatial.DiscrepanciesAsync(companyId, tolerancePercent, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("MappedAreaDiscrepancies");

app.MapGet("/api/housing/map/{layer}", async (One4All.Application.Abstractions.Housing.MapLayer layer, long companyId, long? schemeId, One4All.Application.Abstractions.Housing.ISpatialRegisterService spatial, CancellationToken ct) =>
    Results.Text(await spatial.GeoJsonAsync(companyId, layer, schemeId, ct).ConfigureAwait(false), "application/geo+json"))
.RequireAuthorization()
.WithName("MapLayerGeoJson");

app.MapPost("/api/housing/schemes/{schemeId:long}/servicing", async (long schemeId, long companyId, long jobContractId, One4All.Core.Housing.ServicingBasis basis, One4All.Application.Abstractions.Housing.IServicingCostService servicing, CancellationToken ct) =>
    ActionResponse(await servicing.LinkAsync(companyId, schemeId, jobContractId, basis, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LinkServicingContract");

app.MapPost("/api/housing/stands/{standId:long}/servicing-measures", async (long standId, long companyId, decimal? frontageMetres, decimal? zoningWeight, One4All.Application.Abstractions.Housing.IServicingCostService servicing, CancellationToken ct) =>
    ActionResponse(await servicing.SetMeasuresAsync(companyId, standId, frontageMetres, zoningWeight, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SetStandServicingMeasures");

app.MapPost("/api/housing/schemes/{schemeId:long}/servicing/apportion", async (long schemeId, long companyId, DateOnly date, string servicingWipAccountCode, string standsAvailableAccountCode, One4All.Application.Abstractions.Housing.IServicingCostService servicing, CancellationToken ct) =>
    ValueResponse(await servicing.ApportionAsync(companyId, schemeId, date, servicingWipAccountCode, standsAvailableAccountCode, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ApportionServicingCost");

app.MapGet("/api/housing/schemes/{schemeId:long}/stand-costs", async (long schemeId, long companyId, One4All.Application.Abstractions.Housing.IServicingCostService servicing, CancellationToken ct) =>
    Results.Ok(await servicing.CostPerStandAsync(companyId, schemeId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SchemeCostPerStand");

app.MapPost("/api/messages/templates", async (long companyId, string code, string documentType, string language, One4All.Core.Communications.MessageChannel channel, string? subject, string body, One4All.Application.Abstractions.Communications.INotificationService messages, CancellationToken ct) =>
    ValueResponse(await messages.SaveTemplateAsync(companyId, code, documentType, language, channel, subject ?? string.Empty, body, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SaveMessageTemplate");

app.MapPost("/api/messages/templates/{code}/preview", async (string code, long companyId, string language, One4All.Core.Communications.MessageChannel channel, Dictionary<string, string> values, One4All.Application.Abstractions.Communications.INotificationService messages, CancellationToken ct) =>
    ValueResponse(await messages.PreviewAsync(companyId, code, language, channel, values, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("PreviewMessageTemplate");

app.MapPost("/api/messages/batches", async (long companyId, string name, string code, string language, One4All.Core.Communications.MessageChannel channel, IReadOnlyList<One4All.Application.Abstractions.Communications.DespatchRecipient> recipients, One4All.Application.Abstractions.Communications.INotificationService messages, CancellationToken ct) =>
    ValueResponse(await messages.QueueAsync(companyId, name, code, language, channel, recipients, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("QueueDespatch");

app.MapPost("/api/messages/despatch", async (long companyId, int maxAttempts, One4All.Application.Abstractions.Communications.INotificationService messages, CancellationToken ct) =>
    Results.Ok(await messages.DespatchAsync(companyId, maxAttempts, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DespatchMessages");

app.MapPost("/api/messages/statements", async (long companyId, DateOnly asAt, string templateCode, string language, decimal minimumBalance, One4All.Application.Abstractions.Communications.IStatementDespatchService statements, CancellationToken ct) =>
    ValueResponse(await statements.QueueAsync(companyId, asAt, templateCode, language, minimumBalance, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("QueueStatementEmails");

app.MapGet("/api/messages/batches/{batchId:long}", async (long batchId, long companyId, One4All.Application.Abstractions.Communications.INotificationService messages, CancellationToken ct) =>
    ValueResponse(await messages.BatchStatusAsync(companyId, batchId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DespatchBatchStatus");

app.MapGet("/api/notifications", async (long companyId, long userId, bool unreadOnly, One4All.Application.Abstractions.Communications.INotificationService messages, CancellationToken ct) =>
    Results.Ok(await messages.InboxAsync(companyId, userId, unreadOnly, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("NotificationInbox");

app.MapPost("/api/notifications/{notificationId:long}/read", async (long notificationId, long companyId, long userId, One4All.Application.Abstractions.Communications.INotificationService messages, CancellationToken ct) =>
    ActionResponse(await messages.MarkReadAsync(companyId, userId, notificationId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("MarkNotificationRead");

app.MapPost("/api/municipal/faults/categories", async (long companyId, string code, string name, One4All.Core.Municipal.ServiceType? serviceType, decimal responseHours, decimal resolutionHours, One4All.Application.Abstractions.Municipal.IServiceDeskService desk, CancellationToken ct) =>
    ValueResponse(await desk.DefineCategoryAsync(companyId, code, name, serviceType, responseHours, resolutionHours, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DefineFaultCategory");

app.MapPost("/api/municipal/faults", async (One4All.Application.Abstractions.Municipal.FaultReport report, One4All.Application.Abstractions.Municipal.IServiceDeskService desk, CancellationToken ct) =>
    ValueResponse(await desk.LogAsync(report, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("LogFault");

app.MapPost("/api/municipal/faults/{reference}/assign", async (string reference, long companyId, long userId, DateTimeOffset at, string? note, One4All.Application.Abstractions.Municipal.IServiceDeskService desk, CancellationToken ct) =>
    ActionResponse(await desk.AssignAsync(companyId, reference, userId, at, note, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AssignFault");

app.MapPost("/api/municipal/faults/{reference}/resolve", async (string reference, long companyId, string resolution, DateTimeOffset at, One4All.Application.Abstractions.Municipal.IServiceDeskService desk, CancellationToken ct) =>
    ActionResponse(await desk.ResolveAsync(companyId, reference, resolution, at, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ResolveFault");

app.MapPost("/api/municipal/faults/{reference}/close", async (string reference, long companyId, DateTimeOffset at, One4All.Application.Abstractions.Municipal.IServiceDeskService desk, CancellationToken ct) =>
    ActionResponse(await desk.CloseAsync(companyId, reference, at, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CloseFault");

app.MapPost("/api/municipal/faults/{reference}/reopen", async (string reference, long companyId, string reason, DateTimeOffset at, One4All.Application.Abstractions.Municipal.IServiceDeskService desk, CancellationToken ct) =>
    ActionResponse(await desk.ReopenAsync(companyId, reference, reason, at, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReopenFault");

app.MapGet("/api/municipal/faults/{reference}/status", async (string reference, long companyId, One4All.Application.Abstractions.Municipal.IServiceDeskService desk, CancellationToken ct) =>
    ValueResponse(await desk.PublicStatusAsync(companyId, reference, ct).ConfigureAwait(false)))
.WithName("PublicFaultStatus");

app.MapGet("/api/municipal/faults/sla", async (long companyId, DateTimeOffset now, One4All.Application.Abstractions.Municipal.IServiceDeskService desk, CancellationToken ct) =>
    Results.Ok(await desk.SlaBoardAsync(companyId, now, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("FaultSlaBoard");

app.MapPost("/api/housing/evictions", async (long companyId, string tenancyNumber, DateOnly date, int noticePeriodDays, long userId, One4All.Application.Abstractions.Housing.IEvictionService evictions, CancellationToken ct) =>
    ValueResponse(await evictions.OpenAsync(companyId, tenancyNumber, date, noticePeriodDays, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("OpenEvictionCase");

app.MapPost("/api/housing/evictions/{caseNumber}/advance", async (string caseNumber, long companyId, One4All.Core.Housing.EvictionStage stage, DateOnly date, string? reference, string? refundBankAccountCode, long userId, One4All.Application.Abstractions.Housing.IEvictionService evictions, CancellationToken ct) =>
    ActionResponse(await evictions.AdvanceAsync(companyId, caseNumber, stage, date, reference, refundBankAccountCode, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("AdvanceEvictionCase");

app.MapPost("/api/housing/evictions/{caseNumber}/withdraw", async (string caseNumber, long companyId, string reason, One4All.Application.Abstractions.Housing.IEvictionService evictions, CancellationToken ct) =>
    ActionResponse(await evictions.WithdrawAsync(companyId, caseNumber, reason, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("WithdrawEvictionCase");

app.MapGet("/api/housing/evictions", async (long companyId, bool? openOnly, One4All.Application.Abstractions.Housing.IEvictionService evictions, CancellationToken ct) =>
    Results.Ok(await evictions.RegisterAsync(companyId, openOnly ?? false, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("EvictionRegister");

app.MapPost("/api/housing/repossessions", async (One4All.Application.Abstractions.Housing.RepossessionRequest request, One4All.Application.Abstractions.Housing.IRepossessionService repossessions, CancellationToken ct) =>
    ValueResponse(await repossessions.RepossessAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RepossessStand");

app.MapGet("/api/housing/repossessions", async (long companyId, DateOnly start, DateOnly end, One4All.Application.Abstractions.Housing.IRepossessionService repossessions, CancellationToken ct) =>
    Results.Ok(await repossessions.RegisterAsync(companyId, start, end, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RepossessionRegister");

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

app.MapPost("/api/ar/debt-cases/{caseNumber}/write-off/request", async (string caseNumber, long companyId, long userId, string reason, string? badDebtAccountCode, One4All.Application.Abstractions.Trading.IDebtCollectionService debts, CancellationToken ct) =>
    ActionResponse(string.IsNullOrWhiteSpace(badDebtAccountCode)
        ? await debts.RequestWriteOffAsync(companyId, caseNumber, userId, reason, ct).ConfigureAwait(false)
        : await debts.RequestWriteOffAsync(companyId, caseNumber, userId, reason, badDebtAccountCode, ct).ConfigureAwait(false)))
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

app.MapGet("/api/audit", async (long companyId, DateOnly? from, DateOnly? to, string? entityType, string? entityId, string? userName, One4All.Core.Audit.AuditAction? action, int? take, ClaimsPrincipal user,
    IAuditService audit, CancellationToken ct) =>
    Results.Ok(await audit.QueryAsync(new AuditQuery
    {
        CompanyId = companyId, From = from, To = to, EntityType = entityType, EntityId = entityId, UserName = userName, Action = action, Take = take ?? 500, RequestingUserId = SignedInUser(user) ?? -1,
    }, ct).ConfigureAwait(false)))
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

app.MapPost("/api/system/jobs", async (One4All.Application.Abstractions.Scheduling.JobDefinition definition, One4All.Application.Abstractions.Scheduling.IJobScheduler jobs, CancellationToken ct) =>
    ValueResponse(await jobs.DefineAsync(definition, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DefineScheduledJob");

app.MapPost("/api/system/jobs/{code}/enabled", async (string code, long companyId, bool enabled, One4All.Application.Abstractions.Scheduling.IJobScheduler jobs, CancellationToken ct) =>
    ActionResponse(await jobs.SetEnabledAsync(companyId, code, enabled, DateTimeOffset.UtcNow, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("EnableScheduledJob");

app.MapPost("/api/system/jobs/{code}/run", async (string code, long companyId, One4All.Application.Abstractions.Scheduling.IJobScheduler jobs, CancellationToken ct) =>
    ValueResponse(await jobs.RunNowAsync(companyId, code, DateTimeOffset.UtcNow, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RunScheduledJob");

app.MapGet("/api/system/licence", async (One4All.Application.Abstractions.Licensing.ILicenceService licence, One4AllDbContext db, CancellationToken ct) =>
{
    One4All.Licensing.Core.LicenseValidationResult current = licence.Current;
    var usage = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["companies"] = await db.Companies.CountAsync(ct).ConfigureAwait(false),
        ["named_users"] = await db.Users.CountAsync(u => u.IsActive, ct).ConfigureAwait(false),
        ["pos_terminals"] = await db.PosTerminals.CountAsync(t => t.IsActive, ct).ConfigureAwait(false),
        ["warehouses"] = await db.Warehouses.CountAsync(ct).ConfigureAwait(false),
        ["properties"] = await db.Properties.CountAsync(ct).ConfigureAwait(false),
        ["stands"] = await db.HousingStands.CountAsync(ct).ConfigureAwait(false),
        ["waiting_list"] = await db.HousingApplicants.CountAsync(ct).ConfigureAwait(false),
        ["students"] = await db.Students.CountAsync(ct).ConfigureAwait(false),
        ["employees"] = await db.Employees.CountAsync(e => e.Status != One4All.Core.Payroll.EmployeeStatus.Terminated, ct).ConfigureAwait(false),
        ["transactions_per_year"] = await db.Journals.CountAsync(j => j.PostingDate.Year == DateTime.Today.Year, ct).ConfigureAwait(false),
        ["budget_departments"] = await db.Set<One4All.Core.Budgeting.Budget>().Where(b => b.Department != null).Select(b => b.Department).Distinct().CountAsync(ct).ConfigureAwait(false),
    };
    return Results.Ok(new
    {
        status = current.Status.ToString(),
        current.Message,
        licence.Enforced,
        licence.DaysUntilExpiry,
        licence.Warning,
        customer = current.Payload?.Customer.Name,
        edition = current.Payload?.Edition.ToString(),
        modules = current.Payload?.Modules.Select(m => new { m.Code, m.ValidTo, licensed = licence.IsModuleLicensed(m.Code) }),
        limits = usage.Select(u => new { key = u.Key, inUse = u.Value, limit = licence.GetLimit(u.Key) }),
        machine = licence.MachineFingerprint,
    });
})
.RequireAuthorization()
.WithName("LicenceStatus");

app.MapGet("/api/system/jobs/{code}/history", async (string code, long companyId, One4All.Application.Abstractions.Scheduling.IJobScheduler jobs, CancellationToken ct) =>
    ValueResponse(await jobs.HistoryAsync(companyId, code, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ScheduledJobHistory");

app.MapPost("/api/reports/{code}/run", async (string code, long companyId, One4All.Application.Abstractions.Reporting.ReportFormat format, One4All.Core.Reporting.ReportRunPurpose purpose, Dictionary<string, string>? parameters, ClaimsPrincipal user, One4All.Application.Abstractions.Reporting.IAuditedReportRunner reports, CancellationToken ct) =>
{
    long? userId = long.TryParse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long id) ? id : null;
    Dictionary<string, object?> values = (parameters ?? []).ToDictionary(p => p.Key, p => (object?)(DateOnly.TryParseExact(p.Value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateOnly d) ? d : p.Value), StringComparer.OrdinalIgnoreCase);
    One4All.Core.Common.Result<One4All.Application.Abstractions.Reporting.AuditedReport> run = await reports.RunAsync(companyId, code, values, format, purpose, userId, ct).ConfigureAwait(false);
    return run.IsSuccess ? Results.File(run.Value.Document.Content, run.Value.Document.ContentType, run.Value.Document.FileName) : ValueResponse(run);
})
.RequireAuthorization()
.WithName("RunReportAudited");

// The choices for a report parameter with a list-of-values query; the other query values are the parameters entered so far (cascading).
app.MapGet("/api/reports/{code}/parameters/{name}/options", async (string code, string name, long companyId, HttpRequest request,
    One4All.Application.Abstractions.Reporting.IReportParameterLookupService lookups, CancellationToken ct) =>
{
    Dictionary<string, string> entered = request.Query.Where(q => !string.Equals(q.Key, "companyId", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    return ValueResponse(await lookups.OptionsAsync(companyId, code, name, entered, ct).ConfigureAwait(false));
})
.RequireAuthorization()
.WithName("ReportParameterOptions");

app.MapGet("/api/reports/{code}/runs", async (string code, long companyId, One4All.Application.Abstractions.Reporting.IAuditedReportRunner reports, CancellationToken ct) =>
    Results.Ok(await reports.RunLogAsync(companyId, code, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReportRunLog");

app.MapPost("/api/reports/{code}/copy", async (string code, long companyId, long? userId, One4All.Application.Abstractions.Reporting.IReportDefinitionService reports, CancellationToken ct) =>
    ActionResponse(await reports.CopySystemReportAsync(companyId, code, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("CopySystemReport");

app.MapPut("/api/reports/{code}", async (string code, long companyId, string name, string? module, long? userId, string? note, HttpRequest request, One4All.Application.Abstractions.Reporting.IReportDefinitionService reports, CancellationToken ct) =>
{
    using StreamReader reader = new(request.Body);
    string json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    return ValueResponse(await reports.SaveAsync(companyId, code, name, module, json, userId, note, ct).ConfigureAwait(false));
})
.RequireAuthorization()
.WithName("SaveCompanyReport");

app.MapGet("/api/reports/{code}/versions", async (string code, long companyId, One4All.Application.Abstractions.Reporting.IReportDefinitionService reports, CancellationToken ct) =>
    ValueResponse(await reports.HistoryAsync(companyId, code, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReportVersionHistory");

app.MapGet("/api/reports/{code}/diff", async (string code, long companyId, int fromVersion, int toVersion, One4All.Application.Abstractions.Reporting.IReportDefinitionService reports, CancellationToken ct) =>
    ValueResponse(await reports.DiffAsync(companyId, code, fromVersion, toVersion, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReportVersionDiff");

app.MapPost("/api/reports/{code}/rollback", async (string code, long companyId, int version, long? userId, One4All.Application.Abstractions.Reporting.IReportDefinitionService reports, CancellationToken ct) =>
    ValueResponse(await reports.RollbackAsync(companyId, code, version, userId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RollbackReportVersion");

app.MapPost("/api/reports/subscriptions", async (One4All.Application.Abstractions.Reporting.SubscriptionRequest request, One4All.Application.Abstractions.Reporting.IReportSubscriptionService subscriptions, CancellationToken ct) =>
    ActionResponse(await subscriptions.SubscribeAsync(request, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SubscribeToReport");

app.MapPost("/api/reports/subscriptions/{code}/active", async (string code, long companyId, bool active, One4All.Application.Abstractions.Reporting.IReportSubscriptionService subscriptions, CancellationToken ct) =>
    ActionResponse(await subscriptions.SetActiveAsync(companyId, code, active, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("SetReportSubscriptionActive");

app.MapPost("/api/reports/subscriptions/{code}/run", async (string code, long companyId, One4All.Application.Abstractions.Reporting.IReportSubscriptionService subscriptions, CancellationToken ct) =>
    ValueResponse(await subscriptions.RunAsync(companyId, code, DateTimeOffset.UtcNow, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("RunReportSubscription");

app.MapGet("/api/reports/subscriptions/{code}/deliveries", async (string code, long companyId, One4All.Application.Abstractions.Reporting.IReportSubscriptionService subscriptions, CancellationToken ct) =>
    ValueResponse(await subscriptions.DeliveryLogAsync(companyId, code, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("ReportDeliveryLog");

app.MapGet("/api/accounting/data-health", async (long companyId, DateOnly asAtDate, One4All.Application.Abstractions.Accounting.IDataHealthService health, CancellationToken ct) =>
    ValueResponse(await health.SnapshotAsync(companyId, asAtDate, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("DataHealthDashboard");

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

// Employee self-service (brief §4.15.7): everything is the signed-in user's own record, found through their login.
app.MapGet("/api/ess/me", async (ClaimsPrincipal user, One4All.Application.Abstractions.Payroll.IEmployeeSelfService ess, CancellationToken ct) =>
    SignedInUser(user) is long id ? ValueResponse(await ess.ProfileAsync(id, ct).ConfigureAwait(false)) : Results.Unauthorized())
.RequireAuthorization()
.WithName("EssProfile");

app.MapGet("/api/ess/payslips", async (ClaimsPrincipal user, One4All.Application.Abstractions.Payroll.IEmployeeSelfService ess, CancellationToken ct) =>
    SignedInUser(user) is long id ? ValueResponse(await ess.PayslipsAsync(id, ct).ConfigureAwait(false)) : Results.Unauthorized())
.RequireAuthorization()
.WithName("EssPayslips");

app.MapGet("/api/ess/payslips/{period}", async (string period, ClaimsPrincipal user, One4All.Application.Abstractions.Payroll.IEmployeeSelfService ess, CancellationToken ct) =>
    SignedInUser(user) is long id ? TextResponse(await ess.PayslipTextAsync(id, period, ct).ConfigureAwait(false)) : Results.Unauthorized())
.RequireAuthorization()
.WithName("EssPayslip");

app.MapGet("/api/ess/payslips/{period}/pdf", async (string period, ClaimsPrincipal user, One4All.Application.Abstractions.Payroll.IEmployeeSelfService ess,
    One4All.Application.Abstractions.Payroll.IPayslipDistributionService payslips, CancellationToken ct) =>
{
    if (SignedInUser(user) is not long id)
    {
        return Results.Unauthorized();
    }

    var me = await ess.ProfileAsync(id, ct).ConfigureAwait(false);
    if (me.IsFailure)
    {
        return ValueResponse(me);
    }

    var pdf = await payslips.PayslipPdfAsync(me.Value.CompanyId, me.Value.EmployeeId, period, ct).ConfigureAwait(false);
    return pdf.IsSuccess ? Results.File(pdf.Value, "application/pdf", $"Payslip-{period}.pdf") : ValueResponse(pdf);
})
.RequireAuthorization()
.WithName("EssPayslipPdf");

app.MapPost("/api/payroll/runs/{runNumber}/email-payslips", async (string runNumber, long companyId, One4All.Application.Abstractions.Payroll.IPayslipDistributionService payslips, CancellationToken ct) =>
    ValueResponse(await payslips.EmailRunAsync(companyId, runNumber, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("EmailPayslips");

app.MapGet("/api/ess/tax-certificate", async (int year, ClaimsPrincipal user, One4All.Application.Abstractions.Payroll.IEmployeeSelfService ess, CancellationToken ct) =>
    SignedInUser(user) is long id ? ValueResponse(await ess.TaxCertificateAsync(id, year, ct).ConfigureAwait(false)) : Results.Unauthorized())
.RequireAuthorization()
.WithName("EssTaxCertificate");

app.MapGet("/api/ess/leave", async (ClaimsPrincipal user, One4All.Application.Abstractions.Payroll.IEmployeeSelfService ess, CancellationToken ct) =>
    SignedInUser(user) is long id ? ValueResponse(await ess.LeaveBalancesAsync(id, ct).ConfigureAwait(false)) : Results.Unauthorized())
.RequireAuthorization()
.WithName("EssLeave");

app.MapPost("/api/ess/leave", async (string leaveType, DateOnly fromDate, DateOnly toDate, decimal days, ClaimsPrincipal user, One4All.Application.Abstractions.Payroll.IEmployeeSelfService ess, CancellationToken ct) =>
    SignedInUser(user) is long id ? ValueResponse(await ess.ApplyForLeaveAsync(id, leaveType, fromDate, toDate, days, ct).ConfigureAwait(false)) : Results.Unauthorized())
.RequireAuthorization()
.WithName("EssApplyForLeave");

app.MapPost("/api/ess/detail-change", async (string field, string newValue, ClaimsPrincipal user, One4All.Application.Abstractions.Payroll.IEmployeeSelfService ess, CancellationToken ct) =>
    SignedInUser(user) is long id ? ValueResponse(await ess.RequestDetailChangeAsync(id, field, newValue, ct).ConfigureAwait(false)) : Results.Unauthorized())
.RequireAuthorization()
.WithName("EssDetailChange");

app.MapGet("/api/hr/self-service/pending", async (long companyId, One4All.Application.Abstractions.Payroll.IEmployeeSelfService ess, CancellationToken ct) =>
    Results.Ok(await ess.PendingAsync(companyId, ct).ConfigureAwait(false)))
.RequireAuthorization()
.WithName("EssPending");

app.MapPost("/api/hr/self-service/leave/{applicationId:long}/decide", async (long applicationId, long companyId, bool approve, string? note, ClaimsPrincipal user,
    One4All.Application.Abstractions.Payroll.IEmployeeSelfService ess, CancellationToken ct) =>
    SignedInUser(user) is long id ? ValueResponse(await ess.DecideLeaveAsync(companyId, applicationId, approve, id, note, ct).ConfigureAwait(false)) : Results.Unauthorized())
.RequireAuthorization()
.WithName("EssDecideLeave");

app.MapPost("/api/hr/self-service/detail/{requestId:long}/decide", async (long requestId, long companyId, bool approve, string? note, ClaimsPrincipal user,
    One4All.Application.Abstractions.Payroll.IEmployeeSelfService ess, CancellationToken ct) =>
    SignedInUser(user) is long id ? ValueResponse(await ess.DecideDetailChangeAsync(companyId, requestId, approve, id, note, ct).ConfigureAwait(false)) : Results.Unauthorized())
.RequireAuthorization()
.WithName("EssDecideDetail");

app.Run();

// The top-level scalar values of a JSON request body, read without consuming it (the endpoint still binds it), for the access check.
static async Task<Dictionary<string, string>?> JsonBodyScalarsAsync(HttpRequest request, CancellationToken ct)
{
    if (HttpMethods.IsGet(request.Method) || request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true || request.ContentLength is 0)
    {
        return null;
    }

    request.EnableBuffering();
    try
    {
        using System.Text.Json.JsonDocument document = await System.Text.Json.JsonDocument.ParseAsync(request.Body, cancellationToken: ct).ConfigureAwait(false);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.Json.JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind is System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.String)
            {
                values[property.Name] = property.Value.ValueKind == System.Text.Json.JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.GetRawText();
            }
        }

        return values;
    }
    catch (System.Text.Json.JsonException)
    {
        return null; // the endpoint's own binding reports a malformed body
    }
    finally
    {
        request.Body.Position = 0;
    }
}

// Maps a text-bearing Result to a plain-text response (200), or 404 / 409 / 400 by error type.
// The signed-in user's id from the token's subject, or null.
static long? SignedInUser(ClaimsPrincipal user)
    => long.TryParse(user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long id) ? id : null;

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
/// <param name="OneTimeCode">The authenticator code, for users with two-factor sign-in.</param>
internal sealed record LoginRequest(string Username, string Password, string? OneTimeCode = null);

/// <summary>A password change for the signed-in user.</summary>
/// <param name="CurrentPassword">The current password.</param>
/// <param name="NewPassword">The new password.</param>
internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
