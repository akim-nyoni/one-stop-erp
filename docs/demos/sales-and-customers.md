# Demo: Sales & Customers

Goal: show that customers, quotes and orders, invoices, receipts, statements and debt collection work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Customers
1. Open **Add a customer**. A customer account; raise the credit limit later through approval. Fill in: Account code, Name, Currency, Credit limit. Press OK and point out the result line and the list on the right.
2. Open **Customer addresses and contacts**. Postal, physical and delivery addresses (the default postal address prints on invoices, statements, orders, remittances and letters) and the people who receive statements, remittance advices and purchase orders by email. Fill in: Step, Customer code, Address kind. Press OK and point out the result line and the list on the right.
3. Open **Credit limits**. Lowers a customer's credit limit at once; an increase is requested with the reason and approved (or rejected) by another user. Fill in: Step, New limit, Credit-limit policy. Press OK and point out the result line and the list on the right.

## Quotes and orders
4. Open **Quotations**. Quotes a customer (ITEM:quantity@price; …, valid until a date), records it sent, accepted or declined, and converts an accepted quotation into a sales order. Fill in: Step, Quotation number, Valid until (create), Date. Press OK and point out the result line and the list on the right.
5. Open **Sales orders**. Take an order (lines ITEM:quantity[:price[:tax code]]; no price takes the customer's price book), despatch it from a warehouse (stock issued at cost; lines ITEM:quantity, or blank for everything outstanding - a part despatch leaves the rest on back order), invoice what has been despatched (tax at each line's tax code), take back goods returned from a despatch, or cancel what is left of the order. Fill in: Step, Order number, Date. Press OK and point out the result line and the list on the right.
6. Open **Amend a sales order**. Changes ordered quantities and prices as a new version of the order, keeping the earlier one with the reason. Fill in: Sales order number, Lines: ITEM:quantity:unit price; …, Reason, Date. Press OK and point out the result line and the list on the right.

## Deliveries
7. Open **Confirm a delivery (proof of delivery)**. Records who received a despatched delivery, when, and the signed POD reference. Fill in: Delivery reference, Date, Received by. Press OK and point out the result line and the list on the right.
8. Open **Delivery vehicles and loads**. Register a vehicle (capacity in kg, optional m³ and drops a day, usual driver); put a delivery on a vehicle's run for a day at a stop (refused when it would overload the vehicle by weight or exceed its drops); or show a vehicle's load for a day in run order. Fill in: Step, Vehicle registration, Day. Press OK and point out the result line and the list on the right.
9. Open **Charge for a delivery**. Works out a delivery's charge from the customer's zone; tick 'Invoice' to bill it to the customer (once per delivery). Fill in: Delivery reference, Date, Delivery income account. Press OK and point out the result line and the list on the right.

## Invoices and payments
10. Open **Customer invoice or credit note**. Posts a sales invoice or credit note to a customer's account (the customer's currency unless another is given; tax at the tax code's account). Fill in: Document, Customer, Document number, Date, Net amount, Tax amount, Sales account. Press OK and point out the result line and the list on the right.
11. Open **Customer debit note**. A further charge on an invoice already raised (under-billing, a price correction, a charge omitted): posted as a debit note that refers to the invoice. Fill in: Customer, Debit note number, Invoice it adds to, Date, Net amount, Tax amount, Sales account. Press OK and point out the result line and the list on the right.
12. Open **Customer receipt (bank)**. A payment received into the bank (EFT, deposit) on a customer's account; allocate it to invoices with auto-allocation or the allocation screens. Fill in: Customer, Receipt number, Date, Amount, Bank account. Press OK and point out the result line and the list on the right.

## Prices and commission
13. Open **Price lists**. Create a price list in a currency (one may be the default), set an item's price on it (with a minimum quantity for a quantity break), or put a customer on a list (blank list: back to the default). Fill in: Step. Press OK and point out the result line and the list on the right.
14. Open **Price a list from cost**. Items with no price on the list are priced from cost (average, else standard): a markup on cost or a margin on price, rounded to a price point. Fill in: Price list code, Basis, Markup or margin %, Price point, Rounding. Press OK and point out the result line and the list on the right.
15. Open **Future prices**. Schedules a price from a date, or a percentage change of the whole list from a date (e.g. Fill in: Step, Price list code, From quantity, Price point, Rounding, Effective from. Press OK and point out the result line and the list on the right.

## Statements and collections
16. Open **Customer statement**. The customer's statement as at a date, ready to print or send: open-item, or balance-forward (brought forward, the period's documents with a running balance, carried forward) for customers set to that style or when a period start is given. Fill in: Customer, As at, Set the customer's style. Press OK and point out the result line and the list on the right.
17. Open **Email a customer statement**. Sends the document as an attachment to the contacts who receive it (else the primary contact, else the customer's own email). Fill in: Customer code, Statement date. Press OK and point out the result line and the list on the right.
18. Open **Email all customer statements**. Queues a statement email to every customer owing at least a minimum balance at a date, with an email template and language; reports how many had no email address. Fill in: Statement date, Email template code, Language, Smallest balance worth a statement. Press OK and point out the result line and the list on the right.

## Printed documents
19. Open **Print a quotation**. Prints the quotation in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF. Fill in: Quote number. Press OK and point out the result line and the list on the right.
20. Open **Print an order confirmation**. Prints the order confirmation in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF. Fill in: Sales order number. Press OK and point out the result line and the list on the right.
21. Open **Print a delivery note**. Prints the delivery note in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF. Fill in: Delivery reference. Press OK and point out the result line and the list on the right.

## Reports to end on
- AR-AGE Aged Debtors: Outstanding customer balances by ageing band as at a date. Show the drill-through by clicking a document or account.
- AR-CREDIT Customer Credit Exposure: Each customer's credit limit, current balance and remaining credit, flagging on-hold and over-limit accounts. Show the drill-through by clicking a document or account.
- AR-CUST Customer Register: Customer master: code, name, currency, credit limit, payment terms, on-hold flag and analysis codes. Show the drill-through by clicking a document or account.
