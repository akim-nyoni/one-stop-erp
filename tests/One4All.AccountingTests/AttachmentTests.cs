using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.System;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers attachments: a file goes on an existing record only, the same file twice is refused, it opens checksum-verified and an altered
/// file is refused; removal needs a reason and keeps the file; the support behind a posted journal cannot be removed.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AttachmentTests
{
    private readonly AccountingFixture _fixture;

    public AttachmentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Files_attach_to_records_verified_and_are_never_lost()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Suppliers.Add(new Supplier(companyId, "AT-SUP", "Paper Mill", Scenario.Usd));
        await db.SaveChangesAsync();
        PostingReceipt journal = (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 1), TransactionCurrency = Scenario.Usd, DocumentType = "Manual", DocumentNumber = "AT-J1",
            Narrative = "Accrual", Lines =
            [
                new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 50m },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 50m },
            ],
        })).Value;

        IAttachmentService attachments = scope.ServiceProvider.GetRequiredService<IAttachmentService>();
        byte[] contract = Encoding.ASCII.GetBytes("%PDF-1.4 supplier contract");
        (await attachments.AttachAsync(companyId, AttachmentTarget.Supplier, "NOPE", "contract.pdf", contract, 7, null)).Error.Code.Should().Be("Att.TargetNotFound");
        (await attachments.AttachAsync(companyId, AttachmentTarget.Supplier, "at-sup", "contract.exe", contract, 7, null)).Error.Code.Should().Be("Att.File");
        long onSupplier = (await attachments.AttachAsync(companyId, AttachmentTarget.Supplier, "at-sup", @"C:\scans\contract.pdf", contract, 7, "Signed contract")).Value;
        (await attachments.AttachAsync(companyId, AttachmentTarget.Supplier, "AT-SUP", "copy.pdf", contract, 7, null)).Error.Code.Should().Be("Att.Duplicate");
        (await attachments.ListAsync(companyId, AttachmentTarget.Supplier, "AT-SUP")).Single().Should().Match<AttachmentInfo>(a => a.FileName == "contract.pdf" && a.SizeBytes == contract.Length);

        AttachmentFile opened = (await attachments.OpenAsync(companyId, onSupplier)).Value;
        opened.Bytes.Should().Equal(contract);

        // Altered behind One4All's back: refused on opening.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE sys.attachment_content SET bytes = {Encoding.ASCII.GetBytes("%PDF-1.4 forged")} WHERE attachment_id = {onSupplier}");
        (await attachments.OpenAsync(companyId, onSupplier)).Error.Code.Should().Be("Att.Tampered");

        // Removal keeps the file, with its reason.
        (await attachments.RemoveAsync(companyId, onSupplier, 8, " ")).Error.Code.Should().Be("Att.Reason");
        (await attachments.RemoveAsync(companyId, onSupplier, 8, "Superseded by the 2027 contract")).IsSuccess.Should().BeTrue();
        (await attachments.ListAsync(companyId, AttachmentTarget.Supplier, "AT-SUP")).Should().BeEmpty();
        (await db.Set<Attachment>().AsNoTracking().SingleAsync(a => a.Id == onSupplier)).RemovalReason.Should().Be("Superseded by the 2027 contract");

        // The support behind a posted journal stays.
        long onJournal = (await attachments.AttachAsync(companyId, AttachmentTarget.Journal, journal.JournalNumber, "accrual.png", [0x89, (byte)'P', (byte)'N', (byte)'G', 0, 1], 7, null)).Value;
        (await attachments.RemoveAsync(companyId, onJournal, 8, "Wrong file")).Error.Code.Should().Be("Att.Posted");
    }
}
