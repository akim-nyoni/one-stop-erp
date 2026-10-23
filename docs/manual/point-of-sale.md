# Point of Sale

The till, refunds and exchanges, promotions, loyalty and gift cards.

Works with: Point of Sale.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Till

### Open the till (touch screen)

Ring up sales at the counter.

### Till pickup / paid-out / float top-up

Cash in or out of a drawer other than by sales; posted, and counted in the cash expected at cash-up. A paid-out needs a different supervisor.

| Field | Required | Notes |
| --- | --- | --- |
| Till session | yes | Choice |
| Kind | yes | One of: Pickup, PaidOut, FloatTopUp; default Pickup |
| Amount | yes | Number |
| Till cash account | yes | Text; default 1000 |
| Safe / bank, expense or float source account | yes | Text |
| Reason | yes | Text |
| Supervisor (user name) | no | Text |
| Date | yes | Date |

The list beside the form shows: Open till sessions.

### Till cash-up position

A blind till's cashier declares the count ("120; ZAR 500; EUR 20") before the cash expected is shown; the position shows expected, counted and the variance per currency (a blind till's only after its count is declared). Blind on / off is per till.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Position, Blind on, Blind off; default Position |
| Till session (position) | no | Choice |
| Till (blind on / off) | no | Choice |

The list beside the form shows: Tills.

### Record a till event

Logs a void, discount, price override, no-sale, parked / resumed sale, reprint, fiscal submission or loyalty points. Overrides and no-sales need a supervisor; voids, discounts and overrides need a reason.

| Field | Required | Notes |
| --- | --- | --- |
| Event | yes | One of: LineVoid, SaleVoid, LineDiscount, PriceOverride, NoSale, ParkedSale, ResumedSale, Reprint, FiscalSubmission, LoyaltyEarned, LoyaltyRedeemed, CashierSwitch, ManualWeight, CashUpSignOff; default LineVoid |
| Till code | yes | Text |
| Date | yes | Date |
| Item / description | yes | Text |
| Amount (value, keyed price, points…) | yes | Number |
| List amount (discounts / overrides) | no | Number |
| Sale number | no | Text |
| Reason | no | Text |
| Authorised by (user name) | no | Text |
| Loyalty card / customer | no | Text |
| Reference (fiscal receipt no.) | no | Text |
| Outcome (fiscal) | no | Text |

The list beside the form shows: Recent till events.

### Discard a parked sale

Sales held at the tills under a name, table or customer tag; resumed at any till, or discarded here with a reason.

| Field | Required | Notes |
| --- | --- | --- |
| Parked sale id (from the list) | yes | Number |
| Reason | yes | Text |

The list beside the form shows: Parked sales.

## Refunds and exchanges

### Refund a sale

Refunds units of a completed sale's line (by item code): revenue and tax back, goods back on the shelf if ticked. The money goes back the way the sale was paid (cash out of the till session, card to the card, on account as a credit to the customer, gift card back onto the card) unless another method is chosen, which needs third-level refund authority. Needs a supervisor with refund authority other than you.

| Field | Required | Notes |
| --- | --- | --- |
| Sale number | yes | Text |
| Product | yes | Choice |
| Quantity | yes | Number |
| Till code | yes | Text |
| Date | yes | Date |
| Paid back by | yes | One of: Original tenders, Cash, Card, MobileMoney, BankTransfer, Cheque; default Original tenders |
| Cash (or other method) paid from (account) | no | Text; default 1000 |
| Output tax account | no | Text; default 2200 |
| Reason | yes | Text |
| Authorised by (user name) | yes | Text |
| Put the goods back in stock | no | Flag; default True |
| Paid from till session (cash out of the drawer) | no | Number |

The list beside the form shows: Recent till events.

### Refund without a receipt

Goods brought back with no receipt: a different supervisor authorises it, within the company's limit; optionally restocked.

