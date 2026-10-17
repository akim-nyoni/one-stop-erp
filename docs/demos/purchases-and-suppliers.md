# Demo: Purchases & Suppliers

Goal: show that suppliers, requisitions and orders, receiving goods, supplier invoices and payments work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Suppliers
1. Open **Add a supplier**. A supplier account. Fill in: Account code, Name, Currency. Press OK and point out the result line and the list on the right.
2. Open **Supplier addresses and contacts**. Postal, physical and delivery addresses (the default postal address prints on invoices, statements, orders, remittances and letters) and the people who receive statements, remittance advices and purchase orders by email. Fill in: Step, Supplier code, Address kind. Press OK and point out the result line and the list on the right.
3. Open **Supplier bank details**. A change to a supplier's bank details (or mobile-money wallet) is requested by one user and approved (or rejected) by another; until then the supplier is held out of payment runs. Fill in: Step, Payment priority (1–9). Press OK and point out the result line and the list on the right.

## Requisitions and orders
4. Open **Purchase requisition**. Raise a requisition (budget-checked), submit it, approve or reject it (someone else approves), and convert an approved one into a purchase order. Fill in: Step, Date. Press OK and point out the result line and the list on the right.
5. Open **Requests for quotation**. Issue an RFQ to several suppliers (codes comma-separated) for items (ITEM:quantity; …), print it or email it to the suppliers' order contacts, record each supplier's quote (ITEM@price; …, lead time), compare them (price and lead time weighted, ranked) and award the RFQ to a supplier, which raises the purchase order. Fill in: Step, RFQ number, Lead time (days, quote), Price weight (compare), Closes on (issue), Date. Press OK and point out the result line and the list on the right.
6. Open **Purchase order**. Raise an order directly (budget-checked; over-budget needs an approver), cancel one, or print it. Fill in: Step, Order number, Order date. Press OK and point out the result line and the list on the right.

## Receiving goods
7. Open **Receive goods**. Books a goods received note against an order into a warehouse (stock in, goods-received-not-invoiced accrued). Fill in: Order number, Warehouse, GRN number, Date. Press OK and point out the result line and the list on the right.
8. Open **Quality inspection of a receipt**. Inspects goods received on a GRN held for quality: per item, what passed (released to the warehouse) and what was rejected (returned, with a credit note) — "ITEM:passed:rejected:reason; …". Fill in: GRN number, Results: ITEM:passed:rejected[:reason]; …, Release to warehouse, Date. Press OK and point out the result line and the list on the right.
9. Open **Return stock to a supplier**. Sends accepted stock back to the supplier: the stock leaves the warehouse at average cost and the supplier's account is debited at the agreed return price (a debit note), any difference to the price variance account. Fill in: Supplier, Product, Warehouse, Quantity, Return unit price, Date, Return document number, Price variance account. Press OK and point out the result line and the list on the right.

## Invoices and payments
10. Open **Supplier invoice or credit note**. Posts a supplier's invoice or credit note that has no purchase order (services, utilities); invoices for ordered goods go through Purchasing → Match a supplier invoice. Fill in: Document, Supplier, Supplier's document number, Date, Net amount, Tax amount. Press OK and point out the result line and the list on the right.
11. Open **Match a supplier invoice to the order**. Posts the supplier's invoice against the order (three-way match within the price tolerance; a larger difference is held as a dispute). Fill in: Order number, Supplier invoice number, Price tolerance %, Invoice date. Press OK and point out the result line and the list on the right.
12. Open **Supplier invoice disputes**. Submits a supplier invoice that may not match (outside the tolerance it is held as a dispute rather than refused), then accepts a dispute (posting it, the difference to price variance) or rejects it with a note. Fill in: Step, Price tolerance %, Date. Press OK and point out the result line and the list on the right.

## Statements
13. Open **Supplier statement**. The supplier account as we see it, as at a date. Fill in: Supplier, As at. Press OK and point out the result line and the list on the right.
14. Open **Remittance advice**. The remittance advice for a supplier payment (by its document number), as text to print or send, listing the invoices it settled. Fill in: Payment document number. Press OK and point out the result line and the list on the right.
15. Open **Email a statement, remittance or order**. Sends the document as an attachment to the contacts who receive it (else the primary contact). Fill in: Document, Statement date. Press OK and point out the result line and the list on the right.

## Budget control
16. Open **Set a budget enforcement rule**. What happens when spend overruns the available budget, by department, account prefix and value band (the most specific rule wins; none: allow). Fill in: From value, Action. Press OK and point out the result line and the list on the right.
17. Open **Check or reserve budget**. Shows what is available on an account; tick Reserve to earmark it for known spend with no order yet (released by its reference). Fill in: Expense account, Amount, Date. Press OK and point out the result line and the list on the right.
18. Open **Reconcile budget commitments**. Compares the commitment ledger with purchase orders: stale commitments, orders with none, and amounts that differ. Press OK and point out the result line and the list on the right.

## Printed documents
19. Open **Print a purchase requisition**. Prints the purchase requisition in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF. Fill in: Requisition number. Press OK and point out the result line and the list on the right.
20. Open **Print a goods received note**. Prints the goods received note in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF. Fill in: GRN number. Press OK and point out the result line and the list on the right.
21. Open **Print a supplier debit note**. Prints the supplier debit note in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF. Fill in: Debit note number. Press OK and point out the result line and the list on the right.

## Reports to end on
- AP-AGE Aged Creditors: Outstanding supplier balances by ageing band as at a date. Show the drill-through by clicking a document or account.
- AP-DUE Payments Due: Outstanding supplier invoices ordered by due date, with days until due (negative when overdue). Show the drill-through by clicking a document or account.
- AP-RECUR Recurring Purchases: Standing recurring supplier-invoice templates with amount, tax, interval, next run date and active flag. Show the drill-through by clicking a document or account.
