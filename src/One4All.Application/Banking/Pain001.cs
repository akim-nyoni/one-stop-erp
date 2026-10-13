using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace One4All.Application.Banking;

/// <summary>The paying or paid party of a credit transfer.</summary>
/// <param name="Name">The account holder.</param>
/// <param name="Account">The account: an IBAN, or the bank's own account number.</param>
/// <param name="Bic">The bank's BIC, when known.</param>
public sealed record Pain001Party(string Name, string Account, string? Bic = null);

/// <summary>One credit transfer.</summary>
/// <param name="EndToEndId">The payer's reference, carried to the beneficiary.</param>
/// <param name="Creditor">The beneficiary.</param>
/// <param name="Currency">The currency.</param>
/// <param name="Amount">The amount.</param>
/// <param name="Remittance">The remittance information (what is being paid).</param>
public sealed record Pain001Payment(string EndToEndId, Pain001Party Creditor, string Currency, decimal Amount, string? Remittance = null);

/// <summary>
/// Writes ISO 20022 customer credit transfer initiations (pain.001.001.03, brief §4.6 bank payment files): one payment information block for
/// the debtor's account and execution date, one credit transfer per payment, with the number of transactions and control sum in the group
/// header. An account in IBAN form is written as an IBAN, any other as the bank's own identification.
/// </summary>
public static partial class Pain001
{
    /// <summary>The reserved bank file layout code that asks a payment run for a pain.001 file.</summary>
    public const string LayoutCode = "PAIN001";

    /// <summary>The message namespace.</summary>
    public const string Namespace = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";

    /// <summary>Writes the message.</summary>
    /// <param name="messageId">The message identification (unique per file, at most 35 characters).</param>
    /// <param name="createdAt">When it was created.</param>
    /// <param name="executionDate">The requested execution date.</param>
    /// <param name="debtor">The paying company and account.</param>
    /// <param name="payments">The credit transfers.</param>
    /// <returns>The XML (UTF-8).</returns>
    public static string Write(string messageId, DateTimeOffset createdAt, DateOnly executionDate, Pain001Party debtor, IReadOnlyList<Pain001Payment> payments)
    {
        ArgumentNullException.ThrowIfNull(debtor);
        ArgumentNullException.ThrowIfNull(payments);
        XNamespace ns = Namespace;
        string count = payments.Count.ToString(CultureInfo.InvariantCulture);
        string sum = Amount(payments.Sum(p => p.Amount));

        XElement paymentInfo = new(ns + "PmtInf",
            new XElement(ns + "PmtInfId", Text(messageId, 35)),
            new XElement(ns + "PmtMtd", "TRF"),
            new XElement(ns + "NbOfTxs", count),
            new XElement(ns + "CtrlSum", sum),
            new XElement(ns + "ReqdExctnDt", executionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(ns + "Dbtr", new XElement(ns + "Nm", Text(debtor.Name, 70))),
            Account(ns, "DbtrAcct", debtor.Account),
            Agent(ns, "DbtrAgt", debtor.Bic, required: true),
            new XElement(ns + "ChrgBr", "SLEV"));
        foreach (Pain001Payment payment in payments)
        {
            paymentInfo.Add(new XElement(ns + "CdtTrfTxInf",
                new XElement(ns + "PmtId", new XElement(ns + "EndToEndId", Text(payment.EndToEndId, 35))),
                new XElement(ns + "Amt", new XElement(ns + "InstdAmt", new XAttribute("Ccy", payment.Currency.ToUpperInvariant()), Amount(payment.Amount))),
                Agent(ns, "CdtrAgt", payment.Creditor.Bic, required: false),
                new XElement(ns + "Cdtr", new XElement(ns + "Nm", Text(payment.Creditor.Name, 70))),
                Account(ns, "CdtrAcct", payment.Creditor.Account),
                string.IsNullOrWhiteSpace(payment.Remittance) ? null : new XElement(ns + "RmtInf", new XElement(ns + "Ustrd", Text(payment.Remittance, 140)))));
        }

        XDocument document = new(new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "Document",
                new XElement(ns + "CstmrCdtTrfInitn",
                    new XElement(ns + "GrpHdr",
                        new XElement(ns + "MsgId", Text(messageId, 35)),
                        new XElement(ns + "CreDtTm", createdAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                        new XElement(ns + "NbOfTxs", count),
                        new XElement(ns + "CtrlSum", sum),
                        new XElement(ns + "InitgPty", new XElement(ns + "Nm", Text(debtor.Name, 70)))),
                    paymentInfo)));

        StringBuilder sb = new();
        using (XmlWriter writer = XmlWriter.Create(new Utf8StringWriter(sb), new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
        {
            document.Save(writer);
        }

        return sb.ToString();
    }

    /// <summary>Whether an account number is in IBAN form (two letters, two check digits, up to 30 letters and digits).</summary>
    /// <param name="account">The account number (spaces ignored).</param>
    /// <returns>True for an IBAN.</returns>
    public static bool IsIban(string account) => IbanPattern().IsMatch(account.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant());

    private static XElement Account(XNamespace ns, string name, string account)
    {
        string clean = account.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        return new XElement(ns + name, new XElement(ns + "Id", IsIban(clean)
            ? new XElement(ns + "IBAN", clean)
            : new XElement(ns + "Othr", new XElement(ns + "Id", Text(clean, 34)))));
    }

    // The debtor's agent is mandatory (unknown BIC: NOTPROVIDED); a creditor agent is left out when its BIC is not known.
    private static XElement? Agent(XNamespace ns, string name, string? bic, bool required)
        => !string.IsNullOrWhiteSpace(bic)
            ? new XElement(ns + name, new XElement(ns + "FinInstnId", new XElement(ns + "BIC", bic.Trim().ToUpperInvariant())))
            : required ? new XElement(ns + name, new XElement(ns + "FinInstnId", new XElement(ns + "Othr", new XElement(ns + "Id", "NOTPROVIDED")))) : null;

    private static string Amount(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Text(string value, int max)
    {
        string trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$")]
    private static partial Regex IbanPattern();

    private sealed class Utf8StringWriter(StringBuilder sb) : StringWriter(sb, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
