using One4All.Core.Common;

namespace One4All.Core.Reporting;

/// <summary>
/// A saved version of a report definition (<c>rpt.report_definition_version</c>, brief §13.2 version history): every
/// version a company or user report has had, never overwritten, so any two can be diffed and any one rolled back to.
/// </summary>
public sealed class ReportDefinitionVersion : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReportDefinitionVersion()
    {
        DefinitionJson = string.Empty;
        Note = string.Empty;
    }

    /// <summary>Records a version.</summary>
    /// <param name="reportDefinitionId">The report.</param>
    /// <param name="version">The version number.</param>
    /// <param name="definitionJson">The definition as saved.</param>
    /// <param name="savedByUserId">Who saved it.</param>
    /// <param name="savedAt">When.</param>
    /// <param name="note">Why (e.g. "Copied from system", "Rolled back to v2").</param>
    public ReportDefinitionVersion(long reportDefinitionId, int version, string definitionJson, long? savedByUserId, DateTimeOffset savedAt, string note)
    {
        ReportDefinitionId = reportDefinitionId;
        Version = version;
        DefinitionJson = definitionJson;
        SavedByUserId = savedByUserId;
        SavedAt = savedAt;
        Note = note;
    }

    /// <summary>The report.</summary>
    public long ReportDefinitionId { get; private set; }

    /// <summary>The version number.</summary>
    public int Version { get; private set; }

    /// <summary>The definition as saved.</summary>
    public string DefinitionJson { get; private set; }

    /// <summary>Who saved it.</summary>
    public long? SavedByUserId { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset SavedAt { get; private set; }

    /// <summary>Why.</summary>
    public string Note { get; private set; }
}
