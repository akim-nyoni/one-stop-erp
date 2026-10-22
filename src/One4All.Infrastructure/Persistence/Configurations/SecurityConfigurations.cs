using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence.Converters;

namespace One4All.Infrastructure.Persistence.Configurations;

/// <summary>Shared value comparer for delimited string lists (change tracking of converted collections).</summary>
internal static class Comparers
{
    public static readonly ValueComparer<IReadOnlyList<string>> StringList = new(
        (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
        v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode(StringComparison.Ordinal))),
        v => v.ToList());

    public static readonly ValueComparer<List<long>> LongList = new(
        (a, b) => (a ?? new List<long>()).SequenceEqual(b ?? new List<long>()),
        v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x)),
        v => v.ToList());
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("permission", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(120);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Module).HasMaxLength(60);
        b.Property(x => x.Feature).HasMaxLength(80);
        b.Property(x => x.Name).HasMaxLength(160);
        b.Property(x => x.LicenceModule).HasMaxLength(40);
        b.Property(x => x.AvailableOperations).HasColumnType("bigint");
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("role", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.NormalizedName).HasMaxLength(120);
        b.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("is_deleted = false");
        b.Property(x => x.Description).HasMaxLength(512);

        b.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("role_permission", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.PermissionCode).HasMaxLength(120);
        b.Property(x => x.Operations).HasColumnType("bigint");
        b.HasIndex(x => new { x.RoleId, x.PermissionCode }).IsUnique();
    }
}

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> b)
    {
        b.ToTable("group", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.NormalizedName).HasMaxLength(120);
        b.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("is_deleted = false");
        b.Property(x => x.Description).HasMaxLength(512);

        b.HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class GroupRoleConfiguration : IEntityTypeConfiguration<GroupRole>
{
    public void Configure(EntityTypeBuilder<GroupRole> b)
    {
        b.ToTable("group_role", Schemas.Security);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.GroupId, x.RoleId }).IsUnique();
    }
}

internal sealed class ApiClientConfiguration : IEntityTypeConfiguration<ApiClient>
{
    public void Configure(EntityTypeBuilder<ApiClient> b)
    {
        b.ToTable("api_client", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.ClientId).HasMaxLength(40);
        b.Property(x => x.SecretHash).HasMaxLength(64);
        b.HasIndex(x => x.ClientId).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ActsAsUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("user", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.Username).HasMaxLength(100);
        b.Property(x => x.NormalizedUsername).HasMaxLength(100);
        b.HasIndex(x => x.NormalizedUsername).IsUnique().HasFilter("is_deleted = false");
        b.Property(x => x.DisplayName).HasMaxLength(200);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.PasswordHash).HasMaxLength(512);
        b.Property(x => x.PinHash).HasMaxLength(512);
        b.Property(x => x.CardHash).HasMaxLength(64);
        b.HasIndex(x => x.CardHash).IsUnique().HasFilter("card_hash IS NOT NULL");
        b.Property(x => x.BiometricId).HasMaxLength(100);
        b.HasIndex(x => x.BiometricId).IsUnique().HasFilter("biometric_id IS NOT NULL");
        b.Property(x => x.TwoFactorSecret).HasMaxLength(64);
        b.Property(x => x.SecurityStamp).HasMaxLength(64);

        b.HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Groups).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Overrides).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Groups).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Overrides).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("user_role", Schemas.Security);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();

        b.HasMany(x => x.Scopes).WithOne().HasForeignKey(x => x.UserRoleId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Scopes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class AccessScopeConfiguration : IEntityTypeConfiguration<AccessScope>
{
    public void Configure(EntityTypeBuilder<AccessScope> b)
    {
        b.ToTable("access_scope", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.Dimension).HasConversion<int>();
        b.Property(x => x.Values)
            .HasConversion(new StringListConverter())
            .Metadata.SetValueComparer(Comparers.StringList);
        b.Property(x => x.Values).HasColumnName("values").HasColumnType("text");
    }
}

internal sealed class UserGroupMemberConfiguration : IEntityTypeConfiguration<UserGroupMember>
{
    public void Configure(EntityTypeBuilder<UserGroupMember> b)
    {
        b.ToTable("user_group", Schemas.Security);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.UserId, x.GroupId }).IsUnique();
    }
}

