# Accounting

Journals, the chart of accounts, tax, currencies and closing the books.

Works with: General Ledger.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Journals

### Enter a journal

Enter the lines on the grid (account, debit or credit, cost centre, tax code for the VAT return, line narrative); the totals show when it balances. Or type them separated by ';', each ACCOUNT Dr|Cr AMOUNT with optional cc=, dept=, project=, fund=, branch=, tax=, cur= and rate= (a line in another currency at its rate) and a narrative after '#'; e.g. "6100 Dr 500 project=P-DAM fund=WATER # Contractor; 2410 Cr 500 fund=WATER". It is validated and numbered now, and posts once someone else approves it; an accrual names the date it reverses on.

| Field | Required | Notes |
| --- | --- | --- |
| Posting date | yes | Date |
| Narrative | yes | Text |
| Reference | no | Text |
| Journal lines | yes | Text |
| Currency (blank: base) | no | Text |
| Reverse on (blank: not reversed) | no | Date |

The list beside the form shows: Journals awaiting approval.

### Reverse a journal

Corrects a posted manual journal by posting its exact inverse on the date given, with the reason kept on it; the original stays as it was. Only general ledger journals are reversed here: an invoice, receipt or payment is reversed through its own document.

| Field | Required | Notes |
| --- | --- | --- |
| Posted manual journal | yes | Choice |
| Reversal date | yes | Date |
| Reason | yes | Text |

The list beside the form shows: Manual journals posted.

### Journals awaiting approval

Approve or reject journals others have entered.

### Accounting vouchers

Payment, receipt, journal and petty cash vouchers: prepared by one user (lines ACCOUNT:amount; … - for a journal, debits positive and credits negative), checked by a second, approved and posted by another (or through the approvals inbox when an AccountingVoucher workflow is defined), cancelled while a draft, and printed with prepared / checked / approved signature blocks. Supporting documents attach under Attachments (target AccountingVoucher, key the voucher number).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Prepare, Check, Approve, Cancel, Print; default Prepare |
| Type (prepare) | yes | One of: Payment, Receipt, Journal, PettyCash; default Payment |
| Voucher number | no | Text |
| Paid to / received from / about (prepare) | no | Text |
| Cash or bank account (not a journal) | no | Text |
| Lines: ACCOUNT:amount[:narrative[:cost centre[:department[:project[:tax code]]]]]; … (a taxed line is gross; its VAT is split out) | no | Text |
| Narrative | no | Text |
| Date | yes | Date |

The list beside the form shows: Vouchers.

### Accruals and prepayments

An accrual (an expense incurred, not yet invoiced) or a prepayment (paid ahead), posted on the date and reversed automatically on the reversal date. A prepayment schedule releases a prepayment to expense over the months it covers (from the month of the date, the document number as its reference); Release prepayments charges every month ended by the date (the scheduled task gl.prepayment-release does it daily). Tick Send for approval to hold an accrual or prepayment for someone else to approve (Accounting > Journals > Journals awaiting approval); it posts, and its reversal is booked, when approved.

| Field | Required | Notes |
| --- | --- | --- |
| Kind | yes | One of: Accrual, Prepayment, Prepayment schedule, Release prepayments; default Accrual |
| Date | yes | Date |
| Reverses on (blank: 1st of next month) | no | Date |
| Expense account | yes | Text |
| Accrual / prepayment account | yes | Text |
| Amount | yes | Number |
| Document number | yes | Text |
| Narrative | yes | Text |
| Months covered (schedule) | yes | Number; default 12 |
| Send for approval (accrual / prepayment) | no | Flag; default False |

### Recurring journals

Create: a journal posted again on a schedule ("6100 Dr 500 cc=ADM; 2100 Cr 500"): monthly, quarterly, half-yearly or yearly, from a start month to an end date or for a number of times, in a currency, and reversed on the first of the next month when it is an accrual. Schedule: change those on one. Post: one of them on a date. Post all: every one due. Stop / restart one. Create with an existing code changes its schedule (its lines stay); Delete stops it.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Schedule, Post, Post all, Stop, Restart; default Create |
| Code | no | Text |
| Description | no | Text |
| Journal lines (create) | no | Text |
| Narrative | no | Text |
| Frequency | yes | One of: Monthly, Quarterly, Half-yearly, Yearly; default Monthly |
| Start month (yyyy-MM, blank: any) | no | Text |
| End date (blank: none) | no | Date |
| Post at most this many times (blank: no limit) | no | Number |
| Auto-reverse next month | no | Flag; default False |
| Currency (blank: base) | no | Text |
| Document number (post) | no | Text |
| Date | yes | Date |

