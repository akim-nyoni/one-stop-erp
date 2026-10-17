# Purchases & Suppliers

Suppliers, requisitions and orders, receiving goods, supplier invoices and payments.

Works with: Suppliers, Purchasing.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Suppliers

### Add a supplier

A supplier account. An existing code changes that supplier's name and analysis codes (the currency stays). The three analysis codes (e.g. region, buyer, commodity) are how the account's transactions are analysed in reports.

| Field | Required | Notes |
| --- | --- | --- |
| Account code | yes | Text |
| Name | yes | Text |
| Currency | yes | Text; default USD |
| Analysis code 1 | no | Text |
| Analysis code 2 | no | Text |
| Analysis code 3 | no | Text |
| Default tax code | no | Text |

The list beside the form shows: Suppliers.

### Supplier addresses and contacts

Postal, physical and delivery addresses (the default postal address prints on invoices, statements, orders, remittances and letters) and the people who receive statements, remittance advices and purchase orders by email. Remove takes an address or contact id. The list shows the trader typed in Code.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Add address, Add contact, Remove address, Remove contact; default Add address |
| Supplier code | yes | Text |
| Address kind | yes | One of: Postal, Physical, Delivery; default Postal |
| Address line 1 | no | Text |
| Address line 2 | no | Text |
| Town / city | no | Text |
| Province | no | Text |
| Postal code | no | Text |
| Country | no | Text |
| Contact name | no | Text |
| Role | no | Text |
| Email | no | Text |
| Phone | no | Text |
| Primary contact | no | Flag; default False |
| Receives statements | no | Flag; default False |
| Receives remittances | no | Flag; default False |
| Receives purchase orders | no | Flag; default False |
| Address / contact id (remove) | no | Number |

The list beside the form shows: Directory.

### Supplier bank details

A change to a supplier's bank details (or mobile-money wallet) is requested by one user and approved (or rejected) by another; until then the supplier is held out of payment runs. Payment priority (1 paid first … 9 last) decides who is paid first when a run's funds are short.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Request change, Approve, Reject, Payment priority; default Request change |
| Supplier code (request) | no | Text |
| Account name | no | Text |
| Account number | no | Text |
| Bank | no | Text |
| Mobile-money wallet | no | Text |
| Change id (approve / reject) | no | Number |
| Reason (reject) | no | Text |
| Payment priority (1–9) | yes | Number; default 5 |

The list beside the form shows: Changes awaiting approval.

### Supplier exchange-rate type

The rate type this trader's foreign documents convert at (e.g. a contract or bank rate kept in the rate table). Daily (or blank) uses the daily rate; a document dated before any rate of the type falls back to the daily rate.

| Field | Required | Notes |
| --- | --- | --- |
| Supplier code | yes | Text |
| Rate type | yes | One of: Spot, Daily, PeriodAverage, ClosingRate, Budget, Fixed; default Spot |

The list beside the form shows: Suppliers on their own rate.

### Supplier contracts (framework agreements)

A supplier contract with a term and a ceiling: orders placed under it are checked against both, and report PO133 shows how much of it is used.

| Field | Required | Notes |
| --- | --- | --- |
| Supplier | yes | Choice |
| Agreement reference | yes | Text |
| Description | yes | Text |
| Starts | yes | Date |
| Ends | yes | Date |
| Ceiling value | yes | Number |

The list beside the form shows: Framework agreements.

### Supplier scorecard

Each supplier active in a period: deliveries on time against the orders' required-by dates, quality as the share of goods not returned, spend, and the purchase price variance — invoiced above or below the order prices.

| Field | Required | Notes |
| --- | --- | --- |
| From | yes | Date |
| To | yes | Date |

### Supplier rebates

A volume rebate agreement with a supplier for a period with spend tiers ("0:1; 100000:2.5; 250000:4" — from spend:rate %); Accrue books what each agreement has earned so far.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Accrue; default Create |
| Supplier code (create) | no | Text |
| Agreement code | no | Text |
| From | yes | Date |
| To | yes | Date |
| Tiers: from spend:rate %; … | no | Text |
| Rebate receivable account (accrue) | no | Text |
| Rebate income account (accrue) | no | Text |
| As at (accrue) | yes | Date |

