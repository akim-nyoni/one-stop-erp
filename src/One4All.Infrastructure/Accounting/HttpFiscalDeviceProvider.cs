using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Common;

namespace One4All.Infrastructure.Accounting;

/// <summary>Where a revenue authority's fiscal data management system is and how this installation signs in to it (the <c>Fiscal</c> configuration section).</summary>
public sealed class FiscalOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Fiscal";

    /// <summary>The API's base address (blank: no device; ONE4ALL_FISCAL_SIMULATE=1 for the simulator).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The API key or bearer token.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The device id the authority registered for this installation.</summary>
    public string? DeviceId { get; set; }

    /// <summary>The seconds a call may take before the receipt is queued for retry.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// The reference implementation skeleton (brief §3.5): a fiscal device reached over HTTPS as a revenue authority's fiscal data management system - the
/// day is opened and closed with its Z report, each receipt, refund, invoice and credit note is posted for signing and the code and verification URL
/// come back, and a device that cannot be reached queues the receipt for the retry task. The JSON shapes are the simplest a real API uses; an
/// authority's own field names are a mapping change in this file.
/// </summary>
public sealed class HttpFiscalDeviceProvider : IFiscalDeviceProvider
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly HttpClient _http;
    private readonly FiscalOptions _options;

    /// <summary>Creates the provider.</summary>
    /// <param name="http">The HTTP client (its base address and authorisation are set here).</param>
    /// <param name="options">The options.</param>
    public HttpFiscalDeviceProvider(HttpClient http, FiscalOptions options)
    {
        _http = http;
        _options = options;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            _http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
    }

    /// <inheritdoc />
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.BaseUrl);

    /// <inheritdoc />
    public async Task<Result<int>> OpenDayAsync(long companyId, CancellationToken ct = default)
    {
        Result<DayResponse> opened = await PostAsync<DayResponse>("fiscal-days/open", new { deviceId = _options.DeviceId, companyId }, ct).ConfigureAwait(false);
        return opened.IsSuccess ? opened.Value.DayNumber : opened.Error;
    }

    /// <inheritdoc />
    public async Task<Result<FiscalReceiptResult>> FiscaliseAsync(FiscalReceiptRequest request, CancellationToken ct = default)
    {
        Result<ReceiptResponse> signed = await PostAsync<ReceiptResponse>("receipts", new
        {
            deviceId = _options.DeviceId, request.CompanyId, kind = request.Kind.ToString(), number = request.DocumentNumber, reference = request.ReferenceDocumentNumber,
            total = request.TotalGross, tax = request.TotalTax, request.Currency, issuedAt = request.IssuedAtUtc, buyerTaxNumber = request.CustomerTaxNumber,
        }, ct).ConfigureAwait(false);
        if (signed.IsFailure)
        {
            // Unreachable or refused transiently: the receipt stands and waits for the retry task.
            return signed.Error.Code == "Fiscal.Rejected" ? signed.Error : new FiscalReceiptResult(null, null, null, Queued: true);
        }

        return new FiscalReceiptResult(signed.Value.FiscalCode, signed.Value.VerificationUrl ?? signed.Value.Signature, signed.Value.DayNumber, Queued: false);
    }

    /// <inheritdoc />
    public async Task<Result> SubmitDayReportAsync(FiscalDayReport report, CancellationToken ct = default)
    {
        Result<DayResponse> submitted = await PostAsync<DayResponse>("fiscal-days/report", new
        {
            deviceId = _options.DeviceId, report.CompanyId, date = report.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), report.SaleCount, report.SalesGross, report.SalesTax,
            report.RefundCount, report.RefundsGross, report.VoidCount, report.VoidsGross, report.Currency,
        }, ct).ConfigureAwait(false);
        return submitted.IsSuccess ? Result.Success() : Result.Failure(submitted.Error);
    }

    /// <inheritdoc />
    public async Task<Result> CloseDayAsync(long companyId, CancellationToken ct = default)
    {
        Result<DayResponse> closed = await PostAsync<DayResponse>("fiscal-days/close", new { deviceId = _options.DeviceId, companyId }, ct).ConfigureAwait(false);
        return closed.IsSuccess ? Result.Success() : Result.Failure(closed.Error);
    }

    private async Task<Result<T>> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync(path, body, Json, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                return Error.Rule("Fiscal.Rejected", $"The fiscal device rejected {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)}");
            }

            if (!response.IsSuccessStatusCode)
            {
                return Error.Failure("Fiscal.Device", $"The fiscal device answered {(int)response.StatusCode} to {path}.");
            }

            T? parsed = await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
            return parsed is null ? Error.Failure("Fiscal.Device", $"The fiscal device sent no answer to {path}.") : parsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return Error.Failure("Fiscal.Device", $"The fiscal device cannot be reached: {ex.Message}");
        }
    }

    private sealed record DayResponse(int DayNumber);

    private sealed record ReceiptResponse(string FiscalCode, string? Signature, string? VerificationUrl, int? DayNumber);
}
