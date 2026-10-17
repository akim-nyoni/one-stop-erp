using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Workflow;

/// <summary>
/// Puts a document under the company's approval workflow for its type when one is defined (brief §7), and tells a module whether a
/// document is governed by a workflow still in progress - then it is approved or rejected only through the workflow.
/// </summary>
internal static class WorkflowGate
{
    /// <summary>Starts the workflow for a document when the company has an active definition for its type.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="workflow">The workflow service (null: none in this host).</param>
    /// <param name="companyId">The company.</param>
    /// <param name="documentType">The document type.</param>
    /// <param name="reference">The document reference.</param>
    /// <param name="value">The value the workflow's thresholds see.</param>
    /// <param name="initiatedByUserId">Who raised it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when a workflow now governs it.</returns>
    public static Task<Result<bool>> StartIfDefinedAsync(One4AllDbContext db, Lazy<IWorkflowService>? workflow, long companyId, string documentType, string reference, decimal value,
        long initiatedByUserId, CancellationToken ct)
        => StartIfDefinedAsync(db, workflow, companyId, documentType, reference, value, initiatedByUserId, [], ct);

    /// <summary>
    /// Starts the company's workflow for a document with the attributes its stage rules can test (brief §4.3 / §7: "rules by value band, currency, department,
    /// cost centre, item category, project, requester") - the requester's user name is always added.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="workflow">The workflow service (null: none in this host).</param>
    /// <param name="companyId">The company.</param>
    /// <param name="documentType">The document type.</param>
    /// <param name="reference">The document reference.</param>
    /// <param name="value">The value the workflow's thresholds see.</param>
    /// <param name="initiatedByUserId">Who raised it.</param>
    /// <param name="attributes">The document's attributes (blank ones are left out).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when a workflow now governs it.</returns>
    public static async Task<Result<bool>> StartIfDefinedAsync(One4AllDbContext db, Lazy<IWorkflowService>? workflow, long companyId, string documentType, string reference, decimal value,
        long initiatedByUserId, IReadOnlyList<(string Name, string? Value)> attributes, CancellationToken ct)
    {
        if (workflow is null || !await db.WorkflowDefinitions.AnyAsync(d => d.CompanyId == companyId && d.DocumentType == documentType && d.IsActive, ct).ConfigureAwait(false))
        {
            return false;
        }

        Result<WorkflowStartResult> started = await workflow.Value.StartAsync(companyId, documentType, reference, value, initiatedByUserId,
            await AttributesAsync(db, initiatedByUserId, attributes, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return started.IsFailure ? started.Error : true;
    }

    /// <summary>A document's workflow attributes: the ones given that have a value, and the requester's user name.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="initiatedByUserId">Who raised the document.</param>
    /// <param name="attributes">The attributes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The attributes by name.</returns>
    public static async Task<IReadOnlyDictionary<string, string>> AttributesAsync(One4AllDbContext db, long initiatedByUserId, IReadOnlyList<(string Name, string? Value)> attributes,
        CancellationToken ct)
    {
        Dictionary<string, string> found = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string? value) in attributes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                found[name] = value.Trim();
            }
        }

        if (initiatedByUserId > 0 && !found.ContainsKey("Requester")
            && await db.Users.Where(u => u.Id == initiatedByUserId).Select(u => u.Username).FirstOrDefaultAsync(ct).ConfigureAwait(false) is string requester)
        {
            found["Requester"] = requester;
        }

        return found;
    }

    /// <summary>Whether a workflow in progress governs the document.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="documentType">The document type.</param>
    /// <param name="reference">The document reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True while governed.</returns>
    public static Task<bool> InProgressAsync(One4AllDbContext db, long companyId, string documentType, string reference, CancellationToken ct)
        => db.WorkflowInstances.AnyAsync(i => i.CompanyId == companyId && i.DocumentType == documentType && i.DocumentReference == reference && i.Status == Core.Workflow.WorkflowStatus.InProgress, ct);

    /// <summary>The refusal for acting on a governed document directly.</summary>
    /// <param name="documentType">The document type.</param>
    /// <param name="reference">The document reference.</param>
    /// <returns>The error.</returns>
    public static Error Governed(string documentType, string reference)
        => Error.Rule("Wf.Governed", $"{documentType} {reference} is in its approval workflow: approve or reject it from the approvals inbox.");
}
