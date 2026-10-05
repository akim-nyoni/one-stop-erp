# Report Catalogue Backlog (brief §14)

Gap analysis of brief §14 against `docs/reports/CATALOGUE.md` (2026-10-05). Housing (I-B, the H series) is complete and
omitted. **Data exists** = the entities / services to build the report are already in the system; anything else names the
missing domain data. Items are struck through as they ship (with the report code).

## A. General Ledger / Financial
- ~~2 Multi-currency TB (GL2)~~, ~~5 Journal Listing & Audit (GL5)~~, ~~8 Changes in Equity (GL8)~~, ~~12 Budget vs Actual vs Commitment (GL12)~~, ~~15 FX Revaluation (GL15)~~, ~~16 Realised FX (GL16)~~, ~~17 Suspense & Unallocated (GL17)~~, ~~22 Recurring Due / Missed (GL22)~~, ~~23 Accruals, Prepayments & Deferred Income (GL23)~~, ~~24 Intercompany (GL24)~~
- ~~10 Notes-to-Accounts Schedules (GL10)~~, ~~11 Management Accounts Pack (GL11)~~
- ~~14 Consolidation Elimination Schedule (GL14)~~, ~~18 VAT / bank control legs (GL18)~~, ~~20 Year-End Pack (GL20)~~, ~~25 Hyperinflation Working Papers (GL25)~~

## A-B. Budgeting (B codes)
- ~~B2~~, ~~B12~~, ~~B15~~, ~~B16 / B17 (one register with seal check)~~, ~~B18~~, ~~B20~~, ~~B24~~, ~~B25~~, ~~B26~~, ~~B28~~, ~~B33~~, ~~B34~~, ~~B36~~, ~~B37~~, ~~B44~~, ~~B45~~
- Data exists, partly: B14, B19, B21, B27, B42
- Needs new data: B1, B3–B10 (workflow, targets, hierarchy, drivers, establishment, capital projects), B13 (stage snapshots), B22, B23, B29, B30, B32 (budget holders), B35, B38–B41 (forecasts, cash budget)

## B. Accounts Receivable
- ~~29 Aged detail (AR29)~~, ~~30 by rep / currency (AR30)~~, ~~31 Customer Ledger (AR31)~~, ~~33 Sales Day Book (AR33)~~, ~~35 Top Customers (AR35)~~, ~~39 / 44 Unallocated & advances (AR39)~~, ~~40 DSO (AR40)~~, ~~41 Promise-to-Pay (AR41)~~, ~~43 Write-offs (AR43)~~, ~~45 Lay-byes (AR45)~~, ~~47 Exposure by Currency (AR47)~~, ~~48 New & Dormant (AR48)~~
- ~~34 Sales Analysis with Margin (AR34)~~, ~~36 Sales History & Trend (AR36)~~, ~~49 price lists (SYS258)~~
- Data exists: 38 collections (all receipts), 50 layouts (quotation, SO confirmation, delivery note, remittance slip)
- Needs new data: 28 (parent / child customers), 37 reason codes