| Field | Required | Notes |
| --- | --- | --- |
| Till id | yes | Number |
| Product | yes | Choice |
| Quantity | yes | Number; default 1 |
| Unit price refunded (excl. tax) | yes | Number |
| Tax refunded | yes | Number; default 0 |
| Output tax account | no | Text |
| Paid from (cash) account | yes | Text; default 1000 |
| Reason | yes | Text |
| Supervisor (user name) | yes | Text |
| Restock into warehouse | no | Text |
| Paid from till session (cash) | no | Number |
| Date | yes | Date |

### Exchange goods

Takes back units of a sale's line and sells new goods in one go: the returned value pays towards the new goods and only the difference changes hands (the customer pays it in cash, or is given the excess back). Returns are checked like a refund and need a supervisor other than you.

| Field | Required | Notes |
| --- | --- | --- |
| Original sale number | yes | Text |
| Item coming back | yes | Text |
| Quantity coming back | yes | Number; default 1 |
| Item going out | yes | Text |
| Quantity going out | yes | Number; default 1 |
| Unit price going out (excl. tax) | yes | Number |
| Tax on the new goods | yes | Number; default 0 |
| New sale number | yes | Text |
| Till code | yes | Text |
| Till session | yes | Number |
| Warehouse | yes | Choice |
| Till cash account | yes | Text; default 1000 |
| Output tax account | no | Text; default 2200 |
| Reason | yes | Text |
| Authorised by (user name) | yes | Text |
| Date | yes | Date |

The list beside the form shows: Recent till events.

### How long refunds are allowed

How many days after a sale an ordinary supervisor may authorise its refund (blank: no limit). Past it, the authoriser needs the late-refund authority (Pos.Refund, AuthoriseLevel2).

| Field | Required | Notes |
| --- | --- | --- |
| Days | no | Number |

## Promotions and loyalty

### Promotions

NForPrice (N items for a price), BuyXGetY (no reward items = lowest price free), PercentOff (items or a category), MealDeal (courses separated by '|'). Items as codes separated by ','. An existing code changes that promotion (its kind stays; a stopped one runs again); Delete stops it running (sales already made keep it).

| Field | Required | Notes |
| --- | --- | --- |
| Code | yes | Text |
| Name on receipts | yes | Text |
| Kind | yes | One of: NForPrice, BuyXGetY, PercentOff, MealDeal; default NForPrice |
| Discount account | yes | Text |
| N / X | yes | Number; default 0 |
| Y | yes | Number; default 0 |
| Set price | no | Number |
| Percent off | no | Number |
| Category | no | Text |
| Items | no | Text |
| Reward items | no | Text |
| Meal courses: A,B|C|D | no | Text |
| Priority (lower first) | yes | Number; default 10 |
| Stacks on other promotions | no | Flag; default False |
| Members only | no | Flag; default False |
| For customer types (',', blank: all) | no | Text |
| Coupon code | no | Text |
| Starts | yes | Date |
| Ends | no | Date |
| Happy hour from (HH:mm) | no | Text |
| Happy hour to (HH:mm) | no | Text |

The list beside the form shows: Promotions.

### Promotion results

What each promotion did over a period at the tills: the lines and quantity sold under it, the sales they made and the discount it gave away (voided sales left out).

| Field | Required | Notes |
| --- | --- | --- |
| From | yes | Date |
| To | yes | Date |

The list beside the form shows: Promotions this month.

### Promotional codes

