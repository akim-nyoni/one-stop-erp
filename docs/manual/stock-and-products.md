# Stock & Products

Products and services, warehouses, stock movements, counts and production.

Works with: Stock.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Products

### Add a product or service

A stock, service or other item with the accounts its sales and cost post to, how its stock is costed (weighted average, FIFO, standard with its variance accounts, or specific identification), batch or serial tracking and a shelf life.

| Field | Required | Notes |
| --- | --- | --- |
| Item code | yes | Text |
| Description | yes | Text |
| Type | yes | One of: Stock, NonStock, Service, Kit, Weighed, Fuel, Deposit; default Stock |
| Revenue account | no | Text |
| Cost of sales account | no | Text |
| Costing method | yes | One of: WeightedAverage, Fifo, Standard, SpecificIdentification; default WeightedAverage |
| Standard cost | no | Number |
| Price variance account (standard) | no | Text |
| Usage variance account (standard) | no | Text |
| Tracking | yes | One of: None, Batch, Serial; default None |
| Shelf life (days) | no | Number |
| Category | no | Text |

The list beside the form shows: Items.

### Find a product

Searches codes, PLUs, barcodes (item, pack, alias), synonyms, brands and descriptions.

| Field | Required | Notes |
| --- | --- | --- |
| Search for | yes | Text |

### Product picture, description and size

An item's long description, its length, width and height in centimetres (the volume follows) and weight, and its picture (a JPEG, PNG, GIF or WebP file of at most 1 MB; Remove picture drops it).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Details, Picture, Remove picture; default Details |
| Product | yes | Choice |
| Long description | no | Text |
| Length (cm) | no | Number |
| Width (cm) | no | Number |
| Height (cm) | no | Number |
| Weight (kg) | no | Number |
| Picture file | no | Text |

The list beside the form shows: Items.

### Product barcodes, packs and suppliers

Barcode / Synonym: the value. Pack: unit code, factor and its barcode. Supplier: supplier code, their item code, lead days, minimum order, last cost, preferred.

| Field | Required | Notes |
| --- | --- | --- |
| Product | yes | Choice |
| Add | yes | One of: Barcode, Synonym, Pack, Supplier; default Barcode |
| Barcode / synonym / pack unit / supplier code | no | Text |
| Pack factor | no | Number |
| Pack barcode / supplier's item code | no | Text |
| Lead time (days) | no | Number |
| Minimum order | no | Number |
| Last cost | no | Number |
| Preferred supplier | no | Flag; default False |

### Product sale limits

Age-restricted items need the till's age check, a per-sale limit caps the quantity in one sale, and prescription-only items need a prescription reference.

| Field | Required | Notes |
| --- | --- | --- |
| Product | yes | Choice |
| Minimum age | no | Number |
| Most per sale | no | Number |
| Prescription only | no | Flag; default False |

The list beside the form shows: Items with sale limits.

### Sell a product by weight

Counted in pieces and weighed: receipts need their weight (and may be costed per weight unit), the average weight per piece must be within the tolerance of the nominal weight, and issues are costed by the weight that leaves. Leave the unit blank to make the item ordinary again (only with no stock on hand).

| Field | Required | Notes |
| --- | --- | --- |
| Product | yes | Choice |
| Weight unit (kg, t…) | no | Text; default kg |
| Nominal weight per piece | no | Number |
| Tolerance % | no | Number |

### Print shelf labels

Prints shelf labels at a price list's prices (all items, or a comma-separated list).

| Field | Required | Notes |
| --- | --- | --- |
| Price list code | yes | Text |
| Item codes (comma-separated) | no | Text |
| Labels per row | yes | Number; default 2 |

## Warehouses

### Add a warehouse

A stock location of the current company. An existing code changes that warehouse.

| Field | Required | Notes |
| --- | --- | --- |
| Code | yes | Text |
| Name | yes | Text |

The list beside the form shows: Warehouses.

### Bins and shelves

Add a bin (sequence, capacity), put stock away (never more than is unbinned), move between bins, take picked stock, or plan a pick walk (Lines: ITEM:qty; …).

| Field | Required | Notes |
| --- | --- | --- |
| Warehouse | yes | Choice |
| Action | yes | One of: Pick walk, Add bin, Put away, Move, Take; default Pick walk |
| Bin (from) | no | Text |
| To bin | no | Text |
| Product | no | Choice |
| Quantity | no | Number |
| Pick sequence (add) | no | Number |
| Capacity (add) | no | Number |
| Pick lines: ITEM:qty; … | no | Text |