## C. Accounts Payable
- ~~53 Supplier Ledger (AP53)~~, ~~54 Purchase Day Book (AP54)~~, ~~57 / 58 Payments Made incl. runs (AP58)~~, ~~59 GRNI (AP59)~~, ~~60 Matching Exceptions (AP60)~~, ~~62 WHT Deducted (AP62)~~, ~~63 Scorecard (AP63)~~, ~~64 Balances with debit flags (AP64)~~, ~~65 Bank Detail Change Audit (AP65)~~, ~~66 Top Suppliers (AP66)~~
- ~~55 Purchase Analysis (AP55)~~
- Data exists: 67 layouts (requisition, GRN, debit note)
- Needs new data: 61 (supplier's own statement capture), cheque entity

## D. Cash Book / Treasury
- ~~68 Cash Book Listing (CB68)~~, ~~70 Unpresented & Uncleared (CB70)~~, ~~72 13-week forecast by currency (CB72)~~, ~~73 Petty Cash & Imprest (CB73)~~, ~~74 Inter-account Transfers (CB74)~~
- Needs new data: 75 cheque register, 76 wallet statements

## E. Inventory
- ~~78 Valuation vs GL (INV78)~~, ~~79 Stock on Hand & Availability (INV79)~~, ~~81 Stocktake Variance (INV81)~~, ~~86 Overstock (INV86)~~, ~~87 Negative Stock (INV87)~~, ~~92 Turnover & Days Cover (INV92)~~, ~~93 Gross Margin by Item (INV93)~~, ~~96 Cost Change History (INV96)~~, ~~97 Landed Cost Analysis (INV97)~~
- Data exists: 94 (labels, a print layout), 100 (partly)
- ~~82 Adjustments by Reason (INV82)~~, ~~88 Batch Stock (INV88)~~, ~~89 Serial Register (INV89)~~, ~~90 Expiry (INV90)~~
- ~~91 Goods in Transit (INV91)~~, ~~101 Consignment Stock (INV101)~~
- ~~98 Bill of Materials Costing (INV98)~~, ~~99 Production Yield & Usage Variance (INV99)~~

## F. POS / Retail
- ~~103 Daily Sales Summary (POS103)~~, ~~104 Cashier Performance (POS104)~~, ~~105 Till Variance & Cash-up (POS105)~~, ~~112 Hourly Heatmap & Basket (POS112)~~, ~~113 Product Mix (POS113)~~, ~~114 Promotion Effectiveness (POS114)~~, ~~117 Tender Analysis (POS117)~~, ~~120 Offline Sync Log (POS120)~~
- ~~106 Voids by Reason (POS106)~~, ~~107 Refunds & Returns (POS107)~~, ~~108 Line Discounts (POS108)~~, ~~109 Price Overrides (POS109)~~, ~~110 No-Sale (POS110)~~, ~~111 Parked Sales (POS111)~~, ~~115 Loyalty (POS115)~~, ~~116 Reprint Log (POS116)~~, ~~121 Fiscal Submission Log (POS121)~~

## G. Procurement
- ~~124 Requisition turnaround (PO124)~~, ~~125 Approval history (PO125)~~, ~~126 / 127 PO listing with overdue (PO126)~~, ~~128 Committed cost (PO128)~~, ~~129 GRN & variance (PO129)~~, ~~130 Rejected goods (PO130)~~, ~~131 RFQ comparison (PO131)~~, ~~132 Price variance (PO132)~~, ~~134 Spend analysis (PO134)~~, ~~135 Lead times (PO135)~~
- Data exists: 136 budget availability at requisition (needs an expense account on requisition lines); partly 133 (no framework contracts)

## H. Sales Order Processing
- ~~137 Open Orders & Allocation (SO137)~~, ~~143 Quotation Conversion (SO143)~~
- Data exists: 139 packing / despatch notes (layouts)
- Needs new data: 140 OTIF dates, 141 POD, 142 vehicles / routes

## I. Municipality (the M series)
- ~~146 Billing Run Summary (M146)~~, ~~147 Billing Exceptions (M147)~~, ~~148 Aged Debtors by Ward / Category / Service (M148)~~, ~~151 Valuation Roll (M151)~~, ~~152 Property Register (M152)~~, ~~153 Clearance Register (M153)~~, ~~154 Reading Sheets (M154)~~, ~~155 Reading Exceptions (M155)~~, ~~156 Consumption (M156)~~, ~~159 Meter Register (M159)~~, ~~160 Disconnection / Reconnection (M160)~~, ~~161 Arrangements (M161)~~, ~~162 Indigent & Subsidy (M162)~~, ~~166 Licences (M166)~~, ~~167 Ticket SLA (M167)~~, ~~168 Tariff Schedule (M168)~~, ~~149 Arrears (M149)~~, ~~163 Interest (M163)~~, ~~164 Write-offs (M164)~~, ~~169 Revenue by Source (M169)~~, ~~170 Ward Pack (M170)~~, ~~171 Statutory Age Analysis (M171)~~, ~~172 Prepaid vs Conventional (M172)~~
- Data exists: 145 bill layout (a document template), 158 (partly)
- Needs new data: 157 water balance (bulk supply readings)

## J. Schools
- ~~175 Fee Billing Summary (SC175)~~, ~~176 Arrears by Grade (SC176)~~, ~~177 Collections (SC177)~~, ~~178 Bursaries (SC178)~~, ~~180 Enrolment (SC180, no gender)~~, ~~182 Student Balances (SC182)~~
- Data exists: 174 fee invoice layout, 181 deferred fee income; partly 184
- Needs new data: 179 sponsors, 183 transport, 185 student wallet, streams

## K. Construction / Job Costing
- ~~186 Job Cost by Head (JC186)~~, ~~189 Cost to Complete (JC189)~~, ~~190 Retention (JC190, no defects date)~~, ~~197 Contract Cash Flow (JC197)~~
- Data exists: 188 IPC layout; partly 195
- ~~191 Variation Order Register (JC191)~~, ~~193 Labour & Timesheet Cost (JC193)~~, ~~196 BOQ vs Measured (JC196)~~
- ~~192 Subcontractor Ledger & Retention (JC192)~~, ~~194 Plant Utilisation & Hire Recovery (JC194)~~

## L. Weighbridge
- ~~198 Weighbridge Ticket (WB198)~~, ~~199 Daily Register & Tonnage (WB199)~~, ~~200 Vehicle Turnaround (WB200)~~, ~~201 Deliveries by Customer / Site (WB201)~~, ~~202 Manual Weight Overrides (WB202)~~, ~~203 Stockpile Reconciliation (WB203)~~, ~~204 Concrete Batch Report (WB204)~~, ~~205 Haulage Charge Analysis (WB205)~~

## M. Fixed Assets
- ~~207 Depreciation Schedule (FA207)~~, ~~208 Additions & Transfers (FA208)~~, ~~209 Movement Schedule (FA209)~~, ~~212 Fully Depreciated in Use (FA212)~~
- ~~210 Asset Verification (FA210)~~, ~~213 Insurance & Warranty Expiry (FA213)~~, ~~214 Maintenance Schedule & History (FA214)~~
- ~~211 Capital Work in Progress (FA211)~~

## N. Tax & Statutory
- ~~216 VAT Detail (TX216)~~, ~~217 VAT Reconciliation (TX217)~~, ~~218 Exempt / Zero / Out-of-scope (TX218)~~, ~~220 WHT Return (TX220)~~
- Data exists: 224 AFS pack, 225 SAF-T export; partly 219, 221, 222
- Needs new data: tax tables

## O. Payroll & HR (PR codes)
- ~~PR2, PR3, PR5, PR6, PR12, PR16, PR17, PR29, PR38, PR49, PR59, PR60, PR61, PR64, PR65, PR67, PR70~~
- ~~PR4, PR11, PR13, PR15, PR45, PR46, PR48, PR50, PR51, PR68~~ (after employee master data)
- ~~PR7, PR19, PR20, PR27, PR28, PR30, PR31, PR32, PR33, PR36~~ (after schemes, tax tables and the run register)
- Data exists, partly: PR4, PR24, PR26–PR28, PR34, PR39, PR45, PR46, PR58, PR63, PR66
- Needs new data: payroll runs and approvals, employee bank / tax / ID details, schemes and members, tax tables, timesheets, grades and establishment, HR records (PR7–PR11, PR13–PR15, PR19–PR23, PR25, PR30–PR33, PR35, PR36, PR40–PR43, PR47, PR48, PR50–PR57, PR62, PR68, PR69)

## P. Audit, Security & Compliance
- ~~237 Permission Matrix (SYS237)~~, ~~238 Membership (SYS238)~~, ~~239 SoD all modules (SYS239)~~, ~~240 Master Data Changes (SYS240)~~, ~~242 Back-dated (SYS242)~~, ~~243 Deleted & Voided (SYS243)~~, ~~244 Numbering Gaps (SYS244, journals)~~, ~~245 Unposted (SYS245)~~, ~~248 Licences (SYS248)~~, ~~250 Report Run Log (SYS250)~~
- Data exists: partly 236 (no login history), 241 (overrides), 247 (hash-chain verification is in the audit service)
- Needs new data: 249 login sessions

## Q. Executive / BI Dashboards
- ~~251 Executive Dashboard (DB251)~~, ~~252 Daily Flash (DB252)~~, ~~253 KPI Scorecard (DB253)~~, ~~254 Branch League (DB254)~~, ~~255 Trend & Seasonality (DB255)~~, ~~256 Municipal Executive Dashboard (DB256, without water losses)~~

## R. Master Data & System Listings
- ~~259 Exchange Rates (SYS259)~~, ~~260 Number Sequences (SYS260)~~, ~~261 Company Configuration (SYS261)~~, ~~262 Report Inventory (SYS262)~~, ~~263 Jobs & Deliveries (SYS263)~~
- ~~258 Price List Listing (SYS258)~~
