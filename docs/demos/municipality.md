# Demo: Municipality

Goal: show that properties, services and meters, billing, clearances, licences and faults work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Properties
1. Open **Enquiry desk**. Look a stand up at the counter: balance, services, meters, statement.
2. Open **Property enquiry**. One view of a stand: owners and ratepayer, services and meters, balance and overdue, recent bills and payments, subsidy, arrangement, open orders, licences and clearance. Fill in: Stand number, As at. Press OK and point out the result line and the list on the right.
3. Open **Ratepayer statement**. The ratepayer's statement for a stand, as at a date, ready to print or hand over: the account, then each service with its tariff (fixed charge, rates and steps) and the meter's last readings, and a consumption graph for each meter over the months chosen. Fill in: Stand number, As at, Months of consumption graph (0: none). Press OK and point out the result line and the list on the right.

## Services and meters
4. Open **Service connections**. Connect a council service to a stand (with a quantity and its deposit, invoiced to the ratepayer), change its quantity, or suspend, disconnect, reconnect or close it. Fill in: Step, Stand number, Service code, Date. Press OK and point out the result line and the list on the right.
5. Open **Capture a meter reading**. An actual reading (or an estimate when the meter could not be read); readings outside the usual pattern are flagged for checking. Fill in: Meter serial, Reading date. Press OK and point out the result line and the list on the right.
6. Open **Meter reading routes**. Creates a reading route for a meter reader (meter serials in walking order, comma-separated); Sheet prints it with the last readings; Import loads a CSV of readings taken on the round (each checked like a keyed reading). Fill in: Step. Press OK and point out the result line and the list on the right.

## Billing
7. Open **Billing run**. Propose a run for a service (limited to a ward, a meter-reading route or a billing cycle if you like), review its exceptions, include or exclude stands, then approve to post the bills — someone other than the proposer approves. Fill in: Step, Service, Bill date, Billing cycle. Press OK and point out the result line and the list on the right.
8. Open **Tariffs**. Lists a service's tariffs in force on a date, or resolves which tariff applies to a consumer category in a ward. Fill in: Step, Service, Consumer category (resolve), Date. Press OK and point out the result line and the list on the right.
9. Open **Tariff billing cycle**. How often a tariff is billed: monthly, quarterly, half-yearly or annually from a start month (e.g. Fill in: Tariff code, Cycle, Start month (1–12), Instalments per period. Press OK and point out the result line and the list on the right.

## Clearances, licences and facilities
10. Open **Rates clearance certificates**. Quotes what a stand must pay for a clearance certificate (its balance plus so many months' billing in advance), issues the certificate once that is paid, valid for so many days, and prints it (Print reprints an issued certificate by its number). Fill in: Step, Months in advance, Valid for (days), Date. Press OK and point out the result line and the list on the right.
11. Open **Council licences**. Issues a licence (business, liquor, shop, building plan, hawker…) with its fee and renewal cycle, records inspections (a failed one suspends it), renews it (late renewals with a penalty %), or revokes it. Fill in: Step, Licence kind, Renewal cycle (months), Fee / penalty %, Date. Press OK and point out the result line and the list on the right.
12. Open **Cemeteries**. Adds plots to a cemetery section ("A1,A2,A3") at a price and capacity, sells a plot to a holder, records an interment (with its fee), and lists a cemetery's plots. Fill in: Step, Price / interment fee, Capacity (graves per plot), Date. Press OK and point out the result line and the list on the right.

## Faults
13. Open **Fault desk**. Log, assign and close faults reported by the public.
14. Open **Fault ticket categories**. The kinds of fault the public reports (a burst pipe, a missed refuse collection), each with the service it concerns and its response and resolution times — the clocks the fault desk measures every ticket against. Fill in: Category code, Name, Service, Respond within (hours), Resolve within (hours). Press OK and point out the result line and the list on the right.

## Reports to end on
- DB256 Municipal Executive Dashboard: Billed and collected this year, collection rate, debtors and over-90s, indigent households, disconnections, open faults and licences due; water bought in bulk against water metered to consumers this year (the loss), and ratepayer arrears at each of the last six month-ends. Show the drill-through by clicking a document or account.
- M144 Consumer / Ratepayer Statement: Per property: the ratepayer's account with the services connected, opening balance, charges, payments and interest in the period, closing balance and ageing; one stand, or every stand for bulk (burst) printing. Show the drill-through by clicking a document or account.
- M146 Billing Run Summary & Control: Each billing run: service, period, ward, lines billed and excluded, total billed against the previous run, proposer and approver. Show the drill-through by clicking a document or account.
