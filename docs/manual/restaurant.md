# Restaurant

Tables, orders, the kitchen, bills, reservations and tips.

Works with: Restaurant.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Service

### Restaurant floor

See every table and its open tab.

### Take an order

Opens an order at a table or for takeaway / delivery, adds menu items (by course, seat and price level) and modifiers, voids or comps a line (with an authorising user), moves the order, sends it or a course to the kitchen, tracks a delivery, or shows its lines (with the line ids the other steps use).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Show order, Open at table, Open takeaway / delivery, Add item, Modifier, Seat a line, Void line, Comp line, Spend limit, Move to table, Move line to tab, Merge into tab, Change waiter, Fire order, Fire course, Assign driver, Delivery status, Cancel order; default Show order |
| Order number | yes | Text |
| Table number | no | Text |
| Party size | yes | Number; default 2 |
| Order type | yes | One of: DineIn, Takeaway, Delivery; default DineIn |
| Customer name | no | Text |
| Phone | no | Text |
| Delivery address | no | Text |
| Menu item code | no | Text |
| Quantity | yes | Number; default 1 |
| Course | yes | One of: Drinks, Starter, Main, Dessert; default Drinks |
| Seat number | no | Number |
| Price level | no | Text |
| Line id | no | Number |
| Modifier description | no | Text |
| Modifier price change | yes | Number; default 0 |
| Modifier ingredient item | no | Text |
| Ingredient quantity | yes | Number; default 0 |
| Waiter / driver / authorised by (user name) | no | Text |
| Spend limit (blank: none) | no | Number |
| Other tab's order number (move line / merge) | no | Text |
| Kitchen ticket number | no | Text |
| Delivery status | yes | One of: Pending, Assigned, OutForDelivery, Delivered, Failed; default Pending |
| Reason | no | Text |
| Date | yes | Date |

The list beside the form shows: Open orders.

### Bill an order

Bills an order in full (cash or card), by seat, in part (LINE:quantity; …), across several tenders (ACCOUNT:amount; …), to a gift card or to a customer's house account — with service charge, tip and discount — issuing the recipe ingredients from stock; quotes the automatic gratuity; or reverses a bill.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Full, Seat, Split, Tenders, Gift card, House account, Quote gratuity, Reverse bill; default Full |
| Order number | no | Text |
| Warehouse (stock issued) | no | Text |
| Cash / card account | no | Text |
| Tax account | no | Text |
| Service charge | yes | Number; default 0 |
| Service charge account | no | Text |
| Tip | yes | Number; default 0 |
| Tips payable account | no | Text |
| Discount | yes | Number; default 0 |
| Discount account | no | Text |
| Seat number | no | Number |
| Split: LINE:quantity; … | no | Text |
| Tenders: ACCOUNT:amount; … | no | Text |
| Gift card number | no | Text |
| Gift card liability account | no | Text |
| House account customer | no | Text |
| Sales account (house account) | no | Text |
| Bill id (reverse) | no | Number |
| Reason (reverse) | no | Text |
| Date | yes | Date |

The list beside the form shows: Recent bills.

### Tips

Shares a tip pool among staff by weight (USER:weight; …, e.g. hours or points), pays one person's share, or pays tips out of the tips payable account in one sum.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Distribute, Pay a share, Pay out; default Distribute |
| Amount | yes | Number; default 0 |
| Shares: USER:weight; … (distribute) | no | Text |
| Tips payable account | no | Text |
| Cash / bank account (pay) | no | Text |
| Share id (pay a share) | no | Number |
| Date | yes | Date |

The list beside the form shows: Tips owed to staff.

## Kitchen

### Kitchen tickets

Moves a kitchen ticket along: preparing, ready, served.

| Field | Required | Notes |
| --- | --- | --- |
| Ticket number | yes | Text |
| Now | yes | One of: Sent, Preparing, Ready, Served; default Sent |

The list beside the form shows: Tickets in the kitchen.

### Kitchen station printers

Which printer each preparation station's tickets print on (KITCHEN for dishes with no station): a Windows printer by name, or a network printer by host and port. A station with no printer works from its kitchen display. Reprint sends a ticket (and its per-station tickets) again, marked REPRINT.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Set, Remove, Reprint; default Set |
| Station | no | Text |
| Windows printer name | no | Text |
| Network printer host | no | Text |
| Port | yes | Number; default 9100 |
| Copies | yes | Number; default 1 |
| Ticket number (reprint) | no | Text |

The list beside the form shows: Station printers.

### Kitchen wastage

Writes off spoilt, dropped or expired ingredients from a warehouse at cost, with a reason, to the wastage account.

| Field | Required | Notes |
| --- | --- | --- |
| Stock item | yes | Text |
| Warehouse | yes | Choice |
| Quantity | yes | Number |
| Wastage account | yes | Text |
| Reason | yes | Text |
| Date | yes | Date |

The list beside the form shows: Recent wastage.

## Bookings

### Reservations