internal sealed class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverrideEntity>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverrideEntity> b)
    {
        b.ToTable("user_permission_override", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.PermissionCode).HasMaxLength(120);
        b.Property(x => x.Operations).HasColumnType("bigint");
        b.HasIndex(x => new { x.UserId, x.PermissionCode, x.IsGrant }).IsUnique();
    }
}

internal sealed class AppLogEntryConfiguration : IEntityTypeConfiguration<Core.System.AppLogEntry>
{
    public void Configure(EntityTypeBuilder<Core.System.AppLogEntry> b)
    {
        b.ToTable("app_log", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Level).HasMaxLength(16);
        b.Property(x => x.Source).HasMaxLength(200);
        b.Property(x => x.Message).HasColumnType("text");
        b.Property(x => x.Exception).HasColumnType("text");
        b.Property(x => x.Properties).HasColumnType("text");
        b.Property(x => x.Host).HasMaxLength(40);
        b.Property(x => x.Machine).HasMaxLength(64);
        b.HasIndex(x => x.LoggedOnUtc);
    }
}

internal sealed class LoginEventConfiguration : IEntityTypeConfiguration<LoginEvent>
{
    public void Configure(EntityTypeBuilder<LoginEvent> b)
    {
        b.ToTable("login_event", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.Username).HasMaxLength(100);
        b.Property(x => x.Terminal).HasMaxLength(100);
        b.Property(x => x.Method).HasMaxLength(20);
        b.Property(x => x.Outcome).HasMaxLength(40);
        b.Property(x => x.SessionId).HasMaxLength(40);
        b.HasIndex(x => x.SessionId);
        b.HasIndex(x => new { x.UserId, x.OccurredOnUtc });
    }
}

internal sealed class ScreenUseConfiguration : IEntityTypeConfiguration<ScreenUse>
{
    public void Configure(EntityTypeBuilder<ScreenUse> b)
    {
        b.ToTable("screen_use", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.Module).HasMaxLength(80);
        b.Property(x => x.Screen).HasMaxLength(200);
        b.Property(x => x.Action).HasMaxLength(10);
        b.HasIndex(x => new { x.UserId, x.OccurredOnUtc });
    }
}

internal sealed class PasswordHistoryConfiguration : IEntityTypeConfiguration<PasswordHistory>
{
    public void Configure(EntityTypeBuilder<PasswordHistory> b)
    {
        b.ToTable("password_history", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.PasswordHash).HasMaxLength(512);
        b.HasIndex(x => new { x.UserId, x.SetOnUtc });
    }
}

internal sealed class RoleGrantRequestConfiguration : IEntityTypeConfiguration<RoleGrantRequest>
{
    public void Configure(EntityTypeBuilder<RoleGrantRequest> b)
    {
        b.ToTable("role_grant_request", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.Scopes).HasMaxLength(2000);
        b.Property(x => x.Conflicts).HasMaxLength(2000);
        b.Property(x => x.DecisionNote).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.Status, x.UserId });
    }
}

internal sealed class SodRuleSettingConfiguration : IEntityTypeConfiguration<SodRuleSetting>
{
    public void Configure(EntityTypeBuilder<SodRuleSetting> b)
    {
        b.ToTable("sod_rule", Schemas.Security);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.PermissionA).HasMaxLength(120);
        b.Property(x => x.PermissionB).HasMaxLength(120);
        b.Property(x => x.OperationA).HasColumnType("bigint");
        b.Property(x => x.OperationB).HasColumnType("bigint");
        b.Property(x => x.Severity).HasConversion<int>();
        b.HasIndex(x => x.Code).IsUnique();
    }
}
