using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Communications;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> b)
    {
        b.ToTable("message_template", Schemas.Communications);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(40);
        b.Property(x => x.DocumentType).HasMaxLength(60);
        b.Property(x => x.Language).HasMaxLength(10);
        b.Property(x => x.Channel).HasConversion<int>();
        b.Property(x => x.Subject).HasMaxLength(300);
        b.Property(x => x.Body).HasMaxLength(20000);
        b.HasIndex(x => new { x.CompanyId, x.Code, x.Language, x.Channel }).IsUnique();
    }
}

internal sealed class DespatchBatchConfiguration : IEntityTypeConfiguration<DespatchBatch>
{
    public void Configure(EntityTypeBuilder<DespatchBatch> b)
    {
        b.ToTable("despatch_batch", Schemas.Communications);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.TemplateCode).HasMaxLength(40);
    }
}

internal sealed class OutboundMessageConfiguration : IEntityTypeConfiguration<OutboundMessage>
{
    public void Configure(EntityTypeBuilder<OutboundMessage> b)
    {
        b.ToTable("outbound_message", Schemas.Communications);
        b.HasKey(x => x.Id);
        b.Property(x => x.Channel).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Recipient).HasMaxLength(200);
        b.Property(x => x.Subject).HasMaxLength(300);
        b.Property(x => x.Body).HasMaxLength(20000);
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.Property(x => x.ProviderReference).HasMaxLength(200);
        b.HasOne<DespatchBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.Status });
        b.HasIndex(x => x.BatchId);
    }
}

internal sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> b)
    {
        b.ToTable("notification", Schemas.Communications);
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(300);
        b.Property(x => x.Body).HasMaxLength(4000);
        b.HasIndex(x => new { x.UserId, x.ReadAt });
    }
}
