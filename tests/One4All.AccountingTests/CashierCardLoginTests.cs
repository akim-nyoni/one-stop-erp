using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers signing in at the till by cashier card and by fingerprint enrolment.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CashierCardLoginTests
{
    private readonly AccountingFixture _fixture;

    public CashierCardLoginTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cashiers_sign_in_by_card_or_fingerprint()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        string tag = Guid.NewGuid().ToString("N")[..8];
        User ann = new($"card-ann-{tag}", "Ann Cashier"), ben = new($"card-ben-{tag}", "Ben Cashier");
        db.Users.AddRange(ann, ben);
        await db.SaveChangesAsync();

        IUserAdminService admin = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        string card = $"9{Random.Shared.NextInt64(1_000_000_000_000L, 9_999_999_999_999L)}";
        (await admin.SetCashierCardAsync(ann.Id, $";{card}=2912?")).IsSuccess.Should().BeTrue();
        (await admin.SetCashierCardAsync(ben.Id, card)).Error.Code.Should().Be("Security.CardInUse");
        (await admin.SetBiometricIdAsync(ben.Id, $"FP-{tag}")).IsSuccess.Should().BeTrue();

        IAuthenticationService auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        AuthenticationResult byCard = await auth.AuthenticateByCardAsync($"%B{card}^ANN^2912?", "POS-1");
        (byCard.Succeeded, byCard.UserId).Should().Be((true, ann.Id));
        (await auth.AuthenticateByCardAsync("0000111122223333", "POS-1")).Succeeded.Should().BeFalse();
        AuthenticationResult byFinger = await auth.AuthenticateByBiometricAsync($"FP-{tag}", "POS-1");
        (byFinger.Succeeded, byFinger.UserId).Should().Be((true, ben.Id));
        (await auth.AuthenticateByBiometricAsync("FP-nobody", "POS-1")).Succeeded.Should().BeFalse();

        (await admin.SetCashierCardAsync(ann.Id, null)).IsSuccess.Should().BeTrue();
        (await auth.AuthenticateByCardAsync(card, "POS-1")).Succeeded.Should().BeFalse();
    }
}
