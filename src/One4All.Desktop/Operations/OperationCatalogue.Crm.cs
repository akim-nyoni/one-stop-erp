using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Crm;
using One4All.Core.Accounting;
using One4All.Core.Crm;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>CRM operations: leads, activities and the quotation link.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Crm()
    {
        const string module = "CRM";
        yield return new Operation
        {
            Module = module, Title = "Capture a lead", ListTitle = "Leads",
            Description = "A prospective customer with an e-mail or phone and where they came from; an earlier lead with the same e-mail or phone is flagged.",
            Fields = [Req("Name", "Name"), Opt("Organisation", "Organisation"), Opt("Email", "E-mail"), Opt("Phone", "Phone"), Req("Source", "Source", "web")],
            Run = async (c, i) => Check(await c.Get<ICrmService>().CaptureLeadAsync(new NewLead(c.CompanyId, i.Text("Name"), i.Optional("Organisation"), i.Optional("Email"), i.Optional("Phone"),
                i.Text("Source"), c.UserId)).ConfigureAwait(true), id => $"Lead {id} captured."),
            List = LeadsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Work a lead", ListTitle = "Leads",
            Description = "Mark contacted or qualified, disqualify with a reason, or convert a qualified lead into an opportunity (and, with a code, its customer).",
            Fields = [Num("Lead", "Lead id"), Pick("Step", "Step", ["Contacted", "Qualified", "Disqualify", "Convert"]), Opt("Reason", "Reason (disqualify)"), Opt("Opportunity", "Opportunity name (convert)"),
                OptNum("Value", "Estimated value (convert)"), Opt("Customer", "New customer code (convert, optional)"), Opt("Currency", "Customer currency", "USD")],
            Run = async (c, i) =>
            {
                ICrmService crm = c.Get<ICrmService>();
                long lead = (long)i.Number("Lead");
                return i.Text("Step") switch
                {
                    "Contacted" => Check(await crm.AdvanceLeadAsync(c.CompanyId, lead, LeadStatus.Contacted).ConfigureAwait(true), "Marked contacted."),
                    "Qualified" => Check(await crm.AdvanceLeadAsync(c.CompanyId, lead, LeadStatus.Qualified).ConfigureAwait(true), "Qualified."),
                    "Disqualify" => Check(await crm.DisqualifyLeadAsync(c.CompanyId, lead, i.Optional("Reason") ?? string.Empty).ConfigureAwait(true), "Disqualified."),
                    _ => $"Opportunity {Check(await crm.ConvertLeadAsync(c.CompanyId, lead, i.Text("Opportunity"), i.OptionalNumber("Value") ?? 0m, i.Optional("Customer"),
                        i.Optional("Customer") is null ? null : CurrencyCode.Parse(i.Text("Currency"))).ConfigureAwait(true))} created.",
                };
            },
            List = LeadsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Log an activity", ListTitle = "My agenda (due within a week)",
            Description = "A call, e-mail, meeting, task or note against a lead, opportunity or customer; give a due date for something still to do.",
            Fields = [Pick("Type", "Type", Enum.GetNames<ActivityType>()), Req("Subject", "Subject"), Opt("Notes", "Notes"), Opt("Due", "Due (yyyy-MM-dd HH:mm)"), OptNum("Lead", "Lead id"),
                OptNum("Opportunity", "Opportunity id"), Opt("Customer", "Customer code")],
            Run = async (c, i) =>
            {
                DateTimeOffset? due = i.Optional("Due") is string text ? DateTimeOffset.ParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal) : null;
                long? customer = i.Optional("Customer") is string code ? await CustomerAsync(c, code).ConfigureAwait(true) : null;
                return Check(await c.Get<ICrmService>().LogActivityAsync(new NewActivity(c.CompanyId, Enum.Parse<ActivityType>(i.Text("Type")), i.Text("Subject"), c.UserId, due, i.Optional("Notes"),
                    (long?)i.OptionalNumber("Lead"), (long?)i.OptionalNumber("Opportunity"), customer)).ConfigureAwait(true), id => $"Activity {id} logged.");
            },
            List = AgendaAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Complete an activity", ListTitle = "My agenda (due within a week)", Description = "Marks an activity done with what came of it.",
            Fields = [Num("Activity", "Activity id"), Opt("Outcome", "Outcome")],
            Run = async (c, i) => Check(await c.Get<ICrmService>().CompleteActivityAsync(c.CompanyId, (long)i.Number("Activity"), i.Optional("Outcome")).ConfigureAwait(true), "Done."),
            List = AgendaAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Link a quotation to an opportunity", ListTitle = "Lead funnel by source",
            Description = "Records the quotation sent for an opportunity (same customer) and moves an early opportunity to Proposal.",
            Fields = [Num("Opportunity", "Opportunity id"), Num("Quotation", "Quotation id")],
            Run = async (c, i) => Check(await c.Get<ICrmService>().LinkQuotationAsync(c.CompanyId, (long)i.Number("Opportunity"), (long)i.Number("Quotation")).ConfigureAwait(true), "Quotation linked."),
            List = async c => Table(await c.Get<ICrmService>().LeadFunnelAsync(c.CompanyId).ConfigureAwait(true), ("Source", f => f.Source), ("Leads", f => f.Leads), ("Qualified", f => f.Qualified),
                ("Converted", f => f.Converted), ("Disqualified", f => f.Disqualified), ("Conversion %", f => f.ConversionPercent)),
        };
    }

    private static IEnumerable<Operation> WhatsAppInbox()
    {
        yield return new Operation
        {
            Module = "CRM", Title = "WhatsApp replies", ListTitle = "Replies not yet handled",
            Description = "Messages customers and leads sent on WhatsApp (matched by their number), oldest first. Deal with one, then mark it handled.",
            Fields = [Num("Message", "Message id")],
            Run = async (c, i) => Check(await c.Get<IWhatsAppWebhookService>().MarkHandledAsync(c.CompanyId, (long)i.Number("Message"), c.UserId).ConfigureAwait(true), "Marked handled."),
            List = async c => Table(await c.Get<IWhatsAppWebhookService>().UnhandledAsync(c.CompanyId).ConfigureAwait(true), ("Id", m => m.Id),
                ("Received", m => m.ReceivedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("From", m => m.From), ("Name", m => m.SenderName),
                ("Customer", m => m.CustomerId), ("Lead", m => m.LeadId), ("Message", m => m.Body)),
        };
    }

    private static async Task<DataTable> LeadsAsync(OperationContext c)
        => Table(await c.Db.Set<Lead>().AsNoTracking().Where(l => l.CompanyId == c.CompanyId).OrderByDescending(l => l.Id).Take(300).ToListAsync().ConfigureAwait(true),
            ("Id", l => l.Id), ("Name", l => l.Name), ("Organisation", l => l.Organisation), ("E-mail", l => l.Email), ("Phone", l => l.Phone), ("Source", l => l.Source), ("Status", l => l.Status),
            ("Duplicate of", l => l.PossibleDuplicateOfId), ("Opportunity", l => l.OpportunityId));

    private static async Task<DataTable> AgendaAsync(OperationContext c)
        => Table(await c.Get<ICrmService>().AgendaAsync(c.CompanyId, c.UserId, DateTimeOffset.UtcNow.AddDays(7)).ConfigureAwait(true),
            ("Id", a => a.Id), ("Due", a => a.DueOnUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Type", a => a.Type), ("Subject", a => a.Subject),
            ("Overdue", a => a.DueOnUtc < DateTimeOffset.UtcNow), ("Lead", a => a.LeadId), ("Opportunity", a => a.OpportunityId), ("Customer", a => a.CustomerId));
}
