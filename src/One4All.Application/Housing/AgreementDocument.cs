using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace One4All.Application.Housing;

/// <summary>What goes into an agreement document.</summary>
/// <param name="AgreementNumber">The agreement number.</param>
/// <param name="Version">The version.</param>
/// <param name="AgreementType">The kind of agreement.</param>
/// <param name="CouncilName">The council.</param>
/// <param name="PartyName">The other party.</param>
/// <param name="StandNumber">The stand, if any.</param>
/// <param name="Price">The price or annual rent.</param>
/// <param name="Terms">The payment terms.</param>
/// <param name="SpecialConditions">Special conditions.</param>
public sealed record AgreementContent(string AgreementNumber, int Version, string AgreementType, string CouncilName, string PartyName, string? StandNumber, decimal Price, string Terms, string? SpecialConditions);

/// <summary>
/// Generates the agreement document stored against the agreement register (brief §4.10.5) and the SHA-256 hash that
/// makes it tamper-evident. The text is fixed by its content, so the same version always renders the same. Pure and
/// unit-tested.
/// </summary>
public static class AgreementDocument
{
    /// <summary>Renders the document text.</summary>
    /// <param name="content">The agreement content.</param>
    /// <returns>The document.</returns>
    public static string Render(AgreementContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"AGREEMENT {content.AgreementNumber} (version {content.Version})").Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"{content.AgreementType.ToUpperInvariant()}").Append('\n').Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"Between {content.CouncilName} (the Council) and {content.PartyName}.").Append('\n');
        if (!string.IsNullOrWhiteSpace(content.StandNumber))
        {
            text.Append(CultureInfo.InvariantCulture, $"Property: stand {content.StandNumber}.").Append('\n');
        }

        text.Append(CultureInfo.InvariantCulture, $"Consideration: {content.Price.ToString("N2", CultureInfo.InvariantCulture)}.").Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"Terms: {content.Terms}").Append('\n');
        if (!string.IsNullOrWhiteSpace(content.SpecialConditions))
        {
            text.Append(CultureInfo.InvariantCulture, $"Special conditions: {content.SpecialConditions}").Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The SHA-256 hash of a document, as lower-case hex.</summary>
    /// <param name="document">The document text.</param>
    /// <returns>The hash.</returns>
    public static string Hash(string document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document)));
    }
}
