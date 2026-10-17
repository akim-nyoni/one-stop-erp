using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Numbering;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the document-number allocator: sequential, per-type, formatted numbering.</summary>
[Collection(AccountingCollection.Name)]
public sealed class NumberSequenceTests
{
    private readonly AccountingFixture _fixture;

    public NumberSequenceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Numbers_are_allocated_sequentially_per_document_type()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        DateOnly date = new(2026, 4, 1);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            INumberSequenceService sequences = scope.ServiceProvider.GetRequiredService<INumberSequenceService>();

            // A default sequence is created on first use, then increments.
            (await sequences.AllocateAsync(companyId, "AR.Invoice", date)).Value.Should().Be("AR.Invoice-00001");
            (await sequences.AllocateAsync(companyId, "AR.Invoice", date)).Value.Should().Be("AR.Invoice-00002");
            (await sequences.AllocateAsync(companyId, "AR.Invoice", date)).Value.Should().Be("AR.Invoice-00003");

            // A different document type has its own independent counter.
            (await sequences.AllocateAsync(companyId, "PO", date)).Value.Should().Be("PO-00001");
            (await sequences.AllocateAsync(companyId, "AR.Invoice", date)).Value.Should().Be("AR.Invoice-00004");

            // Configured: prefix, padding, yearly reset (the counter restarts for the year), gapless, a starting number that only moves forward.
            (await sequences.ConfigureAsync(companyId, "Quote", null, new NumberSequenceSetup("QT-", 6, ResetYearly: true))).Value.Should().Match<NumberSequenceRow>(r => r.Prefix == "QT-" && r.Padding == 6 && r.ResetYearly && r.Gapless);
            (await sequences.AllocateAsync(companyId, "Quote", date)).Value.Should().Be("QT-2026-000001");
            (await sequences.AllocateAsync(companyId, "Quote", new DateOnly(2027, 1, 5))).Value.Should().Be("QT-2027-000001");
            (await sequences.ConfigureAsync(companyId, "PO", null, new NumberSequenceSetup("PO-", 5, StartAt: 100))).IsSuccess.Should().BeTrue();
            (await sequences.AllocateAsync(companyId, "PO", date)).Value.Should().Be("PO-00100");
            (await sequences.ConfigureAsync(companyId, "PO", null, new NumberSequenceSetup("PO-", 5, StartAt: 50))).Error.Code.Should().Be("Sequence.StartBehind");
            (await sequences.ConfigureAsync(companyId, "PO", null, new NumberSequenceSetup("TOO-LONG-A-PREFIX-FOR-SURE", 5))).Error.Code.Should().Be("Sequence.Setup");
            (await sequences.ConfigureAsync(companyId, "PO", 999_999, new NumberSequenceSetup("PO-", 5))).Error.Code.Should().Be("Sequence.BranchNotFound");
            (await sequences.ConfigureAsync(companyId, "GRN", null, new NumberSequenceSetup("GRN/", 4, Gapless: false))).Value.Gapless.Should().BeFalse();
            (await sequences.AllocateAsync(companyId, "GRN", date)).Value.Should().Be("GRN/0001");
            (await sequences.ListAsync(companyId)).Select(s => s.DocumentType).Should().Contain(["AR.Invoice", "Quote", "PO", "GRN"]);
        }

        // The counter survives across scopes (it is persisted).
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            INumberSequenceService sequences = scope.ServiceProvider.GetRequiredService<INumberSequenceService>();
            (await sequences.AllocateAsync(companyId, "AR.Invoice", date)).Value.Should().Be("AR.Invoice-00005");
        }
    }
}
