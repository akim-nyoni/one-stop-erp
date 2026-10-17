# Demo: Point of Sale

Goal: show that the till, refunds and exchanges, promotions, loyalty and gift cards work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Till
1. Open **Open the till (touch screen)**. Ring up sales at the counter.
2. Open **Till pickup / paid-out / float top-up**. Cash in or out of a drawer other than by sales; posted, and counted in the cash expected at cash-up. Fill in: Till session id (from the list), Kind, Amount, Till cash account, Safe / bank, expense or float source account, Reason, Date. Press OK and point out the result line and the list on the right.
3. Open **Till cash-up position**. A blind till's cashier declares the count ("120; ZAR 500; EUR 20") before the cash expected is shown; the position shows expected, counted and the variance per currency (a blind till's only after its count is declared). Fill in: Step. Press OK and point out the result line and the list on the right.

## Refunds and exchanges
4. Open **Refund a sale**. Refunds units of a completed sale's line (by item code): revenue and tax back, money out of the refund account, goods back on the shelf if ticked. Fill in: Sale number, Product, Quantity, Till code, Date, Refund paid from (account), Reason, Authorised by (user name). Press OK and point out the result line and the list on the right.
5. Open **Refund without a receipt**. Goods brought back with no receipt: a different supervisor authorises it, within the company's limit; optionally restocked. Fill in: Till id, Product, Quantity, Unit price refunded (excl. tax), Tax refunded, Paid from (cash) account, Reason, Supervisor (user name), Date. Press OK and point out the result line and the list on the right.
6. Open **Exchange goods**. Takes back units of a sale's line and sells new goods in one go: the returned value pays towards the new goods and only the difference changes hands (the customer pays it in cash, or is given the excess back). Fill in: Original sale number, Item coming back, Quantity coming back, Item going out, Quantity going out, Unit price going out (excl. tax), Tax on the new goods, New sale number, Till code, Till session, Warehouse, Till cash account, Reason, Authorised by (user name), Date. Press OK and point out the result line and the list on the right.

## Promotions and loyalty
7. Open **Promotions**. NForPrice (N items for a price), BuyXGetY (no reward items = lowest price free), PercentOff (items or a category), MealDeal (courses separated by '|'). Fill in: Code, Name on receipts, Kind, Discount account, N / X, Y, Priority (lower first), Starts. Press OK and point out the result line and the list on the right.
8. Open **Promotional codes**. A campaign of discount codes (a % or an amount off, a minimum spend, uses per code, a cap on redemptions, a validity window); quote or redeem a code against a spend; and analyse a campaign (codes used, redemptions, spend and discount given). Fill in: Step, Discount, Discount value, Valid from, Valid to, Minimum spend, Uses per code, Spend (quote, redeem), Date. Press OK and point out the result line and the list on the right.
9. Open **Price a basket with promotions**. Shows what the running promotions take off a basket at a date and time. Fill in: Lines: ITEM:quantity; … (at the default price list), When (yyyy-MM-dd HH:mm), Unit prices: ITEM:price; …. Press OK and point out the result line and the list on the right.

## Gift cards
10. Open **Create gift card stock**. A range of printed cards (prefix plus zero-padded serials), inactive with no value or liability until activated at sale. Fill in: Number prefix, First serial, Last serial, Serial digits. Press OK and point out the result line and the list on the right.
11. Open **Activate a gift card**. Loads a card of printed stock with its value when sold (Dr cash, Cr gift card liability). Fill in: Card number, Value, Date, Cash account, Gift card liability account. Press OK and point out the result line and the list on the right.
12. Open **Gift card balance and changes**. Services a gift card: its balance and history, a top-up, blocking a lost card and moving its balance to a replacement, and recognising breakage (the unspent balance of one card, or of every expired card) as income. Fill in: Step, Top-up amount, Date. Press OK and point out the result line and the list on the right.

## Till setup
13. Open **Till favourite buttons**. Items pinned to the till's ★ Favourites tabs — for one terminal or every till, and optionally for one cashier (their own come first, then the till's, then every till's), on a page (★ Favourites, ★ Favourites 2…) in a colour (#RRGGBB or a name). Fill in: Step, Position, Page, Terminal profile. Press OK and point out the result line and the list on the right.
14. Open **Till product categories**. Places a category under another so the till can drill down (blank parent: top level), and sets the costing method items created in it take when none is given. Fill in: Category code, Costing method for new items. Press OK and point out the result line and the list on the right.
15. Open **Change currency and cash rounding**. The currency the till gives change in (blank: base), converted at the day's rate and rounded to its smallest note or coin; the base cash rounding increment (e.g. Press OK and point out the result line and the list on the right.

## Reports to end on
- POS-SUM POS Sales Summary: Point-of-sale transactions with net, tax and gross for a period. Show the drill-through by clicking a document or account.
- POS102 POS Z-Read / X-Read by Till & Cashier: Every till session in the period: till, cashier, opened and closed, Z-read number (open sessions are an X-read so far), float, sales, and the takings per tender and currency. Show the drill-through by clicking a document or account.
- POS103 Daily Sales Summary: Per day, till and cashier: sales, voids, net, tax, the tender split and gross takings. Show the drill-through by clicking a document or account.
