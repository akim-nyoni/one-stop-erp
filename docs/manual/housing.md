# Housing

The waiting list, land and stands, allocation, sales, rentals and title.

Works with: Housing.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Waiting list

### Register an applicant

Registers an applicant (a second registration with the same national ID is refused) and gives the waiting-list number and priority score.

| Field | Required | Notes |
| --- | --- | --- |
| National ID | yes | Text |
| Full name | yes | Text |
| Category | yes | One of: HighDensity, MediumDensity, LowDensity, Flats, Rental; default HighDensity |
| Date of birth (yyyy-MM-dd) | yes | Text |
| Resident since (yyyy-MM-dd) | yes | Text |
| Dependants | yes | Number; default 0 |
| Has a disability | no | Flag; default False |
| Monthly income | yes | Number; default 0 |
| Current tenure | yes | One of: Lodger, Tenant, Homeless, WithRelatives, InformalSettlement; default Lodger |
| Registration date | yes | Date |
| Marital status | yes | One of: Single, Married, Cohabiting, Divorced, Widowed; default Single |
| Marriage regime | yes | One of: , InCommunity, OutOfCommunity, Customary |
| Spouse / co-applicant national ID | no | Text |
| Spouse / co-applicant name | no | Text |

The list beside the form shows: Applicants.

### Find an applicant and their place

With a waiting-list number (or applicant id): the applicant's status, place by seniority and score. Without one: the ranking for a category, by seniority or score.

| Field | Required | Notes |
| --- | --- | --- |
| Waiting-list number (blank for the ranking) | no | Text |
| Category | yes | One of: HighDensity, MediumDensity, LowDensity, Flats, Rental; default HighDensity |
| Order | yes | One of: Seniority, Score; default Seniority |
| As at | yes | Date |

The list beside the form shows: Applicants.

### Applicant intake and duplicate queue

Contact: an applicant's gender, addresses, phone and employer. Lodge / Verify: supporting documents (ID copy, proof of residence, payslip, affidavit) verified one by one with the officer's name — the applicant is verified once all are. Death: adds to the deceased register. Scan: finds suspected duplicates (same ID, similar name with the same birth date, same phone) and applicants on the deceased register and queues them. Merge (keeps the earlier registration), Reject (removes the second) or Clear a case, with a reason.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Contact, Lodge document, Verify document, Death, Scan, Merge, Reject, Clear; default Contact |
| Waiting-list number | no | Text |
| Gender | yes | One of: Unspecified, Female, Male; default Unspecified |
| Physical address | no | Text |
| Postal address | no | Text |
| Phone | no | Text |
| Employer | no | Text |
| Document | yes | One of: IdCopy, ProofOfResidence, Payslip, Affidavit, Other; default IdCopy |
| Description / name (death) | no | Text |
| Document / case id | no | Number |
| Verifying officer | no | Text |
| National ID (death) | no | Text |
| Reason (decision) | no | Text |
| Date | yes | Date |

The list beside the form shows: Duplicate queue.

### Waiting-list control

Request a change to an applicant's registration date (with the reason) and have another user decide it; confirm an applicant's continued interest; run renewals (suspending, then removing, applicants who have not renewed within the cycle and grace); see the notices sent to an applicant.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Request date change, Decide date change, Confirm interest, Run renewals, Notices; default Request date change |
| Waiting-list number | no | Text |
| Reason (date change) | no | Text |
| Request id (decide) | no | Number |
| Approve | no | Flag; default False |
| Renewal cycle (months) | yes | Number; default 12 |
| Grace (days) | yes | Number; default 30 |
| Date | yes | Date |

The list beside the form shows: Applicants.

### Applicant fees and transfer duty

Records a fee an applicant pays (registration, renewal…) to income; withholds transfer duty on a transfer application at a rate (held as a liability) and remits it to the authority.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Applicant fee, Withhold duty, Remit duty; default Applicant fee |
| Waiting-list number (fee) | no | Text |
| Fee | yes | One of: Registration, Renewal; default Registration |
| Transfer application (duty) | no | Text |
| Amount (fee) / rate % (duty) | yes | Number; default 0 |
| Receipt / remittance reference | no | Text |
| Bank account | no | Text |
| Income account (fee) / duty liability account | no | Text |
| Date | yes | Date |

The list beside the form shows: Applicants.

### Waiting-list rules

Policy: who qualifies (minimum age, years resident, overall income ceiling) and the points each factor earns. Qualification rules: first-time buyers only, refuse those whose earlier stand was repossessed or agreement cancelled, require a rates clearance, and an income ceiling per category (blank removes it). Registration checks every rule, for the applicant and the spouse.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Policy, Qualification rules; default Policy |
| Minimum age | yes | Number; default 18 |
| Minimum years resident | yes | Number; default 0 |
| Overall income ceiling (blank: none) | no | Number |
| Points per year on the list | yes | Number; default 10 |
| Points per year resident | yes | Number; default 2 |
| Points per dependant | yes | Number; default 5 |
| Points for a disability | yes | Number; default 15 |
| Points if homeless | yes | Number; default 20 |
| Points if lodging | yes | Number; default 8 |
| First-time buyers only | no | Flag; default False |
| Refuse earlier forfeiture | no | Flag; default False |
| Require rates clearance | no | Flag; default False |
| Category for the ceiling | yes | One of: HighDensity, MediumDensity, LowDensity, Flats, Rental; default HighDensity |
| Its income ceiling (blank: none) | no | Number |

The list beside the form shows: Rules in force.

## Land and stands

### Land, schemes and stands

Acquire land into land WIP at its cost; create a housing scheme; subdivide a parcel into stands ("S1:300; S2:250" — number:size m²), its cost apportioned equally or by area into stands available; sell a stand outright for cash (an invoice to the buyer, cost of sales out of stands available).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Acquire land, Create scheme, Subdivide, Sell stand; default Acquire land |
| Parcel number | no | Text |
| Description / scheme name | no | Text |
| Area m² (acquire) | yes | Number; default 0 |
| Cost (acquire) / price (sell) | yes | Number; default 0 |
| Scheme code | no | Text |
| Delivery model | yes | One of: CouncilDelivered, PublicPrivatePartnership, Cooperative, EmployerAssisted, RuralPeriUrban; default CouncilDelivered |
| Stands: number:size m²; … (subdivide) | no | Text |
| Apportion cost | yes | One of: Equal, ByArea; default Equal |
| Stand number (sell) | no | Text |
| Buyer customer code (sell) | no | Text |
| Land WIP account | no | Text |
| Funding / paid from account (acquire) | no | Text |
| Stands available account | no | Text |
| Sales account (sell) | no | Text |
| Cost of sales account (sell) | no | Text |
| Document / invoice number | no | Text |
| Date | yes | Date |