The list beside the form shows: Bin contents.

### Reorder levels

An item's reorder level and quantity at one warehouse; replenishment for that warehouse uses them.

| Field | Required | Notes |
| --- | --- | --- |
| Product | yes | Choice |
| Warehouse | yes | Choice |
| Reorder level | yes | Number |
| Reorder quantity | yes | Number; default 0 |

### Replenishment policy

Min/max and forecast: at or below the reorder level (on hand plus on order) a reorder is suggested, up to the maximum if set, else the reorder quantity. With a forecast window, the reorder level rises to the average daily issues over that window times the lead time - scaled, with seasonal factors, by the factor for the month the lead time ends in (twelve numbers, January first).

| Field | Required | Notes |
| --- | --- | --- |
| Product | yes | Choice |
| Reorder level (minimum) | yes | Number; default 0 |
| Reorder quantity | yes | Number; default 0 |
| Maximum stock | yes | Number; default 0 |
| Lead time (days) | yes | Number; default 0 |
| Forecast window (days, 0 = none) | yes | Number; default 0 |
| Seasonal factors Jan…Dec (e.g. 0.8,0.8,1,…) | no | Text |

The list beside the form shows: Replenishment policies.

### Stock replenishment

What to reorder or move between warehouses, and raise it.

### Consignment stock

Goods we hold that are not ours: a supplier's consignment stock (with the agreed price) or a customer's goods in our keeping. They stay out of our stock value. Return hands them back; Use (supplier-owned only) takes them into our stock at the agreed price and raises the supplier's invoice through the clearing account.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Take in, Return, Use; default Take in |
| Owner is a | yes | One of: Supplier, Customer; default Supplier |
| Supplier or customer code | no | Text |
| Product | no | Choice |
| Location code | no | Text |
| Quantity | yes | Number; default 1 |
| Agreed price (supplier-owned) | no | Number |
| Holding id | no | Number |
| Reference | no | Text |
| Consignment clearing account (use) | no | Text |
| Date | yes | Date |

The list beside the form shows: Goods held for others.

## Stock movements

### Adjust stock (damage, loss, found)

Sets the counted quantity on hand; the difference posts to the adjustment account under the reason code (DAMAGED, EXPIRED, THEFT…).

| Field | Required | Notes |
| --- | --- | --- |
| Product | yes | Choice |
| Warehouse | yes | Choice |
| Counted quantity | yes | Number |
| Date | yes | Date |
| Adjustment account | yes | Text; default 5000 |
| Reason code | no | Text |
| Reference | no | Text |

The list beside the form shows: Reason codes.

### Send stock to another warehouse

Sends stock from one warehouse towards another through a transit location; receive it at the other end when it arrives.

| Field | Required | Notes |
| --- | --- | --- |
| Product | yes | Choice |
| From warehouse | yes | Text |
| To warehouse | yes | Text |
| Transit location | yes | Text |
| Quantity | yes | Number |
| Date | yes | Date |

The list beside the form shows: Transfers in transit.

### Receive stock from another warehouse

Books what arrived into the destination; any shortfall is written off the transit location to the loss account.

| Field | Required | Notes |
| --- | --- | --- |
| Transfer number | yes | Text |
| Quantity received | yes | Number |
| Date | yes | Date |
| Loss account (if short) | no | Text; default 5000 |

The list beside the form shows: Transfers in transit.

### Store requisitions

A department requisitions stock from the store (ITEM:quantity; …) charged to an account and cost centre; another user approves it; the store issues it, in full or part (lines to issue now, blank for everything outstanding).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Raise, Approve, Issue; default Raise |
| Requisition number (approve / issue) | no | Text |
| Store (warehouse) code | no | Text |
| Charge account (raise) | no | Text |
| Cost centre | no | Text |
| Lines: ITEM:quantity; … | no | Text |
| Date | yes | Date |

The list beside the form shows: Requisitions.

### Record batches / serials on a movement

Records which batches or serial numbers a receipt, issue or transfer moved. Lots: BATCH:qty[:expiry yyyy-MM-dd]; separated by semicolons (serials are one each).

| Field | Required | Notes |
| --- | --- | --- |
| Movement id (from the list) | yes | Number |
| Lots | yes | Text; default B1:10 |

The list beside the form shows: Recent movements of batch / serial items (without lots).

## Stock counts

### Start a stocktake

