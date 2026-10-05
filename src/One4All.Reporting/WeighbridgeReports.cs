using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// Weighbridge and bulk-materials reports from brief §14 L (codes are WB plus the brief's item number). Each definition is a listing
/// over a <c>Wbr*</c> data command served by the weighbridge report data provider.
/// </summary>
public static class WeighbridgeReports
{
    private static readonly (string, ParameterType)[] Period = [("FromDate", ParameterType.Date), ("ToDate", ParameterType.Date)];

    /// <summary>Every WB-numbered definition shipped.</summary>
    /// <returns>The definitions, in brief order.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        yield return Listing("WB198", "Weighbridge Ticket", "Weighbridge", "Each ticket in the period: vehicle, driver, customer or supplier, product, time in and out, gross, tare, deduction and net.",
            "WbrTickets", [new Column("Ticket", "Ticket", 85), new Column("Date", "Date", 75), new Column("Vehicle", "Vehicle", 70), new Column("Driver", "Driver", 90), new Column("Party", "Customer / supplier", 120),
             new Column("Product", "Product", 110), new Column("In", "Time in", 50), new Column("Out", "Time out", 50), new Column("Gross", "Gross kg", 75), new Column("Tare", "Tare kg", 75),
             new Column("Deduction", "Deduction", 65), new Column("Cubic", "m³", 55), new Column("Net", "Net t", 70, true)], Period);
        yield return Listing("WB199", "Daily Weighbridge Register & Tonnage Summary", "Weighbridge", "Completed loads per day, product, customer, vehicle and driver: loads, tonnes and m³.",
            "WbrDaily", [new Column("Date", "Date", 75), new Column("Product", "Product", 120), new Column("Customer", "Customer", 120), new Column("Vehicle", "Vehicle", 70), new Column("Driver", "Driver", 90),
             new Column("Loads", "Loads", 45), new Column("Cubic", "m³", 60), new Column("Tonnes", "Tonnes", 80, true)], Period);
        yield return Listing("WB200", "Vehicle Movement & Turnaround", "Weighbridge", "Per vehicle: loads, average and longest time between weighs, open tickets and overloads against its legal maximum.",
            "WbrVehicles", [new Column("Vehicle", "Vehicle", 70), new Column("Haulier", "Haulier", 110), new Column("Loads", "Loads", 45), new Column("Average", "Avg turnaround (min)", 100),
             new Column("Longest", "Longest (min)", 75), new Column("Open", "Still on site", 70), new Column("Overloads", "Overloads", 65), new Column("Tonnes", "Tonnes", 80, true)], Period);
        yield return Listing("WB201", "Deliveries by Customer / Site", "Weighbridge", "Outbound loads per customer and site: loads, tonnes, m³ and the haulage charged.",
            "WbrDeliveries", [new Column("Customer", "Customer", 140), new Column("Site", "Site", 130), new Column("Loads", "Loads", 45), new Column("Tonnes", "Tonnes", 75), new Column("Cubic", "m³", 60),
             new Column("Haulage", "Haulage", 90, true)], Period);
        yield return Listing("WB202", "Manual Weight Override Report", "Weighbridge", "Tickets where a weight was keyed by hand: operator, authorising supervisor, reason and the net recorded.",
            "WbrManual", [new Column("Ticket", "Ticket", 85), new Column("Date", "Date", 75), new Column("Vehicle", "Vehicle", 70), new Column("Operator", "Operator", 85), new Column("AuthorisedBy", "Authorised by", 85),
             new Column("Reason", "Reason", 200), new Column("Net", "Net t", 70, true)], Period);
        yield return Listing("WB203", "Stockpile Reconciliation", "Weighbridge", "Per weighed product and stockpile: opening, produced, received, sold over the weighbridge, adjusted and closing tonnes.",
            "WbrStockpiles", [new Column("Product", "Product", 140), new Column("Stockpile", "Stockpile", 80), new Column("Opening", "Opening", 70), new Column("Produced", "Produced", 70), new Column("Received", "Received", 70),
             new Column("Sold", "Sold", 70), new Column("Adjusted", "Adjusted / other", 80), new Column("Closing", "Closing", 75, true)], Period);
        yield return Listing("WB204", "Concrete Batch Report", "Weighbridge", "Each batch run in the period: mix, m³ produced and delivered over the weighbridge, materials cost and the variance to the recipe.",
            "WbrBatches", [new Column("Run", "Batch", 85), new Column("Date", "Date", 75), new Column("Mix", "Mix design", 90), new Column("Produced", "m³ produced", 75), new Column("Delivered", "m³ delivered", 75),
             new Column("Loads", "Loads", 45), new Column("Cost", "Materials cost", 90), new Column("Variance", "Variance to recipe", 95, true)], Period);
        yield return Listing("WB205", "Haulage Charge Analysis", "Weighbridge", "Haulage charged in the period per zone: loads, tonnes, kilometres and the charge, with the rate achieved per tonne-km.",
            "WbrHaulage", [new Column("Zone", "Zone", 70), new Column("Name", "Name", 130), new Column("Loads", "Loads", 45), new Column("Tonnes", "Tonnes", 75), new Column("Km", "Km", 65),
             new Column("PerTonneKm", "Per t·km", 70), new Column("Charge", "Charge", 90, true)], Period);
    }
}
