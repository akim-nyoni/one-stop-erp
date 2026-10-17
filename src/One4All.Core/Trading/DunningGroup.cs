using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>
/// A dunning group (<c>ar.dunning_group</c>, brief §4.4): customers chased on their own timetable - government departments given longer,
/// cash customers chased sooner - or not chased at all.
/// </summary>
public sealed class DunningGroup : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DunningGroup()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates a group.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    public DunningGroup(long companyId, string code, string name)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the days overdue before the first reminder.</summary>
    public int FirstReminderDays { get; set; }

    /// <summary>Gets or sets the days overdue before the second reminder.</summary>
    public int SecondReminderDays { get; set; }

    /// <summary>Gets or sets the days overdue before the final notice.</summary>
    public int FinalNoticeDays { get; set; }

    /// <summary>Gets or sets whether the group is never chased.</summary>
    public bool IsExempt { get; set; }
}

/// <summary>A reminder sent to a customer (<c>ar.dunning_log</c>): it is not sent again at the same level until the resend interval passes.</summary>
public sealed class DunningLog : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DunningLog()
    {
    }

    /// <summary>Records a reminder sent.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="level">The reminder level.</param>
    /// <param name="sentOn">When it was sent.</param>
    /// <param name="overdue">The overdue amount it chased.</param>
    public DunningLog(long companyId, long customerId, int level, DateOnly sentOn, decimal overdue)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        Level = level;
        SentOn = sentOn;
        Overdue = overdue;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the customer.</summary>
    public long CustomerId { get; private set; }

    /// <summary>Gets the reminder level.</summary>
    public int Level { get; private set; }

    /// <summary>Gets when it was sent.</summary>
    public DateOnly SentOn { get; private set; }

    /// <summary>Gets the overdue amount chased.</summary>
    public decimal Overdue { get; private set; }
}