A campaign of discount codes (a % or an amount off, a minimum spend, uses per code, a cap on redemptions, a validity window); quote or redeem a code against a spend; and analyse a campaign (codes used, redemptions, spend and discount given). A campaign may be limited to customer types and to item categories (the minimum spend and discount then count only those lines: give the basket as CATEGORY:amount; …). Create campaign with an existing campaign code changes it (and adds any new codes given; the discount is fixed once a code is redeemed); Delete stops its codes working.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create campaign, Quote, Redeem, Analyse; default Create campaign |
| Campaign code | no | Text |
| Name (create) | no | Text |
| Discount | yes | One of: Percent, Amount; default Percent |
| Discount value | yes | Number; default 0 |
| Valid from | yes | Date |
| Valid to | yes | Date |
| Minimum spend | yes | Number; default 0 |
| Uses per code | yes | Number; default 1 |
| Most redemptions (blank: no cap) | no | Number |
| Codes: A1, A2, … (create) / the code (quote, redeem) | no | Text |
| Spend (quote, redeem) | yes | Number; default 0 |
| Sale reference (redeem) | no | Text |
| Date | yes | Date |
| Customer types (create: who may use it, comma-separated; quote/redeem: the customer's type) | no | Text |
| Item categories (create: what it discounts, comma-separated; quote/redeem: the basket CATEGORY:amount; …) | no | Text |

The list beside the form shows: Promo campaigns.

### Price a basket with promotions

Shows what the running promotions take off a basket at a date and time.

| Field | Required | Notes |
| --- | --- | --- |
| Lines: ITEM:quantity; … (at the default price list) | yes | Text |
| When (yyyy-MM-dd HH:mm) | yes | Text; default 2026-10-23 04:09 |
| Loyalty member | no | Flag; default False |
| Customer type | no | Text |
| Coupon codes (',') | no | Text |
| Unit prices: ITEM:price; … | yes | Text |

### Loyalty

Pick an action. Earning books the points' value as a liability; a redemption's value is tendered at the till on the scheme's liability account.

| Field | Required | Notes |
| --- | --- | --- |
| Action | yes | One of: Statement, Enrol, Earn, Redeem; default Statement |
| Member number | yes | Text |
| Name (enrol) | no | Text |
| Spend (earn) / points (redeem) | no | Number |
| Date | yes | Date |
| Sale reference | no | Text |

The list beside the form shows: Loyalty members.

### Expire loyalty points

Expires every member's points past their expiry (oldest first) and releases the liability.

| Field | Required | Notes |
| --- | --- | --- |
| As at | yes | Date |
| Document number | yes | Text |

## Gift cards

### Create gift card stock

A range of printed cards (prefix plus zero-padded serials), inactive with no value or liability until activated at sale.

| Field | Required | Notes |
| --- | --- | --- |
| Number prefix | yes | Text; default GC |
| First serial | yes | Number; default 1 |
| Last serial | yes | Number |
| Serial digits | yes | Number; default 6 |

The list beside the form shows: Gift card stock.

### Activate a gift card

Loads a card of printed stock with its value when sold (Dr cash, Cr gift card liability).

| Field | Required | Notes |
| --- | --- | --- |
| Card number | yes | Text |
| Value | yes | Number |
| Date | yes | Date |
| Expires (optional) | no | Date |
| Cash account | yes | Text; default 1000 |
| Gift card liability account | yes | Text |

The list beside the form shows: Gift card stock.

### Gift card balance and changes

Services a gift card: its balance and history, a top-up, blocking a lost card and moving its balance to a replacement, and recognising breakage (the unspent balance of one card, or of every expired card) as income.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Balance and history, Top up, Block, Replace, Breakage (one card), Breakage (expired cards); default Balance and history |
| Card number | no | Text |
| Replacement card number | no | Text |
| Top-up amount | yes | Number; default 0 |
| Reason (block) | no | Text |
| Cash account (top up) | no | Text |
| Gift card liability account | no | Text |
| Breakage income account | no | Text |
| Date | yes | Date |

The list beside the form shows: Gift cards.

## Till setup

### Till favourite buttons

Items pinned to the till's ★ Favourites tabs - for one terminal or every till, and optionally for one cashier (their own come first, then the till's, then every till's), on a page (★ Favourites, ★ Favourites 2…) in a colour (#RRGGBB or a name); an item already pinned there has its position, page and colour changed; Delete in the list unpins it. Profile: what kind of counter the terminal is. Function keys: the terminal's F1–F12 (F2=TenderCash; F3=TenderCard; …) over its profile's defaults.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Favourite, Profile, Function keys; default Favourite |
| Product | no | Choice |
| Terminal code (blank: every till) | no | Text |
| Cashier user name (blank: anyone) | no | Text |
| Position | yes | Number; default 10 |
| Page | yes | Number; default 1 |
| Tile colour | no | Text |
| Terminal profile | yes | One of: General, Supermarket, Bar, Butchery, WeighbridgeKiosk, Restaurant; default General |
| Function keys F2=TenderCash; … (blank: the profile's) | no | Text |

The list beside the form shows: Favourites.

### Till product categories

Adds an item category (a new code) or changes one (an existing code: its name, and a retired one comes back into use); places it under another so the till can drill down (blank parent: top level), and sets the costing method items created in it take when none is given. Delete retires a category with no active items.

| Field | Required | Notes |
| --- | --- | --- |
| Category code | yes | Text |
| Name (blank: as it is) | no | Text |
| Parent category code | no | Text |
| Costing method for new items | yes | One of: Keep, WeightedAverage, Fifo, Standard, SpecificIdentification; default Keep |

The list beside the form shows: Item categories.

### Change currency and cash rounding

The currency the till gives change in (blank: base), converted at the day's rate and rounded to its smallest note or coin; the base cash rounding increment (e.g. 0.05); and the account rounding differences go to.

| Field | Required | Notes |
| --- | --- | --- |
| Change currency (blank: base) | no | Text |
| Base cash rounding increment | no | Number |
| Rounding difference account | no | Text |

### Receipt layout and preview

How receipts print: 80mm (48 characters) or 58mm (32) paper, a logo picture printed at the top, the title, header and footer lines (separate lines with |), what is shown and how many copies. Blank till: the company's layout; a till code gives that till its own. Preview shows the receipt as it prints, inside the paper's edges, without saving; Save keeps it (a till or the company that already has a layout has it changed); Delete in the list takes a till's own layout away.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Preview, Save, Show saved, Set logo, Remove logo; default Preview |
| Till code (blank: every till) | no | Text |
| Paper | yes | One of: 80mm, 58mm; default 80mm |
| Logo picture file (PNG, JPG, BMP; set logo) | no | Text |
| Title | no | Text; default SALES RECEIPT |
| Header lines (address | phone | message) | no | Text |
| Footer lines (blank: Thank you!) | no | Text |
| Show the cashier | no | Flag; default True |
| Show the till | no | Flag; default True |
| Show quantity x unit price | no | Flag; default False |
| Show subtotal and tax | no | Flag; default True |
| Show the company's tax number | no | Flag; default False |
| Show the fiscal code | no | Flag; default True |
| Copies | yes | Number; default 1 |

The list beside the form shows: Receipt layouts.

### Receipt and till printers

The printers a workstation uses for each kind of printing (reports, documents, receipts, cheques, labels, payslips): a Windows printer (USB or shared) or a network receipt printer by its IP address and port (usually 9100). Set makes it the purpose's only printer; Add another prints to it as well (a customer and a shop copy, a second counter); Remove all takes every printer of the purpose away; Test prints a test slip. Workstation * is the default for every computer without its own; this computer's name is filled in. Tills print receipts on their workstation's receipt printers (copies from the receipt layout); the report viewer's print dialog opens on the chosen printer. Edit opens a printer in the form (Set or Add another saves the change); Delete on the list removes that one printer.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Set, Add another, Remove all, Test; default Set |
| Workstation (computer name, or *) | yes | Text; default BLACKBOX |
| Purpose | yes | One of: Reports, Documents, Receipts, Cheques, Labels, Payslips; default Reports |
| Connection | yes | One of: Windows printer, Network (IP address); default Windows printer |
| Printer (Windows name, or a name for a network printer) | no | Choice |
| Network printer IP address | no | Text |
| Network printer port | yes | Number; default 9100 |
| Tray (Auto, Manual, Cassette, Tractor) | no | Text |
| Copies | yes | Number; default 1 |

The list beside the form shows: Printers.

### Cashier cards and fingerprints

Issues a user the cashier card they swipe or scan to sign in at the till and to authorise (swipe it into the card box; only its hash is kept), or links them to their fingerprint reader enrolment. Blank removes either.

| Field | Required | Notes |
| --- | --- | --- |
| Login name | yes | Text |
| Set | yes | One of: Card, Fingerprint; default Card |
| Card swipe / enrolment id | no | Text |

The list beside the form shows: Users and their roles.

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| POS-SUM | POS Sales Summary | Point-of-sale transactions with net, tax and gross for a period. |
| POS102 | POS Z-Read / X-Read by Till & Cashier | Every till session in the period: till, cashier, opened and closed, Z-read number (open sessions are an X-read so far), float, sales, and the takings per tender and currency. |
| POS103 | Daily Sales Summary | Per day, till and cashier: sales, voids, net, tax, the tender split and gross takings. |
| POS104 | Cashier Performance | Per cashier over the period: sessions, sales, items, average basket, voids, cash-up variance and net sales. |
| POS105 | Till Variance & Cash-up Report | Each closed till session: float, cash taken, change given, expected, counted and the over or short. |
| POS106 | Void & Cancellation Analysis by Reason | Lines and sales voided in the period with who, why and who authorised it. |
| POS107 | Refunds & Returns Register | Every refund in the period: the sale and line, quantity, reason, authoriser, journal and whether the goods were restocked. |
| POS108 | Line Discounts Given | Discounts keyed on lines in the period, with the list amount, the rate and the reason, by cashier. |
| POS109 | Price Overrides | Prices keyed over list in the period: list and keyed price, the difference, reason and the authorising supervisor. |
| POS110 | No-Sale Drawer Openings | Every drawer opened without a sale in the period, with the reason and authoriser, and each cashier's count. |
| POS111 | Parked / Suspended Sales | Sales parked in the period and whether they were resumed or abandoned, with the time they sat. |
| POS112 | Hourly Sales Heatmap & Basket Analysis | Sales by weekday and hour: count, items per basket, average basket and net. |
| POS113 | Product Mix Ranking | Items ranked by POS net sales in the period, with quantity, share and cumulative share. |
| POS114 | Promotion Effectiveness | Each promotion's redemptions in the period, the spend they drove and the discount given. |
| POS115 | Loyalty Points Statement | Per loyalty customer: points earned and redeemed in the period and the balance to date. |
| POS116 | Receipt Reprint Log | Every receipt reprinted in the period: when, by whom, the sale and its value. |
| POS117 | Tender & Multi-currency Tender Analysis | Tenders taken in the period by type and currency, in currency and base, net of change. |
| POS118 | Table / Tab Report | Tabs opened in the period: those still open with their table, waiter, party and spend so far, then per waiter the tabs, average minutes from opening to the bill, average spend (before tips) and tips. |
| POS119 | Kitchen / Bar Ticket Volumes & Void-after-Print | Per day and course: tickets sent to the kitchen or bar, items on them, how many reached the table, and items voided after they were sent, with the value voided and the reasons. |
| POS120 | Offline Sync Log | Operations captured offline in the period: kind, status, attempts, when synced and any error. |
| POS121 | Fiscal Submission Log | Sales submitted to the fiscal device or revenue authority in the period, with the fiscal receipt reference and outcome. |
| POS122 | Receipt / Invoice / Refund Slip Layouts | A sale's slip as printed on 58 mm or 80 mm roll paper or an A4 page, with any refunds taken against it as a refund slip below. |
| POS123 | Allowance Vouchers Outstanding | Fuel, meal and other allowance vouchers issued and not yet redeemed or cancelled as at the To date, per holder and kind, with how many of them have expired. |
| POS124 | Gift Card Liability by Age | Every gift card still carrying value as at the To date, aged from issue (0–90, 91–180, 181–365, over 365 days) with its expiry and status: the liability on the books, and where breakage is likely. |
