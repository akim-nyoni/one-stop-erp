using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>Facility hire: bookings without double-booking, invoiced and refunded through AR.</summary>
public sealed class FacilityBookingService : IFacilityBookingService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The receivables ledger.</param>
    public FacilityBookingService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateFacilityAsync(long companyId, string code, string name, decimal dailyRate, string revenueAccountCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || dailyRate < 0m || string.IsNullOrWhiteSpace(revenueAccountCode))
        {
            return Error.Validation("Mun.FacilityDetails", "A facility needs a code, a name, a daily rate of zero or more and a revenue account.");
        }

        string facilityCode = code.Trim().ToUpperInvariant();
        if (await _db.Set<Facility>().AnyAsync(f => f.CompanyId == companyId && f.Code == facilityCode, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Mun.FacilityExists", $"Facility {facilityCode} already exists.");
        }

        Facility facility = new(companyId, facilityCode, name.Trim(), dailyRate, revenueAccountCode.Trim());
        _db.Set<Facility>().Add(facility);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return facility.Id;
    }

    /// <inheritdoc />
    public Task<Result<FacilityBooked>> BookAsync(FacilityBookingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(() => BookCoreAsync(request, ct), ct);
    }

    private async Task<Result<FacilityBooked>> BookCoreAsync(FacilityBookingRequest request, CancellationToken ct)
    {
        if (request.ToDate < request.FromDate || string.IsNullOrWhiteSpace(request.Purpose))
        {
            return Error.Validation("Mun.BookingDetails", "A booking needs a purpose and a last day on or after its first.");
        }

        Result<Facility> found = await FindFacilityAsync(request.CompanyId, request.FacilityCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        Facility facility = found.Value;
        if (!facility.IsActive)
        {
            return Error.Rule("Mun.FacilityInactive", $"Facility {facility.Code} is not available for hire.");
        }

        Customer? hirer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (hirer is null)
        {
            return Error.NotFound("Mun.HirerNotFound", $"Customer {request.CustomerId} not found.");
        }

        // Serialise bookings of this facility so two clerks cannot take the same day at once.
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM mun.facility WHERE id = {facility.Id} FOR UPDATE", ct).ConfigureAwait(false);
        var clashes = await _db.Set<FacilityBooking>().AsNoTracking()
            .Where(b => b.FacilityId == facility.Id && b.Status == FacilityBookingStatus.Booked && b.FromDate <= request.ToDate && request.FromDate <= b.ToDate)
            .Select(b => b.BookingNumber).ToListAsync(ct).ConfigureAwait(false);
        if (clashes.Count > 0)
        {
            return Error.Conflict("Mun.FacilityTaken", $"{facility.Name} is already booked on some of those days ({string.Join(", ", clashes)}).");
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "FB", request.BookedOn, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        int days = BookingCalendar.Days(request.FromDate, request.ToDate);
        decimal fee = BookingCalendar.Fee(request.FromDate, request.ToDate, facility.DailyRate);
        if (fee > 0m)
        {
            Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = request.CompanyId, CustomerId = hirer.Id, DocumentNumber = number.Value, DocumentDate = request.BookedOn, Currency = hirer.Currency,
                NetAmount = fee, SalesAccountCode = facility.RevenueAccountCode,
                Narrative = $"Hire of {facility.Name} {request.FromDate:yyyy-MM-dd} to {request.ToDate:yyyy-MM-dd}: {request.Purpose.Trim()}",
            }, ct).ConfigureAwait(false);
            if (invoiced.IsFailure)
            {
                return invoiced.Error;
            }
        }

        _db.Set<FacilityBooking>().Add(new FacilityBooking(request.CompanyId, number.Value, facility.Id, hirer.Id, request.FromDate, request.ToDate, fee, request.Purpose.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new FacilityBooked(number.Value, days, fee);
    }

    /// <inheritdoc />
    public Task<Result<decimal>> CancelAsync(long companyId, string bookingNumber, DateOnly date, decimal refundPercent, CancellationToken ct = default)
        => _db.AtomicAsync(() => CancelCoreAsync(companyId, bookingNumber, date, refundPercent, ct), ct);

    private async Task<Result<decimal>> CancelCoreAsync(long companyId, string bookingNumber, DateOnly date, decimal refundPercent, CancellationToken ct)
    {
        if (refundPercent is < 0m or > 100m)
        {
            return Error.Validation("Mun.RefundPercent", "The refund must be between 0% and 100% of the fee.");
        }

        string number = (bookingNumber ?? string.Empty).Trim();
        FacilityBooking? booking = await _db.Set<FacilityBooking>().FirstOrDefaultAsync(b => b.CompanyId == companyId && b.BookingNumber == number, ct).ConfigureAwait(false);
        if (booking is null)
        {
            return Error.NotFound("Mun.BookingNotFound", $"Booking {number} not found.");
        }

        if (booking.Status != FacilityBookingStatus.Booked)
        {
            return Error.Rule("Mun.BookingCancelled", $"Booking {number} has already been cancelled.");
        }

        decimal refund = BookingCalendar.Refund(booking.Fee, refundPercent);
        if (refund > 0m)
        {
            Facility facility = await _db.Set<Facility>().AsNoTracking().FirstAsync(f => f.Id == booking.FacilityId, ct).ConfigureAwait(false);
            Customer hirer = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == booking.CustomerId, ct).ConfigureAwait(false);
            Result<TradingPostingResult> credited = await _ar.PostCreditNoteAsync(new SalesCreditNoteRequest
            {
                CompanyId = companyId, CustomerId = hirer.Id, DocumentNumber = $"{booking.BookingNumber}-CN", DocumentDate = date, Currency = hirer.Currency,
                NetAmount = refund, SalesAccountCode = facility.RevenueAccountCode, Narrative = $"Cancellation of booking {booking.BookingNumber} ({refundPercent:0.##}% refunded)",
            }, ct).ConfigureAwait(false);
            if (credited.IsFailure)
            {
                return credited.Error;
            }
        }

        booking.Cancel(refund);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return refund;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FacilityCalendarEntry>>> CalendarAsync(long companyId, string facilityCode, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        Result<Facility> found = await FindFacilityAsync(companyId, facilityCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        long facilityId = found.Value.Id;
        List<FacilityCalendarEntry> entries = await (
            from b in _db.Set<FacilityBooking>().AsNoTracking()
            join c in _db.Customers.AsNoTracking() on b.CustomerId equals c.Id
            where b.FacilityId == facilityId && b.Status == FacilityBookingStatus.Booked && b.FromDate <= end && start <= b.ToDate
            orderby b.FromDate, b.BookingNumber
            select new FacilityCalendarEntry(b.BookingNumber, c.Code, b.FromDate, b.ToDate, b.Purpose))
            .ToListAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<FacilityCalendarEntry>>.Success(entries);
    }

    private async Task<Result<Facility>> FindFacilityAsync(long companyId, string code, CancellationToken ct)
    {
        string facilityCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        Facility? facility = await _db.Set<Facility>().FirstOrDefaultAsync(f => f.CompanyId == companyId && f.Code == facilityCode, ct).ConfigureAwait(false);
        if (facility is null)
        {
            return Error.NotFound("Mun.FacilityNotFound", $"Facility {facilityCode} not found.");
        }

        return facility;
    }
}