Freezes the warehouse's quantities. Counters see only the items, never the system quantity. Counts outside the tolerance must be recounted by someone else.

| Field | Required | Notes |
| --- | --- | --- |
| Warehouse | yes | Choice |
| Date | yes | Date |
| Stocktake reference | yes | Text |
| Recount tolerance % (blank: none) | no | Number |

The list beside the form shows: Blind count sheets (draft stocktakes).

### Start a cycle count (a few items at a time)

Counts only the items due: never-counted items first, then A items every 30 days, B every 90 and C every 180 (change the days below). Classify items A / B / C first so the busiest items are counted most often. Count, recount and post it like a stocktake.

| Field | Required | Notes |
| --- | --- | --- |
| Warehouse | yes | Choice |
| Date | yes | Date |
| Count reference | yes | Text |
| Days between counts - A items | yes | Number; default 30 |
| Days between counts - B items | yes | Number; default 90 |
| Days between counts - C items | yes | Number; default 180 |
| Most items in this count | yes | Number; default 50 |
| Recount tolerance % (blank: none) | no | Number |

The list beside the form shows: Blind count sheets (draft stocktakes).

### Record a count

The first count of an item, by the signed-in user.

| Field | Required | Notes |
| --- | --- | --- |
| Stocktake id | yes | Number |
| Product | yes | Choice |
| Counted quantity | yes | Number |

The list beside the form shows: Blind count sheets (draft stocktakes).

### Record a recount

A recount of an item flagged as outside the tolerance, by someone other than the first counter.

| Field | Required | Notes |
| --- | --- | --- |
| Stocktake id | yes | Number |
| Product | yes | Choice |
| Recounted quantity | yes | Number |

The list beside the form shows: Blind count sheets (draft stocktakes).

### Post a stocktake

Adjusts stock to the counts at average cost. Refused while recounts are due.

| Field | Required | Notes |
| --- | --- | --- |
| Stocktake id | yes | Number |
| Date | yes | Date |
| Stock adjustment account | yes | Text |

The list beside the form shows: Blind count sheets (draft stocktakes).

## Production

### Make or assemble products

Consumes the BOM's components (or the usage keyed, ITEM:qty;…) and books the output into stock at their cost.

| Field | Required | Notes |
| --- | --- | --- |
| BOM code | yes | Text |
| Warehouse | yes | Choice |
| Planned output | yes | Number |
| Actual output (if different) | no | Number |
| Date | yes | Date |
| Assembly clearing account | yes | Text |
| Actual usage ITEM:qty;… (optional) | no | Text |

The list beside the form shows: Bills of materials.

### Disassemble finished goods

Takes finished units apart into their components; their cost is split over the components recovered at each one's average cost, or by their relative sales value on the default price list (joint products: a carcass into cuts).

| Field | Required | Notes |
| --- | --- | --- |
| BOM code | yes | Text |
| Warehouse | yes | Choice |
| Units taken apart | yes | Number |
| Date | yes | Date |
| Assembly clearing account | yes | Text |
| Recovered ITEM:qty;… (if not the BOM quantities) | no | Text |
| Split by sales value | no | Flag; default False |

The list beside the form shows: Bills of materials.

## Look up

### Stock on hand and back orders

An item's stock on hand, allocated to sales orders, on order and free - available to promise; the list shows every back-ordered sales order line and whether stock now covers it.

| Field | Required | Notes |
| --- | --- | --- |
| Product | yes | Choice |
| Warehouse code (blank: all) | no | Text |

The list beside the form shows: Back orders.

### Trace a batch or serial (recall)

For a recall: where a batch or serial came from (receipts, with the supplier), where it went (issues and sales, with the customer), and what is still on hand in each warehouse.

| Field | Required | Notes |
| --- | --- | --- |
| Product | yes | Choice |
| Batch / serial number | yes | Text |

### Suggest picks (first expiry, first out)

Which batches to pick for a quantity, earliest expiry first, never expired stock.

| Field | Required | Notes |
| --- | --- | --- |
| Product | yes | Choice |
| Warehouse | yes | Choice |
| Quantity | yes | Number |
| Date | yes | Date |

### Classify products A / B / C

A: the items making the first 80% of the value issued in the period, B the next 15%, C the rest and unmoved stock.

| Field | Required | Notes |
| --- | --- | --- |
| From | yes | Date |
| To | yes | Date |

## Valuation

### Revalue an item's standard cost

Sets a new standard cost for a standard-costed item: the stock on hand is revalued at the new standard and the difference posts to the revaluation account.

