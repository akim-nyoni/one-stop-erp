# Demo: Stock & Products

Goal: show that products and services, warehouses, stock movements, counts and production work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Products
1. Open **Add a product or service**. A stock, service or other item with the accounts its sales and cost post to, how its stock is costed (weighted average, FIFO, standard with its variance accounts, or specific identification), batch or serial tracking and a shelf life. Fill in: Item code, Description, Type, Costing method, Tracking. Press OK and point out the result line and the list on the right.
2. Open **Find a product**. Searches codes, PLUs, barcodes (item, pack, alias), synonyms, brands and descriptions. Fill in: Search for. Press OK and point out the result line and the list on the right.
3. Open **Product picture, description and size**. An item's long description, its length, width and height in centimetres (the volume follows) and weight, and its picture (a JPEG, PNG, GIF or WebP file of at most 1 MB; Remove picture drops it). Fill in: Step, Product. Press OK and point out the result line and the list on the right.

## Warehouses
4. Open **Add a warehouse**. A stock location of the current company. Fill in: Code, Name. Press OK and point out the result line and the list on the right.
5. Open **Bins and shelves**. Add a bin (sequence, capacity), put stock away (never more than is unbinned), move between bins, take picked stock, or plan a pick walk (Lines: ITEM:qty; …). Fill in: Warehouse, Action. Press OK and point out the result line and the list on the right.
6. Open **Reorder levels**. An item's reorder level and quantity at one warehouse; replenishment for that warehouse uses them. Fill in: Product, Warehouse, Reorder level, Reorder quantity. Press OK and point out the result line and the list on the right.

## Stock movements
7. Open **Adjust stock (damage, loss, found)**. Sets the counted quantity on hand; the difference posts to the adjustment account under the reason code (DAMAGED, EXPIRED, THEFT…). Fill in: Product, Warehouse, Counted quantity, Date, Adjustment account. Press OK and point out the result line and the list on the right.
8. Open **Send stock to another warehouse**. Sends stock from one warehouse towards another through a transit location; receive it at the other end when it arrives. Fill in: Product, From warehouse, To warehouse, Transit location, Quantity, Date. Press OK and point out the result line and the list on the right.
9. Open **Receive stock from another warehouse**. Books what arrived into the destination; any shortfall is written off the transit location to the loss account. Fill in: Transfer number, Quantity received, Date. Press OK and point out the result line and the list on the right.

## Stock counts
10. Open **Start a stocktake**. Freezes the warehouse's quantities. Fill in: Warehouse, Date, Stocktake reference. Press OK and point out the result line and the list on the right.
11. Open **Start a cycle count (a few items at a time)**. Counts only the items due: never-counted items first, then A items every 30 days, B every 90 and C every 180 (change the days below). Fill in: Warehouse, Date, Count reference, Days between counts - A items, Days between counts - B items, Days between counts - C items, Most items in this count. Press OK and point out the result line and the list on the right.
12. Open **Record a count**. The first count of an item, by the signed-in user. Fill in: Stocktake id, Product, Counted quantity. Press OK and point out the result line and the list on the right.

## Production
13. Open **Make or assemble products**. Consumes the BOM's components (or the usage keyed, ITEM:qty;…) and books the output into stock at their cost. Fill in: BOM code, Warehouse, Planned output, Date, Assembly clearing account. Press OK and point out the result line and the list on the right.
14. Open **Disassemble finished goods**. Takes finished units apart into their components; their cost is split over the components recovered at each one's average cost, or by their relative sales value on the default price list (joint products: a carcass into cuts). Fill in: BOM code, Warehouse, Units taken apart, Date, Assembly clearing account. Press OK and point out the result line and the list on the right.

## Look up
15. Open **Stock on hand and back orders**. An item's stock on hand, allocated to sales orders, on order and free - available to promise; the list shows every back-ordered sales order line and whether stock now covers it. Fill in: Product. Press OK and point out the result line and the list on the right.
16. Open **Trace a batch or serial (recall)**. For a recall: where a batch or serial came from (receipts, with the supplier), where it went (issues and sales, with the customer), and what is still on hand in each warehouse. Fill in: Product, Batch / serial number. Press OK and point out the result line and the list on the right.
17. Open **Suggest picks (first expiry, first out)**. Which batches to pick for a quantity, earliest expiry first, never expired stock. Fill in: Product, Warehouse, Quantity, Date. Press OK and point out the result line and the list on the right.

## Valuation
18. Open **Revalue an item's standard cost**. Sets a new standard cost for a standard-costed item: the stock on hand is revalued at the new standard and the difference posts to the revaluation account. Fill in: Product, New standard cost, Date, Revaluation account. Press OK and point out the result line and the list on the right.
19. Open **Stock write-down provision**. Assesses the IAS 2 provision on stock: down to net realisable value (selling price less cost to sell), slow-moving and obsolete stock by days since last issued; Post books the change against the provision account. Fill in: Step, As at, Cost to sell %, Slow-moving after (days), Slow-moving provision %, Obsolete after (days), Obsolete provision %, Stock provision account. Press OK and point out the result line and the list on the right.

## Reports to end on
- INV-ABC Inventory ABC Analysis: Items ranked by stock value with the cumulative percentage and Pareto class (A/B/C). Show the drill-through by clicking a document or account.
- INV-AGE Stock Ageing: On-hand items with their value and the age band since last movement (0-30 / 31-60 / 61-90 / 90+ days). Show the drill-through by clicking a document or account.
- INV-BACKORDER Back-order Book: Every undespatched sales-order line, oldest order first, with stock on hand allocated to earlier orders so each line shows whether it can be fulfilled and any shortfall. Show the drill-through by clicking a document or account.