The list beside the form shows: Schemes.

### Stand, parcel and scheme details

Stand: address, ward / suburb / zone, intended use, zoning, servicing phase and the services available (water, sewer, road, electricity, storm water). Remark: adds a dated line to the stand's remarks history; Remarks lists it. Parcel: locality, ward, land use, tenure, improvements and occupation. Scheme: funding source and start / completion dates.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Stand, Remark, Remarks, Parcel, Scheme; default Stand |
| Stand / parcel / scheme number | no | Text |
| Street address | no | Text |
| Ward | no | Text |
| Suburb / locality | no | Text |
| Zone | no | Text |
| Intended use / land use | no | Text |
| Zoning | no | Text |
| Servicing phase | no | Text |
| Water | no | Flag; default False |
| Sewer | no | Flag; default False |
| Road | no | Flag; default False |
| Electricity | no | Flag; default False |
| Storm water | no | Flag; default False |
| Remark / improvements | no | Text |
| Parcel tenure | yes | One of: , Freehold, StateLand, Leasehold, Communal, Customary, Trust |
| Parcel occupation | yes | One of: , Vacant, Occupied, InformallySettled, UnderDispute |
| Scheme funding | yes | One of: , OwnRevenue, Loan, Grant, DeveloperContribution, Mixed |
| Scheme start (yyyy-MM-dd) | no | Text |
| Scheme completion (yyyy-MM-dd) | no | Text |

The list beside the form shows: Stand register.

### Change a stand's status

Moves a stand along raw land → planned → surveyed → under servicing → serviced → available → reserved / offered, and after sale → fully paid → title applied → registered → handed over, never jumping a state, each move with its authority; withholds a stand (dispute or caveat) and releases it back; consolidates free stands into one or re-subdivides one ("S1-A:300; S1-B:200"), lineage kept back to the parcel. History shows every move.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: History, Move, Withhold, Release hold, Consolidate, Re-subdivide; default History |
| Stand number (consolidate: several, comma-separated) | yes | Text |
| Move to | yes | One of: Available, Allocated, Sold, Transferred, RawLand, Planned, Surveyed, UnderServicing, Serviced, Reserved, Offered, FullyPaid, TitleApplied, TitleRegistered, TitleHandedOver, Withheld, Surrendered, OfferCancelled, ConsolidatedAway, ReSubdivided, Repossessed; default Available |
| Authority (resolution / minute / document) | no | Text |
| Reason | no | Text |
| New stand number (consolidate) or parts (re-subdivide) | no | Text |
| Date | yes | Date |

The list beside the form shows: Stands.

### Stand pricing

Proposes a pricing rule — for one stand, a size band or a whole scheme, from a date: cost plus overhead %, levies per stand and per m², margin %, rounded — which another user approves; Quote shows a stand's price build-up under the rule in force.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Propose, Approve, Quote; default Propose |
| Applies to | yes | One of: Stand, SizeBand, Scheme; default Stand |
| Scheme code | no | Text |
| Size band from (m²) | no | Number |
| Size band to (m²) | no | Number |
| Stand number (stand rule / quote) | no | Text |
| Overhead % | yes | Number; default 0 |
| Levy per stand | yes | Number; default 0 |
| Levy per m² | yes | Number; default 0 |
| Margin % | yes | Number; default 0 |
| Round to | yes | Number; default 1 |
| Rule id (approve) | no | Number |
| Date | yes | Date |

The list beside the form shows: Pricing rules.

### Layout plans

Submits a layout plan for a land parcel with its stands by use ("Residential:120; Commercial:6; Roads:4"), approves it with the authority, amends it as a new version with the reason, shows every version with what changed, and reconciles the plan in force against the stands on the register.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Submit, Approve, Amend, History, Reconcile; default Submit |
| Plan number | yes | Text |
| Land parcel number (submit / amend) | no | Text |
| Planner | no | Text |
| Density | yes | One of: Low, Medium, High; default Low |
| Stands by use: use:count; … | no | Text |
| Approval authority / amendment reason | no | Text |
| Date | yes | Date |

The list beside the form shows: Layout plans.

### Map stands and parcels

Records a stand's or land parcel's boundary (WKT POLYGON in longitude latitude), lists stands whose mapped area differs from the surveyed area by more than a tolerance, or exports a layer as GeoJSON for a GIS.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Map a stand, Map a parcel, Area discrepancies, Export GeoJSON; default Map a stand |
| Stand / parcel number | no | Text |
| Boundary (WKT POLYGON) | no | Text |
| Tolerance % | yes | Number; default 5 |
| Layer (export) | yes | One of: Parcels, Stands; default Parcels |
| Scheme code (export; blank: all) | no | Text |
| Save GeoJSON to (path) | no | Text |

### Land register

On a land parcel: register an encumbrance (servitude, caveat, mortgage, lease, restrictive condition, wayleave) and lift it; open a land dispute and move it reported → investigation → negotiation → mediation → litigation → judgment → resolved; dispose of land by sale, exchange (receiving a parcel in return), donation or expropriation, with the cost out and the gain or loss.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Encumbrance, Lift encumbrance, Open dispute, Advance dispute, Dispose; default Encumbrance |
| Parcel number | no | Text |
| Encumbrance type | yes | One of: Servitude, Caveat, Mortgage, Lease, RestrictiveCondition, Wayleave; default Servitude |
| Encumbrance id (lift) | no | Number |
| Reference / resolution | no | Text |
| In favour of / claimant / counterparty | no | Text |
| Description / particulars / note | no | Text |
| Dispute type | yes | One of: Ownership, Boundary, Encroachment, Compensation, Occupation; default Ownership |
| Dispute number (advance) | no | Text |
| Dispute stage | yes | One of: Reported, Investigation, Negotiation, Mediation, Litigation, Judgment, Resolved; default Reported |
| Stand number (dispute, optional) | no | Text |
| Disposal | yes | One of: Sale, Exchange, Donation, Expropriation; default Sale |
| Area m² (disposal) | yes | Number; default 0 |
| Consideration | yes | Number; default 0 |
| Land account | no | Text |
| Proceeds account | no | Text |
| Gain / loss account | no | Text |
| Parcel received in exchange: number | no | Text |
| Parcel received: area m² | yes | Number; default 0 |
| Date | yes | Date |

The list beside the form shows: Land disputes.

### Land acquisition

