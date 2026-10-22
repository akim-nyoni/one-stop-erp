# Demo: Cashiering

Goal: show that taking payments at the counter, cashier sessions and the cash office work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Counter
1. Open **Take a payment (teller screen)**. Receive a payment at the counter and print the receipt.
2. Open **Open or close my cashier session**. Open a session at a station with the float, or close yours by declaring the cash counted (shortages and overs are posted). Fill in: Step, Date. Press OK and point out the result line and the list on the right.
3. Open **Take a payment (form)**. A payment on an account (customer code, or a stand number for its ratepayer), allocated oldest first unless chosen otherwise; or a sundry receipt or deposit to an account. Fill in: Payment for, Amount, Tender, Allocate, Date. Press OK and point out the result line and the list on the right.

## Cash office
4. Open **Cash office drawer**. During a session: a pickup moves surplus cash from the drawer to the safe; a petty cash payout pays an expense from the drawer; a supervisor verifies another cashier's closed session; a receipt is reprinted (marked as a copy, counted); a cheque or transfer awaiting clearance is confirmed into the bank. Fill in: Step, Amount, Date. Press OK and point out the result line and the list on the right.
5. Open **Bank the takings**. Takes cash to the bank (cash to cash-in-transit), confirms a deposit as banked (in transit to the bank account), moves money between accounts, and posts bank charges or interest received from the statement. Fill in: Step, Amount, Date. Press OK and point out the result line and the list on the right.

## Accounts
6. Open **Arrangements, dishonours and refunds**. A payment arrangement on a debtor's account (total and monthly instalment from a start date); a dishonoured cheque or reversed transfer, with an optional penalty charged; money held in a suspense control account allocated to an account (naming the unidentified receipt it came in on, where there is one); and a credit balance refunded to the debtor in one currency from a bank account in that currency - asked for by one user, paid only when another with the receipts approval right approves it. Fill in: Step, Amount / total, Monthly instalment (arrangement), Penalty (dishonour), Allocate to, Date. Press OK and point out the result line and the list on the right.

## Setup
7. Open **Counters and field collectors**. A counter (station) cashiers open their sessions at, or a field collector's handheld for rent, market stalls, parking and pound fees: tick Field collector, give the route and the pre-numbered receipt range issued to the collector (receipts stop at the end of the range until a new one is issued). Fill in: Station code, Name, Receipt prefix. Press OK and point out the result line and the list on the right.
8. Open **Collection agents**. Third-party agents who collect from customers for a commission: register one, record a collection (allocated to the customer's debt, owed by the agent) and take the agent's remittance (commission kept, the net banked). Fill in: Step, Agent code, Commission %, Amount, Date. Press OK and point out the result line and the list on the right.
9. Open **Manual receipt books**. Pre-numbered manual receipt books for when the system is down: issue a book (serial range) to a cashier, capture a manual receipt written from it later, and list the serials still unused (every serial must be accounted for). Fill in: Step, Book number, Amount (capture), Issue / receipt date. Press OK and point out the result line and the list on the right.

## Reports to end on
- RC1 Cashier Cash-Up / Session Close: Expected vs counted per currency and denomination, variance and supervisor sign-off. Show the drill-through by clicking a document or account.
- RC10 Hourly Transaction Volume & Queue Analysis: Transactions per hour per station, average handling time, peak periods. Show the drill-through by clicking a document or account.
- RC11 Cashier Productivity: Receipts processed, value, average handling time, variances, cancellations, reprints. Show the drill-through by clicking a document or account.
