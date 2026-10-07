using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.System;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Core.System.Attachment>
{
    public void Configure(EntityTypeBuilder<Core.System.Attachment> b)
    {
        b.ToTable("attachment", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Target).HasConversion<int>();
        b.Property(x => x.TargetKey).HasMaxLength(60);
        b.Property(x => x.FileName).HasMaxLength(255);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.Sha256).HasMaxLength(64);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.RemovalReason).HasMaxLength(300);
        b.HasIndex(x => new { x.CompanyId, x.Target, x.TargetKey });
    }
}

internal sealed class AttachmentContentConfiguration : IEntityTypeConfiguration<Core.System.AttachmentContent>
{
    public void Configure(EntityTypeBuilder<Core.System.AttachmentContent> b)
    {
        b.ToTable("attachment_content", Schemas.System);
        b.HasKey(x => x.AttachmentId);
        b.Property(x => x.AttachmentId).ValueGeneratedNever();
        b.HasOne<Core.System.Attachment>().WithOne().HasForeignKey<Core.System.AttachmentContent>(x => x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