Open an acquisition (method, counterparty, authorising resolution), record its valuation, sign off each due-diligence item, capitalise costs into land WIP, see its position (cost build-up and what still blocks it), complete it into the land bank, or abandon it (costs written off).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Open, Valuation, Council offer, Counterparty offer, Agree price, Break off talks, Sign off, Add cost, Position, Complete, Abandon; default Open |
| Acquisition number | no | Text |
| Parcel number (open) | no | Text |
| Description / finding / reference | no | Text |
| Area m² (open) | yes | Number; default 0 |
| Method (open) | yes | One of: Purchase, Donation, StateGrant, Compulsory, LandSwap, JointVenture, Escheat, Regularisation; default Purchase |
| Counterparty / valuer | no | Text |
| Resolution (open) / reason (abandon) | no | Text |
| Value / cost | yes | Number; default 0 |
| Due-diligence item | yes | One of: TitleSearch, Encumbrances, Caveats, Servitudes, BoundaryDisputes, OutstandingRates, EnvironmentalImpact, Heritage, Geotechnical, Wetland, Occupants; default TitleSearch |
| Acceptable | no | Flag; default True |
| Cost component | yes | One of: PurchasePrice, Survey, Legal, Valuation, OccupantCompensation, TransferDuty, CapitalisedInterest; default PurchasePrice |
| Land WIP account | no | Text |
| Write-off account (abandon) | no | Text |
| Date | yes | Date |

The list beside the form shows: Acquisitions.

### Compulsory acquisition and compensation

Lodge a claim on a land parcel, serve the notice (opening the objection window), record objections and decide them, assess, offer and record the answer, refer a dispute and record the award (each moving the provision), pay in part or settle, take possession (once the window has closed with no objection standing), or withdraw. Ids for objections, offers and disputes are given when they are recorded.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Lodge, Serve notice, Objection, Decide objection, Assess, Provide, Offer, Answer offer, Dispute, Award, Pay part, Settle, Possession, Withdraw; default Lodge |
| Claim number | no | Text |
| Land parcel number (lodge) | no | Text |
| Claimant / objector / assessor / forum / notice reference | no | Text |
| Grounds / note | no | Text |
| Amount | no | Number |
| Objection / offer / dispute id | no | Number |
| Upheld / accepted | no | Flag; default False |
| Objection window (days) | yes | Number; default 30 |
| Land cost account (lodge) | no | Text |
| Provision account (lodge) | no | Text |
| Bank account (pay) | no | Text |
| Date | yes | Date |

The list beside the form shows: Compensation claims.

### Stand blocks and caveats

Places a block on a stand (court interdict, family dispute, suspected fraud, double allocation, boundary dispute…) with its document — a blocked stand cannot be allocated, sold, leased or transferred — or lifts it with the reason. History shows every block on a stand.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Place, Lift, History; default Place |
| Stand number | yes | Text |
| Reason (place) | yes | One of: CourtInterdict, FamilyDispute, SuspectedFraud, DoubleAllocation, BoundaryDispute, Other; default CourtInterdict |
| Detail / reason for lifting | no | Text |
| Document reference | no | Text |
| Date | yes | Date |

The list beside the form shows: Blocks in force.

## Allocation

### Allocation run

Propose a run for a scheme's available stands (seniority, score, or a seeded ballot anyone can re-run to verify); the committee rejects a pairing with a reason or substitutes the next applicant stating why the skipped one was skipped; approve it (someone else; offers go out with a deadline; with a resolution the meeting's date and members are kept), cancel it, or verify a ballot's draw.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Propose, Reject line, Substitute line, Approve, Cancel, Verify draw, Swap stands; default Propose |
| Only these stands (substitution run; ',') | no | Text |
| Pairing sequence (reject / substitute) | no | Number |
| Reason (reject / substitute) | no | Text |
| Committee resolution (approve) | no | Text |
| Meeting date yyyy-MM-dd (approve) | no | Text |
| Committee members present (approve) | no | Text |
| Certificate (swap) | no | Text |
| Other certificate (swap) | no | Text |
| Swap document | no | Text |
| Swap reason | no | Text |
| Scheme code (propose) | no | Text |
| Category | yes | One of: HighDensity, MediumDensity, LowDensity, Flats, Rental; default HighDensity |
| Method | yes | One of: Seniority, Score, Ballot; default Seniority |
| Ballot seed (blank: random) | no | Number |
| Run number | no | Text |
| Date | yes | Date |
| Days to accept an offer | yes | Number; default 30 |

The list beside the form shows: Allocation runs.

### Offers: accept, decline, print letters

Records the applicant's answer to an allocation offer by its certificate number; a decline needs the reason and frees the stand. Offer letter prints the offer (price, deposit, deadline) for the applicant; Allocation letter prints the confirmation once accepted.

| Field | Required | Notes |
| --- | --- | --- |
| Certificate number | yes | Text |
| Answer | yes | One of: Accept, Decline, Offer letter, Allocation letter; default Accept |
| Reason (decline) | no | Text |
| Date | yes | Date |
| Deposit % (letters) | no | Number |

The list beside the form shows: Stands.

### Allocation quotas

Sets the share of allocations reserved for a beneficiary group (women, youth, disabled, elderly, civil servants), for a scheme or overall; declares the groups an applicant belongs to (comma-separated) so allocation runs can meet the quotas.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Set quota, Declare groups; default Set quota |
| Scheme code (blank: all schemes) | no | Text |
| Group (quota) | yes | One of: None, Women, Youth, Disabled, Elderly, CivilServants, WarVeterans; default None |
| Target % | yes | Number; default 0 |
| Waiting-list number (declare) | no | Text |
| Groups, e.g. Women,Youth (declare) | no | Text |

The list beside the form shows: Quotas.

### Allocation appeals

An applicant appeals an allocation run: lodge the grounds, set the hearing, decide (upheld or dismissed, with reasons), and link an upheld appeal to the re-allocation certificate that put it right.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Lodge, Hearing, Decide, Link re-allocation; default Lodge |
| Appeal number | no | Text |
| Waiting-list number (lodge) | no | Text |
| Allocation run number (lodge) | no | Text |
| Grounds / reasons | no | Text |
| Upheld | no | Flag; default False |
| Re-allocation certificate | no | Text |
| Date | yes | Date |

The list beside the form shows: Appeals.

## Sales and instalments

### Agreements

Drafts an agreement with a party for a stand (price, terms, special conditions), records its signing with witnesses, amends it as a new version with a reason, shows its version history, or checks every stored agreement document against its hash.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Draft, Sign, Amend, History, Verify documents; default Draft |
| Agreement number | no | Text |
| Type (draft) | yes | One of: CashSale, InstalmentSale, Lease, Rental, Cooperative, RentToBuy, LeaseWithOption, SiteAndService, LodgerPermit, LongLease; default CashSale |
| Party customer code (draft) | no | Text |
| Stand number | no | Text |
| Price | yes | Number; default 0 |
| Terms | no | Text |
| Special conditions | no | Text |
| Witnesses, comma-separated (sign) | no | Text |
| Amendment reason | no | Text |
| Date | yes | Date |

The list beside the form shows: Agreements.

