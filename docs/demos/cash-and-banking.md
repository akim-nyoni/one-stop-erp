# Demo: Cash & Banking

Goal: show that bank accounts and reconciliation, cheques, transfers and standing orders work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Bank accounts
1. Open **Bank reconciliation**. Tick the bank statement off against your cash book.
2. Open **Import a bank statement**. Import the bank's statement file (CSV, MT940, CAMT.053 or OFX) into the account's unreconciled lines, or sign the reconciliation off as at a date: the statement's closing balance must equal the imported lines' running balance and the books must reconcile to it, and once signed the period's matched items are locked against change. Fill in: Step, Bank account code, File format, Sign off as at (statement date). Press OK and point out the result line and the list on the right.
3. Open **Bank rules**. Rules that match bank statement lines automatically.

## Cheques
4. Open **Issue a cheque**. Records a cheque leaf written out of the cheque book (numbers are unique per bank account). Fill in: Bank account code, Cheque number, Date, Payee, Amount. Press OK and point out the result line and the list on the right.
5. Open **Print a cheque**. Lays a cheque out for its leaf: date, payee, the amount in figures and in words, and the payment it settles (voided or cancelled cheques are not printed). Fill in: Bank account code, Cheque number. Press OK and point out the result line and the list on the right.
6. Open **Void a cheque leaf**. A spoilt or lost leaf that was never issued - kept in the register so the number sequence has no gaps. Fill in: Bank account code, Cheque number, Date, Reason. Press OK and point out the result line and the list on the right.

## Standing orders and debit orders
7. Open **Set up a standing order**. A fixed payment out of the bank on a schedule: to a supplier (purchase ledger) or to a GL account. Fill in: Reference, Bank account, Amount, Frequency, First payment. Press OK and point out the result line and the list on the right.
8. Open **Set up a direct debit mandate**. The customer's authority to collect on a schedule: a fixed amount, or what they owe (optionally capped). Fill in: Mandate reference, Customer, Customer's bank account, Collect into bank account, Frequency, First collection. Press OK and point out the result line and the list on the right.
9. Open **Run standing orders and direct debits**. Pays every standing order and collects every direct debit due up to the date; nothing is ever run twice. Fill in: Run up to. Press OK and point out the result line and the list on the right.

## Bank files
10. Open **Save a bank file layout**. A bank's EFT format as templates: {Field[,width][:format]} - header/trailer: Date, Originator, Currency, Count, Total, Hash; detail: Seq, Date, Reference, Name, Bank, Branch, Account, Currency, Amount (format 'cents' for whole cents; width 010 zero-pads). Fill in: Layout code, Bank / format name, Detail template. Press OK and point out the result line and the list on the right.
11. Open **Write payroll bank files**. A payroll run's net pay in a bank layout, one file per beneficiary bank, with control total and hash (split pay honoured, held salaries left out). Fill in: Run document or journal number, Layout code. Press OK and point out the result line and the list on the right.

## Reports to end on
- CB-FLOW Cash-Flow Forecast: Expected receipts and payments from open receivables and payables, bucketed by due date, with the running net. Show the drill-through by clicking a document or account.
- CB68 Cash Book Listing: Every posting to a bank account's ledger in the period with running balance and whether it is reconciled. Show the drill-through by clicking a document or account.
- CB69 Bank Reconciliation Statement: For a bank account at a date: the statement balance, less payments not yet presented, plus receipts not yet on the ledger, against the cash book balance, with any difference left. Show the drill-through by clicking a document or account.
