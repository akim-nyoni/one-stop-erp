using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>The master records whose changes can be put through approval.</summary>
public enum MasterDataTarget
{
    /// <summary>A municipal tariff (by code; the latest in force).</summary>
    Tariff = 1,

    /// <summary>A customer (by code).</summary>
    Customer = 2,

    /// <summary>A supplier (by code).</summary>
    Supplier = 3,

    /// <summary>A stock item (by code).</summary>
    Item = 4,
}

/// <summary>Where a requested master-data change stands.</summary>
public enum MasterDataChangeStatus
{
    /// <summary>Waiting for its approval workflow.</summary>
    Pending = 0,

    /// <summary>Applied to the record.</summary>
    Applied = 1,

    /// <summary>Rejected; the record was left as it was.</summary>
    Rejected = 2,
}

/// <summary>
/// A requested change to one field of a master record (<c>sys.master_data_change</c>, brief §7: "approval of tariff and master-data changes"): held
/// for the company's "MasterDataChange" workflow when one is defined, and applied only once approved; with the value before and after.
/// </summary>
public sealed class MasterDataChange : Entity, ICompanyScoped
{
    private MasterDataChange()
    {
        Key = string.Empty;
        Field = string.Empty;
        Reference = string.Empty;
    }

    /// <summary>Records the request.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="target">The kind of record.</param>
    /// <param name="key">The record's code.</param>
    /// <param name="field">The field.</param>
    /// <param name="oldValue">The value now.</param>
    /// <param name="newValue">The value asked for.</param>
    /// <param name="requestedByUserId">Who asked.</param>
    /// <param name="requestedOnUtc">When.</param>
    public MasterDataChange(long companyId, MasterDataTarget target, string key, string field, string? oldValue, string? newValue, long requestedByUserId, DateTimeOffset requestedOnUtc)
    {
        CompanyId = companyId;
        Target = target;
        Key = key;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        RequestedByUserId = requestedByUserId;
        RequestedOnUtc = requestedOnUtc;
        Reference = string.Empty;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the kind of record.</summary>
    public MasterDataTarget Target { get; private set; }

    /// <summary>Gets the record's code.</summary>
    public string Key { get; private set; }

    /// <summary>Gets the field.</summary>
    public string Field { get; private set; }

    /// <summary>Gets the value before (as it stood when requested, then when applied).</summary>
    public string? OldValue { get; private set; }

    /// <summary>Gets the value asked for.</summary>
    public string? NewValue { get; private set; }

    /// <summary>Gets who asked.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Gets when.</summary>
    public DateTimeOffset RequestedOnUtc { get; private set; }

    /// <summary>Gets or sets the workflow reference (<c>MDC-{id}</c>).</summary>
    public string Reference { get; set; }

    /// <summary>Gets where it stands.</summary>
    public MasterDataChangeStatus Status { get; private set; }

    /// <summary>Gets who decided (or applied) it.</summary>
    public long? DecidedByUserId { get; private set; }

    /// <summary>Gets when it was decided.</summary>
    public DateTimeOffset? DecidedOnUtc { get; private set; }

    /// <summary>Marks the change applied.</summary>
    /// <param name="oldValue">The value it replaced.</param>
    /// <param name="userId">Who approved it.</param>
    /// <param name="when">When.</param>
    public void Applied(string? oldValue, long userId, DateTimeOffset when)
    {
        OldValue = oldValue;
        Status = MasterDataChangeStatus.Applied;
        DecidedByUserId = userId;
        DecidedOnUtc = when;
    }

    /// <summary>Marks the change rejected.</summary>
    /// <param name="userId">Who rejected it.</param>
    /// <param name="when">When.</param>
    public void Rejected(long userId, DateTimeOffset when)
    {
        Status = MasterDataChangeStatus.Rejected;
        DecidedByUserId = userId;
        DecidedOnUtc = when;
    }
}
