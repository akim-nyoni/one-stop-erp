namespace One4All.Application.JobCosting;

/// <summary>The outcome of an onerous-contract calculation.</summary>
/// <param name="ContractValue">The contract value.</param>
/// <param name="BudgetCost">The forecast total cost.</param>
/// <param name="ExpectedLoss">Forecast cost over contract value (0 when the contract is profitable).</param>
/// <param name="PriorProvision">The provision already carried.</param>
public sealed record ExpectedLossCalculation(decimal ContractValue, decimal BudgetCost, decimal ExpectedLoss, decimal PriorProvision)
{
    /// <summary>The movement to post this run: positive tops the provision up, negative releases it.</summary>
    public decimal Movement => ExpectedLoss - PriorProvision;

    /// <summary>True when the contract is onerous (a loss is expected).</summary>
    public bool IsOnerous => ExpectedLoss > 0m;
}

/// <summary>
/// Onerous-contract provisioning (IAS 37 / IFRS 15): when the forecast cost to complete exceeds the
/// contract value the whole expected loss is provided immediately - not spread over the remaining work -
/// and released again if the forecast improves. Pure and unit-tested.
/// </summary>
public static class ExpectedLoss
{
    /// <summary>Computes the expected loss and the provision movement.</summary>
    /// <param name="contractValue">The contract value.</param>
    /// <param name="budgetCost">The forecast total cost.</param>
    /// <param name="priorProvision">The provision already carried.</param>
    /// <returns>The calculation.</returns>
    public static ExpectedLossCalculation Compute(decimal contractValue, decimal budgetCost, decimal priorProvision) =>
        new(contractValue, budgetCost, Math.Max(0m, decimal.Round(budgetCost - contractValue, 2, MidpointRounding.ToEven)), priorProvision);
}
