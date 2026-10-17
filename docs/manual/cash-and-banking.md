# Cash & Banking

Bank accounts and reconciliation, cheques, transfers and standing orders.

Works with: Banking.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Bank accounts

### Bank reconciliation

Tick the bank statement off against your cash book.

### Import a bank statement

Import the bank's statement file (CSV, MT940, CAMT.053 or OFX) into the account's unreconciled lines, or sign the reconciliation off as at a date: it must balance, and once signed the period's matched items are locked against change.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Import, Sign off; default Import |
| Bank account code | yes | Text |
| Statement file (full path) | no | Text |
| File format | yes | One of: Csv, Mt940, Camt053, Ofx; default Csv |
| Sign off as at | yes | Date |

The list beside the form shows: Bank accounts.

### Bank rules

Rules that match bank statement lines automatically.

### Transfer between bank accounts

Moves money from one bank account to another, through a clearing account when it arrives later; across currencies, the amount received sets the rate and the base-currency difference posts as an exchange gain or loss.

| Field | Required | Notes |
| --- | --- | --- |
| From bank account | yes | Text |
| To bank account | yes | Text |
| Amount sent | yes | Number |
| Amount received (another currency) | no | Number |
| Reference | yes | Text |
| Clearing (in transit) account | no | Text |
| Exchange gain account | no | Text |
| Exchange loss account | no | Text |
| Date | yes | Date |

### Cash-flow forecast

Week by week from the bank balances: receivables and payables by due date, saved payment runs on their dates, open sales and purchase orders on delivery plus terms, standing orders, payroll (repeating the last regular run) and flows keyed in by hand (once or monthly). Weeks where cash would fall below the minimum are marked.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Forecast, Add a flow, Remove a flow; default Forecast |
| From | yes | Date |
| Weeks | yes | Number; default 13 |
| Minimum cash to hold | yes | Number; default 0 |
| Show every flow | no | Flag; default False |
| Flow amount (+ in, − out) | yes | Number; default 0 |
| Flow description | no | Text |
| Repeat monthly until (yyyy-mm-dd) | no | Text |
| Flow id (remove) | no | Number |

The list beside the form shows: Bank accounts.

## Cheques

### Issue a cheque

Records a cheque leaf written out of the cheque book (numbers are unique per bank account).

| Field | Required | Notes |
| --- | --- | --- |
| Bank account code | yes | Text |
| Cheque number | yes | Text |
| Date | yes | Date |
| Payee | yes | Text |
| Amount | yes | Number |
| Payment document number | no | Text |

The list beside the form shows: Cheque register (latest 100).

### Print a cheque

Lays a cheque out for its leaf: date, payee, the amount in figures and in words, and the payment it settles (voided or cancelled cheques are not printed).

| Field | Required | Notes |
| --- | --- | --- |
| Bank account code | yes | Text |
| Cheque number | yes | Text |

The list beside the form shows: Cheque register (latest 100).

### Void a cheque leaf

A spoilt or lost leaf that was never issued — kept in the register so the number sequence has no gaps.

| Field | Required | Notes |
| --- | --- | --- |
| Bank account code | yes | Text |
| Cheque number | yes | Text |
| Date | yes | Date |
| Reason | yes | Text |

The list beside the form shows: Cheque register (latest 100).

### Mark a cheque presented / cancelled

Presented when it clears the bank; cancelled (with a reason) when stopped.

| Field | Required | Notes |
| --- | --- | --- |
| Bank account code | yes | Text |
| Cheque number | yes | Text |
| Action | yes | One of: Presented, Cancelled; default Presented |
| Date | yes | Date |
| Reason (cancel) | no | Text |

The list beside the form shows: Cheque register (latest 100).

## Standing orders and debit orders

### Set up a standing order

A fixed payment out of the bank on a schedule: to a supplier (purchase ledger) or to a GL account. Give a supplier or an account, not both.

