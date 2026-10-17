# Demo: Restaurant

Goal: show that tables, orders, the kitchen, bills, reservations and tips work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Service
1. Open **Restaurant floor**. See every table and its open tab.
2. Open **Take an order**. Opens an order at a table or for takeaway / delivery, adds menu items (by course, seat and price level) and modifiers, voids or comps a line (with an authorising user), moves the order, sends it or a course to the kitchen, tracks a delivery, or shows its lines (with the line ids the other steps use). Fill in: Step, Order number, Party size, Order type, Quantity, Course, Modifier price change, Ingredient quantity, Delivery status, Date. Press OK and point out the result line and the list on the right.
3. Open **Bill an order**. Bills an order in full (cash or card), by seat, in part (LINE:quantity; …), across several tenders (ACCOUNT:amount; …), to a gift card or to a customer's house account — with service charge, tip and discount — issuing the recipe ingredients from stock; quotes the automatic gratuity; or reverses a bill. Fill in: Step, Service charge, Tip, Discount, Date. Press OK and point out the result line and the list on the right.

## Kitchen
4. Open **Kitchen tickets**. Moves a kitchen ticket along: preparing, ready, served. Fill in: Ticket number, Now. Press OK and point out the result line and the list on the right.
5. Open **Kitchen station printers**. Which printer each preparation station's tickets print on (KITCHEN for dishes with no station): a Windows printer by name, or a network printer by host and port. Fill in: Step, Port, Copies. Press OK and point out the result line and the list on the right.
6. Open **Kitchen wastage**. Writes off spoilt, dropped or expired ingredients from a warehouse at cost, with a reason, to the wastage account. Fill in: Stock item, Warehouse, Quantity, Wastage account, Reason, Date. Press OK and point out the result line and the list on the right.

## Bookings
7. Open **Reservations**. Books a table for a guest at a time (yyyy-mm-dd hh:mm), seats the guest when they arrive (opening the order), cancels a booking or marks a no-show. Fill in: Step, Party size, Date. Press OK and point out the result line and the list on the right.
8. Open **Waitlist**. Walk-ins waiting for a table: add a party (with the wait quoted), seat it at a table (opening the order), or take it off the list (left, or a no-show). Fill in: Step, Party size, Quoted wait (minutes), Date. Press OK and point out the result line and the list on the right.

## Setup
9. Open **Menu items**. Creates a menu item with its recipe (ingredients ITEM:quantity; … issued from stock when billed), sets a price for a price level (happy hour, staff…), marks an item available or not, refreshes availability from the stock of a warehouse, sends an item to a preparation station (grill, bar…) — fired orders give each station its own ticket — and shows a station's queue. Fill in: Step, Price. Press OK and point out the result line and the list on the right.
10. Open **Tables and sections**. Sets up the floor: tables and their seats, sections (with a server; on or off), combined tables for a large party, and the automatic gratuity added for parties of a given size. Fill in: Step, Seats / smallest party (gratuity), Gratuity %. Press OK and point out the result line and the list on the right.
11. Open **Floor plan and tab warnings**. Place each table on the floor plan grid (across, down) with its shape; the plan shows every table, its open tab, the tab's total and how long it has been open. Fill in: Step, Across (0–99), Down (0–99), Shape, Warn after (minutes), Warn over (total). Press OK and point out the result line and the list on the right.

## Reports to end on
- RES-BILLS Bills: Every restaurant bill over a period — net, tax, service charge, tip, discount and gross. Show the drill-through by clicking a document or account.
- RES-CAT Category Sales: Billed sales by menu category (starters, mains, drinks…) over a period. Show the drill-through by clicking a document or account.
- RES-COMB Combined Tables: Active table groups — the combined tables, their primary and total seats. Show the drill-through by clicking a document or account.
