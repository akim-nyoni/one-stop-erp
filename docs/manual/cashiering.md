# Cashiering

Taking payments at the counter, cashier sessions and the cash office.

Works with: Receipting.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Counter

### Take a payment (teller screen)

Receive a payment at the counter and print the receipt.

### Open or close my cashier session

Open a session at a station with the float, or close yours by declaring the cash counted (shortages and overs are posted). Type the amount, or count note by note: 50x10 + 20x5 + 1x7 is ten 50s, five 20s and seven 1s; each note or coin must be one of the currency's.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Open, Close; default Open |
| Station code (open) | no | Text |
| Currency | no | Text; default USD |
| Float (open) / cash counted (close) — an amount or notes, e.g. 50x10 + 20x5 | no | Text |
| Date | yes | Date |
| Cash account | no | Text; default 1000 |
| Cash shortages / overs account (close) | no | Text |

The list beside the form shows: Cashier sessions.

### Take a payment (form)

A payment on an account (customer code, or a stand number for its ratepayer), allocated oldest first unless chosen otherwise; or a sundry receipt or deposit to an account. Pick the currency the payer hands over (the company's currencies are listed); a foreign-currency payment converts at today's rate. Tick Preview to see the allocation without posting.

| Field | Required | Notes |
| --- | --- | --- |
| Payment for | yes | One of: Account, Sundry, Deposit; default Account |
| Customer code or stand number (account) | no | Text |
| Amount | yes | Number |
| Currency paid in | no | Choice |
| Tender | yes | One of: Cash, Card, MobileMoney, BankTransfer, Cheque; default Cash |
| Tender reference (card, cheque, mobile money) | no | Text |
| GL account (sundry / deposit) | no | Text |
| Narrative | no | Text |
| Allocate | yes | One of: OldestFirst, NewestFirst, LargestFirst; default OldestFirst |
| Why not oldest first (needed if you change Allocate) | no | Text |
| Date | yes | Date |
| Cash account | no | Text; default 1000 |
| Preview only | no | Flag; default False |

The list beside the form shows: My receipts today.

### Reverse a receipt

Reverses a receipt posted in error (the allocations and journal are undone); a supervisor may be required by your permissions.

| Field | Required | Notes |
| --- | --- | --- |
| Receipt number | yes | Text |
| Reason | yes | Text |
| Date | yes | Date |

The list beside the form shows: My receipts today.

## Cash office

### Cash office drawer

During a session: a pickup moves surplus cash from the drawer to the safe; a petty cash payout pays an expense from the drawer; a supervisor verifies another cashier's closed session; a receipt is reprinted (marked as a copy, counted); a cheque or transfer awaiting clearance is confirmed into the bank.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Pickup, Petty cash payout, Verify session, Reprint receipt, Confirm cleared tender; default Pickup |
| Amount | yes | Number; default 0 |
| Cash (drawer) account | no | Text; default 1000 |
| Safe account (pickup) | no | Text |
| Expense account (petty cash) | no | Text |
| Bank account (cleared tender) | no | Text |
| Reason | no | Text |
| Session id (verify) | no | Number |
| Receipt number (reprint) | no | Text |
| Pending tender id (confirm) | no | Number |
| Date | yes | Date |

The list beside the form shows: Payments awaiting clearance.

### Bank the takings

Takes cash to the bank (cash to cash-in-transit), confirms a deposit as banked (in transit to the bank account), moves money between accounts, and posts bank charges or interest received from the statement.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Bank cash, Confirm banked, Transfer, Bank charge, Interest received; default Bank cash |
| Amount | yes | Number; default 0 |
| Reference / deposit slip | no | Text |
| Cash / from account | no | Text |
| Cash-in-transit account | no | Text |
| Bank / to account | no | Text |
| Charges or interest income account | no | Text |
| Date | yes | Date |

The list beside the form shows: Cash deposits.

## Accounts

### Arrangements, dishonours and refunds

A payment arrangement on a debtor's account (total and monthly instalment from a start date); a dishonoured cheque or reversed transfer, with an optional penalty charged; money held in suspense allocated to an account; and a credit balance refunded to the debtor (approved by the user doing it).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Arrangement, Dishonour, Allocate from suspense, Refund credit; default Arrangement |
| Customer code or stand number | no | Text |
| Amount / total | yes | Number; default 0 |
| Monthly instalment (arrangement) | yes | Number; default 0 |
| Receipt number (dishonour) | no | Text |
| Penalty (dishonour) | yes | Number; default 0 |
| Penalty income account | no | Text |
| Suspense account | no | Text |
| Allocate to | yes | One of: OldestFirst, NewestFirst, LargestFirst, UserDirected; default OldestFirst |
| Bank account (refund) | no | Text |
| Reason | no | Text |
| Date | yes | Date |

The list beside the form shows: Payment arrangements.

## Setup

### Counters and field collectors

A counter (station) cashiers open their sessions at, or a field collector's handheld for rent, market stalls, parking and pound fees: tick Field collector, give the route and the pre-numbered receipt range issued to the collector (receipts stop at the end of the range until a new one is issued). The collector opens a session like a cashier and hands the cash back at close; RC47 shows each collector's day and RC48 the routes.

| Field | Required | Notes |
| --- | --- | --- |
| Station code | yes | Text |
| Name | yes | Text |
| Receipt prefix | yes | Text; default RC |
| Field collector | no | Flag; default False |
| Route (field collector) | no | Text |
| Receipt range from (field collector) | no | Number |
| Receipt range to | no | Number |
| Active | no | Flag; default True |

The list beside the form shows: Stations and collectors.

### Collection agents

Third-party agents who collect from customers for a commission: register one, record a collection (allocated to the customer's debt, owed by the agent) and take the agent's remittance (commission kept, the net banked).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Register, Collection, Remittance; default Register |
| Agent code | yes | Text |
| Name (register) | no | Text |
| Commission % | yes | Number; default 0 |
| Customer code or stand (collection) | no | Text |
| Amount | yes | Number; default 0 |
| Agent control account | no | Text |
| Bank account (remittance) | no | Text |
| Commission expense account (remittance) | no | Text |
| Date | yes | Date |

The list beside the form shows: Agents.

### Manual receipt books

Pre-numbered manual receipt books for when the system is down: issue a book (serial range) to a cashier, capture a manual receipt written from it later, and list the serials still unused (every serial must be accounted for).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Issue, Capture, Unused serials; default Issue |
| Book number | yes | Text |
| First serial (issue) | no | Number |
| Last serial (issue) | no | Number |
| Issued to (user name; blank: me) | no | Text |
| Serial (capture) | no | Number |
| Amount (capture) | yes | Number; default 0 |
| Issue / receipt date | yes | Date |

The list beside the form shows: Receipt books.

### Cashiers' own and related accounts

The accounts a cashier may not take payments on — their own, a spouse's or relative's, a business they have an interest in. Another cashier must take those payments; an attempt is refused and shows on RC23.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Add, Remove; default Add |
| Cashier (user name) | yes | Text |
| Account (customer code) | yes | Text |
| Relationship | yes | One of: Own, Spouse, Relative, Business; default Own |

The list beside the form shows: Accounts cashiers may not receipt.

### Import receipts from a file

Receipts a bank statement, mobile money, card settlement, employer or agent file (CSV: payer reference, payer name, amount, value date): lines matching one account are receipted to it, the rest go to suspense; the same file cannot be imported twice. Matching tries your matching rules first, then the account code (spaces and dashes ignored), then a stand number (its ratepayer), then a code anywhere in the reference.

| Field | Required | Notes |
| --- | --- | --- |
| CSV file path | yes | Text |
| Source | yes | One of: BankStatement, MobileMoney, CardSettlement, EmployerSchedule, AgentCollection; default BankStatement |
| Bank account | no | Text |
| Suspense account | no | Text |
| Date | yes | Date |

The list beside the form shows: Import batches.

### Matching rules for imported payments

Your own rules for matching imported payments: when the reference or the payer's name contains, starts with or equals the text (or fits a regular expression), the payment goes to the account. E.g. payer name contains "MINISTRY OF HEALTH" → the employer's account.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Add, Remove; default Add |
| Look in | yes | One of: Reference, PayerName; default Reference |
| When it | yes | One of: Contains, StartsWith, Equals, Pattern; default Contains |
| Text or pattern | no | Text |
| Account (customer code) | no | Text |
| Priority (lowest first) | yes | Number; default 100 |
| Rule id (remove) | no | Number |

The list beside the form shows: Matching rules (tried first, lowest priority first).

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| RC1 | Cashier Cash-Up / Session Close | Expected vs counted per currency and denomination, variance and supervisor sign-off. |
| RC10 | Hourly Transaction Volume & Queue Analysis | Transactions per hour per station, average handling time, peak periods. |
| RC11 | Cashier Productivity | Receipts processed, value, average handling time, variances, cancellations, reprints. |
| RC12 | Receipting-to-GL Reconciliation | Total collected = debtor control movement + sundry income + deposit movement + suspense movement. |
| RC13 | Cash Control Account Reconciliation | Till → safe → cash in transit → bank; supply the account codes for each stage. |
| RC14 | Till Variance Report | Shortages and overages by cashier and period, with recovery status. |
| RC15 | Cancelled & Reversed Receipts | With reason, authorising supervisor and value. |
| RC16 | Receipt Reprint Log | Every reprint, watermarked COPY, numbered and logged. |
| RC17 | Receipt Number Gap Analysis | Per station — must show no gaps. |
| RC18 | Back-Dated Receipt Report | Receipts posted with a date before the transaction date. |
| RC19 | Allocation Override Report | Where a cashier directed money against the default policy, with the reason. |
| RC2 | X-Read / Z-Read | Mid-shift non-resetting (X) and session-close immutable (Z) totals by tender and currency. |
| RC20 | Manual / Pre-Numbered Stationery Control | Books issued, serials used, serials unaccounted, books unreturned. |
| RC21 | Receipting Audit Trail | Every receipt, cancellation, allocation, suspense movement and refund — before/after values, user, station. |
| RC22 | Segregation of Duties Violations — Receipting | Blocked or granted SoD-conflicting actions. |
| RC23 | Receipts Against Related-Party Accounts | Blocked attempts and any granted exceptions. |
| RC24 | Banking / Deposit Slip | Per bank account and currency. |
| RC25 | Daily Banking Summary | Collected vs handed to treasury vs banked vs confirmed on statement. |
| RC26 | Cash in Transit Ageing | Anything unbanked or unconfirmed beyond N days. |
| RC27 | Unbanked Collections Report | Takings not yet moved into cash-in-transit. |
| RC28 | Pickups, Drops & Petty Cash Pay-outs | Mid-shift cash movements with reason. |
| RC29 | Unallocated / Suspense Receipts Register | With ageing — the report that recovers lost money. |
| RC3 | Daily Collection Summary | Collections by station, cashier, tender type, currency and revenue head. |
| RC30 | Suspense Allocation Report | What was allocated out, by whom, to which account, with the reason. |
| RC31 | Bulk Import Batch Report | File, records, matched, unmatched, duplicates rejected, total value. |
| RC32 | Import Matching Exceptions | Unmatched references, ambiguous matches, amount mismatches. |
| RC33 | Pending Tenders Report | Claimed deposits and transfers not yet confirmed on the bank statement, aged. |
| RC34 | Dishonoured Cheque / Reversed Payment Register | With penalties raised and recovery status. |
| RC35 | Official Receipt | Thermal / A5 / A4 / multi-part — allocation by service, balance after payment, currency and rate. |
| RC36 | Counter Mini-Statement | Last N transactions and the current balance — printed on request. |
| RC37 | Full Account Statement | The AR / municipal / housing / school statement, printable from the counter. |
| RC38 | Payment History per Account | All receipts, allocations and reversals for one payer. |
| RC39 | Credit Balances / Overpayments | Residents in credit, with ageing — a liability that must be reported. |
| RC4 | Receipt Listing / Cashier Day Book | Every receipt with payer, amount, tender and allocation. |
| RC40 | Deposits Held Register | By payer, type, date received, refundable amount. |
| RC41 | Refunds Register | Requested, approved, paid, outstanding — with turnaround times. |
| RC42 | Payment Arrangements Captured at Counter | New arrangements, honoured, defaulted. |
| RC43 | Top Payers / Largest Receipts | For review, and for spotting structuring. |
| RC44 | Accounts With No Payment in N Months | Feeds credit control and disconnection lists. |
| RC45 | Agent Collection Reconciliation | Collected, remitted, outstanding, commission due, per agent. |
| RC46 | Agent Remittance Ageing | Money held by agents and not remitted. |
| RC47 | Field Collector Daily Reconciliation | Receipts issued, value, cash returned, variance, per collector. |
| RC48 | Field Collection Route Performance | Collections by route. |
| RC49 | Collection Efficiency | Billed vs collected, by service, ward and month. |
| RC5 | Collection by Revenue Vote | Sundry income analysed to the GL vote. |
| RC50 | Collection vs Revenue Budget | Against the §4.18 revenue budget, by vote and department. |
| RC51 | Collection Trend & Seasonality | Daily, weekly, monthly, year-on-year. |
| RC52 | Prepaid Token Sales & Credit-off-Arrears Recovery | Token value issued and the arrears portion recovered. |
| RC53 | Receipting Executive Dashboard | Today's collections by channel and service, cash unbanked, suspense pool and its age, variance trend, queue load. |
| RC6 | Collection by Service | What residents paid for — water, rates, refuse, sewer, licences, fees. |
| RC7 | Collection by Tender Type & Channel | Counter cash, card, mobile money, bank, agent, field. |
| RC8 | Collection by Currency | Amounts by currency with the rates applied and the realised FX effect. |
| RC9 | Collection by Ward / Zone | Collections by ward, zone, suburb, class or scheme. |
