using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Documents;

/// <summary>The offer letter and the allocation letter (brief §4.10.4): the applicant, the stand and scheme, the price and deposit, and the deadline or the confirmation.</summary>
public static class HousingLetters
{
    /// <summary>The deposit asked for when none is given.</summary>
    public const decimal DefaultDepositPercent = 10m;

    /// <summary>Renders the offer letter, or the allocation letter once the offer is accepted.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The council.</param>
    /// <param name="certificate">The offer's certificate number.</param>
    /// <param name="offer">True for the offer letter, false for the allocation letter.</param>
    /// <param name="date">The letter's date.</param>
    /// <param name="depositPercent">The deposit as a percentage of the price.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The letter, or why there is none.</returns>
    public static async Task<Result<string>> RenderAsync(One4AllDbContext db, long companyId, string certificate, bool offer, DateOnly date, decimal depositPercent, CancellationToken ct = default)
    {
        string number = certificate.Trim().ToUpperInvariant();
        Core.Housing.StandAllocation? line = await db.Set<Core.Housing.StandAllocation>().AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.CertificateNumber == number, ct)
            .ConfigureAwait(false);
        if (line is null)
        {
            return Error.NotFound("Doc.NotFound", $"No offer with certificate {number}.");
        }

        if (!offer && line.Status != Core.Housing.AllocationStatus.Accepted)
        {
            return Error.Validation("Doc.NotAccepted", $"Offer {number} is {line.Status}; the allocation letter follows acceptance.");
        }

        Core.Housing.HousingStand stand = await db.HousingStands.AsNoTracking().SingleAsync(s => s.Id == line.StandId, ct).ConfigureAwait(false);
        Core.Housing.HousingApplicant applicant = await db.HousingApplicants.AsNoTracking().SingleAsync(a => a.Id == line.ApplicantId, ct).ConfigureAwait(false);
        string scheme = stand.SchemeId is long schemeId
            ? await db.HousingSchemes.AsNoTracking().Where(s => s.Id == schemeId).Select(s => s.Code + " " + s.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "-" : "-";
        string council = await db.Companies.AsNoTracking().Where(x => x.Id == companyId).Select(x => x.Name).SingleAsync(ct).ConfigureAwait(false);
        decimal price = line.OfferedPrice ?? stand.SellingPrice;
        decimal deposit = Math.Round(price * depositPercent / 100m, 2, MidpointRounding.AwayFromZero);
        List<string> text =
        [
            council.ToUpperInvariant(), string.Empty, D(date), string.Empty,
            $"{applicant.FullName}", $"ID {applicant.IdNumber}" + (applicant.WaitingListNumber is null ? string.Empty : $" · waiting list {applicant.WaitingListNumber}"), string.Empty,
            offer ? $"OFFER OF STAND {stand.StandNumber} - certificate {number}" : $"ALLOCATION OF STAND {stand.StandNumber} - certificate {number}", new string('=', 60),
            $"Scheme : {scheme}", $"Stand  : {stand.StandNumber}, {stand.SizeSquareMetres.ToString("0.###", CultureInfo.InvariantCulture)} m²", $"Price  : {M(price)}",
            $"Deposit: {M(deposit)} ({depositPercent.ToString("0.##", CultureInfo.InvariantCulture)}%)", string.Empty,
        ];
        text.AddRange(offer
            ?
            [
                "We are pleased to offer you the stand above from the council's housing waiting list.",
                line.OfferExpires is DateOnly expires
                    ? $"To accept, sign below and return this letter, with the deposit, by {D(expires)}. If we have not heard from you by then the offer lapses and the stand is offered to the next applicant."
                    : "To accept, sign below and return this letter with the deposit.",
                string.Empty, "Accepted: ______________________   Date: ____________",
            ]
            :
            [
                $"Your acceptance has been recorded{(line.DecidedOn is DateOnly decided ? $" on {D(decided)}" : string.Empty)} and the stand is allocated to you.",
                "The balance of the price is payable under the instalment agreement you will be asked to sign; the stand may not be sold, ceded or",
                "built on until the agreement is signed and the deposit is paid.",
                string.Empty, "For the council: ______________________",
            ]);
        return string.Join(Environment.NewLine, text);
    }

    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string M(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