The list beside the form shows: Recurring journals.

### Import journals from a file

Imports manual journals from a CSV file or an Excel workbook (its first sheet), all or nothing (a file with any problem imports nothing); journals over the approval threshold wait for a second user's approval instead of posting.

| Field | Required | Notes |
| --- | --- | --- |
| CSV or .xlsx file path | yes | Text |

### Consolidated statements

The group's consolidated profit or loss and financial position at a date: the companies added up, intercompany balances and intercompany sales and purchases eliminated, the parent's investment set against the subsidiaries' share capital (goodwill for any excess), and the non-controlling interest's share of each partly owned subsidiary (Ownership: COMPANY:percent; ...).

| Field | Required | Notes |
| --- | --- | --- |
| Group companies (codes, comma separated; the parent first) | yes | Text |
| As at | yes | Date |
| Intercompany balance accounts (comma separated) | no | Text |
| Intercompany sales accounts | no | Text |
| Intercompany purchase accounts | no | Text |
| Parent's investment account | no | Text |
| Subsidiaries' share capital accounts | no | Text |
| Ownership: COMPANY:percent; ... | no | Text |

### Intercompany journal

Posts both sides of an intercompany charge at once: in this company against its intercompany account, and in the other company against its own (each in its own base currency, at the day's rate, when the charge is in another currency). You must be allowed into both companies.

| Field | Required | Notes |
| --- | --- | --- |
| Other company code | yes | Text |
| Amount | yes | Number |
| Currency (blank: this company's base) | no | Text |
| Document number | yes | Text |
| Narrative | no | Text |
| This company's account | yes | Text |
| This company's intercompany account | yes | Text |
| Other company's account | yes | Text |
| Other company's intercompany account | yes | Text |
| Date | yes | Date |

### Deferred posting

The Sage 200 deferred-posting option: name the source modules (AR, AP, POS, INV, PAY, CB …) whose postings queue as deferred journals instead of reaching the nominal ledger at once; sub-ledgers update immediately either way. Queued journals are listed by SYS245 (Unposted & Deferred Batches) and posted in a batch from Period close › Release deferred journals. Blank posts everything straight away.

| Field | Required | Notes |
| --- | --- | --- |
| Source modules queued (comma-separated; blank: none) | no | Text |

The list beside the form shows: Deferred posting.

### Petty cash

Disburse: a petty cash payment to an expense account. Top up: reimburses the float back to its fixed amount from the bank (imprest).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Disburse, Top up float; default Disburse |
| Amount / float amount | yes | Number |
| Petty cash account | yes | Text |
| Expense account (disburse) | no | Text |
| Bank account (top up) | no | Text |
| Document number | yes | Text |
| Date | yes | Date |

### Petty cash funds

Imprest funds: Save fund (code, float, petty cash and bank accounts, over/short account, custodian); Pay out from a fund, claiming the recoverable input tax on the receipt when a tax code is given; Top up reimburses the fund to its float; Count records the cash counted against the book; Reconcile posts the count's variance and tops the float up; List shows every fund.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: List, Save fund, Pay out, Top up, Count, Reconcile; default List |
| Fund code | yes | Text |
| Fund name (save) | no | Text |
| Float / amount paid / cash counted | no | Number |
| Petty cash account (save) | no | Text |
| Bank account (save) | no | Text |
| Over/short account (save) | no | Text |
| Custodian username (save) | no | Text |
| Expense account (pay out) | no | Text |
| Tax code on the receipt (pay out) | no | Text |
| Payee (pay out) | no | Text |
| Narrative / note | no | Text |
| Denominations counted, e.g. 100x2, 20x5 (count) | no | Text |
| Count id (reconcile) | no | Number |
| Document number | no | Text |
| Date | yes | Date |

## Look up

### Account enquiry

An account's postings and balance, and the document behind each one.

### Analytics dashboard

Key figures and trends at a glance.

### Group consolidation

Combined accounts of the group's companies.

## Chart of accounts

### Add an account

A ledger account; a control account is posted only by its own ledger. An existing code changes that account (and puts it back in use); its type and control account change only while nothing has been posted to it. Delete takes an account out of use when it has no balance and is not a control account.

| Field | Required | Notes |
| --- | --- | --- |
| Account code (up to 8) | yes | Text |
| Name | yes | Text |
| Type | yes | One of: Asset, Liability, Equity, Income, Expense; default Asset |
| Control account | yes | One of: (not a control account), None, AccountsReceivable, AccountsPayable, Stock, VatInput, VatOutput, Bank, RetainedEarnings, FxGain, FxLoss, Suspense, WorkInProgress, Retention, GoodsReceivedNotInvoiced, Rounding, PayrollControl; default (not a control account) |
| Report category | no | Text |
| Default tax code | no | Text |
| Manual journals allowed (untick for an account the system alone posts) | no | Flag; default True |
| Monetary item (revalued at closing rates when currency-restricted) | no | Flag; default True |
| Equity reserve whose movement is other comprehensive income | no | Flag; default False |

The list beside the form shows: Chart of accounts.

### Add a cost centre or department

GL analysis codes of up to three characters. An existing code of the kind changes its name (and puts it back in use); Delete takes one out of use, keeping what was posted to it.

| Field | Required | Notes |
| --- | --- | --- |
| Kind | yes | One of: Cost centre, Department; default Cost centre |
| Code (up to 3) | yes | Text |
| Name | yes | Text |

The list beside the form shows: Cost centres and departments.

### Funds, projects and fund accounting

Add or change a fund (vote) or a project (closing a project stops postings to it), or turn fund accounting on or off: then every journal line names a fund - the default fund fills sub-ledger postings - and each fund must balance within each journal. An existing code changes that fund or project; Delete closes it (postings to it are kept).

| Field | Required | Notes |
| --- | --- | --- |
| What | yes | One of: Fund, Project, Fund accounting on, Fund accounting off; default Fund |
| Code (default fund for 'on') | no | Text |
| Name | no | Text |
| Restricted fund | no | Flag; default False |
| Project's fund | no | Text |
| Inactive / closed | no | Flag; default False |

The list beside the form shows: Funds and projects.

### Reporting trees

Statement layouts independent of account codes (statutory, management, consolidation): create a tree (optionally started from the chart's types and categories), add headings under parents, map account ranges, check every account is mapped once, and show the rolled-up statement (Statement by Reporting Tree, GL-TREE, prints it). Create with an existing tree code renames it and changes its purpose; a heading saved again with its code is changed.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Create from chart, Heading, Map accounts, Check, Show statement; default Create |
| Tree code | yes | Text |
| Tree / heading name | no | Text |
| Purpose | yes | One of: Statutory, Management, Consolidation; default Statutory |
| Heading code | no | Text |
| Parent heading | no | Text |
| Order | yes | Number; default 10 |
| Show credit-positive | no | Flag; default False |
| From account | no | Text |
| To account | no | Text |
| From date (blank: balance) | no | Date |
| To date | yes | Date |

The list beside the form shows: Reporting trees.

## Tax

### Add a tax code

A tax code with its first rate and the accounts it posts to; what it applies to (a goods-only code is refused on a service line and the other way round), its jurisdiction and the tax-return box it reports in (TX226 VAT return by box). An existing code changes that tax code (and puts it back in use): a different rate is added from the date given, so earlier documents keep the rate they were taxed at; the type stays. Delete takes a code out of use.

| Field | Required | Notes |
| --- | --- | --- |
| Code | yes | Text |
| Name | yes | Text |
| Type | yes | One of: Standard, Zero, Exempt, OutOfScope, ReverseCharge, Withholding, Deemed; default Standard |
| Rate % | yes | Number |
| Rate from | yes | Date |
| Output / payable account | no | Text |
| Input account | no | Text |
| Applies to | yes | One of: Both, Goods, Services; default Both |
| Jurisdiction | no | Text |
| Return box | no | Text |

The list beside the form shows: Tax codes.

### VAT return

The VAT due for a period on the company's basis - invoice date (the ledger), tax point (deliveries move invoices between periods), or cash accounting (invoice VAT due as it is paid) - with every adjustment off the ledger listed. Tick 'Make it the company's basis' to change the basis used from now on.

| Field | Required | Notes |
| --- | --- | --- |
| From | no | Date |
| To | yes | Date |
| Basis | yes | One of: InvoiceDate, TaxPoint, Cash; default InvoiceDate |
| Make it the company's basis | no | Flag; default False |

The list beside the form shows: VAT returns filed.

### Pay or settle VAT

Clears the period's output and input VAT accounts into the VAT payable (or refundable) account and shows output, input and the net. On cash accounting or the tax point, VAT the ledger holds that is not yet due goes to the not-yet-due account (and comes back out when it falls due). The period is then filed: it cannot be settled again and no VAT posts on its days. Left blank, the period runs from the day after the last return filed (or the start of last month) to the end of that month, and the document number is VAT-yyyyMM.

| Field | Required | Notes |
| --- | --- | --- |
| From (blank: day after the last return) | no | Date |
| To (blank: end of that month) | no | Date |
| VAT payable account | yes | Text |
| Document number (blank: VAT-yyyyMM) | no | Text |
| VAT not yet due account (cash / tax point) | no | Text |

The list beside the form shows: VAT returns filed.

### Tax exemption certificates

Record a customer's or supplier's exemption certificate (while valid no tax is charged either way; an exempt tax code needs one), or a supplier's tax clearance (while valid no withholding is deducted from payments), or withdraw one by its id (Delete on the list withdraws it from today). A renewed certificate is recorded as a new one, so the history of each stays. Set default withholding also sets the yearly threshold (1,000 in base currency by default): a supplier without clearance has tax withheld from the payment that brings the year's payments to it to the threshold, and from every later one that year.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Record, Withdraw, Set default withholding; default Record |
| Who | yes | One of: Customer, Supplier; default Customer |
| For | yes | One of: Tax exemption, Tax clearance (supplier); default Tax exemption |
| Customer / supplier code | no | Text |
| Certificate number | no | Text |
| Valid from | yes | Date |
| Valid to | no | Date |
| Issued by | no | Text |
| Certificate id (withdraw) | no | Number |
| Withholding code for suppliers without clearance (blank: none) | no | Text |
| Withhold once the year's payments to a supplier reach (base currency; 0: every payment; blank: keep) | no | Number |

The list beside the form shows: Certificates expiring within 30 days (or lately expired).

## Currencies

### Add a currency

An ISO currency the system may trade or hold balances in. An existing code changes its name and symbol (its decimal places only while nothing has been posted in it); a deleted currency with the code is brought back. Delete is refused for a company's base or reporting currency and for one in use.

| Field | Required | Notes |
| --- | --- | --- |
| ISO code | yes | Text |
| Name | yes | Text |
| Symbol | no | Text |
| Decimal places | yes | Number; default 2 |

The list beside the form shows: Currencies.

### Set an exchange rate

A rate from a date; one already set for that day and type is replaced.

| Field | Required | Notes |
| --- | --- | --- |
| From currency | yes | Text |
| To currency | yes | Text |
| Rate type | yes | One of: Spot, Daily, PeriodAverage, ClosingRate, Budget, Fixed; default Spot |
| Date | yes | Date |
| Rate | yes | Number |

The list beside the form shows: Exchange rates (latest 200).

### Exchange-rate policy

Which rate type a document type (SalesInvoice, Receipt, SalesCreditNote, PurchaseInvoice, PurchaseCreditNote, Payment) or a whole ledger (AR, AP) uses when no rate is keyed. A trader's own rate type still comes first; with no policy the daily rate is used. Delete on the list puts a document type back on the default rate.

| Field | Required | Notes |
| --- | --- | --- |
| Document type or ledger | yes | One of: SalesInvoice, Receipt, SalesCreditNote, PurchaseInvoice, PurchaseCreditNote, Payment, AR, AP; default SalesInvoice |
| Rate type | yes | One of: Spot, Daily, PeriodAverage, ClosingRate, Budget, Fixed; default Spot |

The list beside the form shows: Rate policies.

### Revalue foreign currency balances

Revalues the foreign-currency balances (banks, debtors, creditors, loans) at the closing rate on the date, posting the differences to FX gain / loss and, if ticked, reversing them on the next day.

| Field | Required | Notes |
| --- | --- | --- |
| As at | yes | Date |
| Reverse next day | no | Flag; default True |

### Set the reporting currency

A second currency every posting is also kept in (e.g. USD beside ZWG), each at the base→reporting rate of its day, so statements can be drawn in it (General Ledger report GL2R). It applies to postings from now on - posted journals cannot change - so set it before go-live. Leave blank to stop keeping it.

| Field | Required | Notes |
| --- | --- | --- |
| Reporting currency (blank: none) | no | Text |

The list beside the form shows: Company currencies.

### Hyperinflation (IAS 29)

Index: records a price index value for a month. Restate: the trial balance restated in current terms by the index - non-monetary items moved by the index change since they arose - with the gain or loss on the net monetary position.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Index, Restate; default Index |
| Index code | yes | Text; default CPI |
| Month / as at | yes | Date |
| Index value (index) | yes | Number; default 0 |

## Period end

### Period close checklist

Everything to do before a month is closed, ticked off.

### Close or reopen a period

Sets one period's status for one posting module (GL, AR, AP, INV, POS…): Closed stops postings (a soft close can be reopened), PermanentlyClosed can never take another posting (draft or deferred journals dated in it must be posted or deleted first), Open reopens it.

| Field | Required | Notes |
| --- | --- | --- |
| Fiscal year code | yes | Text |
| Period number | yes | Number; default 1 |
| Posting module | yes | Text; default GL |
| Status | yes | One of: Future, Open, Closed, PermanentlyClosed; default Future |

The list beside the form shows: Periods of the latest fiscal year.

### Create a fiscal year

Monthly periods from the start date (any month - e.g. July to June), or a week calendar: 4-4-5, 4-5-4 or 5-4-4 weeks a quarter, or 13 periods of 4 weeks, with a 53rd week in the last period when the year needs one (the year-end adds it on its own when the calendar would drift from its year-end date); optionally an adjustment period on the last day for audit and year-end adjustments. Tick to open the periods for every ledger at once.

| Field | Required | Notes |
| --- | --- | --- |
| Year code | yes | Text |
| First day | yes | Date |
| Periods (monthly) | yes | Number; default 12 |
| Calendar | yes | One of: Monthly, 4-4-5 weeks, 4-5-4 weeks, 5-4-4 weeks, 13 x 4 weeks; default Monthly |
| 53 weeks (week calendars) | no | Flag; default False |
| Add an adjustment period | no | Flag; default False |
| Open all periods now | no | Flag; default False |

The list beside the form shows: Fiscal years.

### Recognise deferred income

Recognises the income earned by the date on every deferred income schedule (fees billed in advance, annual licences) and shows each schedule's share.

| Field | Required | Notes |
| --- | --- | --- |
| As at | yes | Date |

### Overhead allocation

Rules that recharge a cost centre's overheads to others by fixed %, headcount, floor area or a usage driver (targets "CC1:60; CC2:40", optionally CC:driver:dept). Drivers updates a rule's targets; Preview shows the period's recharge; Run posts it. Create rule with an existing code changes its name, order and targets (the basis and source stay); Delete takes a rule out of use.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create rule, Drivers, Preview, Run; default Create rule |
| Rule code | no | Text |
| Name | no | Text |
| Order | yes | Number; default 1 |
| Basis | yes | One of: FixedPercent, Headcount, FloorArea, UsageDriver; default FixedPercent |
| Source account | no | Text |
| Source cost centre | no | Text |
| Target account (blank: same) | no | Text |
| Targets: CC:driver[:dept]; … | no | Text |
| Period (any day in it) | yes | Date |

The list beside the form shows: Allocation rules.

### Year-end

Run: rolls the year's profit or loss to retained earnings (in the adjustment period when there is one), opens the next year on the same calendar and closes the year. Reverse: undoes a year-end not yet hard-locked so adjustments can be made and it can be run again. Hard lock: makes it final - every period permanently closed.

| Field | Required | Notes |
| --- | --- | --- |
| Fiscal year id | yes | Number |
| Step | yes | One of: Run, Reverse, Hard lock; default Run |
| Retained earnings account | yes | Text; default 3200 |
| Reason (reverse) | no | Text |

The list beside the form shows: Fiscal years.

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| GL-BS | Balance Sheet | Assets, liabilities and equity as at a date; assets equal liabilities plus equity - beside the position a year earlier and the change. |
| GL-CF | Statement of Cash Flows | Cash generated by operating, investing and financing activities for a period, reconciled to the change in cash. |
| GL-CFD | Statement of Cash Flows - Direct Method | Cash received and paid in the period by kind - receipts from customers, payments to suppliers, sales and expenses settled in cash, taxes - then investing and financing flows by account, reconciled from the opening to the closing cash. Cash accounts are those tagged cash and equivalents (the indirect method is GL-CF). |
| GL-COA | Chart of Accounts | Every ledger account with its type, report category, control flag and active status. |
| GL-CTRL | Control Reconciliation | GL control-account balances (AR, AP, Stock) versus their sub-ledger totals, with the difference - zero when they agree. |
| GL-DAYBOOK | Journal Daybook | Every journal over a period - date, number, source module, document, narrative and base total (draft and deferred too when asked; a branch, cost centre or department narrows the lines counted). |
| GL-DEPT-IS | Departmental Income Statement | Income and expenses for a period restricted to one department; the difference is the department's net profit. |
| GL-IS | Income Statement | Income and expenses for a period; the difference is the net profit - beside the same period a year earlier and the change. |
| GL-IS-FN | Income Statement by Function | Revenue, cost of sales and gross profit, then distribution, administrative and other operating expenses, operating profit, finance costs and the profit for the period, beside the same period a year earlier. Expenses fall to a function by their account's report category ("Distribution", "Administrative", "Finance"…); the accounts items post cost of sales to are cost of sales. |
| GL-RECUR | Recurring Journals | Standing recurring journal templates with code, description, source module, value, balanced and active flags. |
| GL-SNAP | Financial Snapshot | Headline figures as at a date: assets, liabilities, equity and year-to-date income, expenses and net profit. |
| GL-TB | Trial Balance | Account balances as at a date; debits must equal credits. |
| GL-TBMOV | Trial Balance Movement | Per account: opening balance brought forward, debits and credits in the period, and the closing balance. |
| GL-TBTYPE | Trial Balance by Type | The trial balance summarised to one line per account type, with debit and credit totals that balance. |
| GL-TREE | Statement by Reporting Tree | A financial statement laid out by a reporting tree (statutory, management or consolidation): each heading with everything mapped beneath it rolled up, against the same period a year earlier. |
| GL-VAT | VAT Return | Output tax on sales less input tax on purchases for a period, giving the net VAT payable. |
| GL-VATC | VAT Return by Code | Output and input tax by tax code for a period, with the net per code - the multi-rate VAT breakdown. |
| GL1 | Trial Balance - Comparative | Account balances at the date beside the same date a year earlier, with the change; both columns balance (the summary by account type is GL-TBTYPE). |
| GL10 | Notes to the Accounts Schedules | Accounts grouped by their note reference: opening balance, the period's movement and closing balance, with a total per note. |
| GL11 | Management Accounts Pack | Results for the period and the year to date, the financial position at the end date and the working-capital balances. |
| GL11-CC | Management Accounts - P&L by Department & Cost Centre vs Budget | Each income and expense account per department and cost centre: actual for the period against the active budget, the variance (favourable positive) and % variance, then the year to date against budget, the full-year budget and a full-year forecast (year-to-date actual plus the budget still to come). |
| GL12 | Budget vs Actual vs Commitment | Year to date per budgeted account and department: budget, actual, open commitments, what is still available and the share used. |
| GL14 | Consolidation Elimination Schedule | Every intercompany document across the group in the period: each side's amount, and whether it eliminates or is unmatched. |
| GL15 | FX Revaluation Report | Unrealised revaluations of foreign-currency balances (ledger and trading) in the period, by account. |
| GL16 | Realised FX Gain / Loss | Exchange differences realised on settlement in the period. |
| GL17 | Suspense & Unallocated Items Review | Balances on suspense accounts and unallocated receipts and payments on the trading ledgers at the date. |
| GL18 | Control Reconciliation - VAT and Bank | Tax accounts against the tax-coded postings, and each bank account's ledger balance against its statement, with what explains the gap. |
| GL19 | Period Close Checklist & Status | Each period of the year to the date: its status per module, whether it is ready to close, and every close check with its count, value and whether it blocks. |
| GL2 | Trial Balance - Multi-currency | Posted balances as at the date per account and transaction currency, in transaction and base. |
| GL20 | Year-End Pack | For the year containing the date: the close checklist, the trial balance before and after the closing journals, the result for the year, every closing entry, the retained earnings movement and the year's journals by source. |
| GL22 | Recurring Journals Due & Missed | Each active recurring template's last posted month against the date: posted, due or missed. |
| GL23 | Accruals, Prepayments & Deferred Income Schedule | Accrual and prepayment journals in the period, then deferred income schedules with what is left to recognise. |
| GL24 | Intercompany Balances & Reconciliation | Intercompany journals in the period and whether the counterparty company holds the mirror entry. |
| GL25 | Hyperinflation (IAS 29) Working Papers | Each account's historical balance restated by the price index at the date, monetary items unchanged, with the restatement. |
| GL26 | FX Revaluation by Trader (before / after) | Every debtor and creditor with foreign-currency open items at the date: the base value booked, the value at the closing rate and the unrealised difference, from the revaluation preview (nothing is posted). |
| GL2R | Trial Balance - Reporting Currency | Posted balances as at the date per account in base and in the company's reporting currency, each posting carried at the rate of its day. |
| GL3 | Nominal Activity / Account Ledger Detail | One account over the period: the opening balance, every posted line with its journal, date and narrative and the running balance, then the closing balance (each journal drills through to its source document). |
| GL5 | Journal Listing & Journal Audit | Every journal in the period with status, source, manual flag, who prepared and approved it, reversals and value. |
| GL8 | Statement of Changes in Equity | Each equity account's opening balance, the period's profit and other movements, and closing balance. |
| TX216 | VAT Detail / Audit Report | Every posted line carrying a tax code in the period, by code, with its journal and document. |
| TX217 | VAT Reconciliation | Per tax code: movement on its output and input tax accounts in the period against the tax-coded lines, with the difference. |
| TX218 | Exempt, Zero-rated & Out-of-scope Supplies | Lines in the period under zero-rated, exempt and out-of-scope tax codes, totalled per code. |
| TX219 | Reverse Charge & Import VAT | Lines in the period under reverse-charge and deemed (import) tax codes: the output tax self-assessed, the input tax claimed and what is left payable. |
| TX220 | Withholding Tax Return | Withholding certificates by month and tax code: count, gross paid, tax withheld and how much has been remitted. |
| TX223 | Tax Invoice / Fiscal Receipt Compliance Layout | A sale laid out as a tax invoice - supplier and tax number, document number and time, lines with tax, the tax summary by code, totals, the fiscal device status - followed by the compliance checks it passes or fails. |
| TX224 | Annual Financial Statements Pack | Statement of financial position and profit or loss by report category for the year to the date, with the prior year's comparatives and the note each line is disclosed in. |
| TX225 | Audit File Export (SAF-T style) | Every posted journal line in the period with its period, source, document, account, tax code, trading party, currency amounts and base debit / credit - the GL audit file. |
| TX226 | VAT Return by Box | The period's tax-coded postings by jurisdiction and return box: per tax code what it applies to, the net supplies (sales positive, purchases negative) and the tax (output positive, input negative), with a payable total per jurisdiction. |
