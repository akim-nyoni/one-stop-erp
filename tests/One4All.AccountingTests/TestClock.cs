using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Common;
using One4All.Licensing.Core;

namespace One4All.AccountingTests;

/// <summary>
/// The clock the accounting tests' providers run on: the real time, unless a test sets the day, so a test can set up a history as though each
/// step had been captured on its own day (a waiting-list registration is dated the day it is captured, say).
/// </summary>
public sealed class TestClock : IClock
{
    /// <summary>Gets or sets the day the clock reads (null: the real time).</summary>
    public DateOnly? Today { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => Today is DateOnly day ? new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero) : DateTimeOffset.UtcNow;

    /// <summary>Registers an applicant as though captured on the application's registration date (the clock reads that day for the call).</summary>
    /// <param name="services">The provider or scope's services.</param>
    /// <param name="application">The application, dated the day it is captured.</param>
    /// <returns>The registration, or why it was refused.</returns>
    public static async Task<Result<WaitingListRegistration>> RegisterCapturedAsync(IServiceProvider services, WaitingListApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        TestClock clock = (TestClock)services.GetRequiredService<IClock>();
        clock.Today = application.RegistrationDate;
        try
        {
            return await services.GetRequiredService<IWaitingListService>().RegisterAsync(application).ConfigureAwait(false);
        }
        finally
        {
            clock.Today = null;
        }
    }
}
