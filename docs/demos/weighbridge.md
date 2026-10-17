# Demo: Weighbridge

Goal: show that weighing loads in and out, invoicing loads and concrete tests work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Weighing
1. Open **First weigh (open a ticket)**. Outbound loads weigh empty first (leave the weight blank to use the vehicle's stored tare); inbound loads weigh loaded first. Fill in: Direction, Vehicle registration, Product (item code), m³ per tonne, Date. Press OK and point out the result line and the list on the right.
2. Open **Second weigh (complete a ticket)**. Completes the ticket: net weight, haulage by zone and — for an outbound load from a stockpile — the stock issue. Fill in: Ticket number, Deduction kg (moisture etc.). Press OK and point out the result line and the list on the right.
3. Open **Invoice weighbridge loads**. Bills a customer for every completed outbound load up to the invoice date that has not been invoiced: net tonnes at the price, plus each load's haulage, with tax on top. Fill in: Customer, Invoice number, Date, Sales account. Press OK and point out the result line and the list on the right.

## Reports to end on
- WB198 Weighbridge Ticket: Each ticket in the period: vehicle, driver, customer or supplier, product, time in and out, gross, tare, deduction and net. Show the drill-through by clicking a document or account.
- WB199 Daily Weighbridge Register & Tonnage Summary: Completed loads per day, product, customer, vehicle and driver: loads, tonnes and m³. Show the drill-through by clicking a document or account.
- WB200 Vehicle Movement & Turnaround: Per vehicle: loads, average and longest time between weighs, open tickets and overloads against its legal maximum. Show the drill-through by clicking a document or account.
