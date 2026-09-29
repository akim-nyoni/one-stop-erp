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
        }

        // The counter survives across scopes (it is persisted).
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            INumberSequenceService sequences = scope.ServiceProvider.GetRequiredService<INumberSequenceService>();
            (await sequences.AllocateAsync(companyId, "AR.Invoice", date)).Value.Should().Be("AR.Invoice-00005");
        }
    }
}
