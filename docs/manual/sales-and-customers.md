# Sales & Customers

Customers, quotes and orders, invoices, receipts, statements and debt collection.

Works with: Customers.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Customers

### Add a customer

A customer account; raise the credit limit later through approval. An existing code changes that customer's name and analysis codes (the currency and limit stay). The three analysis codes (e.g. region, salesperson, sector) are how the account's transactions are analysed in reports.

| Field | Required | Notes |
| --- | --- | --- |
| Account code | yes | Text |
| Name | yes | Text |
| Currency | yes | Text; default USD |
| Credit limit | yes | Number; default 0 |
| Analysis code 1 | no | Text |
| Analysis code 2 | no | Text |
| Analysis code 3 | no | Text |

The list beside the form shows: Customers.

### Customer addresses and contacts

Postal, physical and delivery addresses (the default postal address prints on invoices, statements, orders, remittances and letters) and the people who receive statements, remittance advices and purchase orders by email. Remove takes an address or contact id. The list shows the trader typed in Code.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Add address, Add contact, Remove address, Remove contact; default Add address |
| Customer code | yes | Text |
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

### Credit limits

Lowers a customer's credit limit at once; an increase is requested with the reason and approved (or rejected) by another user. Policy: what an invoice over the limit does - Block (refused unless a supervisor overrides), Warn (posted, flagged and logged) or Ignore.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Lower, Request increase, Approve, Reject, Policy; default Lower |
| Customer | no | Choice |
| New limit | yes | Number; default 0 |
| Reason | no | Text |
| Request id | no | Number |
| Credit-limit policy | yes | One of: Block, Warn, Ignore; default Block |

The list beside the form shows: Increases awaiting approval.

### Customer ageing basis and group

How a customer's debts are aged (from the invoice date or the due date), and the parent account it belongs to for consolidated group statements (blank: none).

| Field | Required | Notes |
| --- | --- | --- |
| Customer | yes | Choice |
| Age by | yes | One of: InvoiceDate, DueDate; default InvoiceDate |
| Parent customer code (blank: none) | no | Text |

The list beside the form shows: Customers in groups or aged by invoice date.

### Customer exchange-rate type

The rate type this trader's foreign documents convert at (e.g. a contract or bank rate kept in the rate table). Daily (or blank) uses the daily rate; a document dated before any rate of the type falls back to the daily rate.

| Field | Required | Notes |
| --- | --- | --- |
| Customer code | yes | Text |
| Rate type | yes | One of: Spot, Daily, PeriodAverage, ClosingRate, Budget, Fixed; default Spot |

The list beside the form shows: Customers on their own rate.

## Quotes and orders

### Quotations

Quotes a customer (ITEM:quantity@price; …, valid until a date), records it sent, accepted or declined, and converts an accepted quotation into a sales order.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Sent, Accepted, Declined, Convert to order; default Create |
| Quotation number | yes | Text |
| Customer code (create) | no | Text |
| Lines: ITEM:quantity@price; … (create) | no | Text |
| Revenue account (create) | no | Text; default 4000 |
| Tax code (create) | no | Text |
| Valid until (create) | yes | Date |
| Sales order number (convert) | no | Text |
| Date | yes | Date |

The list beside the form shows: Quotations.

### Sales orders