| Field | Required | Notes |
| --- | --- | --- |
| Reference | yes | Text |
| Bank account | yes | Text; default 1100 |
| Amount | yes | Number |
| Frequency | yes | One of: Weekly, Monthly, Quarterly, Annually; default Weekly |
| First payment | yes | Date |
| Last date (yyyy-MM-dd, optional) | no | Text |
| Supplier | no | Choice |
| GL account | no | Text |
| What it is for | no | Text |

The list beside the form shows: Standing orders and direct debits.

### Set up a direct debit mandate

The customer's authority to collect on a schedule: a fixed amount, or what they owe (optionally capped).

| Field | Required | Notes |
| --- | --- | --- |
| Mandate reference | yes | Text |
| Customer | yes | Choice |
| Customer's bank account | yes | Text |
| Collect into bank account | yes | Text; default 1100 |
| Frequency | yes | One of: Weekly, Monthly, Quarterly, Annually; default Weekly |
| First collection | yes | Date |
| Last date (yyyy-MM-dd, optional) | no | Text |
| Fixed amount (blank: what is owed) | no | Number |
| Most per collection | no | Number |

The list beside the form shows: Standing orders and direct debits.

### Run standing orders and direct debits

Pays every standing order and collects every direct debit due up to the date; nothing is ever run twice.

| Field | Required | Notes |
| --- | --- | --- |
| Run up to | yes | Date |

The list beside the form shows: Standing orders and direct debits.

### Stop a standing order or mandate

Stops it running from now on.

| Field | Required | Notes |
| --- | --- | --- |
| Reference | yes | Text |

The list beside the form shows: Standing orders and direct debits.

## Bank files

### Save a bank file layout

A bank's EFT format as templates: {Field[,width][:format]} — header/trailer: Date, Originator, Currency, Count, Total, Hash; detail: Seq, Date, Reference, Name, Bank, Branch, Account, Currency, Amount (format 'cents' for whole cents; width 010 zero-pads).

| Field | Required | Notes |
| --- | --- | --- |
| Layout code | yes | Text |
| Bank / format name | yes | Text |
| Header template | no | Text |
| Detail template | yes | Text; default {Reference}|{Name}|{Bank}|{Account}|{Amount} |
| Trailer template | no | Text |

The list beside the form shows: Bank file layouts.

### Write payroll bank files

A payroll run's net pay in a bank layout, one file per beneficiary bank, with control total and hash (split pay honoured, held salaries left out).

| Field | Required | Notes |
| --- | --- | --- |
| Run document or journal number | yes | Text |
| Layout code | yes | Text |

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| CB-FLOW | Cash-Flow Forecast | Expected receipts and payments from open receivables and payables, bucketed by due date, with the running net. |
| CB68 | Cash Book Listing | Every posting to a bank account's ledger in the period with running balance and whether it is reconciled. |
| CB69 | Bank Reconciliation Statement | For a bank account at a date: the statement balance, less payments not yet presented, plus receipts not yet on the ledger, against the cash book balance, with any difference left. |
| CB70 | Unpresented & Uncleared Items Ageing | Ledger postings not yet on a bank statement and statement lines not yet on the ledger, aged at the date. |
| CB71 | Bank Account Balances Summary | Every bank account and wallet at a date: currency, cash book balance, statement lines still to reconcile and when it was last reconciled. |
| CB72 | 13-Week Cash Flow Forecast by Currency | From today's bank balances, week by week: customer receipts and supplier payments falling due, and the closing cash, per currency. |
| CB73 | Petty Cash & Imprest Report | Disbursements and top-ups on the petty cash account in the period with its running balance. |
| CB74 | Inter-account Transfers | Transfers between bank accounts in the period, with the amount out, the amount in and any exchange difference. |
| CB75 | Cheque Register | Every cheque written in the period by bank account: payee, amount and status — issued, presented, cancelled, voided, or stale when unpresented past the limit. |
| CB76 | Mobile Money / Wallet Reconciliation | Per mobile-money wallet at the date: ledger balance against the wallet statement, unmatched items on each side and any difference left unexplained. |
| CB77 | Petty Cash Counts & Imprest Reconciliation | Every petty cash fund with its float and book balance, and each count in the period: cash counted against the book, the variance, who counted, and the variance and reimbursement journals once reconciled. |