Books a table for a guest at a time (yyyy-mm-dd hh:mm), seats the guest when they arrive (opening the order), cancels a booking or marks a no-show.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Reserve, Seat, Cancel, No-show; default Reserve |
| Table number (reserve) | no | Text |
| Guest name | no | Text |
| Party size | yes | Number; default 2 |
| When: yyyy-mm-dd hh:mm (reserve) | no | Text |
| Phone | no | Text |
| Notes | no | Text |
| Reservation id | no | Number |
| Order number (seat) | no | Text |
| Waiter user name (blank: me) | no | Text |
| Reason (cancel) | no | Text |
| Date | yes | Date |

The list beside the form shows: Upcoming reservations.

### Waitlist

Walk-ins waiting for a table: add a party (with the wait quoted), seat it at a table (opening the order), or take it off the list (left, or a no-show).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Add, Seat, Left, No-show; default Add |
| Guest name | no | Text |
| Party size | yes | Number; default 2 |
| Quoted wait (minutes) | yes | Number; default 15 |
| Phone | no | Text |
| Notes | no | Text |
| Waitlist id | no | Number |
| Table number (seat) | no | Text |
| Order number (seat) | no | Text |
| Waiter user name (blank: me) | no | Text |
| Date | yes | Date |

The list beside the form shows: Waiting.

## Setup

### Menu items

Creates a menu item with its recipe (ingredients ITEM:quantity; … issued from stock when billed), sets a price for a price level (happy hour, staff…), marks an item available or not, refreshes availability from the stock of a warehouse, sends an item to a preparation station (grill, bar…) — fired orders give each station its own ticket — and shows a station's queue.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Price level, Available, Unavailable, Refresh availability, Prep station, Station queue; default Create |
| Preparation station | no | Text |
| Menu item code | no | Text |
| Name (create) | no | Text |
| Price | yes | Number; default 0 |
| Revenue account (create) | no | Text |
| Cost of sales account (create) | no | Text |
| Tax code (create) | no | Text |
| Recipe: ITEM:quantity; … (create) | no | Text |
| Price level | no | Text |
| Warehouse (refresh) | no | Text |

The list beside the form shows: Menu.

### Tables and sections

Sets up the floor: tables and their seats, sections (with a server; on or off), combined tables for a large party, and the automatic gratuity added for parties of a given size.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create table, Create section, Table to section, Section server, Section on, Section off, Combine tables, Uncombine, Auto-gratuity policy; default Create table |
| Table number(s): T1, T2 … | no | Text |
| Seats / smallest party (gratuity) | yes | Number; default 4 |
| Section code | no | Text |
| Section name | no | Text |
| Server user name (blank: none) | no | Text |
| Combined group label (uncombine) | no | Text |
| Gratuity % | yes | Number; default 0 |
| Service charge account (gratuity) | no | Text |

The list beside the form shows: Tables.

### Floor plan and tab warnings

Place each table on the floor plan grid (across, down) with its shape; the plan shows every table, its open tab, the tab's total and how long it has been open. Tab warnings flag a tab open longer than so many minutes or running over a total (0 turns either off).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Place table, Tab warnings, Warnings now; default Place table |
| Table number | no | Text |
| Across (0–99) | yes | Number; default 0 |
| Down (0–99) | yes | Number; default 0 |
| Shape | yes | One of: Square, Round, Booth, BarSeat; default Square |
| Warn after (minutes) | yes | Number; default 90 |
| Warn over (total) | yes | Number; default 0 |

The list beside the form shows: Floor plan.

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| RES-BILLS | Bills | Every restaurant bill over a period — net, tax, service charge, tip, discount and gross. |
| RES-CAT | Category Sales | Billed sales by menu category (starters, mains, drinks…) over a period. |
| RES-COMB | Combined Tables | Active table groups — the combined tables, their primary and total seats. |
| RES-COST | Menu Cost Card | Every menu item's price, recipe cost (from ingredient average cost), gross margin and food-cost %. |
| RES-DELIV | Active Deliveries | Delivery orders still in progress — customer, address, driver and dispatch state. |
| RES-KOT | Kitchen Order Tickets | Open kitchen tickets (not yet served) with their status and item count. |
| RES-MENU | Menu | Menu items with category, price, recipe ingredient count and availability. |
| RES-MIX | Menu Engineering | Sales mix by menu item — quantity, revenue, recipe cost, margin and menu-engineering class. |
| RES-OTYPE | Sales by Order Type | Billed sales split by fulfilment type (dine-in / takeaway / delivery) over a period. |
| RES-RESV | Table Reservations | Upcoming table bookings — when, table, guest, party size and contact. |
| RES-SECT | Floor Plan | Floor-plan sections with their tables, seats, live occupancy and assigned server. |
| RES-SERVER | Server Sales | Billed sales by waiter over a period — orders, net, tax and gross (for tip-out and performance). |
| RES-SSALES | Section Sales | Billed sales by floor-plan section over a period — orders, net, tax and gross. |
| RES-TBL | Table Status | Tables with seats and current status. |
| RES-TIPS | Tip Allocations | Pooled tips allocated to staff, with amount and payment status. |
| RES-VOID | Voids &amp; Comps | Voided and comped order lines over a period, with reasons and the authorising manager — for oversight. |
| RES-WAIT | Waitlist | Walk-in parties currently queued for a table, in the order they joined. |
| RES-WASTE | Stock Wastage | Spoilage / waste of stock ingredients over a period, with reasons and value written off. |
