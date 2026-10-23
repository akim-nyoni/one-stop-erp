# Municipality

Properties, services and meters, billing, clearances, licences and faults.

Works with: Municipality.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Properties

### Enquiry desk

Look a stand up at the counter: balance, services, meters, statement.

### Property enquiry

One view of a stand: owners and ratepayer, services and meters, balance and overdue, recent bills and payments, subsidy, arrangement, open orders, licences and clearance.

| Field | Required | Notes |
| --- | --- | --- |
| Stand number | yes | Text |
| As at | yes | Date |

The list beside the form shows: Properties.

### Ratepayer statement

The ratepayer's statement for a stand, as at a date, ready to print or hand over: the account, then each service with its tariff (fixed charge, rates and steps) and the meter's last readings, and a consumption graph for each meter over the months chosen.

| Field | Required | Notes |
| --- | --- | --- |
| Stand number | yes | Text |
| As at | yes | Date |
| Months of consumption graph (0: none) | yes | Number; default 12 |

The list beside the form shows: Properties.

### Property ownership

Registers a stand's owners and their shares ("CUST1:50:R; CUST2:50" - R marks the ratepayer), transfers it to buyers against a clearance certificate, and shows the owners now or the full history. Also keeps the tenants, occupiers and managing agents (each optionally copied on the bills) and a correspondence address separate from the property's own.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Register, Transfer, Settle transfer, Owners, History, Add party, End party, Parties, Correspondence address, Bill goes to; default Register |
| Stand number | yes | Text |
| Owners: CUSTOMER:share[:R]; … | no | Text |
| Clearance certificate (transfer) | no | Text |
| Deeds reference | no | Text |
| Party is | yes | One of: Tenant, Occupier, ManagingAgent; default Tenant |
| Party's name | no | Text |
| Party's own account (customer) | no | Text |
| Party's phone | no | Text |
| Party's e-mail | no | Text |
| Address (party's, or the correspondence address) | no | Text |
| Copy the party on bills | no | Flag; default False |
| Party id (end) | no | Number |
| Date | yes | Date |

### Stand location and meter details

A stand's suburb and GPS position (decimal degrees), and a meter's type, size (mm) and where it is on the stand. Blank fields stay as they are.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Stand location, Meter details; default Stand location |
| Stand number | no | Text |
| Suburb | no | Text |
| Latitude | no | Number |
| Longitude | no | Number |
| Meter serial | no | Text |
| Meter type | no | Text |
| Size (mm) | no | Number |
| Where the meter is | no | Text |

The list beside the form shows: Meters.

### Valuation roll

Import a general valuation roll (a CSV file: stand,value) effective from a date; record a supplementary valuation on one stand (bills since re-rated); lodge an objection and decide it (a revised value re-rates the bills); History lists a stand's valuations. A supplementary value may go back no further than the council's limit (by default the start of the financial year); a rates credit above the council's threshold waits in the credit-note approval workflow. Policy sets both (blank months: start of the financial year; blank threshold: every credit is approved).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Import roll, Supplementary, Objection, Decide objection, History, Policy; default Import roll |
| Roll CSV file (stand,value) | no | Text |
| Roll name | no | Text |
| Stand number | no | Text |
| Value / claimed value / revised value | no | Number |
| Reason / grounds / notes | no | Text |
| Objection number (decide) | no | Text |
| Effective from | yes | Date |
| Date | yes | Date |
| Back-dating limit in months (policy) | no | Number |
| Credit approval threshold (policy) | no | Number |

The list beside the form shows: Open objections.

### Indigent register

Registers a household as indigent (income at or under the council's qualifying maximum, shown below) with its subsidy % and review date. Request: you key the means test; another user holding the indigent approve right then approves or rejects it (with a reason). Review: stays on only while still under the maximum. Apply subsidies: credits the month's charges (not interest or penalties), each currency in that currency. Set threshold: the council's qualifying maximum household income.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Request, Approve, Reject, Review, Apply subsidies, Set threshold; default Request |
| Council's qualifying maximum income (policy) | no | Choice |
| Registration awaiting approval (approve / reject) | no | Choice |
| Stand number (request) | no | Text |
| Registration id (review) | no | Number |
| Household income | yes | Number; default 0 |
| Subsidy % | yes | Number; default 100 |
| New qualifying maximum income (set threshold) | no | Number |
| Next review due | yes | Date |
| Reason (reject) | no | Text |
| Subsidy (expense) account | no | Text |
| Date | yes | Date |

The list beside the form shows: Reviews due in 60 days.

## Services and meters

### Service connections

Connect a council service to a stand (with a quantity and its deposit, invoiced to the ratepayer), change its quantity, or suspend, disconnect, reconnect or close it. Only an active connection is billed, at the tariff times the quantity.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Connect, Quantity, Active, Suspended, Disconnected, Closed; default Connect |
| Stand number | yes | Text |
| Service code | yes | Text |
| Tariff code (connect) | no | Text |
| Date | yes | Date |
| Quantity | no | Number |
| Deposit (connect; blank: the service's) | no | Number |

The list beside the form shows: Connections.

### Capture a meter reading

An actual reading (or an estimate when the meter could not be read); readings outside the usual pattern are flagged for checking.

| Field | Required | Notes |
| --- | --- | --- |
| Meter serial | yes | Text |
| Reading date | yes | Date |
| Reading (blank to estimate) | no | Number |
| Tampering suspected | no | Flag; default False |

The list beside the form shows: Reading exceptions this month.

### Meter reading routes

Creates a reading route for a meter reader (meter serials in walking order, comma-separated); Sheet prints it with the last readings; Import loads a CSV of readings taken on the round (each checked like a keyed reading).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create route, Sheet, Import readings; default Create route |
| Route code | no | Text |
| Route name | no | Text |
| Meter reader | no | Text |
| Meter serials in order, comma-separated | no | Text |
| Readings CSV file (import) | no | Text |

### Disconnections and reconnections

Lists stands whose arrears for a service pass a threshold, raises a disconnection (or reconnection) order for one, authorises a disconnection (someone other than whoever raised it), executes it (with its disconnection or reconnection fee) or cancels it.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Candidates, Raise, Authorise, Execute, Cancel; default Candidates |
| Service | yes | One of: Rates, Water, Sewer, Refuse, StreetLighting, Licence, Electricity, Other; default Rates |
| Arrears threshold | yes | Number; default 0 |
| Stand number (raise) | no | Text |
| Order | yes | One of: Disconnect, Reconnect; default Disconnect |
| Order number | no | Text |
| Disconnection / reconnection fee | yes | Number; default 0 |
| Fee revenue account | no | Text |
| Date | yes | Date |

### Service deposits

Takes a deposit for a service on a stand (held as a liability), refunds it or applies it to the arrears when the account closes; Held lists a stand's deposits.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Take, Refund, Apply to arrears, Held; default Take |
| Stand number (take / held) | no | Text |
| Service | yes | One of: Rates, Water, Sewer, Refuse, StreetLighting, Licence, Electricity, Other; default Rates |
| Amount | yes | Number; default 0 |
| Deposit reference (refund / apply) | no | Text |
| Bank account | no | Text |
| Deposit liability account (take) | no | Text |
| Date | yes | Date |

### Prepaid vending

Sells prepaid units (water or electricity) to a stand: part of the payment can go to its arrears; the rest buys units and gives a token. Until the prepaid vendor's token generator is registered, tokens come from a test generator and each sale says so.

| Field | Required | Notes |
| --- | --- | --- |
| Stand number | yes | Text |
| Service | yes | One of: Water, Electricity; default Water |
| Amount paid | yes | Number |
| Bank / cash account | yes | Text |
| % to arrears | yes | Number; default 0 |
| Channel | yes | One of: Counter, Agent, Mobile; default Counter |
| Date | yes | Date |

The list beside the form shows: Prepaid sales (this month).

## Billing

### Billing run

Propose a run for a service (limited to a ward, a meter-reading route or a billing cycle if you like), review its exceptions, include or exclude stands, then approve to post the bills - someone other than the proposer approves. Email bills sends each ratepayer billed in a posted run their statement (those without email are counted for printing); SMS bills texts each ratepayer with a mobile number from an SMS template (fields {{Name}} {{Account}} {{Stands}} {{Bills}} {{Amount}} {{Balance}} {{Currency}} {{BillDate}}); Print bills prints every bill in the run, one ratepayer per page in stand order - or Print bills not emailed for only those without email.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Propose, Show exceptions, Exclude a stand, Include a stand, Approve, Resume posting, Email bills, SMS bills, Print bills, Print bills not emailed, Cancel; default Propose |
| Service | yes | One of: Rates, Water, Sewer, Refuse, StreetLighting, Licence, Electricity, Other; default Rates |
| Bill date | yes | Date |
| Ward (blank: all) | no | Text |
| Reading route (blank: all) | no | Text |
| Billing cycle | yes | One of: Any, Monthly, Quarterly, HalfYearly, Annual; default Any |
| Council service code (blank: the category's own) | no | Text |
| Invoice prefix | no | Text; default MB |
| Run number | no | Text |
| Stand number | no | Text |
| Email / SMS template (email or SMS bills) | no | Text; default BILL |

The list beside the form shows: Billing runs.

### Tariffs

Lists a service's tariffs in force on a date, or resolves which tariff applies to a consumer category in a ward.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: List, Resolve; default List |
| Service | yes | One of: Rates, Water, Sewer, Refuse, StreetLighting, Licence, Electricity, Other; default Rates |
| Consumer category (resolve) | yes | One of: Domestic, Commercial, Industrial, Institutional, Agricultural, Indigent; default Domestic |
| Ward (resolve) | no | Text |
| Date | yes | Date |

### Tariff billing cycle

How often a tariff is billed: monthly, quarterly, half-yearly or annually from a start month (e.g. 7 for a July rates year). Its amounts are per period; a period's charge is raised once by the first run in the period, or over monthly instalments from its start (the last takes the rounding), pro rata to the days the service is in place.

| Field | Required | Notes |
| --- | --- | --- |
| Tariff code | yes | Text |
| Cycle | yes | One of: Monthly, Quarterly, HalfYearly, Annual; default Monthly |
| Start month (1–12) | yes | Number; default 1 |
| Instalments per period | yes | Number; default 1 |

The list beside the form shows: Tariffs.

### Council service types

The council's own services (e.g. extra refuse bins, fire levy): billed like a built-in category, metered or not, with the deposit a new connection pays. An existing code changes that service (and brings a retired one back into use); Delete stops new connections to it, existing ones carry on.

| Field | Required | Notes |
| --- | --- | --- |
| Code | yes | Text |
| Name | yes | Text |
| Bills as | yes | One of: Rates, Water, Sewer, Refuse, StreetLighting, Licence, Electricity, Other; default Rates |
| Metered | no | Flag; default False |
| Deposit on connection | yes | Number; default 0 |
| Deposit account | no | Text |

The list beside the form shows: Service types.

### Print a municipal bill

Prints a stand's monthly municipal bill, in the standard layout or through the company's own (as a PDF).

| Field | Required | Notes |
| --- | --- | --- |
| Stand number | yes | Text |
| Any day in the month | yes | Date |
| PDF file path (when a layout is set) | no | Text |

## Clearances, licences and facilities

### Rates clearance certificates

Quotes what a stand must pay for a clearance certificate (its balance plus so many months' billing in advance), issues the certificate once that is paid, valid for so many days, and prints it (Print reprints an issued certificate by its number).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Quote, Issue, Print; default Quote |
| Stand number (quote, issue) | no | Text |
| Months in advance | yes | Number; default 3 |
| Valid for (days) | yes | Number; default 60 |
| Date | yes | Date |
| Certificate number (print) | no | Text |

### Council licences

Issues a licence (business, liquor, shop, building plan, hawker…) with its fee and renewal cycle, records inspections (a failed one suspends it), renews it (late renewals with a penalty %), or revokes it.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Issue, Inspection, Renew, Revoke; default Issue |
| Licence kind | yes | One of: Business, Liquor, Shop, BuildingPlan, Hawker, Other; default Business |
| Holder customer code (issue) | no | Text |
| Stand number (optional) | no | Text |
| Description / inspector / reason | no | Text |
| Renewal cycle (months) | yes | Number; default 12 |
| Fee / penalty % | yes | Number; default 0 |
| Revenue / penalty account | no | Text |
| Fees-in-advance account (issue; blank: revenue at once) | no | Text |
| Licence number | no | Text |
| Inspection passed | no | Flag; default True |
| Notes | no | Text |
| Date | yes | Date |

The list beside the form shows: Renewals due in 60 days.

### Cemeteries

Adds plots to a cemetery section ("A1,A2,A3") at a price and capacity, sells a plot to a holder, records an interment (with its fee), and lists a cemetery's plots.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Add plots, Sell plot, Interment, Plots; default Add plots |
| Cemetery | no | Text |
| Section | no | Text |
| Plot numbers, comma-separated | no | Text |
| Price / interment fee | yes | Number; default 0 |
| Capacity (graves per plot) | yes | Number; default 1 |
| Plot reference (sell / interment) | no | Text |
| Holder customer code | no | Text |
| Deceased name | no | Text |
| Date of death | no | Date |
| Revenue / fee account | no | Text |
| Date | yes | Date |

The list beside the form shows: Burials this year.

### Facility bookings

Halls, grounds and other council facilities: create one with its daily rate, book it for a customer (the fee invoiced; double bookings refused), cancel a booking with a refund %, and see a facility's calendar.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create facility, Book, Cancel, Calendar; default Create facility |
| Facility code (booking number to cancel) | yes | Text |
| Name / purpose | no | Text |
| Daily rate / refund % | yes | Number; default 0 |
| Revenue account (create) | no | Text |
| Customer code (book) | no | Text |
| From | yes | Date |
| To | yes | Date |
| Date | yes | Date |

## Faults

### Fault desk

Log, assign and close faults reported by the public.

### Fault ticket categories

The kinds of fault the public reports (a burst pipe, a missed refuse collection), each with the service it concerns and its response and resolution times - the clocks the fault desk measures every ticket against. An existing code changes that category (tickets already logged keep their due times).

| Field | Required | Notes |
| --- | --- | --- |
| Category code | yes | Text |
| Name | yes | Text |
| Service | yes | One of: (none), Rates, Water, Sewer, Refuse, StreetLighting, Licence, Electricity, Other; default (none) |
| Respond within (hours) | yes | Number; default 4 |
| Resolve within (hours) | yes | Number; default 48 |

The list beside the form shows: Fault ticket categories.

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| DB256 | Municipal Executive Dashboard | Billed and collected this year, collection rate, debtors and over-90s, indigent households, disconnections, open faults and licences due; water bought in bulk against water metered to consumers this year (the loss), and ratepayer arrears at each of the last six month-ends. |
| M144 | Consumer / Ratepayer Statement | Per property: the ratepayer's account with the services connected, opening balance, charges, payments and interest in the period, closing balance and ageing; one stand, or every stand for bulk (burst) printing. |
| M146 | Billing Run Summary & Control | Each billing run: service, period, ward, lines billed and excluded, total billed against the previous run, proposer and approver. |
| M147 | Billing Exceptions Report | Lines of every billing run flagged as already billed, nothing to bill, failed or a large change on last time. |
| M148 | Aged Debtors by Ward, Category and Service | Ratepayers' open items as at the date, aged 30/60/90/120+, grouped by ward, consumer category and service. |
| M149 | Arrears Listing | Ratepayers with amounts past due at the date: overdue, total owing, last payment and arrangement status. |
| M150 | Revenue Collection Report | Billed against collected by month, ward and service, with the collection efficiency. |
| M151 | Valuation Roll & Supplementary Roll | Every valuation entry (general, supplementary, objection) with its effective date, and objections with their outcome. |
| M152 | Property Register (full) | Every property with owner(s), ratepayer, services and tariffs, meters, valuation and ward. |
| M153 | Rates Clearance Certificate Register | Certificates issued with property, validity, advance held and who issued them, and whether still valid at the date. |
| M154 | Meter Reading Sheets / Route Books | Each reading route in walking order: meter, stand, last reading and date, blank for the new reading. |
| M155 | Meter Reading Exceptions | Readings flagged high or low consumption, rollover, negative or tampered, and estimates, with whether they were accepted. |
| M156 | Consumption Analysis | Metered consumption for the twelve months to the date against the twelve before, per meter with its category. |
| M157 | Water Balance / Non-Revenue Water | Per supply zone over the period: bulk water put in, metered consumption billed to properties in the zone, the water lost and the loss rate. |
| M158 | Unread / Faulty / Tampered Meter Report | Active meters not read for longer than the limit, running on estimates, or whose readings in the last year were flagged as tampered, negative or rolled over. |
| M159 | Meter Register & Replacement History | Every meter with stand, service, installation and removal, what replaced it, last actual reading and units estimated since. |
| M160 | Disconnection & Reconnection Lists | Service orders to disconnect for arrears or reconnect after payment, with status, execution and the fee raised. |
| M161 | Payment Arrangements Register (all debtors) | Every payment arrangement: terms, status and standing as at the date. |
| M162 | Indigent Register & Subsidy Cost | Registered indigent households with income, subsidy rate, review date and the subsidy credited in the year to the date. |
| M163 | Interest & Penalties Charged | Interest raised on ratepayers' overdue accounts in the year to the date. |
| M164 | Write-offs & Adjustments | Bad debts written off (with the debt case and who approved it) and credit adjustments on ratepayers' accounts in the year. |
| M165 | Deposits Held by Consumer | Service deposits held at the date, by ward and consumer: the stand, service, when taken and the amount. |
| M166 | Licences & Permits Register and Renewals Due | Every licence with holder, expiry, days to expiry at the date, renewals and the latest inspection. |
| M167 | Service Request / Fault Ticket SLA | Every ticket with category, priority, response and resolution against their SLA deadlines. |
| M168 | Tariff Schedule | Every tariff with its type, category, ward, effective date, charges, blocks and seasonal rates. |
| M169 | Revenue by Source / Vote | Year to date: billed charges by service and revenue account, prepaid sales and licence fees. |
| M170 | Ward Councillor Pack | One ward: properties, billing, debtors, arrangements, disconnections, indigent households, open faults and licences. |
| M171 | Statutory Debtors Age Analysis | Debtors by consumer category aged 30/60/90/120+ as at the date, with a grand total - the statutory return layout. |
| M172 | Prepaid vs Conventional | Month by month for the year: prepaid sales (vends, units, amount, recovered to arrears) against conventional billing, per service. |
| MUN-PROP | Property Register | Stands on the municipal register with ward, valuation and ratepayer. |