The list beside the form shows: Rebate agreements.

## Requisitions and orders

### Purchase requisition

Raise a requisition (budget-checked), submit it, approve or reject it (someone else approves), and convert an approved one into a purchase order. Lines: ITEM:quantity@price; ITEM:quantity@price

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Submit, Approve, Reject, Convert to order; default Create |
| Requisition number | no | Text |
| Supplier code (create) | no | Text |
| Currency | no | Text; default USD |
| Lines (create) | no | Text |
| New order number (convert) | no | Text |
| Date | yes | Date |
| Expense account the lines are budget-checked against (create) | no | Text |
| Required by yyyy-MM-dd (create) | no | Text |
| Justification (create) | no | Text |
| Department (create) | no | Text |
| Cost centre (create) | no | Text |
| Project (create) | no | Text |

The list beside the form shows: Requisitions.

### Requests for quotation

Issue an RFQ to several suppliers (codes comma-separated) for items (ITEM:quantity; …), print it or email it to the suppliers' order contacts, record each supplier's quote (ITEM@price; …, lead time), compare them (price and lead time weighted, ranked) and award the RFQ to a supplier, which raises the purchase order.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Issue, Print, Email, Quote, Compare, Award; default Issue |
| RFQ number | yes | Text |
| Supplier codes (issue) / supplier (quote, award) | no | Text |
| Items: ITEM:quantity; … (issue) or ITEM@price; … (quote) | no | Text |
| Lead time (days, quote) | yes | Number; default 0 |
| Terms / award reason | no | Text |
| Price weight (compare) | yes | Number; default 0.7 |
| Closes on (issue) | yes | Date |
| Date | yes | Date |

The list beside the form shows: RFQs.

### Purchase order

Raise an order directly (budget-checked; over-budget needs an approver), cancel one, or print it. Lines: ITEM:quantity@price; ITEM:quantity@price

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Print, Cancel; default Create |
| Order number | yes | Text |
| Supplier code (create) | no | Text |
| Currency | no | Text; default USD |
| Lines (create) | no | Text |
| Required by (yyyy-MM-dd) | no | Text |
| Budget approver user (over budget) | no | Text |
| Reason (cancel) | no | Text |
| Order date | yes | Date |
| For job (contract number): received straight to the job | no | Text |
| Job WIP account | no | Text |
| Job cost head | no | Text; default Materials |

The list beside the form shows: Purchase orders.

### Amend a purchase order

Changes order lines (ITEM:quantity@price; …) as a new version with the reason — quantities never below what was received, prices not once invoiced; a rise over the threshold sends the order back for re-approval. Re-approve clears it (another user); History lists its versions.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Amend, Re-approve, History; default Amend |
| Order number | yes | Text |
| Lines: ITEM:quantity@price; … | no | Text |
| Reason | no | Text |
| Re-approval threshold % | yes | Number; default 10 |
| Date | yes | Date |

The list beside the form shows: Purchase orders.

### Schedule an order's deliveries

Spreads an order line over due dates (DATE:qty;… as yyyy-MM-dd:qty). The quantities must add up to the line's ordered quantity.

| Field | Required | Notes |
| --- | --- | --- |
| Purchase order number | yes | Text |
| Product | yes | Choice |
| Deliveries yyyy-MM-dd:qty;… | yes | Text |

The list beside the form shows: Deliveries late or due within a week.

## Receiving goods

### Receive goods

Books a goods received note against an order into a warehouse (stock in, goods-received-not-invoiced accrued). Leave Lines blank to receive everything still outstanding; otherwise ITEM:quantity; ITEM:quantity.

| Field | Required | Notes |
| --- | --- | --- |
| Order number | yes | Text |
| Warehouse | yes | Choice |
| GRN number | yes | Text |
| Lines (blank: all outstanding) | no | Text |
| Date | yes | Date |
| Supervisor authorising an over-receipt (user name) | no | Text |

The list beside the form shows: Purchase orders.

### Quality inspection of a receipt