| Field | Required | Notes |
| --- | --- | --- |
| Product | yes | Choice |
| New standard cost | yes | Number |
| Date | yes | Date |
| Revaluation account | yes | Text |

### Stock write-down provision

Assesses the IAS 2 provision on stock: down to net realisable value (selling price less cost to sell), slow-moving and obsolete stock by days since last issued; Post books the change against the provision account.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Assess, Post; default Assess |
| As at | yes | Date |
| Cost to sell % | yes | Number; default 10 |
| Slow-moving after (days) | yes | Number; default 180 |
| Slow-moving provision % | yes | Number; default 25 |
| Obsolete after (days) | yes | Number; default 365 |
| Obsolete provision % | yes | Number; default 100 |
| Stock provision account | yes | Text |
| Write-down expense account (post) | no | Text |

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| INV-ABC | Inventory ABC Analysis | Items ranked by stock value with the cumulative percentage and Pareto class (A/B/C). |
| INV-AGE | Stock Ageing | On-hand items with their value and the age band since last movement (0-30 / 31-60 / 61-90 / 90+ days). |
| INV-BACKORDER | Back-order Book | Every undespatched sales-order line, oldest order first, with stock on hand allocated to earlier orders so each line shows whether it can be fulfilled and any shortfall. |
| INV-ITEMS | Item Master | Stock item master: code, description, unit, reorder level, default accounts and active status. |
| INV-MOVE | Stock Movements | Every stock movement over a period - receipts, issues, transfers, adjustments and reversals. |
| INV-REORDER | Reorder Report | Items at or below their reorder level, with the suggested reorder quantity - a purchasing aid. |
| INV-SLOW | Slow-moving Stock | Items on hand with no movement since the given date - capital tied up in dead stock. |
| INV-VAL | Stock Valuation | Quantity on hand and value per item and warehouse, with the item's category and costing method - now, or as at any earlier date (from the stock movements to it). |
| INV100 | Wastage / Shrinkage Report | Stock written down in the period through adjustments, by department (item category) and reason, against the department's sales. |
| INV101 | Consignment Stock | Our stock held at customers' sites on consignment, per customer and item, with the last movement and value at average cost. |
| INV78 | Stock Valuation vs GL Stock Control | The stock register's value at the date by warehouse against the stock control accounts, with the difference. |
| INV79 | Stock on Hand & Availability | Per item and warehouse: on hand, committed to orders, on order, free and available to promise. |
| INV81 | Stocktake Count Sheets & Variance | Each stocktake's lines: system quantity, counted, variance in quantity and value (uncounted lines flagged). |
| INV82 | Stock Adjustments by Reason Code | Adjustments in the period per reason code: count, units gained and lost, and the net value. |
| INV86 | Overstock Report | Items holding more than their reorder level plus reorder quantity (or more than 90 days of issues), with the excess value. |
| INV87 | Negative Stock Report | Item and warehouse balances below zero - an integrity exception to clear. |
| INV88 | Batch / Lot Stock Report | Batch-tracked stock on hand at the date per item, warehouse and batch, with first receipt, expiry and value at average cost. |
| INV89 | Serial Number Register | Every serial number: where it is, when it came in and when and how it last moved, and whether it is still in stock. |
| INV90 | Expiry Report | Batches on hand that have expired or expire within the window, soonest first, with the value at risk. |
| INV91 | Goods in Transit | Transfers dispatched but not yet received at the date: from, to, days on the road, quantity and value. |
| INV92 | Stock Turnover & Days Cover | Per item over the period: cost of issues, average stock value, turnover and days of cover at the current rate. |
| INV93 | Gross Margin by Item | Per item over the period: net sales (POS and invoiced orders), cost of issues and the margin. |
| INV95 | Item Master Listing & Variant Matrix | Every item with its type, unit, category, brand, barcode, tax, costing, tracking and reorder settings; variants sit under their parent with size and colour, and each parent shows its size × colour matrix. |
| INV96 | Cost Change History | Each receipt at a unit cost different from the item's previous receipt, with the change. |
| INV97 | Landed Cost Analysis | Landed costs added to stock in the period, per item and reference, against the goods' own receipt value. |
| INV98 | Bill of Materials Costing | Each BOM's components with scrap allowance and current average cost, rolled up to a cost per finished unit. |
| INV99 | Production Yield & Usage Variance | Assembly runs in the period: planned and actual output, yield, and each component's standard against actual usage and cost. |
