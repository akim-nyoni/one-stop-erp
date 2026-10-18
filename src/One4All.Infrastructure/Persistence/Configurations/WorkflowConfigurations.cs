using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence.Converters;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> b)
    {
        b.ToTable("definition", Schemas.Workflow);
        b.HasKey(x => x.Id);
        b.Property(x => x.DocumentType).HasMaxLength(60);
        b.Property(x => x.Name).HasMaxLength(160);
        b.HasMany(x => x.Stages).WithOne().HasForeignKey(x => x.WorkflowDefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Stages).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.DocumentType }).HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class WorkflowStageConfiguration : IEntityTypeConfiguration<WorkflowStage>
{
    public void Configure(EntityTypeBuilder<WorkflowStage> b)
    {
        b.ToTable("stage", Schemas.Workflow);
        b.HasKey(x => x.Id);
        b.Property(x => x.Condition).HasMaxLength(500);
        b.Property(x => x.DynamicApprover).HasConversion<int>();
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.MinValue).HasPrecision(19, 4);
        b.Property(x => x.RequiredRoleName).HasMaxLength(120);
        b.Property(x => x.RequiredGroupName).HasMaxLength(100);
        b.Property<List<long>>("_eligibleUserIds")
            .HasColumnName("eligible_user_ids")
            .HasColumnType("text")
            .HasConversion(new LongListConverter())
            .Metadata.SetValueComparer(Comparers.LongList);
        b.Ignore(x => x.EligibleUserIds);
        b.HasIndex(x => new { x.WorkflowDefinitionId, x.Sequence }).IsUnique();
    }
}

internal sealed class WorkflowActionTokenConfiguration : IEntityTypeConfiguration<WorkflowActionToken>
{
    public void Configure(EntityTypeBuilder<WorkflowActionToken> b)
    {
        b.ToTable("action_token", Schemas.Workflow);
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.WorkflowInstanceId);
    }
}

internal sealed class MasterDataChangeConfiguration : IEntityTypeConfiguration<Core.System.MasterDataChange>
{
    public void Configure(EntityTypeBuilder<Core.System.MasterDataChange> b)
    {
        b.ToTable("master_data_change", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).HasMaxLength(40);
        b.Property(x => x.Field).HasMaxLength(60);
        b.Property(x => x.OldValue).HasMaxLength(500);
        b.Property(x => x.NewValue).HasMaxLength(500);
        b.Property(x => x.Reference).HasMaxLength(30);
        b.HasIndex(x => new { x.CompanyId, x.Reference });
        b.HasIndex(x => new { x.CompanyId, x.Target, x.Key, x.Status });
    }
}

internal sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> b)
    {
        b.ToTable("instance", Schemas.Workflow);
        b.HasKey(x => x.Id);
        b.Property(x => x.AttributesJson).HasColumnType("text");
        b.Property(x => x.DocumentType).HasMaxLength(60);
        b.Property(x => x.DocumentReference).HasMaxLength(60);
        b.Property(x => x.Value).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasMany(x => x.Approvals).WithOne().HasForeignKey(x => x.WorkflowInstanceId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Approvals).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.DocumentType, x.DocumentReference });
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class WorkflowApprovalConfiguration : IEntityTypeConfiguration<WorkflowApproval>
{
    public void Configure(EntityTypeBuilder<WorkflowApproval> b)
    {
        b.ToTable("approval", Schemas.Workflow);
        b.HasKey(x => x.Id);
        b.Property(x => x.Snapshot).HasColumnType("text");
        b.Property(x => x.SnapshotHash).HasMaxLength(64);
        b.Property(x => x.Machine).HasMaxLength(80);
        b.Property(x => x.Action).HasConversion<int>();
        b.Property(x => x.Comment).HasMaxLength(1024);
    }
}

internal sealed class WorkflowInstanceApproverConfiguration : IEntityTypeConfiguration<WorkflowInstanceApprover>
{
    public void Configure(EntityTypeBuilder<WorkflowInstanceApprover> b)
    {
        b.ToTable("instance_approver", Schemas.Workflow);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasConversion<int>();
        b.HasIndex(x => new { x.WorkflowInstanceId, x.StageSequence });
    }
}

internal sealed class WorkflowCommentConfiguration : IEntityTypeConfiguration<WorkflowComment>
{
    public void Configure(EntityTypeBuilder<WorkflowComment> b)
    {
        b.ToTable("comment", Schemas.Workflow);
        b.HasKey(x => x.Id);
        b.Property(x => x.Text).HasMaxLength(2000).IsRequired();
        b.HasIndex(x => new { x.WorkflowInstanceId, x.PostedOnUtc });
    }
}

internal sealed class ApprovalDelegationConfiguration : IEntityTypeConfiguration<ApprovalDelegation>
{
    public void Configure(EntityTypeBuilder<ApprovalDelegation> b)
    {
        b.ToTable("delegation", Schemas.Workflow);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.ToUserId });
    }
}
