using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>The state of a table group (Restaurant module).</summary>
public enum TableGroupStatus
{
    /// <summary>Active — the tables are combined.</summary>
    Active = 1,

    /// <summary>Released — the tables have been separated again.</summary>
    Released = 2,
}

/// <summary>
/// A combination of two or more tables pushed together to seat a large party (<c>res.table_group</c>).
/// One member is the <see cref="PrimaryTableId"/>: the order is opened against it and the whole group is
/// occupied and freed together. Operational only (no GL).
/// </summary>
public sealed class TableGroup : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private TableGroup() => Label = string.Empty;

    /// <summary>Creates an active table group.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="label">The label (e.g. the joined table numbers).</param>
    /// <param name="primaryTableId">The primary table the order is opened against.</param>
    /// <param name="seats">The combined number of seats.</param>
    /// <param name="memberCount">The number of tables combined.</param>
    public TableGroup(long companyId, string label, long primaryTableId, int seats, int memberCount)
    {
        CompanyId = companyId;
        Label = label;
        PrimaryTableId = primaryTableId;
        Seats = seats;
        MemberCount = memberCount;
        Status = TableGroupStatus.Active;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The label (e.g. the joined table numbers).</summary>
    public string Label { get; private set; }

    /// <summary>The primary table the order is opened against.</summary>
    public long PrimaryTableId { get; private set; }

    /// <summary>The combined number of seats.</summary>
    public int Seats { get; private set; }

    /// <summary>The number of tables combined.</summary>
    public int MemberCount { get; private set; }

    /// <summary>The status.</summary>
    public TableGroupStatus Status { get; private set; }

    /// <summary>Releases the group (the tables are separated again).</summary>
    public void Release() => Status = TableGroupStatus.Released;
}
