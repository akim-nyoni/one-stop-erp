using FluentAssertions;
using One4All.Licensing.Core;
using One4All.Licensing.Generator.Vendor;
using Xunit;

namespace One4All.UnitTests.Licensing;

/// <summary>
/// Covers the vendor licence desk: terms, edition templates and per-module dates, renew / upgrade / reissue (from a request code) as
/// replacements in the customer's history, revocation published as a signed list that the validator honours, bulk issue with a bad row,
/// the expiry dashboard, the certificate, and the issue log's hash chain catching an edit.
/// </summary>
public sealed class LicenceDeskTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "o4a-desk-" + Guid.NewGuid().ToString("N"));
    private readonly Ed25519KeyPair _keys = Ed25519KeyPair.Generate();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("perpetual", null)]
    [InlineData("30d", "2026-10-30")]
    [InlineData("6m", "2027-03-31")]
    [InlineData("1y", "2027-09-30")]
    [InlineData("2027-06-30", "2027-06-30")]
    public void Terms_end_on_the_right_day(string spec, string? end)
        => LicenceTerm.EndOf(spec, new DateOnly(2026, 10, 1)).Should().Be(end is null ? null : DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("12w")]
    [InlineData("2026-01-01")]
    [InlineData("soon")]
    public void Bad_terms_are_refused(string spec)
    {
        Action parse = () => LicenceTerm.EndOf(spec, new DateOnly(2026, 10, 1));
        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void A_customer_licence_is_issued_renewed_upgraded_reissued_and_revoked()
    {
        LicenceDesk desk = new(_folder, _keys.PrivateKeyBase64, "desk-k", () => Now);
        desk.SaveCustomer("cust-7", "Gweru <Hardware> & Co", "ops@gweru.test", ["Main", "Depot"]);
        Action unknown = () => desk.Issue(new IssueRequest { CustomerId = "NOPE" });
        unknown.Should().Throw<InvalidOperationException>();

        IssuedLicence first = desk.Issue(new IssueRequest
        {
            CustomerId = "CUST-7", Edition = LicenceEdition.Standard, Term = "1y", ModuleTerms = new Dictionary<string, string> { ["POS"] = "6m", ["GL"] = "5y" },
        });
        first.Modules.Select(m => m.Code).Should().Equal(EditionTemplates.ModulesFor(LicenceEdition.Standard));
        first.Modules.Single(m => m.Code == "POS").ValidTo.Should().Be(new DateOnly(2027, 4, 5));
        first.Modules.Single(m => m.Code == "GL").ValidTo.Should().Be(first.ValidTo, "a module never outlives its licence");
        Validate(first.Token).Status.Should().Be(LicenseStatus.Valid);

        IssuedLicence renewed = desk.Renew(first.LicenceId[..8], "1y");
        (renewed.ValidFrom, renewed.ValidTo, renewed.SupersedesId).Should().Be((new DateOnly(2026, 10, 6), new DateOnly(2028, 10, 5), first.LicenceId));
        Action again = () => desk.Renew(first.LicenceId, "1y");
        again.Should().Throw<InvalidOperationException>().WithMessage("*replaced*");

        IssuedLicence upgraded = desk.Change(renewed.LicenceId, LicenceEdition.Professional, add: ["BUD"], remove: ["POS"]);
        upgraded.Action.Should().Be("Upgrade");
        upgraded.Modules.Select(m => m.Code).Should().Contain("BUD").And.NotContain("POS");
        desk.Change(upgraded.LicenceId, null, remove: ["BUD", "CB"]).Action.Should().Be("Downgrade");

        string fingerprint = MachineFingerprint.FromComponents(["board", "guid", "mac", "disk"]).ToString();
        string request = new LicenceRequestCode(fingerprint, "CUST-7", "Server-2", Now).Encode();
        LicenceRequestCode.TryParse(request)!.Site.Should().Be("Server-2");
        LicenceRequestCode.TryParse("O4A-REQ-garbage").Should().BeNull();
        IssuedLicence current = desk.History("CUST-7").Single(l => l.IsCurrent);
        IssuedLicence bound = desk.Reissue(current.LicenceId, [request]);
        bound.Fingerprints.Should().Equal(fingerprint);
        Validate(bound.Token, MachineFingerprint.Parse(fingerprint)).Status.Should().Be(LicenseStatus.Valid);
        Validate(bound.Token, MachineFingerprint.FromComponents(["other", "pc", "x", "y"])).Status.Should().Be(LicenseStatus.MachineMismatch);
        desk.History("cust-7").Should().HaveCount(5).And.ContainSingle(l => l.IsCurrent);

        // Revocation: published as a signed list the ERP's validator honours.
        desk.Revoke(bound.LicenceId, "Chargeback");
        Action twice = () => desk.Revoke(bound.LicenceId, "again");
        twice.Should().Throw<InvalidOperationException>();
        (string crl, int sequence) = desk.PublishRevocationList();
        sequence.Should().Be(1);
        (RevocationList? list, string? error) = RevocationListCodec.Verify(crl, Trusted());
        error.Should().BeNull();
        list!.Revoked.Should().Equal(bound.LicenceId);
        Validate(bound.Token, MachineFingerprint.Parse(fingerprint), list.Revoked).Should().Match<LicenseValidationResult>(r => r.Status == LicenseStatus.Revoked && r.IsReadOnly && !r.IsUsable);
        RevocationListCodec.Verify(crl, new Dictionary<string, byte[]> { ["desk-k"] = Convert.FromBase64String(Ed25519KeyPair.Generate().PublicKeyBase64) }).List.Should().BeNull();
        RevocationListCodec.Verify(crl[..^4] + "AAAA", Trusted()).List.Should().BeNull();

        // The register survives reopening; the certificate escapes the name.
        LicenceDesk reopened = new(_folder, _keys.PrivateKeyBase64, "desk-k", () => Now);
        reopened.Licences.Should().HaveCount(5);
        reopened.Certificate(bound.LicenceId).Should().Contain("Gweru &lt;Hardware&gt; &amp; Co").And.Contain("REVOKED").And.Contain(bound.Token);
        reopened.PublishRevocationList().Sequence.Should().Be(2);
    }

    [Fact]
    public void Bulk_issue_the_expiry_dashboard_and_the_log_chain()
    {
        LicenceDesk desk = new(_folder, _keys.PrivateKeyBase64, "desk-k", () => Now);
        IReadOnlyList<BulkIssueRow> rows = desk.BulkIssue("""
            customer_id,customer_name,edition,type,term,modules
            C-1,First School,Education,Subscription,20d,
            C-2,Second Town,Municipal,Rental,2026-12-31,GL MUNI
            C-3,Broken,Platinum,Subscription,1y
            C-4,Forever Shop,Standard,Perpetual,perpetual
            """);
        rows.Select(r => r.Error is null).Should().Equal(true, true, false, true);
        rows[2].Line.Should().Be(4);
        desk.Customers.Should().HaveCount(3);

        IReadOnlyList<ExpiringItem> soon = desk.Expiring(30);
        soon.Should().ContainSingle().Which.Should().Match<ExpiringItem>(e => e.CustomerId == "C-1" && e.What == "Licence" && e.DaysLeft == 19);
        desk.Expiring(90).Select(e => e.CustomerId).Should().Equal("C-1", "C-2");

        (int entries, int? broken) = desk.VerifyLog();
        (entries, broken).Should().Be((6, (int?)null));
        string log = Path.Combine(_folder, "issue-log.jsonl");
        File.WriteAllText(log, File.ReadAllText(log).Replace("Second Town", "Third Town", StringComparison.Ordinal));
        desk.VerifyLog().BrokenAt.Should().Be(3);
    }

    private Dictionary<string, byte[]> Trusted() => new() { ["desk-k"] = Convert.FromBase64String(_keys.PublicKeyBase64) };

    private LicenseValidationResult Validate(string token, MachineFingerprint? fingerprint = null, IReadOnlyCollection<string>? revoked = null)
        => new LicenseValidator(new LicenseVerifier(Trusted()), new FixedClock(Now), "One4All ERP").Validate(token, fingerprint, revoked);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
