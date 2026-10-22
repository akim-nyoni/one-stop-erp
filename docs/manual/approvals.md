# Approvals

Things waiting for your approval, delegation and approval rules.

Works with: Approvals.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## My work

### My approvals

Documents waiting for you to approve or reject.

### My notifications

Messages and alerts for you.

### Approve or reject several at once

Acts on every listed instance (ids separated by commas); each succeeds or fails on its own.

| Field | Required | Notes |
| --- | --- | --- |
| Instance ids (1,2,3) | yes | Text |
| Action | yes | One of: Approve, Reject, Return; default Approve |
| Comment | no | Text |

The list beside the form shows: Waiting on me.

### Discuss a document

The comment thread of a document under approval: ask the requester a question, answer an approver, or add a fact, without approving or rejecting. The requester, anyone who has acted on it and anyone who may approve its current stage can write; leave the comment blank to read the thread.

| Field | Required | Notes |
| --- | --- | --- |
| Workflow instance id | yes | Number |
| Comment (blank: read the thread) | no | Text |

The list beside the form shows: Waiting on me.

### Delegate my approvals

While you are away, the delegate may approve anything you could, recorded as on your behalf. You delegate only your own approvals (a system administrator may record one for an approver who is away). With a delegation id (Edit fills it in) the delegate, dates and reason of that delegation change (the approver stays); Delete withdraws a delegation early (kept in history).

| Field | Required | Notes |
| --- | --- | --- |
| Approver who is away (blank: me) | no | Text |
| Delegate (user name) | yes | Text |
| Starts | yes | Date |
| Ends | yes | Date |
| Reason | yes | Text; default Leave |
| Delegation id (change) | no | Number |

The list beside the form shows: Approval delegations.

## Approval rules

### Approval stages and rules

A condition (e.g. Department = IT and Value > 5000; ops = != > >= < <= in), a role whose holders approve, and/or the requester's manager.

| Field | Required | Notes |
| --- | --- | --- |
| Workflow definition id | yes | Number |
| Stage sequence | yes | Number |
| Condition | no | Text |
| Approving role | no | Text |
| Dynamic approver | yes | One of: None, RequesterManager; default None |

The list beside the form shows: Workflow stages.

### Set a user's manager

The manager approves stages that ask for the requester's manager.

| Field | Required | Notes |
| --- | --- | --- |
| User name | yes | Text |
| Manager's user name (blank: none) | no | Text |

The list beside the form shows: Waiting on me.

### Add an approver to a document

Adds someone to the current stage of one document's approval, alongside the stage's own approvers. Only an approver of that stage (or a system administrator) can add one, and never the person who raised the document nor themselves.

| Field | Required | Notes |
| --- | --- | --- |
| Workflow instance id | yes | Number |
| Approver (user name) | yes | Text |

The list beside the form shows: Waiting on me.

### Segregation-of-duties rules

The conflicts no one person may hold (create a supplier and approve its payment, say). Add a rule, re-tune a built-in one by its code, or switch it off (inactive); the control reports and role assignments check the rules in force. Operations by name: Create, Edit, Approve, Post…

| Field | Required | Notes |
| --- | --- | --- |
| Rule code | yes | Text |
| What the conflict is | yes | Text |
| Permission (e.g. Purchasing.Supplier) | yes | Text |
| Its operations (Create, Edit…) | yes | Text |
| Conflicting permission | yes | Text |
| Its operations | yes | Text |
| Severity | yes | One of: Medium, High; default Medium |
| In force | no | Flag; default True |

The list beside the form shows: Rules in force.

### Escalate overdue approvals

Adds each overdue stage's escalation user to documents that have waited longer than the stage allows.

The list beside the form shows: Waiting on me.

### Request a master-data change

Changes a tariff's rates or a customer's, supplier's or item's field - held for the "MasterDataChange" workflow when one is defined, applied at once otherwise. Tariff: FixedCharge, PerUnitRate, RatePerValuationCents, RatePerSquareMetre, MinimumCharge, IsActive. Customer: Name, PaymentTermsDays, SettlementDiscountPercent, SettlementDiscountDays, OnHold, InterestExempt, TaxRegistration, Email, IsActive. Supplier: Name, PaymentTermsDays, SettlementDiscountPercent, SettlementDiscountDays, OnHold, TaxRegistration, TaxClearanceExpiry, IsActive. Item: Description, TaxCode, StandardCost, CostOfSalesAccountCode, RevenueAccountCode, IsActive.

| Field | Required | Notes |
| --- | --- | --- |
| Record | yes | One of: Tariff, Customer, Supplier, Item; default Tariff |
| Code | yes | Text |
| Field | yes | Text |
| New value (blank clears) | no | Text |

The list beside the form shows: Master-data changes.

### Email approval links

The server address approvers reach from their email (e.g. https://erp.example.com). When set, each approver with an email address is also sent single-use approve / reject links (valid 7 days, only while the document is still at their stage). Blank stops the links.

| Field | Required | Notes |
| --- | --- | --- |
| Server address | no | Text |