### Instalment sales of stands

Sign: sells a stand on instalments (deposit, rate, zero / flat / reducing-balance interest, term) and shows the schedule. Raise due: bills the instalments due by a date. Quote / settle early: the principal outstanding and the interest rebated when paid off now.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Sign, Raise due, Quote settlement, Settle early, Set market rate; default Sign |
| Stand number (sign) | no | Text |
| Buyer customer code (sign) | no | Text |
| Agreement number (quote / settle) | no | Text |
| Cash price | yes | Number; default 0 |
| Deposit | yes | Number; default 0 |
| Annual interest % | yes | Number; default 0 |
| Interest | yes | One of: Zero, Flat, ReducingBalance; default Zero |
| Tenure | yes | One of: InstalmentSale, RentToBuy, LeaseWithOption; default InstalmentSale |
| Term (months) | yes | Number; default 12 |
| Yearly instalment escalation % | yes | Number; default 0 |
| Signed / as at | yes | Date |
| First instalment due | yes | Date |
| Sales account | no | Text |
| Stands available (inventory) account | no | Text |
| Cost of sales account | no | Text |
| Interest income account | no | Text |
| Bank account (deposit) | no | Text |
| Deferred financing account (plans over a year below the market rate) | no | Text |
| Market credit rate % (set market rate; blank: no discounting) | no | Number |

The list beside the form shows: Instalment agreements.

### Stand deposits and cancellations

Take a deposit against an allocation (held as a liability), apply it when the sale goes ahead or refund it; cancel an instalment agreement at the buyer's request (requested by you, approved by another user): what was paid less occupation rental, the admin charge and arrears is refunded and the stand repossessed.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Take deposit, Apply deposit, Refund deposit, Cancel agreement; default Take deposit |
| Allocation certificate (deposit) | no | Text |
| Instalment agreement (cancel) | no | Text |
| Customer code (take) | no | Text |
| Amount (take) / NRV (cancel) | yes | Number; default 0 |
| Receipt reference / reason | no | Text |
| Bank account | no | Text |
| Deposit liability account (take) | no | Text |
| Monthly occupation rental (cancel) | yes | Number; default 0 |
| Admin charge % (cancel) | yes | Number; default 0 |
| Approving user name (cancel) | no | Text |
| Stands available account (cancel) | no | Text |
| Gain / loss account (cancel) | no | Text |
| Date | yes | Date |

The list beside the form shows: Allocation certificates.

### Instalment arrears and default notices

The arrears stages: a reminder from the first unpaid instalment, a letter of demand after 30 days, a notice of breach after 60 and a notice of cancellation after 90 (once the breach notice has stood 30 days) — each issued only after the previous one is served, with proof of service recorded. Penalty: sets an agreement's annual penalty rate on arrears; Charge penalties invoices a month's penalty interest on each agreement's arrears once.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Issue next notice, Record service, Penalty rate, Charge penalties; default Issue next notice |
| Agreement number | no | Text |
| Notice number (service) | no | Text |
| Served by (hand, registered post, email) | no | Text |
| Who served it | no | Text |
| Penalty rate % a year | no | Number |
| Date | yes | Date |

The list beside the form shows: Agreements in arrears.

### Repossess a stand

Takes back a stand sold on instalments (requested by you, approved by another user): the receivable is derecognised, the stand comes back into inventory at its net realisable value, and the difference is a gain or loss.

| Field | Required | Notes |
| --- | --- | --- |
| Instalment agreement number | yes | Text |
| Net realisable value | yes | Number |
| Reason | yes | Text |
| Approving user name | yes | Text |
| Stands available (inventory) account | yes | Text |
| Gain / loss account | yes | Text |
| Date | yes | Date |

The list beside the form shows: Repossessions this year.

### Stand leases

Leases a stand (commercial, industrial, agricultural…) for a term at an annual rent reviewed and escalated every so many years; Schedule shows the rent for each year; the monthly run bills the leases and recognises the income straight-line.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Schedule, Run month; default Create |
| Lease number (schedule) | no | Text |
| Stand number (create) | no | Text |
| Lessee customer code | no | Text |
| Use | yes | One of: Commercial, Industrial, Agricultural; default Commercial |
| Term (years) | yes | Number; default 10 |
| Annual rent | yes | Number; default 0 |
| Review every (years) | yes | Number; default 1 |
| Escalation % | yes | Number; default 0 |
| Lease income account | no | Text |
| Accrued income account | no | Text |
| Date | yes | Date |

The list beside the form shows: Stand leases.

### Housing grants

Receives a grant (deferred until its conditions are assured), assures the conditions, and records expenditure to date so income is recognised as the money is spent (IPSAS 23); the list is the disclosure of each grant's recognised and deferred amounts.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Receive, Assure conditions, Expenditure to date; default Receive |
| Grant number | no | Text |
| Grantor (receive) | no | Text |
| Amount / expenditure to date | yes | Number; default 0 |
| Budgeted expenditure (receive) | yes | Number; default 0 |
| Conditions (receive) | no | Text |
| Bank account | no | Text |
| Deferred income account | no | Text |
| Grant income account | no | Text |
| Date | yes | Date |

The list beside the form shows: Grant disclosure.

### Link stands to municipal accounts

Sets when a housing stand becomes a municipal property (on allocation accepted, sale or title registered) for a ward, and which service tariffs it starts on (tariff codes, comma-separated); the link then happens as those events occur.

| Field | Required | Notes |
| --- | --- | --- |
| When | yes | One of: AllocationAccepted, Sale, TitleRegistered; default AllocationAccepted |
| Ward | yes | Text |
| Tariff codes, comma-separated | no | Text |

The list beside the form shows: Schemes.

## Ownership and title

### Transfer of ownership

Lodge a transfer (sale, cession, inheritance, donation, court order) to a new holder; verify each requirement with its document; the checklist shows what is outstanding (rates and housing account clearances are checked live); submit, approve (by someone else), complete or reject.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Lodge, Verify, Checklist, Submit, Approve, Complete, Reject; default Lodge |
| Application number | no | Text |
| Stand number (lodge) | no | Text |
| Transfer type | yes | One of: Sale, Cession, Inheritance, Donation, CourtOrder, Matrimonial, CompanyOrTrust, ChangeOfName, Correction, DeceasedJointHolder; default Sale |
| New holder customer code (lodge) | no | Text |
| Consideration | yes | Number; default 0 |
| Requirement (verify) | yes | One of: MunicipalClearance, HousingAccountClear, Identity, SpousalConsent, OriginalDocuments, EstateDocuments, FeesReceipted, TaxWithholding; default MunicipalClearance |
| Document reference / reason | no | Text |
| Date | yes | Date |

The list beside the form shows: Transfer applications.

### Joint ownership

