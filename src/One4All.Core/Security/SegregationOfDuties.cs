namespace One4All.Core.Security;

/// <summary>Severity of a segregation-of-duties conflict.</summary>
public enum SodSeverity
{
    /// <summary>Advisory conflict — warn on assignment, allow with justification.</summary>
    Medium = 1,

    /// <summary>Serious conflict — a classic fraud vector; warn prominently and report to auditors.</summary>
    High = 2,
}

/// <summary>
/// A pair of duties that the same principal should not hold together (brief §5.1). Assigning both
/// sides to one user or role raises a warning at assignment time and appears on the SoD violation
/// report. Each side is a (permission code, operation) requirement.
/// </summary>
/// <param name="Code">Stable rule code.</param>
/// <param name="Name">Human-readable description of the conflict.</param>
/// <param name="PermissionA">First permission code.</param>
/// <param name="OperationA">Operation required on <paramref name="PermissionA"/> to be one side of the conflict.</param>
/// <param name="PermissionB">Second permission code.</param>
/// <param name="OperationB">Operation required on <paramref name="PermissionB"/> to be the other side.</param>
/// <param name="Severity">Conflict severity.</param>
public sealed record SodRule(
    string Code,
    string Name,
    string PermissionA,
    OperationFlags OperationA,
    string PermissionB,
    OperationFlags OperationB,
    SodSeverity Severity)
{
    /// <summary>
    /// Returns true when the supplied effective permissions hold <em>both</em> sides of this rule,
    /// i.e. the principal has a conflict.
    /// </summary>
    /// <param name="permissions">The principal's resolved permissions.</param>
    /// <returns>True when both sides are satisfied.</returns>
    public bool IsViolatedBy(EffectivePermissions permissions) =>
        permissions.Has(PermissionA, OperationA) && permissions.Has(PermissionB, OperationB);
}

/// <summary>
/// The default segregation-of-duties rules pre-loaded from the brief (§4.10.11, §4.15.10, §4.18.7, §5.1).
/// Seeded into <c>sec.sod_rule</c>; customers may add their own.
/// </summary>
public static class SegregationOfDutiesCatalogue
{
    /// <summary>All default SoD rules.</summary>
    public static IReadOnlyList<SodRule> All { get; } = Build();

    private static List<SodRule> Build()
    {
        List<SodRule> rules = [];

        void Rule(string code, string name, string a, OperationFlags opA, string b, OperationFlags opB, SodSeverity sev = SodSeverity.High) =>
            rules.Add(new SodRule(code, name, a, opA, b, opB, sev));

        // Accounts Payable / general
        Rule("SOD-AP-01", "Create supplier and approve payment run",
            "Purchasing.Supplier", OperationFlags.Create, "Purchasing.PaymentRun", OperationFlags.Approve);
        Rule("SOD-AP-02", "Change supplier bank details and approve payment run",
            "Purchasing.SupplierBankDetail", OperationFlags.Edit, "Purchasing.PaymentRun", OperationFlags.Approve);

        // Housing
        Rule("SOD-HSE-01", "Create applicant and approve allocation",
            "Housing.WaitingList", OperationFlags.Create, "Housing.AllocationApprove", OperationFlags.Approve);
        Rule("SOD-HSE-02", "Allocate stand and approve ownership change",
            "Housing.AllocationRun", OperationFlags.Create, "Housing.OwnershipApprove", OperationFlags.Approve);
        Rule("SOD-HSE-03", "Set price and approve agreement",
            "Housing.Pricing", OperationFlags.Edit, "Housing.Agreement", OperationFlags.Approve);
        Rule("SOD-HSE-04", "Place caveat and lift caveat",
            "Housing.Caveat", OperationFlags.Create, "Housing.Caveat", OperationFlags.Delete);
        Rule("SOD-HSE-05", "Verify clearance and approve transfer",
            "Housing.OwnershipChange", OperationFlags.Edit, "Housing.OwnershipApprove", OperationFlags.Approve);

        // Payroll (role-assignment conflicts; the self-payslip block is a run-time rule, see remarks)
        Rule("SOD-PAY-01", "Process payroll and approve payroll",
            "Payroll.Calculate", OperationFlags.Create, "Payroll.Approve", OperationFlags.Approve);
        Rule("SOD-PAY-02", "Maintain employee master and approve payroll",
            "Payroll.Employee", OperationFlags.Edit, "Payroll.Approve", OperationFlags.Approve);
        Rule("SOD-PAY-03", "Change bank details and produce the bank payment file",
            "Payroll.BankDetails", OperationFlags.Edit, "Payroll.BankFile", OperationFlags.Create);

        // Budget
        Rule("SOD-BUD-01", "Prepare budget and approve budget",
            "Budget.Prepare", OperationFlags.Create, "Budget.Approve", OperationFlags.Approve);
        Rule("SOD-BUD-02", "Submit budget and activate budget",
            "Budget.SubmitOwnDepartment", OperationFlags.Post, "Budget.Activate", OperationFlags.Post, SodSeverity.Medium);
        Rule("SOD-BUD-03", "Raise requisition and override budget block",
            "Procurement.Requisition", OperationFlags.Create, "Budget.OverrideBlock", OperationFlags.OverridePrice);
        Rule("SOD-BUD-04", "Virement request and virement approval",
            "Budget.Virement", OperationFlags.Create, "Budget.VirementApprove", OperationFlags.Approve);

        return rules;
    }

    /// <summary>Returns every default rule that the supplied permissions violate.</summary>
    /// <param name="permissions">The principal's resolved permissions.</param>
    /// <returns>The violated rules.</returns>
    public static IEnumerable<SodRule> Violations(EffectivePermissions permissions) =>
        All.Where(rule => rule.IsViolatedBy(permissions));
}
