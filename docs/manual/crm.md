# CRM

Leads, opportunities, activities and messages.

Works with: CRM.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Leads and opportunities

### Capture a lead

A prospective customer with an e-mail or phone and where they came from; an earlier lead with the same e-mail or phone is flagged.

| Field | Required | Notes |
| --- | --- | --- |
| Name | yes | Text |
| Organisation | no | Text |
| E-mail | no | Text |
| Phone | no | Text |
| Source | yes | Text; default web |

The list beside the form shows: Leads.

### Work a lead

Mark contacted or qualified, disqualify with a reason, or convert a qualified lead into an opportunity (and, with a code, its customer).

| Field | Required | Notes |
| --- | --- | --- |
| Lead id | yes | Number |
| Step | yes | One of: Contacted, Qualified, Disqualify, Convert; default Contacted |
| Reason (disqualify) | no | Text |
| Opportunity name (convert) | no | Text |
| Estimated value (convert) | no | Number |
| New customer code (convert, optional) | no | Text |
| Customer currency | no | Text; default USD |

The list beside the form shows: Leads.

### Opportunity pipeline

Move an opportunity through the pipeline (lead, qualified, proposal, negotiation) with its probability and expected close date, or close it as won or lost (with the reason); Forecast shows the weighted pipeline by stage; Timeline lists everything done on it; Convert raises the sales order from it (lines ITEM:qty[:price]; …).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Move, Forecast, Timeline, Convert to order; default Move |
| Opportunity id | no | Number |
| Stage | yes | One of: Lead, Qualified, Proposal, Negotiation, Won, Lost; default Lead |
| Probability % (blank: the stage's) | no | Number |
| Expected close (yyyy-MM-dd) | no | Text |
| Why lost | no | Text |
| Sales order number | no | Text |
| Lines ITEM:qty[:price]; … | no | Text |
| Date | yes | Date |

The list beside the form shows: Open opportunities.

### Link a quotation to an opportunity

Records the quotation sent for an opportunity (same customer) and moves an early opportunity to Proposal.

| Field | Required | Notes |
| --- | --- | --- |
| Opportunity id | yes | Number |
| Quotation id | yes | Number |

The list beside the form shows: Lead funnel by source.

## Activities

### Log an activity

A call, e-mail, meeting, task or note against a lead, opportunity or customer; give a due date for something still to do.

| Field | Required | Notes |
| --- | --- | --- |
| Type | yes | One of: Call, Email, Meeting, Task, Note; default Call |
| Subject | yes | Text |
| Notes | no | Text |
| Due (yyyy-MM-dd HH:mm) | no | Text |
| Lead id | no | Number |
| Opportunity id | no | Number |
| Customer | no | Choice |

The list beside the form shows: My agenda (due within a week).

### Complete an activity

Marks an activity done with what came of it.

| Field | Required | Notes |
| --- | --- | --- |
| Activity id | yes | Number |
| Outcome | no | Text |

The list beside the form shows: My agenda (due within a week).

### WhatsApp replies

Messages customers and leads sent on WhatsApp (matched by their number), oldest first. Deal with one, then mark it handled.

| Field | Required | Notes |
| --- | --- | --- |
| Message id | yes | Number |

The list beside the form shows: Replies not yet handled.

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| CRM-PIPE | Sales Pipeline | Open opportunities by stage - estimated value, win probability and the probability-weighted value. |