Registers a stand's joint holders ("CUST1:50; CUST2:50", jointly with survivorship or in common), transfers part of a holder's share, records a holder's death (survivors take the share under survivorship) and shows the holders on a date.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Register, Transfer share, Record death, Holders; default Register |
| Stand number | yes | Text |
| Regime | yes | One of: JointWithSurvivorship, InCommon; default JointWithSurvivorship |
| Holders: CUSTOMER:share %; … | no | Text |
| From / deceased customer code | no | Text |
| To customer code | no | Text |
| Portion (% of the stand) | yes | Number; default 0 |
| Date | yes | Date |

The list beside the form shows: Stands.

### Title deeds

Apply for a stand's title, then move the application through conveyancer assigned → lodged → query raised / resolved → registered → in custody → handed over (a conveyancer is named when assigned). The list shows open applications with days at their stage.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Apply, Advance; default Apply |
| Stand number (apply) | no | Text |
| Title application number (advance) | no | Text |
| To stage | yes | One of: Applied, ConveyancerAssigned, Lodged, QueryRaised, QueryResolved, Registered, InCustody, HandedOver; default Applied |
| Conveyancer | no | Text |
| Note | no | Text |
| Date | yes | Date |

The list beside the form shows: Title pipeline.

### Title deed pipeline

Where every title deed application stands.

### Sectional title schemes

Registers a sectional title scheme on a stand with its units' floor areas ("U1:80; U2:120" — participation quotas follow from the areas), transfers a unit, lists the units and their owners, and raises a period's levy shared by participation quota.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Register, Transfer unit, Units, Raise levy; default Register |
| Scheme code | yes | Text |
| Name (register) | no | Text |
| Stand number (register) | no | Text |
| Units: number:floor area; … (register) | no | Text |
| Unit number (transfer) | no | Text |
| To customer code (transfer) | no | Text |
| Consideration / levy | yes | Number; default 0 |
| Levy period (yyyyMM) | yes | Number; default 0 |
| Levy income account | no | Text |
| Date | yes | Date |

The list beside the form shows: Stands.

## Rentals

### Rental units and tenancies

Add unit: a council rental unit with its rooms and rent. Let: a tenancy to a customer (the deposit is held as a liability). Rent run: bills every tenancy for a month (lodger fees included). End: final rent, the deposit applied to arrears and the rest refunded. Occupancy limit: the most people a unit may house.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Add unit, Let, Rent run, End tenancy, Occupancy limit; default Add unit |
| Unit code | no | Text |
| Description (add unit) | no | Text |
| Unit type | yes | One of: House, Flat, Room, HostelBed, Cottage; default House |
| Rooms | yes | Number; default 1 |
| Monthly rent | yes | Number; default 0 |
| Rent income account (add unit) | no | Text |
| Tenant customer code (let) | no | Text |
| Tenancy number (end) | no | Text |
| Deposit (let) | yes | Number; default 0 |
| Bank account (deposit / refund) | no | Text |
| Deposit liability account (let) | no | Text |
| Most people (blank: no limit) | no | Number |
| Date | yes | Date |

The list beside the form shows: Tenancies.

### Rent tariffs and review