Take an order (lines ITEM:quantity[:price[:tax code]]; no price takes the customer's price book), despatch it from a warehouse (stock issued at cost; lines ITEM:quantity, or blank for everything outstanding - a part despatch leaves the rest on back order), invoice what has been despatched (tax at each line's tax code), take back goods returned from a despatch, or cancel what is left of the order.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Despatch, Invoice, Return, Cancel; default Create |
| Order number | yes | Text |
| Date | yes | Date |
| Customer code (create) | no | Text |
| Lines (create: ITEM:qty[:price[:tax]]; despatch/return: ITEM:qty) | no | Text |
| Revenue account (create; blank: the item's) | no | Text |
| Warehouse code (despatch, return) | no | Text |
| Delivery / invoice number | no | Text |
| Vehicle (despatch) | no | Text |
| Driver (despatch) | no | Text |
| Output tax account (invoice) | no | Text |
| Reason (return, cancel) | no | Text |

The list beside the form shows: Open sales orders.

### Amend a sales order

Changes ordered quantities and prices as a new version of the order, keeping the earlier one with the reason. Quantities cannot go below what has been despatched or invoiced; invoiced prices cannot change.

| Field | Required | Notes |
| --- | --- | --- |
| Sales order number | yes | Text |
| Lines: ITEM:quantity:unit price; … | yes | Text |
| Reason | yes | Text |
| Date | yes | Date |

The list beside the form shows: Sales order amendments.

### Reserve stock for a sales order

Holds free stock in a warehouse for an order's lines still to despatch, first come first served; no other issue (a till sale, another order) can take it until the order despatches it. Release gives it back; cancelling the order releases it too.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Reserve, Release; default Reserve |
| Order number | yes | Text |
| Warehouse code (reserve) | no | Text |

The list beside the form shows: Stock reserved for orders.

### Pick list for a sales order

The pick list for what is still to despatch on a sales order from a warehouse, in bin order, as text to print.

| Field | Required | Notes |
| --- | --- | --- |
| Sales order number | yes | Text |
| Warehouse | yes | Choice |

### Drop-ship order lines

The supplier sends sales-order items straight to the customer. Untick 'Confirm' to place it; tick it (with the supplier's invoice) once delivered - cost of sales is charged and the lines become invoiceable, no stock moves.

| Field | Required | Notes |
| --- | --- | --- |
| Drop-shipment reference | yes | Text |
| Confirm delivered | no | Flag; default False |
| Sales order number (placing) | no | Text |
| Supplier code (placing) | no | Text |
| Lines: ITEM:quantity:unit cost; … (placing) | no | Text |
| Date | yes | Date |
| Supplier invoice (confirming) | no | Text |
| Cost of sales account | yes | Text; default 5000 |

The list beside the form shows: Drop-shipments.

### Create a customer contract

Agreed quantities at agreed prices for a period, drawn down by call-off orders.

| Field | Required | Notes |
| --- | --- | --- |
| Customer | yes | Choice |
| Contract number | yes | Text |
| From | yes | Date |
| To | yes | Date |
| Lines: ITEM:quantity:price; … | yes | Text |

### Call off against a contract

Raises a sales order at the contract prices; never past what remains on the contract.

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| Order number (blank = automatic) | no | Text |
| Date | yes | Date |
| Lines: ITEM:quantity; … | yes | Text |

### Sales discount limit

The largest discount off the price list a sales order may carry without authority. Beyond it the order needs an authoriser who may override discounts (Sales.Invoice), or the company's SalesDiscount workflow approves it (the order cannot be despatched until then). Blank: no limit.

| Field | Required | Notes |
| --- | --- | --- |
| Largest discount % without authority (blank: no limit) | no | Number |

The list beside the form shows: Orders waiting for discount approval.

### Lay-bys

Opens a lay-by (goods ITEM:quantity@price; … reserved against a deposit, paid off by a final date), takes payments (completed when paid up), cancels one (the forfeit % kept, the rest refunded) or defaults every overdue plan.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Open, Pay, Cancel, Default overdue; default Open |
| Plan number | no | Text |
| Customer code (open) | no | Text |
| Warehouse (open) | no | Text |
| Goods: ITEM:quantity@price; … (open) | no | Text |
| Deposit / payment | yes | Number; default 0 |
| Minimum deposit % | yes | Number; default 10 |
| Forfeit % on cancel | yes | Number; default 10 |
| Final payment date (open) | yes | Date |
| Bank / refund account | no | Text |
| Lay-by deposits account | no | Text |
| Revenue account | no | Text |
| Forfeiture income account | no | Text |
| Tax code the prices include (open) | no | Text |
| Date | yes | Date |

The list beside the form shows: Lay-by plans.

## Deliveries

### Confirm a delivery (proof of delivery)

Records who received a despatched delivery, when, and the signed POD reference.

| Field | Required | Notes |
| --- | --- | --- |
| Delivery reference | yes | Text |
| Date | yes | Date |
| Received by | yes | Text |
| POD reference | no | Text |

The list beside the form shows: Deliveries not yet confirmed.

### Delivery vehicles and loads

Register a vehicle (capacity in kg, optional m³ and drops a day, usual driver); put a delivery on a vehicle's run for a day at a stop (refused when it would overload the vehicle by weight or exceed its drops); or show a vehicle's load for a day in run order.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Assign, Load, Register vehicle; default Assign |
| Vehicle registration | yes | Text |
| Delivery reference (assign) | no | Text |
| Day | yes | Date |
| Route | no | Text |
| Driver | no | Text |
| Stop number (assign; blank: last) | no | Number |
| Description (register) | no | Text |
| Capacity kg (register) | no | Number |
| Capacity m³ (register) | no | Number |
| Most drops a day (register) | no | Number |

The list beside the form shows: Delivery vehicles.

### Charge for a delivery

Works out a delivery's charge from the customer's zone; tick 'Invoice' to bill it to the customer (once per delivery).

| Field | Required | Notes |
| --- | --- | --- |
| Delivery reference | yes | Text |
| Invoice it | no | Flag; default False |
| Invoice number | no | Text |
| Date | yes | Date |
| Delivery income account | yes | Text; default 4000 |
| Tax code | no | Text |
| Output tax account | no | Text |

### Delivery zone rates

A zone's charge per delivery plus per kg and per m³ of goods sent (from item weights and volumes), free above a goods value.

| Field | Required | Notes |
| --- | --- | --- |
| Zone | yes | Text |
| Charge per delivery | yes | Number; default 0 |
| Per kg | yes | Number; default 0 |
| Per m³ | yes | Number; default 0 |
| Free above goods value | no | Number |

The list beside the form shows: Delivery zone rates.

## Invoices and payments

### Customer invoice or credit note

Posts a sales invoice or credit note to a customer's account (the customer's currency unless another is given; tax at the tax code's account).

| Field | Required | Notes |
| --- | --- | --- |
| Document | yes | One of: Invoice, Credit note; default Invoice |
| Customer | yes | Choice |
| Document number | yes | Text |
| Date | yes | Date |
| Net amount | yes | Number |
| Tax amount | yes | Number; default 0 |
| Sales account | yes | Text |
| Tax account | no | Text |
| Tax code | no | Text |
| Currency (blank: the customer's) | no | Text |
| Reason code (credit note) | no | Text |
| Narrative | no | Text |
| Supervisor allowing it over the credit limit (user name) | no | Text |
| Cost centre | no | Text |
| Department | no | Text |
| Project | no | Text |

The list beside the form shows: Open customer items.

### Customer debit note

A further charge on an invoice already raised (under-billing, a price correction, a charge omitted): posted as a debit note that refers to the invoice.

| Field | Required | Notes |
| --- | --- | --- |
| Customer | yes | Choice |
| Debit note number | yes | Text |
| Invoice it adds to | yes | Text |
| Date | yes | Date |
| Net amount | yes | Number |
| Tax amount | yes | Number; default 0 |
| Sales account | yes | Text |
| Tax account | no | Text |
| Narrative | no | Text |

The list beside the form shows: Open customer items.

### Customer receipt (bank)

A payment received into the bank (EFT, deposit) on a customer's account; allocate it to invoices with auto-allocation or the allocation screens.

| Field | Required | Notes |
| --- | --- | --- |
| Customer | yes | Choice |
| Receipt number | yes | Text |
| Date | yes | Date |
| Amount | yes | Number |
| Bank account | yes | Text; default 1100 |
| Currency (blank: the customer's) | no | Text |
| Narrative | no | Text |

The list beside the form shows: Open customer items.

### Match payments to invoices

Match a receipt or credit note to an invoice (in part: give the amount; blank matches as much as both have open), undo an allocation, settle a customer invoice against the same party's supplier invoice (contra), or write off a bad debt claiming back the VAT on it once it is overdue long enough.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Allocate, Unallocate, Contra, Write off; default Allocate |
| Customer code | yes | Text |
| Invoice number | no | Text |
| Receipt / credit note number | no | Text |
| Amount (blank: as much as is open) | no | Number |
| Date | yes | Date |
| Allocation id (unallocate; blank: the latest on the document) | no | Number |
| Supplier code (contra) | no | Text |
| Supplier invoice number (contra) | no | Text |
| Contra clearing account | no | Text |
| Bad debt expense account (write off) | no | Text |
| Reason (write off / back) | no | Text |
| Overdue at least (days) for VAT relief | yes | Number; default 180 |
| Document number (contra) | no | Text |

The list beside the form shows: Open customer items.

### Settlement discount

An invoice paid early with a settlement discount: Suggest gives the customer's discount on the outstanding amount when paid within the customer's settlement days (else by the due date); Settle (a customer invoice) or Pay (a supplier invoice) posts the cash and the discount and settles the invoice. With a tax account, the invoice's tax share of the discount comes back off output (or input) tax.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Suggest, Settle, Pay; default Suggest |
| Invoice number | yes | Text |
| Cash received / paid | yes | Number; default 0 |
| Discount | yes | Number; default 0 |
| Bank account | no | Text |
| Discount allowed / received account | no | Text |
| Tax account (adjusts the tax on the discount) | no | Text |
| Receipt / payment number | no | Text |
| Date | yes | Date |

The list beside the form shows: Open customer items.

### Recurring sales invoices

An invoice to a customer raised every so many months from a first date; Generate raises all that are due; Stop / restart one.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Generate due, Stop, Restart; default Create |
| Customer code (create) | no | Text |
| Reference | no | Text |
| Net amount | yes | Number; default 0 |
| Tax amount | yes | Number; default 0 |
| Sales account | no | Text |
| Tax account | no | Text |
| Every (months) | yes | Number; default 1 |
| First run date | yes | Date |
| Narrative | no | Text |
| Generate as of | yes | Date |

The list beside the form shows: Recurring invoices.

## Prices and commission

### Price lists

Create a price list in a currency (one may be the default), set an item's price on it (with a minimum quantity for a quantity break), or put a customer on a list (blank list: back to the default).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Set price, Assign customer; default Create |
| Price list code | no | Text |
| Name (create) | no | Text |
| Currency (create; blank: base) | no | Text |
| The default list (create) | no | Flag; default False |
| Item code (set price) | no | Text |
| From quantity (set price) | no | Number |
| Unit price (set price) | no | Number |
| Customer code (assign) | no | Text |

The list beside the form shows: Price lists.

### Price a list from cost

Items with no price on the list are priced from cost (average, else standard): a markup on cost or a margin on price, rounded to a price point. Basis None turns it off.

| Field | Required | Notes |
| --- | --- | --- |
| Price list code | yes | Text |
| Basis | yes | One of: None, Markup, Margin; default None |
| Markup or margin % | yes | Number; default 0 |
| Price point | yes | Number; default 0.05 |
| Rounding | yes | One of: Nearest, Up, Down; default Nearest |

The list beside the form shows: Price lists.

### Future prices

Schedules a price from a date, or a percentage change of the whole list from a date (e.g. 1 July); orders and sales dated before it keep the earlier price. History lists an item's standing and dated prices.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Schedule price, Schedule list change, History; default Schedule price |
| Price list code | yes | Text |
| Product | no | Choice |
| From quantity | yes | Number; default 1 |
| Unit price | no | Number |
| Change % | no | Number |
| Price point | yes | Number; default 0.05 |
| Rounding | yes | One of: Nearest, Up, Down; default Nearest |
| Effective from | yes | Date |

The list beside the form shows: Price lists.

### Sales commissions

A sales rep's commission statement for a period (by customer, on the rep's basis: invoiced, collected or gross margin), or the month-end accrual of every rep's commission earned. Save a rep sets up or changes one; Assign customer puts a customer with a rep. Link employee makes a rep an employee; To payroll sends every linked rep's commission for From–To to the pay period's commission element (instead of accruing it).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Statement, Accrue, Save rep, Assign customer, Link employee, To payroll; default Statement |
| Sales rep code | no | Text |
| From | yes | Date |
| To / month end | yes | Date |
| Commission expense account (accrue) | no | Text |
| Commission payable account (accrue) | no | Text |
| Rep name (save rep) | no | Text |
| Commission basis | yes | One of: Invoiced, Collected, Margin; default Invoiced |
| Commission % | yes | Number; default 0 |
| Customer code (assign) | no | Text |
| Employee number (link) | no | Text |
| Pay period (to payroll) | no | Text |
| Commission element | no | Text; default COMMISSION |

The list beside the form shows: Sales reps.

## Statements and collections

### Customer statement

The customer's statement as at a date, ready to print or send: open-item, or balance-forward (brought forward, the period's documents with a running balance, carried forward) for customers set to that style or when a period start is given. Style sets the customer's statement style.

| Field | Required | Notes |
| --- | --- | --- |
| Customer | yes | Choice |
| As at | yes | Date |
| Balance-forward period from (yyyy-MM-dd; blank: the customer's style) | no | Text |
| Set the customer's style | yes | One of: (leave), OpenItem, BalanceForward; default (leave) |

The list beside the form shows: Open customer items.

### Email a customer statement

Sends the document as an attachment to the contacts who receive it (else the primary contact, else the customer's own email).

| Field | Required | Notes |
| --- | --- | --- |
| Customer code | yes | Text |
| Statement date | yes | Date |

### Email all customer statements

Queues a statement email to every customer owing at least a minimum balance at a date, with an email template and language; reports how many had no email address.

| Field | Required | Notes |
| --- | --- | --- |
| Statement date | yes | Date |
| Email template code | yes | Text |
| Language | yes | Text; default en |
| Smallest balance worth a statement | yes | Number; default 0 |

### Credit control

Who owes what, overdue accounts and what to do next.

### Customer documents

Invoices, statements and other documents sent to a customer.

### Dunning reminders

Lists the customers due a reminder by how long their oldest debt is overdue (first reminder, second, final notice - days configurable, or a dunning group's own; exempt groups are not chased), renders one customer's letter, or emails every letter due (not the same level again within the resend days). Groups and customer types are kept here too.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Assess, Letter, Send, Save group, Set customer; default Assess |
| Customer code (letter / set customer) | no | Text |
| First reminder after (days) | yes | Number; default 7 |
| Second after (days) | yes | Number; default 30 |
| Final notice after (days) | yes | Number; default 60 |
| As at | yes | Date |
| Email template (send) | no | Text; default REMINDER |
| Resend after (days) | yes | Number; default 14 |
| Dunning group code | no | Text |
| Group name (save group) | no | Text |
| Group is not chased | no | Flag; default False |
| Customer type (set customer) | no | Text |

### Debt collection cases

Hands a customer's overdue debt to a collector or lawyer (a case), records the summons, asks for the case to be written off (approved by another user), and closes it as settled or withdrawn.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Hand over, Summons, Request write-off, Approve write-off, Close; default Hand over |
| Customer code (hand over) | no | Text |
| Case number | no | Text |
| Handed over to / court | no | Text |
| Summons number | no | Text |
| Hearing date (yyyy-MM-dd) | no | Text |
| Reason (write-off) | no | Text |
| Bad debt account | no | Text |
| Outcome (close) | yes | One of: Settled, Withdrawn; default Settled |
| Date | yes | Date |

The list beside the form shows: Summons register.

### Promises to pay

Records a customer's promise to pay an amount by a date; Review shows, as at a date, which promises were kept, which were broken and how many are still open.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Record, Review; default Record |
| Customer | no | Choice |
| Amount | yes | Number; default 0 |
| Pay by | yes | Date |
| Note | no | Text |
| Made on / as at | yes | Date |

The list beside the form shows: Open promises.

### Interest on overdue accounts

Charges interest on overdue customer balances past the grace days at the annual rate (simple or compound, with a minimum charge), optionally exempting indigent customers. Tick "Save as the monthly policy" to keep these terms: the scheduled task ar.finance-charges (Administration › Scheduled jobs) then charges on them every month without anyone running it.

| Field | Required | Notes |
| --- | --- | --- |
| As at | yes | Date |
| Annual rate % | yes | Number |
| Grace days | yes | Number; default 30 |
| Minimum charge | yes | Number; default 0 |
| Interest income account | yes | Text |
| Document prefix | yes | Text; default FC |
| Compound | no | Flag; default False |
| Exempt indigent customers | no | Flag; default True |
| Save as the monthly policy | no | Flag; default False |
| Save the policy only (charge nothing now) | no | Flag; default False |

The list beside the form shows: Monthly finance-charge policy.

### Expected credit loss on debtors

Assesses the IFRS 9 loss allowance on trade debtors by arrears band (default provision matrix) against the allowance booked; Post books the difference.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Assess, Post; default Assess |
| As at | yes | Date |
| Loss allowance account | yes | Text |
| Impairment expense account (post) | no | Text |

## Printed documents

### Print a quotation

Prints the quotation in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF.

| Field | Required | Notes |
| --- | --- | --- |
| Quote number | yes | Text |
| PDF file path (when a layout is set) | no | Text |

### Print an order confirmation

Prints the order confirmation in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF.

| Field | Required | Notes |
| --- | --- | --- |
| Sales order number | yes | Text |
| PDF file path (when a layout is set) | no | Text |

### Print a delivery note

Prints the delivery note in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF.

| Field | Required | Notes |
| --- | --- | --- |
| Delivery reference | yes | Text |
| PDF file path (when a layout is set) | no | Text |

### Print a packing slip

Prints the packing slip in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF.

| Field | Required | Notes |
| --- | --- | --- |
| Delivery reference | yes | Text |
| PDF file path (when a layout is set) | no | Text |

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| AR-AGE | Aged Debtors | Outstanding customer balances by ageing band as at a date. |
| AR-CREDIT | Customer Credit Exposure | Each customer's credit limit, current balance and remaining credit, flagging on-hold and over-limit accounts. |
| AR-CUST | Customer Register | Customer master: code, name, currency, credit limit, payment terms, on-hold flag and analysis codes. |
| AR-RECUR | Recurring Invoices | Standing recurring-invoice templates with amount, tax, interval, next run date and active flag. |
| AR-STMT | Customer Statement | Outstanding items for a customer, with the closing balance. |
| AR28 | Customer Statement - Consolidated Group | Per group (parent account and its branches): each account's opening balance, debits and credits in the period and closing balance, with the group total. |
| AR29 | Aged Debtors Detail | Every open item as at the date with its age from due date (or invoice date for customers aged that way) and band. |
| AR30 | Aged Debtors by Sales Rep and Currency | Debtors aged as at the date, grouped by sales rep (or agent) and currency, in base. |
| AR31 | Customer Ledger | One customer's documents in date order with running balance and what is still outstanding on each. |
| AR33 | Sales Day Book | Invoices, credit notes and debit notes raised in the period, in transaction and base currency. |
| AR34 | Sales Analysis with Margin | Invoiced order lines and POS sales in the period by customer and item, against current average cost. |
| AR35 | Top Customers (Pareto) | Customers ranked by net sales in the period, with share and cumulative share. |
| AR36 | Customer Sales History & Trend | Per customer: net sales this year to date against the same period last year, and the last three months against the three before. |
| AR37 | Credit Notes & Returns Analysis | Credit notes raised in the period by reason code, with customer and narrative. |
| AR38 | Receipts / Collections Report | Customer receipts in the period by day, bank account (method), currency and the user who posted them. |
| AR39 | Unallocated Receipts, Credits & Advances | Receipts, credit notes and advances not yet allocated to invoices at the date, with how long they have been unallocated. |
| AR40 | Debtor Days (DSO) | Per customer: what is owed at the date against sales over the 90 days to it, as days of sales outstanding. |
| AR41 | Promise-to-Pay Diary | Promises recorded by credit control: amount, date promised and whether kept, broken or still open at the date. |
| AR42 | Dunning / Reminder Letters | Every customer with overdue debt at the date, at its reminder level (first, second, final), with the letter itself - overdue amount after unallocated credits and the oldest days overdue. |
| AR43 | Bad Debt Write-off Listing | Write-offs in the period with the customer, document and the debt case that approved it. |
| AR44 | Customer Deposits & Advance Payments Held | Money received from customers and not yet applied to an invoice at the date - deposits, advances and payments on account - with how long each has been held, in transaction currency and base. |
| AR45 | Lay-bye Status & Arrears | Every lay-bye plan with price, paid, balance and days to (or past) its final date. |
| AR46 | Sales Agent Commission Statement & Accrual Schedule | Per sales rep for the period: commission earned on each customer's base, the rep's total to accrue, optionally for one rep. |
| AR47 | Debtor Exposure by Currency | What customers owe at the date in each currency, in transaction and base, with each currency's share. |
| AR48 | New & Dormant Customers | Customers first invoiced in the period, and customers not invoiced for 90 days to its end. |
| SO137 | Open Sales Orders & Allocation | Open order lines with what is outstanding to despatch and the free stock that can cover it. |
| SO140 | Despatch / Delivery Performance (OTIF) | Orders due in the period: requested date, what was ordered and what was signed for by then - on time, in full and OTIF. |
| SO141 | Proof of Delivery Outstanding | Delivery notes despatched by the date that have not been signed for, oldest first. |
| SO142 | Delivery Schedule / Load Sheet | Deliveries planned in the period by date, vehicle and route: what is on each load and its weight. |
| SO143 | Quotation Conversion Rate | Quotations by month: count, converted to orders, value quoted, value converted and the conversion rate. |
| SOP-BO | Sales Back-Orders | Order lines not yet fully despatched, with the ordered, despatched and outstanding quantities. |
| SOP-QUOTE | Sales Quotations | Quotations with customer, dates, value and status, newest first. |