Inspects goods received on a GRN held for quality: per item, what passed (released to the warehouse) and what was rejected (returned, with a credit note) — "ITEM:passed:rejected:reason; …".

| Field | Required | Notes |
| --- | --- | --- |
| GRN number | yes | Text |
| Results: ITEM:passed:rejected[:reason]; … | yes | Text |
| Release to warehouse | yes | Text |
| Price variance account | no | Text |
| Date | yes | Date |

### Return stock to a supplier

Sends accepted stock back to the supplier: the stock leaves the warehouse at average cost and the supplier's account is debited at the agreed return price (a debit note), any difference to the price variance account.

| Field | Required | Notes |
| --- | --- | --- |
| Supplier | yes | Choice |
| Product | yes | Choice |
| Warehouse | yes | Choice |
| Quantity | yes | Number |
| Return unit price | yes | Number |
| Date | yes | Date |
| Return document number | yes | Text |
| Price variance account | yes | Text; default 5010 |

The list beside the form shows: Recent supplier returns.

### Landed costs on a receipt

Spreads a freight, duty or clearing charge over a GRN's items by value or quantity: the share of stock still on hand is capitalised, the share already sold goes to cost of sales.

| Field | Required | Notes |
| --- | --- | --- |
| GRN number | yes | Text |
| Charge | yes | Number |
| Spread by | yes | One of: Value, Quantity, Weight, Volume; default Value |
| Clearing (charge) account | yes | Text |
| Reference | yes | Text |
| Date | yes | Date |

## Invoices and payments

### Supplier invoice or credit note

Posts a supplier's invoice or credit note that has no purchase order (services, utilities); invoices for ordered goods go through Purchasing → Match a supplier invoice.