Tariff: the monthly rent for a kind of unit from the first of a month, optionally only in one suburb and for one income category (the most specific applies). Suburb / Income category: place a unit, classify a tenant household. Apply tariffs: from the first of a month every unit's asking rent and every running tenancy's rent follows its tariff. Review by %: raises every rent (or one unit type's) by a percentage.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Tariff, Apply tariffs, Review by %, Suburb, Income category; default Tariff |
| Unit type | yes | One of: (any), House, Flat, Room, HostelBed, Cottage; default (any) |
| Suburb (blank: any) | no | Text |
| Income category | yes | One of: (any), Low, Middle, High; default (any) |
| Monthly rent / percentage | yes | Number; default 0 |
| Unit code (suburb) | no | Text |
| Tenancy number (income category) | no | Text |
| Effective from (first of the month) | yes | Date |

The list beside the form shows: Rent tariffs.

### Occupants and lodger permits

Add occupant / move out: the household living with the tenant, within the unit's occupancy limit. Permit: a lodger on a permit (the tenant keeps a room of their own; a monthly fee goes on the rent). Revoke: ends a permit. Who lives there: the household and lodgers on a date.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Who lives there, Add occupant, Move out, Issue permit, Revoke permit; default Who lives there |
| Tenancy number (permit: to revoke, its number) | yes | Text |
| Name (occupant / lodger) | no | Text |
| National ID | no | Text |
| Relationship to the tenant | no | Text |
| Occupant id (move out) | no | Number |
| Rooms let to the lodger | yes | Number; default 1 |
| Monthly lodger fee | yes | Number; default 0 |
| Lodger fee income account | no | Text |
| Date / from | yes | Date |
| Permit last day | yes | Date |
| Reason (revoke) | no | Text |

The list beside the form shows: Lodger permits.

### Rental maintenance

Report a repair on a unit; complete it with its cost (recharged to the tenant when they caused it); record an inspection with its condition grade (1–5) and whether it needs follow-up, and close the follow-up; History summarises a unit's repairs and condition. Void details record why a void unit is empty and its refurbishment job. The planned maintenance programme: plan a task (due on the date, then every so many months), list what is due, and mark one done — which raises the repair that carries its cost.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Report repair, Complete repair, Inspection, Close follow-up, History, Void details, Plan task, Planned due, Planned done; default Report repair |
| Unit code | no | Text |
| Repair number (complete) | no | Text |
| Void reason | no | Text |
| Refurbishment job (contract) number | no | Text |
| Planned: every (months, 0 = once) | yes | Number; default 12 |
| Planned task id | no | Number |
| Description / findings | no | Text |
| Cost | yes | Number; default 0 |
| Repairs expense account | no | Text |
| Paid from account | no | Text |
| Tenant caused it | no | Flag; default False |
| Recharge income account | no | Text |
| Inspector | no | Text |
| Condition grade (1–5) | yes | Number; default 3 |
| Needs follow-up | no | Flag; default False |
| Inspection id (close) | no | Number |
| Date | yes | Date |

The list beside the form shows: Rental units.

### Evictions

Open a case on a tenancy in arrears (with the notice period), advance it reminder → demand → notice to vacate → legal handover → court order → eviction → unit returned (each with its reference; the last settles the deposit), or withdraw it with the reason.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Open, Advance, Withdraw; default Open |
| Tenancy number (open) | no | Text |
| Case number | no | Text |
| Notice period (days) | yes | Number; default 30 |
| Stage (advance) | yes | One of: Reminder, Demand, NoticeToVacate, LegalHandover, CourtOrder, Evicted; default Reminder |
| Reference / reason | no | Text |
| Refund bank account (unit returned) | no | Text |
| Date | yes | Date |

The list beside the form shows: Open eviction cases.

### Rental property accounting (IAS 40)

Rental units are held to earn rentals: carry each as investment property (cost or fair value model) or PPE, never as stand stock. Designate brings the unit into its asset account at its carrying amount from the account it comes from; Revalue remeasures fair-value investment property, the change to the fair-value gain or loss account.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Designate, Revalue; default Designate |
| Unit code | yes | Text |
| Carried as | yes | One of: NotDesignated, InvestmentPropertyCost, InvestmentPropertyFairValue, PropertyPlantEquipment; default NotDesignated |
| Asset account | no | Text |
| Carrying amount / fair value | yes | Number; default 0 |
| From account (designate) | no | Text |
| Fair-value gain/loss account (revalue) | no | Text |
| Date | yes | Date |

The list beside the form shows: Investment property and PPE register.

## Schemes and development

### Scheme budget and costs

Sets a housing scheme's budget for each cost element (land, servicing, professional fees, overheads) and records actual costs against them, capitalised into the scheme's stands.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Set budget, Record cost; default Set budget |
| Scheme code | yes | Text |
| Cost element | yes | One of: Land, Servicing, ProfessionalFees, Overheads; default Land |
| Amount | yes | Number |
| Reference (cost) | no | Text |
| Description (cost) | no | Text |
| Stands (WIP) account | no | Text |
| Paid from / funding account | no | Text |
| Date | yes | Date |

The list beside the form shows: Schemes.

### Scheme progress

Records survey progress on stands (pegged, diagram lodged, diagram approved — several stands comma-separated), a bulk-service servicing certificate (water, sewer, roads, electricity, storm water: % complete and stands reached), and a land parcel's valuation.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Survey, Servicing certificate, Valuation; default Survey |
| Stand numbers, comma-separated (survey) | no | Text |
| Survey stage | yes | One of: Pegged, DiagramLodged, DiagramApproved; default Pegged |
| Scheme code (servicing) | no | Text |
| Bulk service | yes | One of: Water, Sewer, Roads, Electricity, StormWater; default Water |
| % complete | yes | Number; default 0 |
| Stands reached | yes | Number; default 0 |
| Land parcel number (valuation) | no | Text |
| Value | yes | Number; default 0 |
| Valuer | no | Text |
| Reference | no | Text |
| Date | yes | Date |

The list beside the form shows: Schemes.

### Servicing cost apportionment

Links a scheme to the servicing contract whose cost it carries and how it is shared (equally, by area, by frontage, by zoning weight); sets a stand's frontage and zoning weight; apportions the contract's cost to date to the stands; Cost per stand shows each stand's land and servicing cost.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Link contract, Stand measures, Apportion, Cost per stand; default Link contract |
| Scheme code | no | Text |
| Job contract number (link) | no | Text |
| Share by | yes | One of: Equal, ByArea, ByFrontage, ByZoningWeight; default Equal |
| Stand number (measures) | no | Text |
| Frontage (m) | no | Number |
| Zoning weight | no | Number |
| Servicing WIP account (apportion) | no | Text |
| Stands available account (apportion) | no | Text |
| Date | yes | Date |

The list beside the form shows: Schemes.

### Developer agreements

A developer services a scheme for a share of its stands: create the agreement (council and developer quotas, delivery deadline, penalty per stand a month late), add and complete milestones, assign stands to a quota ("S1,S2"), hand stands over, reconcile delivery against the quotas, and charge accrued penalties.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Add milestone, Complete milestone, Assign stands, Hand over, Reconcile, Charge penalties; default Create |
| Agreement number | no | Text |
| Scheme code (create) | no | Text |
| Developer customer code (create) | no | Text |
| Council stands | yes | Number; default 0 |
| Developer stands | yes | Number; default 0 |
| Delivery deadline / milestone due | yes | Date |
| Penalty per stand per month late | yes | Number; default 0 |
| Milestone description | no | Text |
| Quota (assign) | yes | One of: Council, Developer; default Council |
| Stand number(s), comma-separated | no | Text |
| Penalty income account | no | Text |
| Date | yes | Date |

The list beside the form shows: Developer agreements.

### Development levies

Defines a levy charged on allocation or on transfer — flat, per m² or a percentage of the price — to an income account, or deactivates it; charges the levies due on an allocation certificate or a transfer application.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Define, Deactivate, Charge allocation, Charge transfer; default Define |
| Levy code | no | Text |
| Name | no | Text |
| Charged on | yes | One of: Allocation, Transfer; default Allocation |
| Basis | yes | One of: Flat, PerSquareMetre, PercentOfPrice; default Flat |
| Rate / amount | yes | Number; default 0 |
| Income account | no | Text |
| Certificate / application number (charge) | no | Text |
| Date | yes | Date |

The list beside the form shows: Levies.

### Development covenants

A covenant that the holder develops a stand within so many years: record its milestones (plan submitted / approved, construction started, completed), grant an extension (with any fee), run the breach check (a penalty levy on those past their deadline) and issue a notice.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Milestone, Extension, Run breaches, Notice; default Create |
| Covenant number | no | Text |
| Stand number (create) | no | Text |
| Holder customer code (create) | no | Text |
| Years to develop (create) | yes | Number; default 2 |
| Milestone | yes | One of: PlanSubmitted, PlanApproved, ConstructionStarted, Completed; default PlanSubmitted |
| Reference | no | Text |
| Extension months | yes | Number; default 6 |
| Extension fee / penalty levy | yes | Number; default 0 |
| Fee / levy income account | no | Text |
| Date | yes | Date |

The list beside the form shows: Covenants.

### Housing cooperatives

Registers a cooperative, adds and removes members, allocates stands to it ("S1,S2") and sub-allocates each to a member; Stands lists what it holds and to whom. Account: links its receivables account (customer code). Charge: invoices it. Contribution: a member's payment receipted on its account. Statement: its account for the period from the date in Since to Date, with each member's contributions.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Register, Add member, Remove member, Allocate stands, Sub-allocate, Stands, Account, Charge, Contribution, Statement; default Register |
| Cooperative code | yes | Text |
| Name / member name | no | Text |
| Registration number | no | Text |
| Member national ID | no | Text |
| Stand number(s), comma-separated | no | Text |
| Account customer code | no | Text |
| Amount | yes | Number; default 0 |
| Income account (charge) / bank (contribution) | no | Text |
| Document / receipt number | no | Text |
| Statement from (yyyy-MM-dd) | no | Text |
| Date | yes | Date |

The list beside the form shows: Cooperatives.

### Informal settlements

Registers a settlement (with its enumeration cut-off date), approves its upgrading plan, runs an enumeration of its households (eligibility checked on each), closes it, then turns an eligible household into a waiting-list registration or regularises it on a stand; Summary shows where the settlement stands.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Register, Approve plan, Start enumeration, Enumerate household, Close enumeration, To waiting list, Regularise, Summary; default Register |
| Settlement code | no | Text |
| Name / plan / enumeration reference | no | Text |
| Ward (register) | no | Text |
| Cut-off date (register) | yes | Date |
| Household number | no | Text |
| Head's national ID | no | Text |
| Head's name | no | Text |
| Head's date of birth (yyyy-MM-dd) | no | Text |
| Household size | yes | Number; default 1 |
| Monthly income | yes | Number; default 0 |
| Occupied since (yyyy-MM-dd) | no | Text |
| Structure | yes | One of: Shack, Brick, Mixed, Other; default Shack |
| Structure reference | no | Text |
| Waiting-list category | yes | One of: HighDensity, MediumDensity, LowDensity, Flats, Rental; default HighDensity |
| Stand number (regularise) | no | Text |
| Date | yes | Date |

The list beside the form shows: Settlements.

## Accounts

### Receivables control per tenure type

Instalment debtors post to a dedicated receivables control account per tenure type (instalment sale, rent-to-buy, lease with option): a buyer goes onto it when their agreement is signed. Customer control sets one customer's control directly (only before they have items). The control reconciliation matches each control to its own customers.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Tenure control, Customer control; default Tenure control |
| Tenure type | yes | One of: InstalmentSale, RentToBuy, LeaseWithOption; default InstalmentSale |
| Customer | no | Choice |
| Receivables control account (blank: main) | no | Text |

The list beside the form shows: Tenure control accounts.

### Housing expected credit loss

Assesses the IFRS 9 expected credit loss on housing debtors (instalment buyers and tenants) by arrears band with the default matrices, showing the allowance needed against what is booked; Post books the difference.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Assess, Post; default Assess |
| As at | yes | Date |
| Loss allowance account | no | Text |
| Impairment expense account | no | Text |

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| H1 | Land Bank Register | All parcels with area, cost, status and mapped area. |
| H10 | Layout Plans Register & Approval Status | Layouts with the version in force, pending amendments and approval turnaround. |
| H11 | Stand Creation / Subdivision Report | Stands created from each parent parcel, with size, scheme, status and carried cost. |
| H12 | Survey Status Report | Per scheme as at the date: stands not pegged, pegged, awaiting the Surveyor-General and approved, with the longest wait. |
| H13 | Servicing Status by Scheme | Water, sewer, roads, electricity and storm water % complete, with stands serviced and unserviced. |
| H14 | Servicing Cost Apportionment Schedule | Servicing cost per stand by contract and basis. |
| H15 | Cost per Stand / Selling Price Build-up | Land and servicing cost, overhead, levy and margin to price, with margin %. |
| H16 | Price List by Scheme & Stand Size Band | Every pricing rule with its scope, effective date, terms and approval — the price change history. |
| H17 | Stand Inventory Movement & Reconciliation | Year to date: opening stands + created + repossessed − sold = closing, in units and cost, reconciled to the stands control account. |
| H18 | Waiting List Register | Active applicants by category in seniority order with score and position. |
| H19 | Waiting List Summary | Active applicants by category, income band and age band. |
| H2 | Land Acquisition Register | Acquisitions by method with counterparty, council resolution, status and cost. |
| H20 | Waiting List Age Analysis | How long active applicants have been waiting, by category. |
| H21 | Applicant Detail / Verification Sheet | One applicant: every captured detail, notices, date changes and the score derivation — printable for the applicant. |
| H22 | Priority Score Derivation | How each active applicant's points were computed, factor by factor. |
| H23 | Duplicate & Suspected Fraudulent Applicants | Fuzzy name, ID and date-of-birth matches across the waiting list, stand holders and the property register. |
| H24 | Applicants Already Owning Property | Active applicants holding a council stand or owning rated property — disqualification candidates. |
| H25 | Registration Renewal Compliance | Applicants by status with last confirmation, months since and the notices sent. |
| H26 | Registration Date Change Audit | Every requested change to a seniority date: who asked, who decided, why. |
| H27 | Registration Fee Collection Report | Waiting-list fees received, and applicants registered without paying the registration fee. |
| H28 | New Registrations & Removals Movement | Month by month for the year to date: opening list, registered, removed, allocated, closing. |
| H29 | Demand vs Supply (the Housing Gap) | Active applicants by category against stands and rental units available for it. |
| H3 | Land Acquisition Cost Build-up per Parcel | Each acquisition's cost by element with the total and cost per m². |
| H30 | Proposed Allocation List | Pre-approval pairings of runs awaiting approval: stand, applicant, place and method. |
| H31 | Allocation Register | Allocations with stand, beneficiary, certificate, frozen price and approval. |
| H32 | Ballot / Draw Result Report | Ballot runs with the stored seed and input hash, re-verified now. |
| H33 | Allocations by Scheme, Month and Approver | Approved allocations counted by scheme, approval month and approving officer. |
| H34 | Skipped Applicant / Substitution Report | Per allocation run: listed applicants left out and why, and offers declined, lapsed or cancelled — the appeal-defence report. |
| H35 | Offers Outstanding / Expiring / Expired | Open offers with the acceptance deadline countdown. |
| H36 | Declined, Lapsed & Cancelled Offers | Offers that did not proceed, with when and why. |
| H37 | Quota Compliance Report | Per scheme: allocations to each beneficiary group against its policy quota. |
| H38 | Appeals Register & Outcomes | Appeals lodged against allocation runs with hearing and outcome. |
| H39 | Double Allocation Exception Report | Stands promised through more than one route — must always be empty; any row is an incident. |
| H4 | Due Diligence Outstanding | Open acquisitions and the due-diligence items blocking each. |
| H40 | Beneficiaries with More Than One Stand | People (by national ID) holding or allocated more than one stand across schemes. |
| H41 | Allocation-to-Agreement Conversion | Accepted allocations with no agreement or instalment sale yet, with age since acceptance. |
| H42 | Agreement Register | Agreements by type with party, stand, version in force, signature and price. |
| H43 | Beneficiary Account Statement | Charges, instalments, receipts and balance per beneficiary, with the next instalment — one or all (burst by beneficiary). |
| H44 | Instalment Amortisation Schedule | Each instalment's capital, interest and balance — for one agreement or all. |
| H45 | Housing Debtors Age Analysis | Instalment debtors aged 30/60/90/120+ by tenure type and scheme, as at any date. |
| H46 | Instalment Arrears & Default Report | Active instalment sales with overdue instalments, amount owing and last payment. |
| H47 | Payment Arrangements Register | Arrangements with housing debtors and tenants: terms, status and standing as at the date. |
| H48 | Collections Report | Receipts from housing debtors and tenants in the month, by scheme or tenancy, day, method, currency and officer. |
| H49 | Fully Paid / Ready for Transfer | Sold stands whose holders owe nothing and have no title application — the most actionable list. |
| H5 | Compulsory Acquisition Compensation Register | Compulsory acquisitions: notice served, objections, assessed, offered, paid and outstanding, and possession. |
| H50 | Early Settlement & Rebate Report | Instalment sales settled early with the interest rebated. |
| H51 | Deposits Held Register | Liability listing as at the date: purchase deposits against accepted offers and tenancy deposits, by scheme and beneficiary. |
| H52 | Expected Credit Loss (IFRS 9) Provision Working | Housing debtors' exposure and provision by portfolio and arrears band, with the allowance booked. |
| H53 | Cancellations & Refund Computation | Agreements cancelled at the buyer's request: paid, less occupation rental, admin charge and arrears, the refund. |
| H54 | Development Covenant Compliance | Covenants with the latest milestone, deadline, extensions and breach status. |
| H55 | Repossession Register | Stands repossessed with the receivable derecognised, NRV, inventory recognised and gain or loss. |
| H56 | Ownership History / Chain of Title per Stand | Every holder of a stand in sequence with the change type, date and consideration, and whether the hash chain verifies. |
| H57 | Change of Ownership Register | Completed transfers by type with parties, consideration and clearance relied on. |
| H58 | Transfers in Progress / Pending Approval | Open transfer applications with status, approvals and age. |
| H59 | Transfers Blocked by Outstanding Rates or Housing Arrears | Open transfers failing a live check, with what is needed to clear them. |
| H6 | Land Disposals & Exchanges Register | Land sold, exchanged, donated or expropriated: resolution, area, cost out, consideration and gain or loss. |
| H60 | Transfer Checklist Exception Report | Open transfer applications and the mandatory documents still missing. |
| H61 | Deceased Estate Transfers Register | Inheritance and deceased-joint-holder transfers with the estate documents relied on. |
| H62 | Development Levy & Transfer Fee Collection | Levies and fees raised, by levy, with the event they were raised on. |
| H63 | Transfer Duty Deducted and Remitted | Duty collected as agent on each transfer, and whether it has been paid over. |
| H64 | Title Deed Pipeline Report | Open title applications with stage, days at the stage, days since applied and conveyancer. |
| H65 | Title Deeds Held but Uncollected | Deeds in council custody not yet handed over, aged, with the owner's contact — the chase list. |
| H66 | Deeds Register | Registered deeds with registration date and current custody. |
| H67 | Conveyancer Performance | Files allocated, registered, average days to registration and registry queries per conveyancer. |
| H68 | Caveats, Blocks & Disputes Register | Every block placed: reason, particulars, document, who placed and lifted it, age. |
| H69 | Stands with No Recorded Owner / Broken Ownership Chain | Data-integrity exceptions — must be empty. |
| H7 | Encumbrances, Servitudes & Caveats on Land | Every right or restriction registered against land, in force or lifted. |
| H70 | Sectional Title Unit Register & Participation Quotas | Units of every sectional scheme with floor area, quota and owner. |
| H71 | Rent Roll | Let units with tenant, rent and arrears. |
| H72 | Housing Stock Register & Condition | Every unit with type, rooms, occupancy and its latest inspection grade. |
| H73 | Occupancy & Voids Report | Void units with days void and rent lost to voids. |
| H74 | Tenant Statement | Rent charges, receipts and balance per tenant — one or all (burst by tenant). |
| H75 | Rent Arrears Age Analysis | Tenants' arrears aged 30/60/90/120+ by unit type, as at any date. |
| H76 | Eviction Pipeline & Register | Eviction cases with the current stage, its date and the arrears at opening. |
| H77 | Lease Expiry & Renewal Due | Stand leases and tenancies ending within a year of the date. |
| H78 | Tenant Deposits Held | Deposits held on active tenancies (a liability listing). |
| H79 | Maintenance & Repairs Cost per Unit | Completed repairs per unit, separating what was recharged to tenants. |
| H8 | Disputed & Encroached Land | Disputes open at the date with their age and current legal stage. |
| H80 | Inspections Due & Findings Summary | Units never inspected or not inspected for a year, and follow-ups outstanding. |
| H81 | Straight-lined Rental Income Schedule (IFRS 16) | Each stand lease's contractual rent per year against the straight-line income recognised. |
| H82 | Scheme Delivery Dashboard | Per scheme: target, stands created, serviced, allocated, sold and titled, and % delivered. |
| H83 | Scheme Profitability & Cost Recovery | Per scheme: cost in (land and servicing), revenue from stands sold, cost recovered and margin. |
| H84 | Scheme Budget vs Actual | Per scheme and cost element (land, servicing, professional fees, overheads): budget, actual, variance and share used. |
| H85 | Developer / Joint Venture Reconciliation | Stands contracted vs delivered per quota, overdue milestones and penalties. |
| H86 | Housing Cooperative Register | Cooperatives with active members, members' contributions, the balance on the cooperative's account, stands held and stands sub-allocated. |
| H87 | Informal Settlement / Enumeration Register | Settlements with ward, cut-off date, households counted and eligible. |
| H88 | Regularisation Progress Report | Per settlement: enumerated → eligible → waiting-listed or regularised → outstanding. |
| H89 | Housing Subsidy & Grant Utilisation | Grants received, spent, recognised and deferred (IAS 20), with conditions. |
| H9 | Land Bank Valuation vs Carrying Value (IAS 2 NRV test) | Undivided parcels against their latest valuation and unsold stands against their approved price, less costs to sell — the write-down working paper. |
| H90 | Housing Audit Trail Report | Every recorded change to housing records — before and after values, user, time. |
| H91 | Allocation & Ownership Change Approval Audit | Who requested and who approved each run, transfer, seniority change, price rule and block lift — turnaround and dual-authorisation exceptions. |
| H92 | Segregation of Duties Violations — Housing | Users whose permissions combine housing duties that must be separated. |
| H93 | Manual Override & Exception Report | Seniority dates changed, stand-specific prices, offers without an approved price and blocks lifted — each with the stated reason. |
| H94 | Housing Revenue Summary | Year to date against the same period last year: stand sales, instalment interest, rentals and recharges, development levies. |
| H95 | Housing Executive Dashboard | Land bank, stands, waiting list, allocations, titles and debtors at a glance. |
| H96 | Housing Delivery / Statutory Return | Stands delivered in the year by density, tenure and beneficiary category. |
| H97 | Ward Housing Pack | One ward for its councillor: schemes, stands by status, allocations, beneficiaries, instalment arrears and the council-wide waiting list. |
| HSE-STAND | Housing Stand Register | Stands with status, apportioned cost, selling price and current holder. |
