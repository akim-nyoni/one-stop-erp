using System.Security.Cryptography;
using Dapper;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Core.Audit;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Security;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers payroll encryption at rest: the cipher (random nonces, tamper and unknown-key refusal, plaintext from before passing through, key
/// rotation), salary / bank / national ID stored as ciphertext and read back in clear, the audit log never holding them in clear, and the
/// maintenance service finding and encrypting rows written before encryption.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollEncryptionTests
{
    private readonly AccountingFixture _fixture;

    public PayrollEncryptionTests(AccountingFixture fixture) => _fixture = fixture;

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void The_cipher_protects_rotates_and_refuses_tampering()
    {
        string k1 = NewKey(), k2 = NewKey();
        FieldCipher first = new(new FieldEncryptionOptions { Keys = { ["k1"] = k1 }, ActiveKeyId = "k1" });
        string stored = first.Protect("62-123456-X-42")!;
        stored.Should().StartWith("enc:v1:k1:").And.NotContain("123456");
        first.Protect("62-123456-X-42").Should().NotBe(stored, "each value gets its own nonce");
        first.Unprotect(stored).Should().Be("62-123456-X-42");
        first.Unprotect("legacy plaintext").Should().Be("legacy plaintext");
        first.Protect(null).Should().BeNull();
        first.UnprotectDecimal(first.ProtectDecimal(4_250.75m)).Should().Be(4_250.75m);
        FieldCipher.KeyIdOf(stored).Should().Be("k1");

        char[] chars = stored.ToCharArray();
        chars[^3] = chars[^3] == 'A' ? 'B' : 'A';
        Action tampered = () => first.Unprotect(new string(chars));
        tampered.Should().Throw<CryptographicException>();

        // Rotation: the new key writes, the old one still reads.
        FieldCipher rotated = new(new FieldEncryptionOptions { Keys = { ["k1"] = k1, ["k2"] = k2 }, ActiveKeyId = "k2" });
        rotated.Unprotect(stored).Should().Be("62-123456-X-42");
        FieldCipher.KeyIdOf(rotated.Protect("x")).Should().Be("k2");
        Action unknown = () => new FieldCipher(new FieldEncryptionOptions { Keys = { ["k2"] = k2 }, ActiveKeyId = "k2" }).Unprotect(stored);
        unknown.Should().Throw<CryptographicException>().WithMessage("*k1*");
        Action shortKey = () => new FieldCipher(new FieldEncryptionOptions { Keys = { ["s"] = Convert.ToBase64String(new byte[16]) }, ActiveKeyId = "s" });
        shortKey.Should().Throw<ArgumentException>();
    }

    [SkippableFact]
    public async Task Payroll_fields_are_ciphertext_in_the_database_and_never_in_the_audit_log()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Employee employee = new(companyId, "ENC-1", "Rudo Chari", 3_456.78m, new DateOnly(2025, 1, 1))
        {
            NationalIdNumber = "63-998877-K-63", TaxNumber = "TIN-55", BankName = "CBZ", BankBranch = "Harare 1", BankAccountNumber = "01234567890", MobileMoneyNumber = "0771234567",
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        System.Data.Common.DbConnection connection = db.Database.GetDbConnection();
        var raw = await connection.QuerySingleAsync<(string Salary, string Id, string Account, string Bank)>(
            "SELECT basic_salary, national_id_number, bank_account_number, bank_name FROM hr.employee WHERE id = @id", new { id = employee.Id });
        raw.Salary.Should().StartWith("enc:v1:").And.NotContain("3456");
        raw.Id.Should().StartWith("enc:v1:").And.NotContain("998877");
        raw.Account.Should().StartWith("enc:v1:").And.NotContain("01234567890");
        raw.Bank.Should().Be("CBZ", "the bank's name is not secret");

        Employee read = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id);
        (read.BasicSalary, read.NationalIdNumber, read.BankAccountNumber, read.MobileMoneyNumber).Should().Be((3_456.78m, "63-998877-K-63", "01234567890", "0771234567"));

        Employee changed = await db.Employees.SingleAsync(e => e.Id == employee.Id);
        changed.BasicSalary = 3_900m;
        await db.SaveChangesAsync();
        List<ChangeLogEntry> audit = await db.ChangeLog.AsNoTracking().Where(c => c.EntityType == nameof(Employee) && c.EntityId == employee.Id.ToString()).ToListAsync();
        audit.Should().NotBeEmpty();
        string trail = string.Join("|", audit.Select(a => a.OldValues + a.NewValues));
        trail.Should().Contain("[encrypted]").And.NotContain("3456").And.NotContain("3900").And.NotContain("998877").And.NotContain("01234567890").And.Contain("Rudo Chari");

        // A row written before encryption (plaintext) still reads, is found by the status, and is encrypted by the maintenance run.
        await connection.ExecuteAsync("UPDATE hr.employee SET bank_account_number = '99887766', basic_salary = '1500.00' WHERE id = @id", new { id = employee.Id });
        db.ChangeTracker.Clear();
        (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id)).Should().Match<Employee>(e => e.BankAccountNumber == "99887766" && e.BasicSalary == 1500m);
        IFieldEncryptionService encryption = scope.ServiceProvider.GetRequiredService<IFieldEncryptionService>();
        FieldEncryptionReport before = await encryption.StatusAsync();
        before.Pending["hr.employee.bank_account_number"].Should().BeGreaterThanOrEqualTo(1);
        before.Complete.Should().BeFalse();
        (await encryption.ReencryptAsync()).Should().BeGreaterThanOrEqualTo(1);
        (await encryption.StatusAsync()).Complete.Should().BeTrue();
        (await connection.QuerySingleAsync<string>("SELECT bank_account_number FROM hr.employee WHERE id = @id", new { id = employee.Id })).Should().StartWith("enc:v1:");
        db.ChangeTracker.Clear();
        (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id)).BankAccountNumber.Should().Be("99887766");
    }
}