| Field | Required | Notes |
| --- | --- | --- |
| Document | yes | One of: Invoice, Credit note; default Invoice |
| Supplier | yes | Choice |
| Supplier's document number | yes | Text |
| Date | yes | Date |
| Net amount | yes | Number |
| Tax amount | yes | Number; default 0 |
| Expense account | no | Text |
| Tax account | no | Text |
| Tax code | no | Text |
| Currency (blank: the supplier's) | no | Text |
| Narrative | no | Text |
| Cost centre | no | Text |
| Department | no | Text |
| Project | no | Text |
| Expense lines ACCOUNT:amount[:cost centre[:department]]; … (invoice) | no | Text |

The list beside the form shows: Open supplier items.

### Match a supplier invoice to the order

Posts the supplier's invoice against the order (three-way match within the price tolerance; a larger difference is held as a dispute). Leave Lines blank to invoice everything received and not yet invoiced at the order price; otherwise ITEM:quantity@price.

| Field | Required | Notes |
| --- | --- | --- |
| Order number | yes | Text |
| Supplier invoice number | yes | Text |
| Lines (blank: received, at order price) | no | Text |
| Price tolerance % | yes | Number; default 5 |
| Price variance account | no | Text |
| Budget approver user (over budget) | no | Text |
| Invoice date | yes | Date |

The list beside the form shows: Purchase orders.

### Supplier invoice disputes

Submits a supplier invoice that may not match (outside the tolerance it is held as a dispute rather than refused), then accepts a dispute (posting it, the difference to price variance) or rejects it with a note.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Submit, Accept, Reject; default Submit |
| Order number (submit) | no | Text |
| Supplier invoice number (submit) | no | Text |
| Lines: ITEM:quantity@price; … (submit; blank: received at order price) | no | Text |
| Price tolerance % | yes | Number; default 5 |
| Dispute id | no | Number |
| Note | no | Text |
| Price variance account | no | Text |
| Date | yes | Date |

The list beside the form shows: Open disputes.

### Match payments to invoices

Match a payment or credit note to an invoice (in part: give the amount; blank matches as much as both have open), undo an allocation, or write back an old creditor invoice that will never be paid (the liability released to income).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Allocate, Unallocate, Write back; default Allocate |
| Supplier code | yes | Text |
| Debit document (payment / debit note) | no | Text |
| Invoice number | no | Text |
| Amount (blank: as much as is open) | no | Number |
| Date | yes | Date |
| Allocation id (unallocate; blank: the latest on the document) | no | Number |
| Write-back income account | no | Text |
| Reason (write off / back) | no | Text |
| Document number (contra) | no | Text |

The list beside the form shows: Open supplier items.

### Supplier payment

A single payment to a supplier from the bank (several at once go through the payment run).

| Field | Required | Notes |
| --- | --- | --- |
| Supplier | yes | Choice |
| Payment number | yes | Text |
| Date | yes | Date |
| Amount | yes | Number |
| Bank account | yes | Text; default 1100 |
| Currency (blank: the supplier's) | no | Text |
| Narrative | no | Text |

The list beside the form shows: Open supplier items.

### Supplier advances

Advances and prepayments to suppliers. Pay: money paid ahead of the invoice into the advances account (held suppliers refused). Apply: moves an advance into the purchase ledger against the invoice (the open item id) and allocates it. Refund: the supplier returns the balance. List: advances for the supplier, or all.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: List, Pay, Apply, Refund; default List |
| Supplier (pay / list) | no | Choice |
| Document number (pay / refund) | no | Text |
| Date | yes | Date |
| Amount (pay; apply / refund: blank for all) | no | Number |
| Advances account (pay) | no | Text |
| Bank account (pay / refund) | no | Text; default 1100 |
| Purchase order (pay) | no | Text |
| Currency (blank: the supplier's) | no | Text |
| Advance id (apply / refund) | no | Number |
| Invoice open item id (apply) | no | Number |
| Narrative | no | Text |

The list beside the form shows: Open supplier items.

### Supplier payment run

Choose which supplier invoices to pay and produce the payments.

### Payment run approval

Saves a proposed payment run for review: everyone due (held suppliers left out with the reason); lines can be left out or part-paid (oldest invoices first); someone other than the proposer approves it, which pays exactly what it holds. Require approval stops direct payment runs for the company. With funds available (and a currency), the proposal pays suppliers by payment priority (1 first) until the funds run out. Mobile money writes a payout file to each wallet.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Save proposal, Set line, Approve, Cancel, Show, Require approval, Allow direct runs; default Save proposal |
| Run number | no | Text |
| Payment date | yes | Date |
| Pay invoices due on or before | yes | Date |
| Bank account | yes | Text; default 1100 |
| Currency (blank: all) | no | Text |
| Withholding tax code | no | Text |
| Bank file layout | no | Text |
| Pay by | yes | One of: Eft, Cheque, Cash, MobileMoney; default Eft |
| First cheque number (cheques) | no | Text |
| Supplier code (set line) | no | Text |
| Pay this supplier | no | Flag; default True |
| Amount (blank: all due) | no | Number |
| Funds available (save proposal) | no | Number |

The list beside the form shows: Saved payment runs.

### Recurring purchase invoices

An invoice from a supplier raised every so many months from a first date; Generate raises all that are due; Stop / restart one.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Generate due, Stop, Restart; default Create |
| Supplier code (create) | no | Text |
| Reference | no | Text |
| Net amount | yes | Number; default 0 |
| Tax amount | yes | Number; default 0 |
| Expense account | no | Text |
| Tax account | no | Text |
| Every (months) | yes | Number; default 1 |
| First run date | yes | Date |
| Narrative | no | Text |
| Generate as of | yes | Date |

The list beside the form shows: Recurring invoices.

### Allowance vouchers

Numbered fuel and meal vouchers issued to staff or a department, then reconciled against the supplier's invoice (serial@date; … as billed) — showing any difference between the vouchers' value and the amount invoiced.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Issue, Reconcile invoice; default Issue |
| Serial prefix (issue) | no | Text |
| First number (issue) | no | Number |
| How many (issue) | no | Number |
| Kind | yes | One of: Fuel, Meal, Other; default Fuel |
| Face value | yes | Number; default 0 |
| Issued to (issue) | no | Text |
| Expires on (issue) | yes | Date |
| Supplier code (reconcile) | no | Text |
| Invoice number | no | Text |
| Invoiced amount | yes | Number; default 0 |
| Billed: SERIAL@yyyy-mm-dd; … | no | Text |
| Date | yes | Date |

The list beside the form shows: Outstanding allowance vouchers.

## Statements

### Supplier statement

The supplier account as we see it, as at a date.

| Field | Required | Notes |
| --- | --- | --- |
| Supplier | yes | Choice |
| As at | yes | Date |

The list beside the form shows: Open supplier items.

### Remittance advice

The remittance advice for a supplier payment (by its document number), as text to print or send, listing the invoices it settled.

| Field | Required | Notes |
| --- | --- | --- |
| Payment document number | yes | Text |

### Email a statement, remittance or order

Sends the document as an attachment to the contacts who receive it (else the primary contact). Remittance: the payment's open-item id; order: the purchase order id.

| Field | Required | Notes |
| --- | --- | --- |
| Document | yes | One of: Statement, Remittance advice, Purchase order; default Statement |
| Supplier code (statement) | no | Text |
| Statement date | yes | Date |
| Payment item / order id | no | Number |

### Capture a supplier statement

The supplier's closing balance and lines, for reconciliation against the ledger (report AP statement reconciliation).

| Field | Required | Notes |
| --- | --- | --- |
| Supplier | yes | Choice |
| Date | yes | Date |
| Closing balance | yes | Number |
| Lines: DOC:amount:yyyy-MM-dd; … | yes | Text |

### Supplier statement reconciliation

Compare the supplier's statement with your records.

### Withholding tax returns

The return of tax withheld from supplier payments in a period (by certificate), its remittance to the revenue authority, or the text of one certificate.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Return, Remit, Certificate; default Return |
| From | yes | Date |
| To | yes | Date |
| Remittance date | yes | Date |
| Bank account (remit) | no | Text |
| Certificate id | no | Number |

## Budget control

### Set a budget enforcement rule

What happens when spend overruns the available budget, by department, account prefix and value band (the most specific rule wins; none: allow).

| Field | Required | Notes |
| --- | --- | --- |
| Department (blank: any) | no | Text |
| Account prefix (blank: any) | no | Text |
| From value | yes | Number; default 0 |
| Action | yes | One of: Allow, Warn, RequireApproval, Block; default Allow |

The list beside the form shows: Budget enforcement rules.

### Check or reserve budget

Shows what is available on an account; tick Reserve to earmark it for known spend with no order yet (released by its reference).

| Field | Required | Notes |
| --- | --- | --- |
| Expense account | yes | Text |
| Department | no | Text |
| Amount | yes | Number |
| Date | yes | Date |
| Reserve it | no | Flag; default False |
| Reservation reference | no | Text |

The list beside the form shows: Open budget commitments.

### Reconcile budget commitments

Compares the commitment ledger with purchase orders: stale commitments, orders with none, and amounts that differ. Release a stale one by its reference.

| Field | Required | Notes |
| --- | --- | --- |
| Reference to release (optional) | no | Text |

The list beside the form shows: Commitment exceptions.

## Printed documents

### Print a purchase requisition

Prints the purchase requisition in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF.

| Field | Required | Notes |
| --- | --- | --- |
| Requisition number | yes | Text |
| PDF file path (when a layout is set) | no | Text |

### Print a goods received note

Prints the goods received note in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF.

| Field | Required | Notes |
| --- | --- | --- |
| GRN number | yes | Text |
| PDF file path (when a layout is set) | no | Text |

### Print a supplier debit note

Prints the supplier debit note in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF.

| Field | Required | Notes |
| --- | --- | --- |
| Debit note number | yes | Text |
| PDF file path (when a layout is set) | no | Text |

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| AP-AGE | Aged Creditors | Outstanding supplier balances by ageing band as at a date. |
| AP-DUE | Payments Due | Outstanding supplier invoices ordered by due date, with days until due (negative when overdue). |
| AP-RECUR | Recurring Purchases | Standing recurring supplier-invoice templates with amount, tax, interval, next run date and active flag. |
| AP-SUP | Supplier Register | Supplier master: code, name, currency, payment terms, on-hold flag and analysis codes. |
| AP51 | Supplier Statement / Remittance Statement | Our view of a supplier's account for confirmation: opening balance, every document in the period with running balance, closing balance and what is outstanding aged by due date. |
| AP53 | Supplier Ledger | One supplier's documents in date order with running balance and what is still outstanding on each. |
| AP54 | Purchase Day Book | Supplier invoices, credit notes and debit notes posted in the period, in transaction and base currency. |
| AP55 | Purchase Analysis by Supplier and Item | Order lines placed in the period (cancelled orders excluded) by supplier and item: quantity, average price and value. |
| AP56 | Cash Requirement Forecast / Payment Due Report | What must be paid to suppliers by week of due date and currency — overdue, each of the coming weeks and later — net of unallocated payments and debit notes. |
| AP58 | Payments Made | Payments to suppliers in the period (payment runs and single payments) with tax withheld. |
| AP59 | Goods Received Not Invoiced (GRNI) | Order lines received but not yet invoiced: the GRNI accrual by order and item, with its age. |
| AP60 | Invoice Matching Exceptions | Supplier invoices disputed against their order: reason, status and resolution. |
| AP61 | Supplier Statement Reconciliation | Each supplier's latest captured statement at the date against our ledger: both balances, items only they show, items only we show, and amounts that differ. |
| AP62 | Withholding Tax Deducted | Tax withheld from supplier payments in the period, by certificate, and whether it has been remitted. |
| AP63 | Supplier Performance Scorecard | Per supplier in the period: deliveries, on time, quantity received and returned, quality and spend. |
| AP64 | Supplier Balances (with debit balances) | What is owed to each supplier at the date; suppliers in debit (owing us) flagged, with hold and blacklist status. |
| AP65 | Supplier Bank Detail Change Audit | Every request to change a supplier's bank details: who asked, who reviewed, outcome (account numbers masked). |
| AP66 | Top Suppliers & Concentration | Suppliers ranked by net purchases in the period, with share and cumulative share. |
| AP67 | Supplier Advances & Prepayments | Every advance paid to a supplier in the period (or still held at its end): the purchase order it is for, what was applied to invoices, what the supplier refunded and the balance still held in the advances account. |
| PO124 | Requisition Approval Turnaround | Requisitions in the period with who approved them and how many days it took. |
| PO125 | Approval History / Authorisation Audit | Every workflow action in the period: document, stage, who, what and when, with the document value. |
| PO126 | Purchase Order Listing | Every order in the period with status, required-by date, overdue flag and the value still to be received. |
| PO128 | Committed Cost by Budget Line | Commitments raised against the budget, by account and department, with their status. |
| PO129 | Goods Received Listing & Variance | Goods received in the period against what was ordered, with quantity and price variances. |
| PO130 | Rejected Goods / Returns | Quality inspections in the period that rejected goods, with the reason and the value rejected. |
| PO131 | RFQ Comparison & Award | Every quote received on each RFQ, ranked by total price, with lead time and the award and its reason. |
| PO132 | Purchase Price Variance | Each item ordered in the period at a different price from the order before it, with the variance on the quantity ordered. |
| PO133 | Contract / Framework Agreement Utilisation | Each framework agreement: supplier, term, ceiling, orders called off and their value, how much is used and left, and whether it is live at the date. |
| PO134 | Procurement Spend Analysis | Order value in the period by supplier and currency, with lines, items and share. |
| PO135 | Supplier Lead Time Analysis | Per supplier: days from order to first receipt (average, fastest, slowest) and orders received by their required date. |
| PO136 | Budget Availability at Requisition | Open requisition lines charged to a budgeted expense account: the vote's appropriation, spent and committed to date, available before the line and after it. |
| PO137 | Approval Turnaround & SLA | Per document type and stage: decisions in the period, average and longest hours to decide, how many took longer than the stage's escalation time, and what is still waiting with the oldest wait. |
| POP-OPEN | Open Purchase Orders | Purchase orders not yet fully received/invoiced, with their order value. |
| POP-REQ | Purchase Requisitions | Requisitions with supplier, date, estimated value and status, newest first. |
