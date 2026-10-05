using One4All.Reporting.Model;

namespace One4All.Reporting;

/// <summary>
/// Every system report shipped with the product (brief §14 report catalogue), in catalogue order: the seeder writes them to
/// <c>rpt.report_definition</c> and <c>docs/reports/CATALOGUE.md</c> is generated from them.
/// </summary>
public static class SystemReportCatalogue
{
    /// <summary>The system report definitions.</summary>
    /// <returns>Each definition, freshly built.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return SystemReports.TrialBalance();
        yield return SystemReports.CustomerStatement();
        yield return SystemReports.IncomeStatement();
        yield return SystemReports.BalanceSheet();
        yield return SystemReports.CashFlow();
        yield return SystemReports.DepartmentalIncomeStatement();
        yield return SystemReports.TrialBalanceByType();
        yield return SystemReports.JournalDaybook();
        yield return SystemReports.TrialBalanceMovement();
        yield return SystemReports.ChartOfAccounts();
        yield return SystemReports.FinancialSnapshot();
        yield return SystemReports.VatReturn();
        yield return SystemReports.ControlReconciliation();
        yield return SystemReports.VatReturnByCode();
        yield return SystemReports.CustomerRegister();
        yield return SystemReports.SupplierRegister();
        yield return SystemReports.AgedDebtors();
        yield return SystemReports.AgedCreditors();
        yield return SystemReports.CreditExposure();
        yield return SystemReports.CashFlowForecast();
        yield return SystemReports.PaymentsDue();
        yield return SystemReports.StockValuation();
        yield return SystemReports.StockMovements();
        yield return SystemReports.Reorder();
        yield return SystemReports.BackOrders();
        yield return SystemReports.SlowStock();
        yield return SystemReports.StockAgeing();
        yield return SystemReports.AbcAnalysis();
        yield return SystemReports.ItemMaster();
        yield return SystemReports.AssetRegister();
        yield return SystemReports.DepreciationForecast();
        yield return SystemReports.AssetMaintenance();
        yield return SystemReports.AssetDisposals();
        yield return SystemReports.PayrollSummary();
        yield return SystemReports.EmployeeLoans();
        yield return SystemReports.PayrollDeductions();
        yield return SystemReports.EmployeeRegister();
        yield return SystemReports.LeaveBalances();
        yield return SystemReports.PropertyRegister();
        yield return SystemReports.BudgetVsActual();
        yield return SystemReports.BudgetList();
        yield return SystemReports.RecurringJournals();
        yield return SystemReports.PeriodStatus();
        yield return SystemReports.Pipeline();
        yield return SystemReports.ExpenseClaims();
        yield return SystemReports.PosSales();
        yield return SystemReports.JobProfitability();
        yield return SystemReports.ProgressCertificates();
        yield return SystemReports.JobWip();
        yield return SystemReports.HousingStands();
        yield return SystemReports.StudentRegister();
        yield return SystemReports.OpenPurchaseOrders();
        yield return SystemReports.PurchaseRequisitions();
        yield return SystemReports.Quotations();
        yield return SystemReports.SalesBackOrders();
        yield return SystemReports.RecurringInvoices();
        yield return SystemReports.RecurringPurchases();
        yield return SystemReports.Menu();
        yield return SystemReports.TableStatus();
        yield return SystemReports.KitchenTickets();
        yield return SystemReports.TipAllocations();
        yield return SystemReports.MenuEngineering();
        yield return SystemReports.Reservations();
        yield return SystemReports.Waitlist();
        yield return SystemReports.TableGroups();
        yield return SystemReports.FloorPlan();
        yield return SystemReports.SectionSales();
        yield return SystemReports.VoidsAndComps();
        yield return SystemReports.OrderTypeSales();
        yield return SystemReports.Deliveries();
        yield return SystemReports.ServerSales();
        yield return SystemReports.CategorySales();
        yield return SystemReports.Wastage();
        yield return SystemReports.Bills();
        yield return SystemReports.MenuCost();

        // Housing, Land & Estates catalogue (brief §14 I-B, the H series).
        foreach (ReportDefinition definition in HousingReports.All())
        {
            yield return definition;
        }

        // Accounts Receivable additions (brief §14 B, AR + item number).
        foreach (ReportDefinition definition in ReceivablesReports.All())
        {
            yield return definition;
        }

        // Cash book & treasury additions (brief §14 D, CB + item number).
        foreach (ReportDefinition definition in CashBookReports.All())
        {
            yield return definition;
        }

        // General ledger additions (brief §14 A, GL + item number).
        foreach (ReportDefinition definition in LedgerReports.All())
        {
            yield return definition;
        }

        // Audit, security & system listings (brief §14 P and R, SYS + item number).
        foreach (ReportDefinition definition in ControlReports.All())
        {
            yield return definition;
        }

        // Accounts Payable additions (brief §14 C, AP + item number).
        foreach (ReportDefinition definition in PayablesReports.All())
        {
            yield return definition;
        }

        // Procurement additions (brief §14 G, PO + item number).
        foreach (ReportDefinition definition in ProcurementReports.All())
        {
            yield return definition;
        }

        // Municipality / Council catalogue (brief §14 I, the M series).
        foreach (ReportDefinition definition in MunicipalReports.All())
        {
            yield return definition;
        }

        // Cashiering & Receipting catalogue (brief §RCP-16, the RC series).
        foreach (ReportDefinition definition in ReceiptingReports.All())
        {
            yield return definition;
        }
    }
}
