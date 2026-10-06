using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Integration;

namespace One4All.Infrastructure.Persistence.Configurations;

/// <summary>Maps webhook subscriptions (<c>sys.webhook_subscription</c>).</summary>
internal sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> b)
    {
        b.ToTable("webhook_subscription", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(40);
        b.Property(x => x.Url).HasMaxLength(500);
        b.Property(x => x.Secret).HasMaxLength(200);
        b.HasIndex(x => x.CompanyId);
    }
}

/// <summary>Maps the webhook delivery outbox (<c>sys.webhook_delivery</c>).</summary>
internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> b)
    {
        b.ToTable("webhook_delivery", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(40);
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.LastError).HasMaxLength(500);
        b.HasIndex(x => new { x.Status, x.NextAttemptOnUtc });
    }
}
