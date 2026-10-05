# Report Catalogue

Generated from the system report definitions (`SystemReportCatalogue.All()`); do not edit by hand — run the `ReportCatalogueDocumentTests` with `ONE4ALL_WRITE_CATALOGUE=1` to regenerate.

435 reports. Every report renders to HTML · PDF · XLSX · CSV · Text.

## Budget

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| B12 | Budget Version Comparison | For each budget with more than one version: the latest against the one before, per account. | AsAtDate (Date, optional) | Budget, Versions, Account, Name, Previous, Current, Percent, Change | — | Sum(Change) |
| B15 | Multi-Year Budget & Actual Comparison | Each budgeted account's budget and actual for this year and the two before. | AsAtDate (Date, optional) | Account, Name, Budget2, Actual2, Budget1, Actual1, Budget0, Actual0 | — | Sum(Actual0) |
| B17 | Budget Versions, Seals & Superseded Budgets | Every budget held: type, version, status, why it was revised, and whether its seal still verifies. | AsAtDate (Date, optional) | Budget, Year, Type, Version, Status, Active, Reason, Seal, Total | — | Sum(Total) |
| B18 | Budget Line Change Log | Every recorded change to budget lines in the year, with who made it and the fields changed. | AsAtDate (Date, optional) | When, User, Id, Action, Changed | — | — |
| B2 | Departmental Budget Submission | Every budget of the year by department and version: each account's quarters and annual total. | AsAtDate (Date, optional) | Budget, Version, Status, Department, Account, Name, Q1, Q2, Q3, Q4, Total | — | Sum(Total) |
| B20 | Virement Register | Transfers between budget lines in the year: from, to, period, amount, justification, who asked and who reviewed. | AsAtDate (Date, optional) | Requested, From, To, Period, Justification, Status, RequestedBy, ReviewedBy, Amount | — | Sum(Amount) |
| B24 | Vote Book | Per budgeted account and department: the year's budget, actual to date, open commitments and what remains. | AsAtDate (Date, optional) | Account, Name, Department, Budget, Actual, Committed, Remaining | — | Sum(Remaining) |
| B25 | Commitment / Encumbrance Register | Every commitment of the year with reference, account, department, period and status. | AsAtDate (Date, optional) | Reference, Account, Department, Period, Type, Status, Amount | — | Sum(Amount) |
| B26 | Commitments vs Open Purchase Orders | Open commitments against the open value of the purchase order they reference, with any difference. | AsAtDate (Date, optional) | Reference, Order, OpenValue, Committed, Difference | — | Sum(Difference) |
| B28 | Expenditure Against Unappropriated Votes | Expense accounts with spending this year but no line in an approved budget. | AsAtDate (Date, optional) | Account, Name, Postings, Actual | — | Sum(Actual) |
| B33 | Budget Utilisation (RAG) | Year-to-date use of each budgeted account's year-to-date budget: green under 90%, amber to 100%, red over. | AsAtDate (Date, optional) | Account, Name, Budget, Used, Rag, Actual | — | Sum(Actual) |
| B34 | Over-Budget & Threshold Exceptions | Only the budgeted accounts at amber or red, worst first. | AsAtDate (Date, optional) | Account, Name, Budget, Used, Rag, Over | — | Sum(Over) |
| B36 | Variance Commentary Pack | The year's variance explanations by period and account, beside the budget and actual they explain. | AsAtDate (Date, optional) | Period, Account, Name, Budget, Actual, Author, Comment | — | — |
| B37 | Departmental Management Accounts | Per department, year to date: income and expenditure against budget, and the surplus or deficit. | AsAtDate (Date, optional) | Department, IncomeBudget, Income, ExpenseBudget, Expense, Surplus | — | Sum(Surplus) |
| B44 | Budget Accuracy Review | For the year before the as-at date's year: each account's full-year budget against actual, the variance and how accurate the budget was. | AsAtDate (Date, optional) | Account, Name, Budget, Actual, Percent, Accuracy, Variance | — | Sum(Variance) |
| B45 | Budget Audit Trail | Every recorded change in the year to budgets, lines, virements, commitments and variance comments. | AsAtDate (Date, optional) | When, User, Entity, Id, Action, Changed | — | — |
| BUD-BVA | Budget vs Actual | Active budget compared to GL actuals by account, with the variance. | FiscalYearId (Integer, optional), Department (String, optional), Period (Integer, optional) | AccountCode, Budget, Actual, Variance | — | Sum(Variance) |
| BUD-LIST | Budget Listing | All budget versions with type, department, status, active flag and annual total. | — | Label, Type, Department, Version, Status, Active, AnnualTotal | — | Sum(AnnualTotal) |

## CRM

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| CRM-PIPE | Sales Pipeline | Open opportunities by stage — estimated value, win probability and the probability-weighted value. | — | Name, Contact, Stage, Probability, Estimated, Weighted | — | Sum(Weighted) |

## CashBook

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| CB-FLOW | Cash-Flow Forecast | Expected receipts and payments from open receivables and payables, bucketed by due date, with the running net. | AsAtDate (Date, optional) | Bucket, Inflow, Outflow, Net, RunningNet | — | Sum(RunningNet) |
| CB68 | Cash Book Listing | Every posting to a bank account's ledger in the period with running balance and whether it is reconciled. | BankCode (String, optional), FromDate (Date, optional), ToDate (Date, optional) | Bank, Date, Journal, Document, Narrative, Receipts, Payments, Balance, Reconciled | — | — |
| CB70 | Unpresented & Uncleared Items Ageing | Ledger postings not yet on a bank statement and statement lines not yet on the ledger, aged at the date. | AsAtDate (Date, optional) | Bank, Kind, Date, Reference, Days, Band, Amount | — | Sum(Amount) |
| CB72 | 13-Week Cash Flow Forecast by Currency | From today's bank balances, week by week: customer receipts and supplier payments falling due, and the closing cash, per currency. | AsAtDate (Date, optional) | Week, Currency, Opening, Receipts, Payments, Closing | — | — |
| CB73 | Petty Cash & Imprest Report | Disbursements and top-ups on the petty cash account in the period with its running balance. | AccountCode (String, optional), FromDate (Date, optional), ToDate (Date, optional) | Date, Journal, Document, Narrative, In, Out, Balance | — | — |
| CB74 | Inter-account Transfers | Transfers between bank accounts in the period, with the amount out, the amount in and any exchange difference. | FromDate (Date, optional), ToDate (Date, optional) | Date, Journal, Document, From, To, Out, In, Difference | — | Sum(Difference) |

## Expenses

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| EXP-CLAIMS | Expense Claims | Employee expense claims with reference, date, total, status and reimbursement journal. | — | Reference, Employee, ClaimDate, Total, Status, Journal | — | Sum(Total) |

## FixedAssets

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| FA-DISP | Asset Disposals | Disposed assets with proceeds, net book value at disposal, the gain or loss, and the journal — newest first. | — | AssetNumber, Date, Proceeds, NetBookValue, GainOrLoss, Journal | — | Sum(GainOrLoss) |
| FA-FCST | Depreciation Forecast | Projected monthly depreciation across active assets and the aggregate closing net book value. | StartMonth (Date, optional), Months (Integer, optional) | Period, Charge, ClosingNetBookValue | — | Sum(ClosingNetBookValue) |
| FA-MAINT | Asset Maintenance Log | Maintenance and service events logged against assets, newest first, with the informational cost. | — | AssetNumber, Asset, Date, Work, Provider, Cost | — | Sum(Cost) |
| FA-REG | Fixed Asset Register | Assets with cost, accumulated depreciation and net book value. | AsAtDate (Date, optional) | AssetNumber, Description, Cost, AccumulatedDepreciation, NetBookValue | — | Sum(NetBookValue) |
| FA207 | Depreciation Schedule (actual) | Depreciation charged in the period, by month and asset, from the posted depreciation runs. | FromDate (Date, optional), ToDate (Date, optional) | Month, Asset, Description, Class, Charge | — | Sum(Charge) |
| FA208 | Asset Additions & Transfers | Assets acquired in the period, and changes of location or custodian recorded in the audit trail. | FromDate (Date, optional), ToDate (Date, optional) | Kind, Asset, Description, Class, Date, Detail, Cost | — | Sum(Cost) |
| FA209 | Asset Movement Schedule (note) | Per asset class: cost and accumulated depreciation brought forward, additions, disposals, charge and carried forward, and the net book value. | FromDate (Date, optional), ToDate (Date, optional) | Class, CostOpening, Additions, Disposals, CostClosing, DepOpening, Charge, DepDisposals, DepClosing, Nbv | — | Sum(Nbv) |
| FA212 | Fully Depreciated Assets Still in Use | Assets written down to residual value that are still held, with where they are and who holds them. | — | Asset, Description, Class, Acquired, Location, Custodian, Cost | — | Sum(Cost) |

## GeneralLedger

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| GL-BS | Balance Sheet | Assets, liabilities and equity as at a date; assets equal liabilities plus equity. | AsAtDate (Date, optional) | Category, AccountName, Amount | — | Sum(Amount) |
| GL-CF | Statement of Cash Flows | Cash generated by operating, investing and financing activities for a period, reconciled to the change in cash. | FromDate (Date, optional), ToDate (Date, optional) | Activity, Amount | — | Sum(Amount) |
| GL-COA | Chart of Accounts | Every ledger account with its type, report category, control flag and active status. | — | AccountCode, AccountName, Type, ReportCategory, IsControl, IsActive | — | — |
| GL-CTRL | Control Reconciliation | GL control-account balances (AR, AP, Stock) versus their sub-ledger totals, with the difference — zero when they agree. | AsAtDate (Date, optional) | Control, AccountCode, GlBalance, SubLedgerBalance, Difference | — | Sum(Difference) |
| GL-DAYBOOK | Journal Daybook | Every posted journal over a period — date, number, source module, document, narrative and base total. | FromDate (Date, optional), ToDate (Date, optional) | Date, JournalNumber, SourceModule, DocumentType, DocumentNumber, Narrative, Amount | — | Sum(Amount) |
| GL-DEPT-IS | Departmental Income Statement | Income and expenses for a period restricted to one department; the difference is the department's net profit. | Department (String, optional), FromDate (Date, optional), ToDate (Date, optional) | Category, AccountName, Amount | — | Sum(Amount) |
| GL-IS | Income Statement | Income and expenses for a period; the difference is the net profit. | FromDate (Date, optional), ToDate (Date, optional) | Category, AccountName, Amount | — | Sum(Amount) |
| GL-RECUR | Recurring Journals | Standing recurring journal templates with code, description, source module, value, balanced and active flags. | — | Code, Description, SourceModule, Amount, Balanced, Active | — | Sum(Amount) |
| GL-SNAP | Financial Snapshot | Headline figures as at a date: assets, liabilities, equity and year-to-date income, expenses and net profit. | AsAtDate (Date, optional) | Metric, Amount | — | Sum(Amount) |
| GL-TB | Trial Balance | Account balances as at a date; debits must equal credits. | AsAtDate (Date) | AccountCode, AccountName, Debit, Credit | — | Sum(Debit), Sum(Credit) |
| GL-TBMOV | Trial Balance Movement | Per account: opening balance brought forward, debits and credits in the period, and the closing balance. | FromDate (Date, optional), ToDate (Date, optional) | AccountCode, AccountName, Opening, Debit, Credit, Closing | — | Sum(Closing) |
| GL-TBTYPE | Trial Balance by Type | The trial balance summarised to one line per account type, with debit and credit totals that balance. | AsAtDate (Date, optional) | Type, Debit, Credit | — | Sum(Credit) |
| GL-VAT | VAT Return | Output tax on sales less input tax on purchases for a period, giving the net VAT payable. | FromDate (Date, optional), ToDate (Date, optional) | Metric, Amount | — | Sum(Amount) |
| GL-VATC | VAT Return by Code | Output and input tax by tax code for a period, with the net per code — the multi-rate VAT breakdown. | FromDate (Date, optional), ToDate (Date, optional) | TaxCode, OutputTax, InputTax, Net | — | Sum(Net) |
| GL10 | Notes to the Accounts Schedules | Accounts grouped by their note reference: opening balance, the period's movement and closing balance, with a total per note. | FromDate (Date, optional), ToDate (Date, optional) | Note, Account, Name, Opening, Movement, Closing | — | Sum(Closing) |
| GL11 | Management Accounts Pack | Results for the period and the year to date, the financial position at the end date and the working-capital balances. | FromDate (Date, optional), ToDate (Date, optional) | Section, Item, Period, YearToDate | — | — |
| GL12 | Budget vs Actual vs Commitment | Year to date per budgeted account and department: budget, actual, open commitments, what is still available and the share used. | AsAtDate (Date, optional) | Account, Name, Department, Budget, Actual, Committed, Used, Available | — | Sum(Available) |
| GL14 | Consolidation Elimination Schedule | Every intercompany document across the group in the period: each side's amount, and whether it eliminates or is unmatched. | FromDate (Date, optional), ToDate (Date, optional) | Document, Date, FromCompany, FromAmount, ToCompany, ToAmount, Status, Eliminated | — | Sum(Eliminated) |
| GL15 | FX Revaluation Report | Unrealised revaluations of foreign-currency balances (ledger and trading) in the period, by account. | FromDate (Date, optional), ToDate (Date, optional) | Date, Journal, Type, Narrative, Account, Amount | — | Sum(Amount) |
| GL16 | Realised FX Gain / Loss | Exchange differences realised on settlement in the period. | FromDate (Date, optional), ToDate (Date, optional) | Date, Journal, Document, Narrative, Account, Amount | — | Sum(Amount) |
| GL17 | Suspense & Unallocated Items Review | Balances on suspense accounts and unallocated receipts and payments on the trading ledgers at the date. | AsAtDate (Date, optional) | Item, Reference, Count, Oldest, Balance | — | Sum(Balance) |
| GL18 | Control Reconciliation — VAT and Bank | Tax accounts against the tax-coded postings, and each bank account's ledger balance against its statement, with what explains the gap. | AsAtDate (Date, optional) | Control, Account, Ledger, Other, Explained, Difference | — | Sum(Difference) |
| GL2 | Trial Balance — Multi-currency | Posted balances as at the date per account and transaction currency, in transaction and base. | AsAtDate (Date, optional) | Account, Name, Currency, Transaction, Base | — | Sum(Base) |
| GL20 | Year-End Pack | For the year containing the date: the close checklist, the trial balance totals, the result for the year and the year's journals by source. | AsAtDate (Date, optional) | Section, Item, Value | — | — |
| GL22 | Recurring Journals Due & Missed | Each active recurring template's last posted month against the date: posted, due or missed. | AsAtDate (Date, optional) | Code, Description, LastPosted, Status, Value | — | Sum(Value) |
| GL23 | Accruals, Prepayments & Deferred Income Schedule | Accrual and prepayment journals in the period, then deferred income schedules with what is left to recognise. | FromDate (Date, optional), ToDate (Date, optional) | Kind, Reference, Date, Detail, Recognised, Amount | — | Sum(Amount) |
| GL24 | Intercompany Balances & Reconciliation | Intercompany journals in the period and whether the counterparty company holds the mirror entry. | FromDate (Date, optional), ToDate (Date, optional) | Document, Date, Narrative, Counterparty, Status, Amount | — | Sum(Amount) |
| GL25 | Hyperinflation (IAS 29) Working Papers | Each account's historical balance restated by the price index at the date, monetary items unchanged, with the restatement. | AsAtDate (Date, optional), IndexCode (String, optional) | Account, Name, Type, Monetary, Historical, Restated, Restatement | — | Sum(Restatement) |
| GL5 | Journal Listing & Journal Audit | Every journal in the period with status, source, manual flag, who prepared and approved it, reversals and value. | FromDate (Date, optional), ToDate (Date, optional) | Journal, Date, Status, Source, Document, Manual, PreparedBy, ApprovedBy, Reversal, Value | — | Sum(Value) |
| GL8 | Statement of Changes in Equity | Each equity account's opening balance, the period's profit and other movements, and closing balance. | FromDate (Date, optional), ToDate (Date, optional) | Line, Opening, Profit, Movements, Closing | — | Sum(Closing) |

## Housing

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| H1 | Land Bank Register | All parcels with area, cost, status and mapped area. | — | Parcel, Description, AreaM2, MappedM2, Status, Cost | — | Sum(Cost) |
| H10 | Layout Plans Register & Approval Status | Layouts with the version in force, pending amendments and approval turnaround. | — | Plan, Parcel, Version, Status, Stands, TurnaroundDays | — | — |
| H11 | Stand Creation / Subdivision Report | Stands created from each parent parcel, with size, scheme, status and carried cost. | — | Parcel, Stand, SizeM2, Scheme, Status, Cost | — | Sum(Cost) |
| H12 | Survey Status Report | Per scheme as at the date: stands not pegged, pegged, awaiting the Surveyor-General and approved, with the longest wait. | AsAtDate (Date, optional) | Scheme, Stands, NotPegged, Pegged, AwaitingSg, Approved, LongestWait | — | — |
| H13 | Servicing Status by Scheme | Water, sewer, roads, electricity and storm water % complete, with stands serviced and unserviced. | AsAtDate (Date, optional) | Scheme, Water, Sewer, Roads, Electricity, StormWater, Stands, Serviced, Unserviced | — | — |
| H14 | Servicing Cost Apportionment Schedule | Servicing cost per stand by contract and basis. | — | Stand, Contract, Basis, Date, Journal, Amount | — | Sum(Amount) |
| H15 | Cost per Stand / Selling Price Build-up | Land and servicing cost, overhead, levy and margin to price, with margin %. | AsAtDate (Date, optional) | Stand, Scope, Cost, Overhead, Levy, Margin, MarginPercent, Price | — | Sum(Price) |
| H16 | Price List by Scheme & Stand Size Band | Every pricing rule with its scope, effective date, terms and approval — the price change history. | — | Scope, Applies, Effective, Terms, Approved | — | — |
| H17 | Stand Inventory Movement & Reconciliation | Year to date: opening stands + created + repossessed − sold = closing, in units and cost, reconciled to the stands control account. | AsAtDate (Date, optional), StandsAccountCode (String, optional) | Line, Units, Value | — | — |
| H18 | Waiting List Register | Active applicants by category in seniority order with score and position. | AsAtDate (Date, optional) | Category, Position, WaitingListNumber, Name, Registered, Score | — | — |
| H19 | Waiting List Summary | Active applicants by category, income band and age band. | AsAtDate (Date, optional) | Category, IncomeBand, AgeBand, Applicants | — | — |
| H2 | Land Acquisition Register | Acquisitions by method with counterparty, council resolution, status and cost. | — | Acquisition, Parcel, Method, Counterparty, Resolution, Status, Cost | — | Sum(Cost) |
| H20 | Waiting List Age Analysis | How long active applicants have been waiting, by category. | AsAtDate (Date, optional) | Category, UnderOne, OneToThree, ThreeToFive, FiveToTen, TenPlus, Total | — | — |
| H21 | Applicant Detail / Verification Sheet | One applicant: every captured detail, notices, date changes and the score derivation — printable for the applicant. | WaitingListNumber (String, optional), AsAtDate (Date, optional) | Section, Item, Value | — | — |
| H22 | Priority Score Derivation | How each active applicant's points were computed, factor by factor. | AsAtDate (Date, optional) | WaitingListNumber, Factor, Detail, Points, Total | — | — |
| H23 | Duplicate & Suspected Fraudulent Applicants | Fuzzy name, ID and date-of-birth matches across the waiting list, stand holders and the property register. | — | FirstSource, First, FirstName, SecondSource, Second, SecondName, Reason | — | — |
| H24 | Applicants Already Owning Property | Active applicants holding a council stand or owning rated property — disqualification candidates. | — | WaitingListNumber, Name, Holds | — | — |
| H25 | Registration Renewal Compliance | Applicants by status with last confirmation, months since and the notices sent. | AsAtDate (Date, optional) | Status, WaitingListNumber, Applicant, LastConfirmed, MonthsSince, Notices, LastNotice | — | — |
| H26 | Registration Date Change Audit | Every requested change to a seniority date: who asked, who decided, why. | — | WaitingListNumber, OldDate, NewDate, Reason, Status, RequestedBy, DecidedBy | — | — |
| H27 | Registration Fee Collection Report | Waiting-list fees received, and applicants registered without paying the registration fee. | — | Date, WaitingListNumber, Applicant, Type, Receipt, Amount | — | Sum(Amount) |
| H28 | New Registrations & Removals Movement | Month by month for the year to date: opening list, registered, removed, allocated, closing. | AsAtDate (Date, optional) | Month, Opening, Registered, Removed, Allocated, Closing | — | — |
| H29 | Demand vs Supply (the Housing Gap) | Active applicants by category against stands and rental units available for it. | — | Category, Demand, Supply, Gap | — | — |
| H3 | Land Acquisition Cost Build-up per Parcel | Each acquisition's cost by element with the total and cost per m². | — | Acquisition, Parcel, Element, PerM2, Amount | — | Sum(Amount) |
| H30 | Proposed Allocation List | Pre-approval pairings of runs awaiting approval: stand, applicant, place and method. | — | Run, Method, Sequence, Stand, WaitingListNumber, Applicant | — | — |
| H31 | Allocation Register | Allocations with stand, beneficiary, certificate, frozen price and approval. | — | Certificate, Run, Stand, WaitingListNumber, Beneficiary, Status, ApprovedOn, Price | — | Sum(Price) |
| H32 | Ballot / Draw Result Report | Ballot runs with the stored seed and input hash, re-verified now. | — | Run, Status, Seed, InputHash, Verified | — | — |
| H33 | Allocations by Scheme, Month and Approver | Approved allocations counted by scheme, approval month and approving officer. | — | Scheme, Month, Approver, Allocations | — | — |
| H34 | Skipped Applicant / Substitution Report | Per allocation run: listed applicants left out and why, and offers declined, lapsed or cancelled — the appeal-defence report. | — | Run, Scheme, Method, WaitingListNumber, Applicant, Outcome, Reason | — | — |
| H35 | Offers Outstanding / Expiring / Expired | Open offers with the acceptance deadline countdown. | AsAtDate (Date, optional) | Certificate, Stand, Beneficiary, Expires, DaysLeft, State | — | — |
| H36 | Declined, Lapsed & Cancelled Offers | Offers that did not proceed, with when and why. | — | Certificate, Stand, Applicant, Status, DecidedOn, Reason | — | — |
| H37 | Quota Compliance Report | Per scheme: allocations to each beneficiary group against its policy quota. | — | Scheme, Group, Allocations, Members, Actual, Target, Variance, Status | — | — |
| H38 | Appeals Register & Outcomes | Appeals lodged against allocation runs with hearing and outcome. | — | Appeal, WaitingListNumber, Run, Lodged, Status, Hearing, Outcome | — | — |
| H39 | Double Allocation Exception Report | Stands promised through more than one route — must always be empty; any row is an incident. | — | Stand, Claims, Routes | — | — |
| H4 | Due Diligence Outstanding | Open acquisitions and the due-diligence items blocking each. | — | Acquisition, Parcel, Outstanding | — | — |
| H40 | Beneficiaries with More Than One Stand | People (by national ID) holding or allocated more than one stand across schemes. | — | IdNumber, Name, Stands, Detail | — | — |
| H41 | Allocation-to-Agreement Conversion | Accepted allocations with no agreement or instalment sale yet, with age since acceptance. | AsAtDate (Date, optional) | Certificate, Stand, Beneficiary, AcceptedOn, DaysWaiting | — | — |
| H42 | Agreement Register | Agreements by type with party, stand, version in force, signature and price. | — | Agreement, Type, Party, Stand, Version, Status, Price | — | Sum(Price) |
| H43 | Beneficiary Account Statement | Charges, instalments, receipts and balance per beneficiary, with the next instalment — one or all (burst by beneficiary). | Beneficiary (String, optional) | Beneficiary, Date, Document, Type, Due, Balance, Amount | — | Sum(Amount) |
| H44 | Instalment Amortisation Schedule | Each instalment's capital, interest and balance — for one agreement or all. | AgreementNumber (String, optional) | Agreement, Number, Due, Interest, Principal, Balance, State, Amount | — | Sum(Amount) |
| H45 | Housing Debtors Age Analysis | Instalment-sale debtors aged 30/60/90/120+ by scheme, as at any date. | AsAtDate (Date, optional) | Scheme, Debtor, Current, D30, D60, D90, D120, Over120, Total | — | Sum(Total) |
| H46 | Instalment Arrears & Default Report | Active instalment sales with overdue instalments, amount owing and last payment. | AsAtDate (Date, optional) | Agreement, Stand, Buyer, Overdue, LastPaid, Owing | — | Sum(Owing) |
| H47 | Payment Arrangements Register | Arrangements with housing debtors and tenants: terms, status and standing as at the date. | AsAtDate (Date, optional) | Scheme, Debtor, Started, Total, Instalment, Status, DueToDate, Paid, Missed, NextDue, Shortfall | — | Sum(Shortfall) |
| H48 | Collections Report | Receipts from housing debtors and tenants in the month, by scheme or tenancy, day, method, currency and officer. | AsAtDate (Date, optional) | Scheme, Day, Method, Currency, Officer, Receipts, Amount | — | Sum(Amount) |
| H49 | Fully Paid / Ready for Transfer | Sold stands whose holders owe nothing and have no title application — the most actionable list. | — | Stand, Holder, Status, SoldOn, Price | — | Sum(Price) |
| H5 | Compulsory Acquisition Compensation Register | Compensation claims with provision, settlement and status. | — | Claim, Parcel, Claimant, Lodged, Status, Provision, Settled | — | Sum(Settled) |
| H50 | Early Settlement & Rebate Report | Instalment sales settled early with the interest rebated. | — | Agreement, Stand, Buyer, SettledOn, Rebated, InterestRebated | — | Sum(InterestRebated) |
| H51 | Deposits Held Register | Liability listing as at the date: purchase deposits against accepted offers and tenancy deposits, by scheme and beneficiary. | AsAtDate (Date, optional) | Scheme, Beneficiary, Type, Reference, Received, Amount | — | Sum(Amount) |
| H52 | Expected Credit Loss (IFRS 9) Provision Working | Housing debtors' exposure and provision by portfolio and arrears band, with the allowance booked. | AsAtDate (Date, optional) | Portfolio, Band, Exposure, Provision | — | Sum(Provision) |
| H53 | Cancellations & Refund Computation | Agreements cancelled at the buyer's request: paid, less occupation rental, admin charge and arrears, the refund. | — | Agreement, Buyer, Date, Paid, Months, Rental, Admin, Arrears, Repossession, Refund | — | Sum(Refund) |
| H54 | Development Covenant Compliance | Covenants with the latest milestone, deadline, extensions and breach status. | AsAtDate (Date, optional) | Covenant, Stand, Holder, Deadline, Milestone, Extensions, Status, DaysLeft | — | — |
| H55 | Repossession Register | Stands repossessed with the receivable derecognised, NRV, inventory recognised and gain or loss. | — | Reference, Stand, Date, Reason, Receivable, Nrv, Inventory, GainOrLoss | — | Sum(GainOrLoss) |
| H56 | Ownership History / Chain of Title per Stand | Every holder of a stand in sequence with the change type, date and consideration, and whether the hash chain verifies. | StandNumber (String, optional) | Stand, Sequence, ChangeType, Holder, Effective, ChainVerified, Consideration | — | Sum(Consideration) |
| H57 | Change of Ownership Register | Completed transfers by type with parties, consideration and clearance relied on. | — | Application, Stand, Type, From, To, Completed, Clearance, Consideration | — | Sum(Consideration) |
| H58 | Transfers in Progress / Pending Approval | Open transfer applications with status, approvals and age. | AsAtDate (Date, optional) | Application, Stand, Type, Status, Approvals, DaysOpen, Waiting | — | — |
| H59 | Transfers Blocked by Outstanding Rates or Housing Arrears | Open transfers failing a live check, with what is needed to clear them. | AsAtDate (Date, optional) | Application, Stand, Check, Detail | — | — |
| H6 | Land Disposals & Exchanges Register | Land sold, exchanged, donated or expropriated: resolution, area, cost out, consideration and gain or loss. | — | Disposal, Date, Parcel, Type, Counterparty, Resolution, Area, Cost, Consideration, Received, GainOrLoss | — | Sum(GainOrLoss) |
| H60 | Transfer Checklist Exception Report | Open transfer applications and the mandatory documents still missing. | — | Application, Stand, Type, Status, Lodged, Missing | — | — |
| H61 | Deceased Estate Transfers Register | Inheritance and deceased-joint-holder transfers with the estate documents relied on. | — | Application, Stand, Type, Deceased, Beneficiary, Status, Documents | — | — |
| H62 | Development Levy & Transfer Fee Collection | Levies and fees raised, by levy, with the event they were raised on. | — | Levy, Event, Stand, Customer, ChargedOn, Invoice, Amount | — | Sum(Amount) |
| H63 | Transfer Duty Deducted and Remitted | Duty collected as agent on each transfer, and whether it has been paid over. | — | Application, Stand, Consideration, Rate, Withheld, Receipt, Remitted, Acknowledgement, Amount | — | Sum(Amount) |
| H64 | Title Deed Pipeline Report | Open title applications with stage, days at the stage, days since applied and conveyancer. | AsAtDate (Date, optional) | Application, Stand, Stage, DaysAtStage, DaysSinceApplied, Conveyancer | — | — |
| H65 | Title Deeds Held but Uncollected | Deeds in council custody not yet handed over, aged, with the owner's contact — the chase list. | AsAtDate (Date, optional) | Application, Stand, Deed, Owner, Contact, DaysHeld | — | — |
| H66 | Deeds Register | Registered deeds with registration date and current custody. | — | Deed, Stand, Registered, Custody | — | — |
| H67 | Conveyancer Performance | Files allocated, registered, average days to registration and registry queries per conveyancer. | — | Conveyancer, Files, Registered, AverageDays, Queries | — | — |
| H68 | Caveats, Blocks & Disputes Register | Every block placed: reason, particulars, document, who placed and lifted it, age. | AsAtDate (Date, optional) | Stand, Reason, Detail, Document, PlacedOn, State, AgeDays | — | — |
| H69 | Stands with No Recorded Owner / Broken Ownership Chain | Data-integrity exceptions — must be empty. | — | Stand, Problem | — | — |
| H7 | Encumbrances, Servitudes & Caveats on Land | Every right or restriction registered against land, in force or lifted. | — | Parcel, Type, Reference, InFavourOf, Registered, Status, Description | — | — |
| H70 | Sectional Title Unit Register & Participation Quotas | Units of every sectional scheme with floor area, quota and owner. | — | Scheme, Building, Unit, FloorArea, Quota, Owner | — | — |
| H71 | Rent Roll | Let units with tenant, rent and arrears. | — | Unit, Type, Tenancy, Tenant, Since, Rent, Arrears | — | Sum(Arrears) |
| H72 | Housing Stock Register & Condition | Every unit with type, rooms, occupancy and its latest inspection grade. | — | Unit, Description, Type, Rooms, Occupancy, Inspected, Grade | — | — |
| H73 | Occupancy & Voids Report | Void units with days void and rent lost to voids. | AsAtDate (Date, optional) | Unit, Type, VacantSince, DaysVoid, RentLost | — | Sum(RentLost) |
| H74 | Tenant Statement | Rent charges, receipts and balance per tenant — one or all (burst by tenant). | Tenant (String, optional) | Tenant, Date, Document, Type, Due, Balance, Amount | — | Sum(Amount) |
| H75 | Rent Arrears Age Analysis | Tenants' arrears aged 30/60/90/120+ by unit type, as at any date. | AsAtDate (Date, optional) | Type, Tenant, Current, D30, D60, D90, D120, Over120, Total | — | Sum(Total) |
| H76 | Eviction Pipeline & Register | Eviction cases with the current stage, its date and the arrears at opening. | — | Case, Tenancy, Stage, StageDate, Status, Arrears | — | Sum(Arrears) |
| H77 | Lease Expiry & Renewal Due | Stand leases and tenancies ending within a year of the date. | AsAtDate (Date, optional) | Kind, Reference, Subject, Ends, DaysLeft | — | — |
| H78 | Tenant Deposits Held | Deposits held on active tenancies (a liability listing). | — | Tenancy, Unit, Tenant, Since, Deposit | — | Sum(Deposit) |
| H79 | Maintenance & Repairs Cost per Unit | Completed repairs per unit, separating what was recharged to tenants. | — | Unit, Repairs, Recharged, Council, Cost | — | Sum(Cost) |
| H8 | Disputed & Encroached Land | Disputes open at the date with their age and current legal stage. | AsAtDate (Date, optional) | Dispute, Parcel, Stand, Type, Claimant, Opened, Stage, StageSince, AgeDays | — | — |
| H80 | Inspections Due & Findings Summary | Units never inspected or not inspected for a year, and follow-ups outstanding. | AsAtDate (Date, optional) | Unit, LastInspected, Grade, Due | — | — |
| H81 | Straight-lined Rental Income Schedule (IFRS 16) | Each stand lease's contractual rent per year against the straight-line income recognised. | — | Lease, Year, Contractual, StraightLine, Difference | — | Sum(Difference) |
| H82 | Scheme Delivery Dashboard | Per scheme: target, stands created, serviced, allocated, sold and titled, and % delivered. | — | Scheme, Target, Stands, Serviced, Allocated, Sold, Titled, Delivered | — | — |
| H83 | Scheme Profitability & Cost Recovery | Per scheme: cost in (land and servicing), revenue from stands sold, cost recovered and margin. | — | Scheme, Stands, Sold, Cost, Revenue, Recovered, CostPerStand, Margin | — | Sum(Margin) |
| H84 | Scheme Budget vs Actual | Per scheme and cost element (land, servicing, professional fees, overheads): budget, actual, variance and share used. | — | Scheme, Element, Budget, Actual, Used, Variance | — | Sum(Variance) |
| H85 | Developer / Joint Venture Reconciliation | Stands contracted vs delivered per quota, overdue milestones and penalties. | AsAtDate (Date, optional) | Agreement, Quota, Contracted, HandedOver, Outstanding, OverdueMilestones, PenaltyAccrued | — | Sum(PenaltyAccrued) |
| H86 | Housing Cooperative Register | Cooperatives with active members, stands held and stands sub-allocated. | — | Cooperative, Name, Registration, Members, Stands, SubAllocated | — | — |
| H87 | Informal Settlement / Enumeration Register | Settlements with ward, cut-off date, households counted and eligible. | — | Settlement, Name, Ward, CutOff, Households, Eligible, Plan | — | — |
| H88 | Regularisation Progress Report | Per settlement: enumerated → eligible → waiting-listed or regularised → outstanding. | — | Settlement, Enumerated, Eligible, WaitingListed, Regularised, Outstanding | — | — |
| H89 | Housing Subsidy & Grant Utilisation | Grants received, spent, recognised and deferred (IAS 20), with conditions. | — | Grant, Grantor, Received, Conditions, Amount, Spent, Recognised, Deferred | — | Sum(Deferred) |
| H9 | Land Bank Valuation vs Carrying Value (IAS 2 NRV test) | Undivided parcels against their latest valuation and unsold stands against their approved price, less costs to sell — the write-down working paper. | AsAtDate (Date, optional), CostToSellPercent (Decimal, optional) | Item, Kind, Scheme, Carrying, Basis, Price, CostsToSell, Nrv, WriteDown | — | Sum(WriteDown) |
| H90 | Housing Audit Trail Report | Every recorded change to housing records — before and after values, user, time. | AsAtDate (Date, optional) | When, User, Entity, Id, Action, Changed | — | — |
| H91 | Allocation & Ownership Change Approval Audit | Who requested and who approved each run, transfer, seniority change, price rule and block lift — turnaround and dual-authorisation exceptions. | — | Area, Reference, RequestedBy, ApprovedBy, Requested, Approved, Days, Exception, Value | — | Sum(Value) |
| H92 | Segregation of Duties Violations — Housing | Users whose permissions combine housing duties that must be separated. | — | User, Rule, Conflict | — | — |
| H93 | Manual Override & Exception Report | Seniority dates changed, stand-specific prices, offers without an approved price and blocks lifted — each with the stated reason. | — | Type, Reference, Detail, Date, By, Reason | — | — |
| H94 | Housing Revenue Summary | Year to date against the same period last year: stand sales, instalment interest, rentals and recharges, development levies. | AsAtDate (Date, optional) | Stream, PriorYear, Change, YearToDate | — | Sum(YearToDate) |
| H95 | Housing Executive Dashboard | Land bank, stands, waiting list, allocations, titles and debtors at a glance. | AsAtDate (Date, optional) | Measure, Value | — | — |
| H96 | Housing Delivery / Statutory Return | Stands delivered in the year by density, tenure and beneficiary category. | AsAtDate (Date, optional) | Density, Tenure, Category, Stands | — | — |
| H97 | Ward Housing Pack | One ward for its councillor: schemes, stands by status, allocations, beneficiaries, instalment arrears and the council-wide waiting list. | Ward (String, optional), AsAtDate (Date, optional) | Section, Item, Value | — | — |
| HSE-STAND | Housing Stand Register | Stands with status, apportioned cost, selling price and current holder. | — | StandNumber, Status, Holder, AllocatedCost, SellingPrice | — | Sum(SellingPrice) |

## HumanResources

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| HR-EMP | Employee Register | All employees with number, name, department, cost centre, status and basic salary. | — | EmployeeNumber, FullName, Department, CostCentre, Status, BasicSalary | — | Sum(BasicSalary) |

## Inventory

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| INV-ABC | Inventory ABC Analysis | Items ranked by stock value with the cumulative percentage and Pareto class (A/B/C). | — | ItemCode, Description, Value, CumulativePercent, Class | — | Sum(CumulativePercent) |
| INV-AGE | Stock Ageing | On-hand items with their value and the age band since last movement (0-30 / 31-60 / 61-90 / 90+ days). | AsAtDate (Date, optional) | ItemCode, Description, Warehouse, Quantity, Value, AgeBand | — | Sum(Value) |
| INV-BACKORDER | Back-order Book | Every undespatched sales-order line, oldest order first, with stock on hand allocated to earlier orders so each line shows whether it can be fulfilled and any shortfall. | — | OrderNumber, OrderDate, Customer, ItemCode, Outstanding, OnHand, CanFulfil, Shortfall | — | Sum(Shortfall) |
| INV-ITEMS | Item Master | Stock item master: code, description, unit, reorder level, default accounts and active status. | — | Code, Description, Uom, ReorderLevel, RevenueAccount, CostAccount, Active | — | Sum(ReorderLevel) |
| INV-MOVE | Stock Movements | Every stock movement over a period — receipts, issues, transfers, adjustments and reversals. | FromDate (Date, optional), ToDate (Date, optional) | Date, Item, Warehouse, Type, Qty, Value, Reference | — | Sum(Value) |
| INV-REORDER | Reorder Report | Items at or below their reorder level, with the suggested reorder quantity — a purchasing aid. | — | ItemCode, Description, OnHand, ReorderLevel, Suggested | — | Sum(Suggested) |
| INV-SLOW | Slow-moving Stock | Items on hand with no movement since the given date — capital tied up in dead stock. | SinceDate (Date, optional) | ItemCode, Description, Warehouse, Quantity, Value, LastMovement | — | Sum(Value) |
| INV-VAL | Stock Valuation | Quantity on hand and value at moving average cost, per item and warehouse. | AsAtDate (Date, optional) | ItemCode, Description, WarehouseCode, Quantity, AverageCost, Value | — | Sum(Value) |
| INV101 | Consignment Stock | Our stock held at customers' sites on consignment, per customer and item, with the last movement and value at average cost. | AsAtDate (Date, optional) | Customer, Warehouse, Item, OnHand, LastMovement, Value | — | Sum(Value) |
| INV78 | Stock Valuation vs GL Stock Control | The stock register's value at the date by warehouse against the stock control accounts, with the difference. | AsAtDate (Date, optional) | Line, Items, Quantity, Value | — | Sum(Value) |
| INV79 | Stock on Hand & Availability | Per item and warehouse: on hand, committed to orders, on order, free and available to promise. | — | Item, Warehouse, OnHand, Committed, OnOrder, Free, Atp, Value | — | Sum(Value) |
| INV81 | Stocktake Count Sheets & Variance | Each stocktake's lines: system quantity, counted, variance in quantity and value (uncounted lines flagged). | — | Stocktake, Date, Warehouse, Status, Item, System, Counted, Variance, Value | — | Sum(Value) |
| INV82 | Stock Adjustments by Reason Code | Adjustments in the period per reason code: count, units gained and lost, and the net value. | FromDate (Date, optional), ToDate (Date, optional) | Reason, Name, Count, Gained, Lost, Value | — | Sum(Value) |
| INV86 | Overstock Report | Items holding more than their reorder level plus reorder quantity (or more than 90 days of issues), with the excess value. | AsAtDate (Date, optional) | Item, OnHand, Ceiling, Excess, DaysCover, Value | — | Sum(Value) |
| INV87 | Negative Stock Report | Item and warehouse balances below zero — an integrity exception to clear. | — | Item, Warehouse, OnHand, AverageCost, Value | — | Sum(Value) |
| INV88 | Batch / Lot Stock Report | Batch-tracked stock on hand at the date per item, warehouse and batch, with first receipt, expiry and value at average cost. | AsAtDate (Date, optional) | Item, Warehouse, Batch, Received, Expiry, OnHand, Value | — | Sum(Value) |
| INV89 | Serial Number Register | Every serial number: where it is, when it came in and when and how it last moved, and whether it is still in stock. | AsAtDate (Date, optional) | Item, Serial, Status, Warehouse, Received, LastMoved, LastMovement | — | — |
| INV90 | Expiry Report | Batches on hand that have expired or expire within the window, soonest first, with the value at risk. | AsAtDate (Date, optional), Days (Integer, optional) | Item, Warehouse, Batch, Expiry, Days, Status, OnHand, Value | — | Sum(Value) |
| INV91 | Goods in Transit | Transfers dispatched but not yet received at the date: from, to, days on the road, quantity and value. | AsAtDate (Date, optional) | Transfer, Item, From, To, Dispatched, Days, Quantity, Value | — | Sum(Value) |
| INV92 | Stock Turnover & Days Cover | Per item over the period: cost of issues, average stock value, turnover and days of cover at the current rate. | FromDate (Date, optional), ToDate (Date, optional) | Item, Issued, AverageStock, Turns, DaysCover, Value | — | Sum(Value) |
| INV93 | Gross Margin by Item | Per item over the period: net sales (POS and invoiced orders), cost of issues and the margin. | FromDate (Date, optional), ToDate (Date, optional) | Item, Category, Quantity, Sales, Cost, MarginPercent, Margin | — | Sum(Margin) |
| INV96 | Cost Change History | Each receipt at a unit cost different from the item's previous receipt, with the change. | FromDate (Date, optional), ToDate (Date, optional) | Item, Date, Reference, Previous, Cost, Change, Quantity | — | — |
| INV97 | Landed Cost Analysis | Landed costs added to stock in the period, per item and reference, against the goods' own receipt value. | FromDate (Date, optional), ToDate (Date, optional) | Date, Reference, Item, Goods, Uplift, Landed | — | Sum(Landed) |
| INV98 | Bill of Materials Costing | Each BOM's components with scrap allowance and current average cost, rolled up to a cost per finished unit. | — | Bom, Parent, Component, PerBatch, Scrap, PerUnit, UnitCost, Extended | — | Sum(Extended) |
| INV99 | Production Yield & Usage Variance | Assembly runs in the period: planned and actual output, yield, and each component's standard against actual usage and cost. | FromDate (Date, optional), ToDate (Date, optional) | Run, Date, Item, Yield, Component, Standard, Actual, StandardCost, ActualCost, Variance | — | Sum(Variance) |

## JobCosting

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| JC186 | Job Cost Report by Cost Head | Per contract and cost head: cost to date and share of the contract's cost, against its budget. | — | Contract, Name, CostHead, Source, Share, Cost | — | Sum(Cost) |
| JC189 | Cost to Complete & Forecast Final Cost | Per contract: value, budget, cost to date, % complete by certification, forecast final cost and the variance to budget. | — | Contract, Name, Value, Budget, Actual, Complete, Forecast, Variance | — | Sum(Variance) |
| JC190 | Retention Held by Contract | Retention held on each contract's certificates, with the contract status. | — | Contract, Name, Customer, Status, Rate, Certified, Retention | — | Sum(Retention) |
| JC197 | Contract Cash Flow | Per contract over the period: net certified (after retention) against costs incurred, and the net cash position. | FromDate (Date, optional), ToDate (Date, optional) | Contract, Name, Certified, Costs, Net | — | Sum(Net) |
| JOB-IPC | Progress Certificates | Interim payment certificates with gross, retention and net payable for a period. | FromDate (Date, optional), ToDate (Date, optional) | CertificateNumber, Date, Gross, Retention, Net | — | Sum(Net) |
| JOB-PROF | Job Profitability | Contract value, budget, actual cost, certified-to-date and percentage complete per contract. | — | ContractNumber, Name, ContractValue, BudgetCost, ActualCost, CertifiedToDate, PercentComplete | — | Sum(CertifiedToDate) |
| JOB-WIP | Job WIP | Per contract: revenue recognised on cost-to-cost completion versus certified-to-date, giving the WIP position (contract asset if positive, liability if negative). | — | ContractNumber, Name, ContractValue, PercentComplete, RevenueRecognised, CertifiedToDate, WipPosition | — | Sum(WipPosition) |

## Municipal

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| DB256 | Municipal Executive Dashboard | Billed and collected this year, collection rate, debtors and over-90s, indigent households, disconnections, open faults and licences due. | AsAtDate (Date, optional) | Measure, Value | — | — |
| M146 | Billing Run Summary & Control | Each billing run: service, period, ward, lines billed and excluded, total billed against the previous run, proposer and approver. | — | Run, Service, Period, Ward, Status, Lines, Excluded, Previous, Approver, Billed | — | Sum(Billed) |
| M147 | Billing Exceptions Report | Lines of every billing run flagged as already billed, nothing to bill, failed or a large change on last time. | — | Run, Stand, Exceptions, Message, Included, Previous, Charge | — | Sum(Charge) |
| M148 | Aged Debtors by Ward, Category and Service | Ratepayers' open items as at the date, aged 30/60/90/120+, grouped by ward, consumer category and service. | AsAtDate (Date, optional) | Ward, Category, Service, Accounts, Current, D30, D60, D90, D120, Over120, Total | — | Sum(Total) |
| M149 | Arrears Listing | Ratepayers with amounts past due at the date: overdue, total owing, last payment and arrangement status. | AsAtDate (Date, optional) | Debtor, Ward, Properties, Overdue, LastPayment, Arrangement, Owing | — | Sum(Owing) |
| M151 | Valuation Roll & Supplementary Roll | Every valuation entry (general, supplementary, objection) with its effective date, and objections with their outcome. | — | Stand, Ward, Kind, Reference, EffectiveFrom, Objection, Value | — | Sum(Value) |
| M152 | Property Register (full) | Every property with owner(s), ratepayer, services and tariffs, meters, valuation and ward. | — | Stand, Ward, Address, Owners, Ratepayer, Services, Meters, Valuation | — | Sum(Valuation) |
| M153 | Rates Clearance Certificate Register | Certificates issued with property, validity, advance held and who issued them, and whether still valid at the date. | AsAtDate (Date, optional) | Certificate, Stand, Issued, ValidUntil, State, IssuedBy, AdvanceHeld | — | Sum(AdvanceHeld) |
| M154 | Meter Reading Sheets / Route Books | Each reading route in walking order: meter, stand, last reading and date, blank for the new reading. | RouteCode (String, optional) | Route, Reader, Sequence, Meter, Stand, Address, LastReading, LastDate, NewReading | — | — |
| M155 | Meter Reading Exceptions | Readings flagged high or low consumption, rollover, negative or tampered, and estimates, with whether they were accepted. | AsAtDate (Date, optional) | Date, Meter, Stand, Kind, Flags, Previous, Reading, Accepted, Consumption | — | Sum(Consumption) |
| M156 | Consumption Analysis | Metered consumption for the twelve months to the date against the twelve before, per meter with its category. | AsAtDate (Date, optional) | Stand, Meter, Service, Category, Readings, PriorYear, Change, Consumption | — | Sum(Consumption) |
| M159 | Meter Register & Replacement History | Every meter with stand, service, installation and removal, what replaced it, last actual reading and units estimated since. | — | Meter, Stand, Service, Installed, Removed, ReplacedBy, LastActual, Status, EstimatedSinceActual | — | Sum(EstimatedSinceActual) |
| M160 | Disconnection & Reconnection Lists | Service orders to disconnect for arrears or reconnect after payment, with status, execution and the fee raised. | — | Order, Kind, Stand, Ward, Service, Raised, Status, Executed, Fee, Arrears | — | Sum(Arrears) |
| M161 | Payment Arrangements Register (all debtors) | Every payment arrangement: terms, status and standing as at the date. | AsAtDate (Date, optional) | Ward, Debtor, Started, Total, Instalment, Status, DueToDate, Paid, Missed, Shortfall | — | Sum(Shortfall) |
| M162 | Indigent Register & Subsidy Cost | Registered indigent households with income, subsidy rate, review date and the subsidy credited in the year to the date. | AsAtDate (Date, optional) | Stand, Ward, Income, Rate, ApprovedFrom, ReviewDue, Status, Subsidy | — | Sum(Subsidy) |
| M163 | Interest & Penalties Charged | Interest raised on ratepayers' overdue accounts in the year to the date. | AsAtDate (Date, optional) | Date, Debtor, Ward, Document, Amount | — | Sum(Amount) |
| M164 | Write-offs & Adjustments | Bad debts written off (with the debt case and who approved it) and credit adjustments on ratepayers' accounts in the year. | AsAtDate (Date, optional) | Date, Debtor, Ward, Type, Document, Approval, Amount | — | Sum(Amount) |
| M166 | Licences & Permits Register and Renewals Due | Every licence with holder, expiry, days to expiry at the date, renewals and the latest inspection. | AsAtDate (Date, optional) | Licence, Kind, Holder, Expires, Due, Status, Renewals, Inspection, Fee | — | Sum(Fee) |
| M167 | Service Request / Fault Ticket SLA | Every ticket with category, priority, response and resolution against their SLA deadlines. | AsAtDate (Date, optional) | Ticket, Category, Priority, Reported, Status, Response, Resolution, Hours | — | — |
| M168 | Tariff Schedule | Every tariff with its type, category, ward, effective date, charges, blocks and seasonal rates. | — | Tariff, Name, Service, Type, Category, Ward, Effective, Charges, Blocks, Active | — | — |
| M169 | Revenue by Source / Vote | Year to date: billed charges by service and revenue account, prepaid sales and licence fees. | AsAtDate (Date, optional) | Source, Vote, Documents, Amount | — | Sum(Amount) |
| M170 | Ward Councillor Pack | One ward: properties, billing, debtors, arrangements, disconnections, indigent households, open faults and licences. | Ward (String, optional), AsAtDate (Date, optional) | Section, Item, Value | — | — |
| M171 | Statutory Debtors Age Analysis | Debtors by consumer category aged 30/60/90/120+ as at the date, with a grand total — the statutory return layout. | AsAtDate (Date, optional) | Category, Accounts, Current, D30, D60, D90, D120, Over120, Total | — | Sum(Total) |
| M172 | Prepaid vs Conventional | Month by month for the year: prepaid sales (vends, units, amount, recovered to arrears) against conventional billing, per service. | AsAtDate (Date, optional) | Month, Service, Vends, Units, ToArrears, Conventional, Share, Prepaid | — | Sum(Prepaid) |
| MUN-PROP | Property Register | Stands on the municipal register with ward, valuation and ratepayer. | — | StandNumber, Ward, Ratepayer, Valuation | — | Sum(Valuation) |

## Payroll

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| PAY-DED | Payroll Deductions | Deductions aggregated by element across payslips in the period, with the number of employees affected. | FromDate (Date, optional), ToDate (Date, optional) | ElementCode, Description, Employees, Amount | — | Sum(Amount) |
| PAY-LEAVE | Leave Balances | Employee leave balances by type — days accrued, taken and available. | — | Employee, LeaveType, Accrued, Taken, Balance | — | Sum(Balance) |
| PAY-LOANS | Employee Loans | Outstanding employee loans — principal, balance, installment and status. | — | Employee, Reference, Principal, Outstanding, Installment | — | Sum(Installment) |
| PAY-SUM | Payroll Summary | Gross, deductions and net pay per employee for a period. | FromDate (Date, optional), ToDate (Date, optional) | Employee, Gross, Deductions, Net | — | Sum(Net) |
| PR11 | Bank Transfer (EFT) Schedule | Net pay for the period to employees paid by bank transfer (held salaries excluded), with bank, branch and account. | FromDate (Date, optional), ToDate (Date, optional) | Employee, Bank, Branch, Account, Reference, Net | — | Sum(Net) |
| PR12 | Cash Pay Denomination Breakdown | Net pay in the period broken into the base currency's notes and coins, per employee, for cash payouts. | FromDate (Date, optional), ToDate (Date, optional) | Employee, Breakdown, Net | — | Sum(Net) |
| PR13 | Mobile Money Payment Schedule | Net pay for the period to employees paid by mobile money (held salaries excluded), with the wallet. | FromDate (Date, optional), ToDate (Date, optional) | Employee, Wallet, Reference, Net | — | Sum(Net) |
| PR15 | Held Salaries | Employees whose pay is held back, why, and the net pay held for the period. | FromDate (Date, optional), ToDate (Date, optional) | Employee, Department, Method, Reason, Net | — | Sum(Net) |
| PR16 | Net Pay Control Reconciliation | Per pay month: gross, deductions and net from the payslips against the net pay credited in the payroll journals. | FromDate (Date, optional), ToDate (Date, optional) | Month, Payslips, Gross, Deductions, Net, Journal, Difference | — | Sum(Difference) |
| PR17 | Earnings Analysis | Earnings in the period by department and element. | FromDate (Date, optional), ToDate (Date, optional) | Department, Element, Description, Employees, Amount | — | Sum(Amount) |
| PR19 | Third-Party Remittance Schedules | Per scheme and member for the period: the employee's deduction and the employer's contribution — the schedule each provider needs. | FromDate (Date, optional), ToDate (Date, optional), SchemeCode (String, optional) | Scheme, Employee, Member, EmployeePart, EmployerPart, Total | — | Sum(Total) |
| PR2 | Payroll Summary by Element | Totals for the period by earning, deduction and employer contribution, with how many employees each. | FromDate (Date, optional), ToDate (Date, optional) | Type, Element, Description, Employees, Amount | — | Sum(Amount) |
| PR20 | Deductions vs Remittances | Per scheme and period: what the payroll deducted and contributed against what was paid over, with the gap. | FromDate (Date, optional), ToDate (Date, optional) | Scheme, Period, Deducted, Remitted, Status, Outstanding | — | Sum(Outstanding) |
| PR26 | Allowance Analysis | Earnings other than basic salary in the period, per employee and allowance. | FromDate (Date, optional), ToDate (Date, optional) | Employee, Department, Element, Description, Taxable, Amount | — | Sum(Amount) |
| PR27 | PAYE Remittance Return | Per month: employees, taxable earnings, PAYE deducted and PAYE by the tax table in force, with any difference. | FromDate (Date, optional), ToDate (Date, optional) | Month, Employees, Taxable, ByTable, Difference, Deducted | — | Sum(Deducted) |
| PR28 | Annual Tax Certificates | Per employee for the year: ID and tax numbers, gross, taxable, pension contributions and PAYE — the certificate data. | FromDate (Date, optional), ToDate (Date, optional) | Employee, NationalId, TaxNumber, Gross, Taxable, Pension, Paye | — | Sum(Paye) |
| PR29 | Tax Reconciliation | Per employee over the period: taxable earnings, PAYE deducted and the effective rate. | FromDate (Date, optional), ToDate (Date, optional) | Employee, Taxable, Rate, Tax | — | Sum(Tax) |
| PR3 | Payroll Journal | The payroll postings of the period by account, with debits and credits. | FromDate (Date, optional), ToDate (Date, optional) | Journal, Date, Account, Name, Debit, Credit, Net | — | Sum(Net) |
| PR30 | Social Security Contribution Schedule | Social security members with the period's employee and employer contributions. | FromDate (Date, optional), ToDate (Date, optional) | Scheme, Employee, Member, EmployeePart, EmployerPart, Total | — | Sum(Total) |
| PR31 | Pension Fund Contribution Schedule | Pension fund members with the period's employee and employer contributions. | FromDate (Date, optional), ToDate (Date, optional) | Scheme, Employee, Member, EmployeePart, EmployerPart, Total | — | Sum(Total) |
| PR32 | Medical Aid Contribution Schedule | Medical aid members with the period's employee and employer contributions. | FromDate (Date, optional), ToDate (Date, optional) | Scheme, Employee, Member, EmployeePart, EmployerPart, Total | — | Sum(Total) |
| PR33 | Workers' Compensation & Skills Levy Schedule | Workers' compensation and skills levy contributions for the period by member. | FromDate (Date, optional), ToDate (Date, optional) | Scheme, Employee, Member, EmployeePart, EmployerPart, Total | — | Sum(Total) |
| PR36 | Tax Table in Force | The PAYE bands that apply at the date, with the tax at the top of each band. | AsAtDate (Date, optional) | Table, Effective, From, To, Rate, Cumulative | — | — |
| PR38 | Leave Liability (IAS 19) | Leave days owed at the date per employee and type, valued at the daily rate of basic salary (annual ÷ 260). | — | Employee, LeaveType, Accrued, Taken, Balance, DailyRate, Liability | — | Sum(Liability) |
| PR4 | Pre-Payroll Validation | Active employees whose records would stop a clean payment or return: missing ID, tax number, bank or wallet details, date of birth. | AsAtDate (Date, optional) | Employee, Department, Method, Issues | — | — |
| PR45 | Headcount Reconciliation | Per department over the period: opening headcount, starters, leavers and closing. | FromDate (Date, optional), ToDate (Date, optional) | Department, Opening, Starters, Leavers, Closing | — | — |
| PR46 | Starters & Leavers | Employees engaged or leaving in the period, with the date and the reason for leaving. | FromDate (Date, optional), ToDate (Date, optional) | Kind, Employee, Department, Date, Reason, Basic | — | Sum(Basic) |
| PR48 | Grade & Gender Distribution | Active headcount per grade with the gender split and the basic salary range. | — | Grade, Headcount, Female, Male, Unspecified, Range, Average | — | Sum(Average) |
| PR49 | Salary Change Audit | Every change to an employee's basic salary in the period, with who changed it. | FromDate (Date, optional), ToDate (Date, optional) | When, Employee, User, Old, New | — | — |
| PR5 | Payroll Variance / Movement | Each employee's net pay this pay month against last, with starters, leavers and changes over 10% flagged. | FromDate (Date, optional), ToDate (Date, optional) | Employee, Department, Previous, Current, Flag, Change | — | Sum(Change) |
| PR50 | Employee Bank Detail Change Audit | Every change to how or where an employee is paid, with who changed it (account numbers masked). | FromDate (Date, optional), ToDate (Date, optional) | When, Employee, User, Changed, New | — | — |
| PR51 | Contract, Probation & Permit Expiry | Fixed-term contracts, probations and work permits ending within 90 days of the date, or already passed. | AsAtDate (Date, optional) | Kind, Employee, Department, Date, Due | — | — |
| PR58 | Total Cost to Company | Per employee over the period: gross pay plus employer contributions, and the annualised cost. | FromDate (Date, optional), ToDate (Date, optional) | Employee, Department, Months, Gross, Employer, Annualised, Cost | — | Sum(Cost) |
| PR59 | Labour Cost by Department & Cost Centre | Gross pay plus employer contributions in the period by department and cost centre. | FromDate (Date, optional), ToDate (Date, optional) | Department, CostCentre, Employees, Gross, Employer, Cost | — | Sum(Cost) |
| PR6 | Payroll Calculation Trace | Every payslip line in the period with how its element is calculated and the employee's basic salary. | FromDate (Date, optional), ToDate (Date, optional) | Employee, Period, Element, Basis, Rule, Basic, Amount | — | Sum(Amount) |
| PR60 | Payroll Cost vs Budget | Year to date on each payroll expense account: budget, actual and the variance. | AsAtDate (Date, optional) | Account, Name, Budget, Actual, Variance | — | Sum(Variance) |
| PR61 | Payroll as % of Revenue | Month by month: payroll cost (gross + employer contributions) against revenue. | FromDate (Date, optional), ToDate (Date, optional) | Month, Revenue, Share, Payroll | — | Sum(Payroll) |
| PR64 | Payroll GL Reconciliation | Per element account: what the payslips say against what the ledger holds for the period. | FromDate (Date, optional), ToDate (Date, optional) | Account, Name, Payslips, Ledger, Difference | — | Sum(Difference) |
| PR65 | Payroll Audit Trail | Every recorded change in the period to employees, elements, assignments, loans, leave and payslips. | FromDate (Date, optional), ToDate (Date, optional) | When, User, Entity, Id, Action, Changed | — | — |
| PR67 | Payroll Report Access & Export Log | Every run of a payroll report in the period — who, what, format and copy. | FromDate (Date, optional), ToDate (Date, optional) | When, Report, User, Purpose, Format, Copy | — | — |
| PR68 | Ghost Worker Investigation Pack | Active employees sharing a bank account, wallet or national ID with another, with no ID, or with no leave in over a year. | AsAtDate (Date, optional) | Employee, Department, Engaged, Signals | — | — |
| PR7 | Payroll Run Control Register | Every payroll run in the period: employees, gross, deductions, net, the journal and who ran it. | FromDate (Date, optional), ToDate (Date, optional) | Period, PayDate, Employees, Gross, Deductions, Journal, RunBy, Net | — | Sum(Net) |
| PR70 | Year-to-Date Payroll Register | Per employee for the year to the end of the period: gross, taxable, deductions, employer contributions and net. | FromDate (Date, optional), ToDate (Date, optional) | Employee, Department, Gross, Taxable, Deductions, Employer, Net | — | Sum(Net) |

## PointOfSale

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| POS-SUM | POS Sales Summary | Point-of-sale transactions with net, tax and gross for a period. | FromDate (Date, optional), ToDate (Date, optional) | SaleNumber, SaleDate, TotalNet, TotalTax, TotalGross | — | Sum(TotalGross) |
| POS103 | Daily Sales Summary | Per day, till and cashier: sales, voids, net, tax, the tender split and gross takings. | FromDate (Date, optional), ToDate (Date, optional) | Date, Till, Cashier, Sales, Voided, Net, Tax, Tenders, Gross | — | Sum(Gross) |
| POS104 | Cashier Performance | Per cashier over the period: sessions, sales, items, average basket, voids, cash-up variance and net sales. | FromDate (Date, optional), ToDate (Date, optional) | Cashier, Sessions, Sales, Items, AverageBasket, Voids, Variance, Net | — | Sum(Net) |
| POS105 | Till Variance & Cash-up Report | Each closed till session: float, cash taken, change given, expected, counted and the over or short. | FromDate (Date, optional), ToDate (Date, optional) | Session, Till, Cashier, Closed, Float, CashIn, Change, Expected, Counted, Variance | — | Sum(Variance) |
| POS106 | Void & Cancellation Analysis by Reason | Lines and sales voided in the period with who, why and who authorised it. | FromDate (Date, optional), ToDate (Date, optional) | Date, Till, Cashier, Kind, Sale, Description, Reason, AuthorisedBy, Amount | — | Sum(Amount) |
| POS107 | Refunds & Returns Register | Every refund in the period: the sale and line, quantity, reason, authoriser, journal and whether the goods were restocked. | FromDate (Date, optional), ToDate (Date, optional) | Date, Till, Cashier, Sale, Description, Quantity, Reason, AuthorisedBy, Journal, Restock, Amount | — | Sum(Amount) |
| POS108 | Line Discounts Given | Discounts keyed on lines in the period, with the list amount, the rate and the reason, by cashier. | FromDate (Date, optional), ToDate (Date, optional) | Date, Till, Cashier, Sale, Description, List, Rate, Reason, Amount | — | Sum(Amount) |
| POS109 | Price Overrides | Prices keyed over list in the period: list and keyed price, the difference, reason and the authorising supervisor. | FromDate (Date, optional), ToDate (Date, optional) | Date, Till, Cashier, AuthorisedBy, Sale, Description, List, Keyed, Reason, Difference | — | Sum(Difference) |
| POS110 | No-Sale Drawer Openings | Every drawer opened without a sale in the period, with the reason and authoriser, and each cashier's count. | FromDate (Date, optional), ToDate (Date, optional) | Opened, Till, Cashier, Reason, AuthorisedBy, CashierCount | — | — |
| POS111 | Parked / Suspended Sales | Sales parked in the period and whether they were resumed or abandoned, with the time they sat. | FromDate (Date, optional), ToDate (Date, optional) | Parked, Till, Cashier, Sale, Status, Resumed, Waited, Amount | — | Sum(Amount) |
| POS112 | Hourly Sales Heatmap & Basket Analysis | Sales by weekday and hour: count, items per basket, average basket and net. | FromDate (Date, optional), ToDate (Date, optional) | Weekday, Hour, Sales, ItemsPerBasket, AverageBasket, Net | — | Sum(Net) |
| POS113 | Product Mix Ranking | Items ranked by POS net sales in the period, with quantity, share and cumulative share. | FromDate (Date, optional), ToDate (Date, optional) | Rank, Item, Quantity, Share, Cumulative, Net | — | Sum(Net) |
| POS114 | Promotion Effectiveness | Each promotion's redemptions in the period, the spend they drove and the discount given. | FromDate (Date, optional), ToDate (Date, optional) | Campaign, Name, Valid, Redemptions, Spend, AverageSpend, DiscountRate, Discount | — | Sum(Discount) |
| POS115 | Loyalty Points Statement | Per loyalty customer: points earned and redeemed in the period and the balance to date. | FromDate (Date, optional), ToDate (Date, optional) | Customer, Transactions, Earned, Redeemed, LastActivity, Balance | — | Sum(Balance) |
| POS116 | Receipt Reprint Log | Every receipt reprinted in the period: when, by whom, the sale and its value. | FromDate (Date, optional), ToDate (Date, optional) | Printed, Till, Cashier, Sale, Reason, Amount | — | Sum(Amount) |
| POS117 | Tender & Multi-currency Tender Analysis | Tenders taken in the period by type and currency, in currency and base, net of change. | FromDate (Date, optional), ToDate (Date, optional) | Tender, Currency, Count, Amount, Share, Base | — | Sum(Base) |
| POS120 | Offline Sync Log | Operations captured offline in the period: kind, status, attempts, when synced and any error. | FromDate (Date, optional), ToDate (Date, optional) | Created, Kind, Status, Attempts, Synced, Error | — | — |
| POS121 | Fiscal Submission Log | Sales submitted to the fiscal device or revenue authority in the period, with the fiscal receipt reference and outcome. | FromDate (Date, optional), ToDate (Date, optional) | Submitted, Till, Sale, Reference, Outcome, Amount | — | Sum(Amount) |

## Purchasing

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| AP-AGE | Aged Creditors | Outstanding supplier balances by ageing band as at a date. | AsAtDate (Date, optional) | TraderCode, TraderName, Current, Days1To30, Days31To60, Days61To90, Days91Plus, Total | — | Sum(Total) |
| AP-DUE | Payments Due | Outstanding supplier invoices ordered by due date, with days until due (negative when overdue). | AsAtDate (Date, optional) | SupplierCode, SupplierName, DocumentNumber, DueDate, DaysUntilDue, Outstanding | — | Sum(Outstanding) |
| AP-RECUR | Recurring Purchases | Standing recurring supplier-invoice templates with amount, tax, interval, next run date and active flag. | — | Reference, Supplier, NetAmount, TaxAmount, IntervalMonths, NextRunDate, Active | — | Sum(IntervalMonths) |
| AP-SUP | Supplier Register | Supplier master: code, name, currency, payment terms and on-hold flag. | — | Code, Name, Currency, TermsDays, OnHold | — | Sum(TermsDays) |
| AP53 | Supplier Ledger | One supplier's documents in date order with running balance and what is still outstanding on each. | SupplierCode (String, optional) | Date, Type, Document, Due, Debit, Credit, Balance, Outstanding | — | Sum(Outstanding) |
| AP54 | Purchase Day Book | Supplier invoices, credit notes and debit notes posted in the period, in transaction and base currency. | FromDate (Date, optional), ToDate (Date, optional) | Date, Document, Type, Supplier, Currency, Transaction, Base | — | Sum(Base) |
| AP55 | Purchase Analysis by Supplier and Item | Order lines placed in the period (cancelled orders excluded) by supplier and item: quantity, average price and value. | FromDate (Date, optional), ToDate (Date, optional) | Supplier, Item, Orders, Quantity, AveragePrice, Currency, Value | — | Sum(Value) |
| AP58 | Payments Made | Payments to suppliers in the period (payment runs and single payments) with tax withheld. | FromDate (Date, optional), ToDate (Date, optional) | Date, Document, Supplier, Currency, Transaction, Withheld, Base | — | Sum(Base) |
| AP59 | Goods Received Not Invoiced (GRNI) | Order lines received but not yet invoiced: the GRNI accrual by order and item, with its age. | AsAtDate (Date, optional) | Order, Supplier, Item, Received, Invoiced, Price, LastReceipt, Days, Value | — | Sum(Value) |
| AP60 | Invoice Matching Exceptions | Supplier invoices disputed against their order: reason, status and resolution. | — | Invoice, Date, Supplier, Order, Reason, Status, Resolution, Value | — | Sum(Value) |
| AP62 | Withholding Tax Deducted | Tax withheld from supplier payments in the period, by certificate, and whether it has been remitted. | FromDate (Date, optional), ToDate (Date, optional) | Certificate, Supplier, Payment, Date, Tax, Rate, Gross, Remitted, Withheld | — | Sum(Withheld) |
| AP63 | Supplier Performance Scorecard | Per supplier in the period: deliveries, on time, quantity received and returned, quality and spend. | FromDate (Date, optional), ToDate (Date, optional) | Supplier, Deliveries, OnTime, Received, Returned, Quality, Spend | — | Sum(Spend) |
| AP64 | Supplier Balances (with debit balances) | What is owed to each supplier at the date; suppliers in debit (owing us) flagged, with hold and blacklist status. | AsAtDate (Date, optional) | Supplier, Currency, Flag, Status, Balance | — | Sum(Balance) |
| AP65 | Supplier Bank Detail Change Audit | Every request to change a supplier's bank details: who asked, who reviewed, outcome (account numbers masked). | — | Requested, Supplier, RequestedBy, NewDetails, Status, ReviewedBy, Reason | — | — |
| AP66 | Top Suppliers & Concentration | Suppliers ranked by net purchases in the period, with share and cumulative share. | FromDate (Date, optional), ToDate (Date, optional) | Rank, Supplier, Documents, Share, Cumulative, Purchases | — | Sum(Purchases) |
| PO124 | Requisition Approval Turnaround | Requisitions in the period with who approved them and how many days it took. | FromDate (Date, optional), ToDate (Date, optional) | Requisition, Supplier, Requested, Status, Approver, Approved, Days, Value | — | Sum(Value) |
| PO125 | Approval History / Authorisation Audit | Every workflow action in the period: document, stage, who, what and when, with the document value. | FromDate (Date, optional), ToDate (Date, optional) | When, Document, Reference, Stage, User, Action, Comment, Value | — | Sum(Value) |
| PO126 | Purchase Order Listing | Every order in the period with status, required-by date, overdue flag and the value still to be received. | FromDate (Date, optional), ToDate (Date, optional), AsAtDate (Date, optional) | Order, Date, Supplier, Status, RequiredBy, Overdue, Currency, Ordered, Received, Outstanding | — | Sum(Outstanding) |
| PO128 | Committed Cost by Budget Line | Commitments raised against the budget, by account and department, with their status. | — | Account, Department, Period, Reference, Type, Status, Amount | — | Sum(Amount) |
| PO129 | Goods Received Listing & Variance | Goods received in the period against what was ordered, with quantity and price variances. | FromDate (Date, optional), ToDate (Date, optional) | Grn, Date, Order, Supplier, Item, Ordered, Received, QuantityVariance, PriceVariance, Value | — | Sum(Value) |
| PO130 | Rejected Goods / Returns | Quality inspections in the period that rejected goods, with the reason and the value rejected. | FromDate (Date, optional), ToDate (Date, optional) | Grn, Inspected, Supplier, Item, Passed, Rejected, Reason, Value | — | Sum(Value) |
| PO131 | RFQ Comparison & Award | Every quote received on each RFQ, ranked by total price, with lead time and the award and its reason. | — | Rfq, Supplier, Received, LeadDays, Rank, Awarded, Reason, Total | — | Sum(Total) |
| PO132 | Purchase Price Variance | Each item ordered in the period at a different price from the order before it, with the variance on the quantity ordered. | FromDate (Date, optional), ToDate (Date, optional) | Item, Order, Date, Supplier, Previous, Price, Change, Quantity, Variance | — | Sum(Variance) |
| PO134 | Procurement Spend Analysis | Order value in the period by supplier and currency, with lines, items and share. | FromDate (Date, optional), ToDate (Date, optional) | Supplier, Currency, Orders, Lines, Items, Share, Spend | — | Sum(Spend) |
| PO135 | Supplier Lead Time Analysis | Per supplier: days from order to first receipt (average, fastest, slowest) and orders received by their required date. | FromDate (Date, optional), ToDate (Date, optional) | Supplier, Orders, Average, Fastest, Slowest, OnTime | — | — |
| POP-OPEN | Open Purchase Orders | Purchase orders not yet fully received/invoiced, with their order value. | — | OrderNumber, OrderDate, Supplier, Status, TotalNet | — | Sum(TotalNet) |
| POP-REQ | Purchase Requisitions | Requisitions with supplier, date, estimated value and status, newest first. | — | RequisitionNumber, Supplier, RequestedDate, Total, Status | — | Sum(Total) |

## Receipting

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| RC1 | Cashier Cash-Up / Session Close | Expected vs counted per currency and denomination, variance and supervisor sign-off. | Session (String, optional), Currency (String, optional) | — | — | — |
| RC10 | Hourly Transaction Volume & Queue Analysis | Transactions per hour per station, average handling time, peak periods. | Date (Date, optional) | Hour, Station, Transactions, AvgSeconds | — | — |
| RC11 | Cashier Productivity | Receipts processed, value, average handling time, variances, cancellations, reprints. | FromDate (Date, optional), ToDate (Date, optional) | Cashier, Receipts, Value, Variances | — | Sum(Value) |
| RC12 | Receipting-to-GL Reconciliation | Total collected = debtor control movement + sundry income + deposit movement + suspense movement. | FromDate (Date, optional), ToDate (Date, optional) | Component, Amount | — | Sum(Amount) |
| RC13 | Cash Control Account Reconciliation | Till → safe → cash in transit → bank; supply the account codes for each stage. | CashAccount (String, optional), SafeAccount (String, optional), TransitAccount (String, optional), BankAccount (String, optional) | Stage, Debit, Credit | — | Sum(Credit) |
| RC14 | Till Variance Report | Shortages and overages by cashier and period, with recovery status. | FromDate (Date, optional), ToDate (Date, optional) | Cashier, Session, Variance, Status | — | Sum(Variance) |
| RC15 | Cancelled & Reversed Receipts | With reason, authorising supervisor and value. | FromDate (Date, optional), ToDate (Date, optional) | ReceiptNumber, Reason, Supervisor, Amount | — | Sum(Amount) |
| RC16 | Receipt Reprint Log | Every reprint, watermarked COPY, numbered and logged. | FromDate (Date, optional), ToDate (Date, optional) | ReceiptNumber, PrintedOn, PrintedBy | — | — |
| RC17 | Receipt Number Gap Analysis | Per station — must show no gaps. | Station (String, optional) | Station, FromNumber, ToNumber, Gaps | — | — |
| RC18 | Back-Dated Receipt Report | Receipts posted with a date before the transaction date. | FromDate (Date, optional), ToDate (Date, optional) | ReceiptNumber, ReceiptDate, CapturedOn, Amount | — | Sum(Amount) |
| RC19 | Allocation Override Report | Where a cashier directed money against the default policy, with the reason. | FromDate (Date, optional), ToDate (Date, optional) | ReceiptNumber, Account, Reason, Amount | — | Sum(Amount) |
| RC2 | X-Read / Z-Read | Mid-shift non-resetting (X) and session-close immutable (Z) totals by tender and currency. | Session (String, optional) | — | — | — |
| RC20 | Manual / Pre-Numbered Stationery Control | Books issued, serials used, serials unaccounted, books unreturned. | AsAtDate (Date, optional) | BookNumber, Range, Used, Unaccounted | — | — |
| RC21 | Receipting Audit Trail | Every receipt, cancellation, allocation, suspense movement and refund — before/after values, user, station. | FromDate (Date, optional), ToDate (Date, optional) | When, Action, User, Detail | — | — |
| RC22 | Segregation of Duties Violations — Receipting | Blocked or granted SoD-conflicting actions. | FromDate (Date, optional), ToDate (Date, optional) | User, Conflict, When | — | — |
| RC23 | Receipts Against Related-Party Accounts | Blocked attempts and any granted exceptions. | FromDate (Date, optional), ToDate (Date, optional) | Cashier, Account, Outcome, Amount | — | Sum(Amount) |
| RC24 | Banking / Deposit Slip | Per bank account and currency. | Deposit (String, optional) | — | — | — |
| RC25 | Daily Banking Summary | Collected vs handed to treasury vs banked vs confirmed on statement. | Date (Date, optional) | Stage, Amount | — | Sum(Amount) |
| RC26 | Cash in Transit Ageing | Anything unbanked or unconfirmed beyond N days. | AsAtDate (Date, optional) | Reference, DepositDate, Days, Amount | — | Sum(Amount) |
| RC27 | Unbanked Collections Report | Takings not yet moved into cash-in-transit. | AsAtDate (Date, optional) | Station, Session, Amount | — | Sum(Amount) |
| RC28 | Pickups, Drops & Petty Cash Pay-outs | Mid-shift cash movements with reason. | FromDate (Date, optional), ToDate (Date, optional) | When, Type, Reason, Amount | — | Sum(Amount) |
| RC29 | Unallocated / Suspense Receipts Register | With ageing — the report that recovers lost money. | AsAtDate (Date, optional) | Reference, PayerName, Days, Amount | — | Sum(Amount) |
| RC3 | Daily Collection Summary | Collections by station, cashier, tender type, currency and revenue head. | Date (Date, optional) | Category, Count, Amount | — | Sum(Amount) |
| RC30 | Suspense Allocation Report | What was allocated out, by whom, to which account, with the reason. | FromDate (Date, optional), ToDate (Date, optional) | When, Account, Reason, Amount | — | Sum(Amount) |
| RC31 | Bulk Import Batch Report | File, records, matched, unmatched, duplicates rejected, total value. | FromDate (Date, optional), ToDate (Date, optional) | FileReference, Matched, Unmatched, TotalAmount | — | Sum(TotalAmount) |
| RC32 | Import Matching Exceptions | Unmatched references, ambiguous matches, amount mismatches. | FromDate (Date, optional), ToDate (Date, optional) | PayerReference, PayerName, Reason, Amount | — | Sum(Amount) |
| RC33 | Pending Tenders Report | Claimed deposits and transfers not yet confirmed on the bank statement, aged. | AsAtDate (Date, optional) | Reference, Tender, Days, Amount | — | Sum(Amount) |
| RC34 | Dishonoured Cheque / Reversed Payment Register | With penalties raised and recovery status. | FromDate (Date, optional), ToDate (Date, optional) | Reference, Drawer, Penalty, Amount | — | Sum(Amount) |
| RC35 | Official Receipt | Thermal / A5 / A4 / multi-part — allocation by service, balance after payment, currency and rate. | Receipt (String, optional) | — | — | — |
| RC36 | Counter Mini-Statement | Last N transactions and the current balance — printed on request. | Account (String, optional) | — | — | — |
| RC37 | Full Account Statement | The AR / municipal / housing / school statement, printable from the counter. | Account (String, optional) | — | — | — |
| RC38 | Payment History per Account | All receipts, allocations and reversals for one payer. | Account (String, optional), FromDate (Date, optional), ToDate (Date, optional) | Date, ReceiptNumber, Type, Amount | — | Sum(Amount) |
| RC39 | Credit Balances / Overpayments | Residents in credit, with ageing — a liability that must be reported. | AsAtDate (Date, optional) | Account, Name, Days, Credit | — | Sum(Credit) |
| RC4 | Receipt Listing / Cashier Day Book | Every receipt with payer, amount, tender and allocation. | FromDate (Date, optional), ToDate (Date, optional) | ReceiptNumber, Payer, Tender, Amount | — | Sum(Amount) |
| RC40 | Deposits Held Register | By payer, type, date received, refundable amount. | AsAtDate (Date, optional) | Payer, Type, ReceivedOn, Amount | — | Sum(Amount) |
| RC41 | Refunds Register | Requested, approved, paid, outstanding — with turnaround times. | FromDate (Date, optional), ToDate (Date, optional) | Payer, Status, Days, Amount | — | Sum(Amount) |
| RC42 | Payment Arrangements Captured at Counter | New arrangements, honoured, defaulted. | FromDate (Date, optional), ToDate (Date, optional) | Account, StartDate, Status, Instalment | — | Sum(Instalment) |
| RC43 | Top Payers / Largest Receipts | For review, and for spotting structuring. | FromDate (Date, optional), ToDate (Date, optional) | Payer, Count, Amount | — | Sum(Amount) |
| RC44 | Accounts With No Payment in N Months | Feeds credit control and disconnection lists. | AsAtDate (Date, optional) | Account, Name, LastPayment, Balance | — | Sum(Balance) |
| RC45 | Agent Collection Reconciliation | Collected, remitted, outstanding, commission due, per agent. | AsAtDate (Date, optional) | Agent, Collected, Remitted, Outstanding | — | Sum(Outstanding) |
| RC46 | Agent Remittance Ageing | Money held by agents and not remitted. | AsAtDate (Date, optional) | Agent, Days, Outstanding | — | Sum(Outstanding) |
| RC47 | Field Collector Daily Reconciliation | Receipts issued, value, cash returned, variance, per collector. | Date (Date, optional) | Collector, Receipts, Value, Variance | — | Sum(Variance) |
| RC48 | Field Collection Route Performance | Collections by route. | FromDate (Date, optional), ToDate (Date, optional) | Route, Collections, Amount | — | Sum(Amount) |
| RC49 | Collection Efficiency | Billed vs collected, by service, ward and month. | FromDate (Date, optional), ToDate (Date, optional) | Service, Billed, Collected, Percent | — | Sum(Collected) |
| RC5 | Collection by Revenue Vote | Sundry income analysed to the GL vote. | FromDate (Date, optional), ToDate (Date, optional) | AccountCode, AccountName, Amount | — | Sum(Amount) |
| RC50 | Collection vs Revenue Budget | Against the §4.18 revenue budget, by vote and department. | FromDate (Date, optional), ToDate (Date, optional) | Vote, Budget, Actual, Variance | — | Sum(Variance) |
| RC51 | Collection Trend & Seasonality | Daily, weekly, monthly, year-on-year. | FromDate (Date, optional), ToDate (Date, optional) | Period, Amount | — | Sum(Amount) |
| RC52 | Prepaid Token Sales & Credit-off-Arrears Recovery | Token value issued and the arrears portion recovered. | FromDate (Date, optional), ToDate (Date, optional) | Date, TokenValue, ArrearsRecovered | — | Sum(ArrearsRecovered) |
| RC53 | Receipting Executive Dashboard | Today's collections by channel and service, cash unbanked, suspense pool and its age, variance trend, queue load. | Date (Date, optional) | — | — | — |
| RC6 | Collection by Service | What residents paid for — water, rates, refuse, sewer, licences, fees. | FromDate (Date, optional), ToDate (Date, optional) | Service, Amount | — | Sum(Amount) |
| RC7 | Collection by Tender Type & Channel | Counter cash, card, mobile money, bank, agent, field. | FromDate (Date, optional), ToDate (Date, optional) | Tender, Channel, Amount | — | Sum(Amount) |
| RC8 | Collection by Currency | Amounts by currency with the rates applied and the realised FX effect. | FromDate (Date, optional), ToDate (Date, optional) | Currency, Transaction, Rate, BaseAmount | — | Sum(BaseAmount) |
| RC9 | Collection by Ward / Zone | Collections by ward, zone, suburb, class or scheme. | FromDate (Date, optional), ToDate (Date, optional) | Area, Amount | — | Sum(Amount) |

## Restaurant

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| RES-BILLS | Bills | Every restaurant bill over a period — net, tax, service charge, tip, discount and gross. | FromDate (Date, optional), ToDate (Date, optional) | Date, Order, Net, Tax, Service, Tip, Discount, Gross | — | Sum(Gross) |
| RES-CAT | Category Sales | Billed sales by menu category (starters, mains, drinks…) over a period. | FromDate (Date, optional), ToDate (Date, optional) | Category, Qty, Net, Tax, Gross | — | Sum(Gross) |
| RES-COMB | Combined Tables | Active table groups — the combined tables, their primary and total seats. | — | Group, Primary, Tables, Seats | — | — |
| RES-COST | Menu Cost Card | Every menu item's price, recipe cost (from ingredient average cost), gross margin and food-cost %. | — | Code, Name, Price, Cost, Margin, FoodCostPct | — | Sum(FoodCostPct) |
| RES-DELIV | Active Deliveries | Delivery orders still in progress — customer, address, driver and dispatch state. | — | Order, Customer, Address, Driver, Status | — | — |
| RES-KOT | Kitchen Order Tickets | Open kitchen tickets (not yet served) with their status and item count. | — | Ticket, Status, Items, Sent | — | — |
| RES-MENU | Menu | Menu items with category, price, recipe ingredient count and availability. | — | Code, Name, Category, Ingredients, Available, Price | — | Sum(Price) |
| RES-MIX | Menu Engineering | Sales mix by menu item — quantity, revenue, recipe cost, margin and menu-engineering class. | FromDate (Date, optional), ToDate (Date, optional) | Item, QtySold, Revenue, Cost, Margin, Class | — | Sum(Margin) |
| RES-OTYPE | Sales by Order Type | Billed sales split by fulfilment type (dine-in / takeaway / delivery) over a period. | FromDate (Date, optional), ToDate (Date, optional) | Type, Orders, Net, Tax, Gross | — | Sum(Gross) |
| RES-RESV | Table Reservations | Upcoming table bookings — when, table, guest, party size and contact. | — | When, Table, Guest, Party, Phone | — | — |
| RES-SECT | Floor Plan | Floor-plan sections with their tables, seats, live occupancy and assigned server. | — | Section, Tables, Seats, Occupied, Server, Active | — | — |
| RES-SERVER | Server Sales | Billed sales by waiter over a period — orders, net, tax and gross (for tip-out and performance). | FromDate (Date, optional), ToDate (Date, optional) | Server, Orders, Net, Tax, Gross | — | Sum(Gross) |
| RES-SSALES | Section Sales | Billed sales by floor-plan section over a period — orders, net, tax and gross. | FromDate (Date, optional), ToDate (Date, optional) | Section, Orders, Net, Tax, Gross | — | Sum(Gross) |
| RES-TBL | Table Status | Tables with seats and current status. | — | TableNumber, Seats, Status | — | — |
| RES-TIPS | Tip Allocations | Pooled tips allocated to staff, with amount and payment status. | — | Staff, Date, Status, Amount | — | Sum(Amount) |
| RES-VOID | Voids &amp; Comps | Voided and comped order lines over a period, with reasons and the authorising manager — for oversight. | FromDate (Date, optional), ToDate (Date, optional) | Date, Order, Item, Qty, Type, Reason, Authorised | — | — |
| RES-WAIT | Waitlist | Walk-in parties currently queued for a table, in the order they joined. | — | Added, Guest, Party, Quoted, Phone | — | — |
| RES-WASTE | Stock Wastage | Spoilage / waste of stock ingredients over a period, with reasons and value written off. | FromDate (Date, optional), ToDate (Date, optional) | Date, Item, Qty, Value, Reason | — | Sum(Value) |

## Sales

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| AR-AGE | Aged Debtors | Outstanding customer balances by ageing band as at a date. | AsAtDate (Date, optional) | TraderCode, TraderName, Current, Days1To30, Days31To60, Days61To90, Days91Plus, Total | — | Sum(Total) |
| AR-CREDIT | Customer Credit Exposure | Each customer's credit limit, current balance and remaining credit, flagging on-hold and over-limit accounts. | AsAtDate (Date, optional) | CustomerCode, CustomerName, CreditLimit, Balance, Available, OnHold, OverLimit | — | Sum(Available) |
| AR-CUST | Customer Register | Customer master: code, name, currency, credit limit, payment terms and on-hold flag. | — | Code, Name, Currency, CreditLimit, TermsDays, OnHold | — | Sum(TermsDays) |
| AR-RECUR | Recurring Invoices | Standing recurring-invoice templates with amount, tax, interval, next run date and active flag. | — | Reference, Customer, NetAmount, TaxAmount, IntervalMonths, NextRunDate, Active | — | Sum(IntervalMonths) |
| AR-STMT | Customer Statement | Outstanding items for a customer, with the closing balance. | — | Date, Type, Number, Due, Outstanding | — | Sum(Outstanding) |
| AR29 | Aged Debtors Detail | Every open item as at the date with its age from due date (or invoice date for customers aged that way) and band. | AsAtDate (Date, optional) | Customer, Document, Type, Date, Due, Days, Band, Outstanding | — | Sum(Outstanding) |
| AR30 | Aged Debtors by Sales Rep and Currency | Debtors aged as at the date, grouped by sales rep (or agent) and currency, in base. | AsAtDate (Date, optional) | Rep, Currency, Customers, Current, D30, D60, D90, D120, Over120, Total | — | Sum(Total) |
| AR31 | Customer Ledger | One customer's documents in date order with running balance and what is still outstanding on each. | CustomerCode (String, optional) | Date, Type, Document, Due, Debit, Credit, Balance, Outstanding | — | Sum(Outstanding) |
| AR33 | Sales Day Book | Invoices, credit notes and debit notes raised in the period, in transaction and base currency. | FromDate (Date, optional), ToDate (Date, optional) | Date, Document, Type, Customer, Currency, Transaction, Base | — | Sum(Base) |
| AR34 | Sales Analysis with Margin | Invoiced order lines and POS sales in the period by customer and item, against current average cost. | FromDate (Date, optional), ToDate (Date, optional) | Customer, Item, Quantity, Sales, Cost, MarginPercent, Margin | — | Sum(Margin) |
| AR35 | Top Customers (Pareto) | Customers ranked by net sales in the period, with share and cumulative share. | FromDate (Date, optional), ToDate (Date, optional) | Rank, Customer, Documents, Share, Cumulative, Sales | — | Sum(Sales) |
| AR36 | Customer Sales History & Trend | Per customer: net sales this year to date against the same period last year, and the last three months against the three before. | AsAtDate (Date, optional) | Customer, LastYear, Change, Last3, Prior3, Trend, ThisYear | — | Sum(ThisYear) |
| AR39 | Unallocated Receipts, Credits & Advances | Receipts, credit notes and advances not yet allocated to invoices at the date, with how long they have been unallocated. | AsAtDate (Date, optional) | Customer, Document, Type, Date, Days, Unallocated | — | Sum(Unallocated) |
| AR40 | Debtor Days (DSO) | Per customer: what is owed at the date against sales over the 90 days to it, as days of sales outstanding. | AsAtDate (Date, optional) | Customer, Sales90, Dso, Terms, Owing | — | Sum(Owing) |
| AR41 | Promise-to-Pay Diary | Promises recorded by credit control: amount, date promised and whether kept, broken or still open at the date. | AsAtDate (Date, optional) | PayBy, Customer, Made, Status, Resolved, Note, Amount | — | Sum(Amount) |
| AR43 | Bad Debt Write-off Listing | Write-offs in the period with the customer, document and the debt case that approved it. | FromDate (Date, optional), ToDate (Date, optional) | Date, Customer, Document, Case, Amount | — | Sum(Amount) |
| AR45 | Lay-bye Status & Arrears | Every lay-bye plan with price, paid, balance and days to (or past) its final date. | AsAtDate (Date, optional) | Plan, Customer, Opened, Final, Status, Price, Paid, Due, Balance | — | Sum(Balance) |
| AR47 | Debtor Exposure by Currency | What customers owe at the date in each currency, in transaction and base, with each currency's share. | AsAtDate (Date, optional) | Currency, Customers, Transaction, Share, Base | — | Sum(Base) |
| AR48 | New & Dormant Customers | Customers first invoiced in the period, and customers not invoiced for 90 days to its end. | FromDate (Date, optional), ToDate (Date, optional) | Status, Customer, First, Last, Lifetime | — | Sum(Lifetime) |

## SalesOrders

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| SO137 | Open Sales Orders & Allocation | Open order lines with what is outstanding to despatch and the free stock that can cover it. | — | Order, Date, Customer, Item, Ordered, Despatched, Outstanding, Free, Cover, Value | — | Sum(Value) |
| SO143 | Quotation Conversion Rate | Quotations by month: count, converted to orders, value quoted, value converted and the conversion rate. | FromDate (Date, optional), ToDate (Date, optional) | Month, Quotes, Converted, Rate, Quoted, ConvertedValue | — | Sum(ConvertedValue) |
| SOP-BO | Sales Back-Orders | Order lines not yet fully despatched, with the ordered, despatched and outstanding quantities. | — | OrderNumber, Customer, OrderDate, Description, Ordered, Despatched, Outstanding | — | Sum(Outstanding) |
| SOP-QUOTE | Sales Quotations | Quotations with customer, dates, value and status, newest first. | — | QuoteNumber, Customer, QuoteDate, ValidUntil, Total, Status | — | Sum(Total) |

## School

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| SC175 | Fee Billing Run Summary | Fees billed in the period by term and grade: students billed and the amount. | FromDate (Date, optional), ToDate (Date, optional) | Term, Grade, Students, Boarders, Billed | — | Sum(Billed) |
| SC176 | Fee Arrears by Grade | Unpaid fee invoices as at the date per student, aged, grouped by grade. | AsAtDate (Date, optional) | Grade, Student, Guardian, Invoices, Oldest, Band, Outstanding | — | Sum(Outstanding) |
| SC177 | Fee Collection Report | Receipts from guardians in the period, with the students they pay for. | FromDate (Date, optional), ToDate (Date, optional) | Date, Receipt, Guardian, Students, Amount | — | Sum(Amount) |
| SC178 | Bursary & Discount Report | Students on bursaries with type and value, and what that is worth against their grade's fees. | — | Student, Grade, Type, Rate, Fees, Worth | — | Sum(Worth) |
| SC180 | Enrolment & Headcount | Per grade: enrolled, boarders, day scholars, bursary holders and students no longer enrolled. | — | Grade, Enrolled, Boarders, Day, Bursary, Left | — | — |
| SC182 | Student Balances Listing | Every student with guardian, grade, enrolment and what is owed on their fee invoices at the date. | AsAtDate (Date, optional) | Admission, Student, Grade, Guardian, Status, Balance | — | Sum(Balance) |
| SCH-STU | Student Register | Enrolled students with grade, boarding status and bursary. | — | AdmissionNumber, FullName, Grade, Boarder, BursaryValue | — | Sum(BursaryValue) |

## System

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| DB251 | Executive Dashboard | Year to date at the date: revenue, expenses, profit, cash, debtors, creditors, stock, working capital and the cash cycle. | AsAtDate (Date, optional) | Measure, Value | — | — |
| DB252 | Daily Flash Report | The day's sales, receipts and payments against the same day last week and the month to date, with cash at close. | AsAtDate (Date, optional) | Measure, Today, LastWeek, MonthToDate | — | — |
| DB253 | KPI Scorecard | This month against last: revenue, profit, gross margin, debtor, creditor and stock days, cash cycle — with the direction of travel. | AsAtDate (Date, optional) | Kpi, ThisMonth, LastMonth, Trend | — | — |
| DB254 | Branch Comparison League Table | Branches ranked by POS net sales in the period, with transactions, average basket and share. | FromDate (Date, optional), ToDate (Date, optional) | Rank, Branch, Transactions, AverageBasket, Share, Sales | — | Sum(Sales) |
| DB255 | Trend & Seasonality Analysis | Revenue, expenses and profit month by month for the 24 months to the date, with each month against the same month a year before. | AsAtDate (Date, optional) | Month, Revenue, Expenses, LastYear, YearOnYear, Profit | — | Sum(Profit) |
| SYS-PERIOD | Period Status | Accounting periods with their open/closed status for the key ledgers (GL, AR, AP) — a month-end close aid. | — | Period, Start, End, GL, AR, AP | — | — |
| SYS237 | Permission Matrix | Every role with each permission it grants and the operations allowed. | — | Role, Module, Permission, Name, Operations | — | — |
| SYS238 | User, Role & Group Membership | Every user with direct roles, groups, roles held through groups and individual overrides. | — | User, Name, Status, Roles, Groups, GroupRoles, Overrides | — | — |
| SYS239 | Segregation of Duties Violations (all modules) | Every active user whose combined permissions break a separation-of-duties rule. | — | User, Rule, Conflict, Severity | — | — |
| SYS240 | Master Data Change Report | Changes in the period to customers, suppliers, items, accounts, tariffs, properties, employees, price lists, users and roles. | FromDate (Date, optional), ToDate (Date, optional) | When, User, Entity, Id, Action, Changed | — | — |
| SYS242 | Back-dated Transactions | Journals entered in the period with a posting date more than the given number of days before they were entered. | FromDate (Date, optional), ToDate (Date, optional), Days (Integer, optional) | Journal, Posting, Entered, DaysBack, Source, Document, PreparedBy, Narrative | — | — |
| SYS243 | Deleted & Voided Documents | Records deleted or retired in the period, and journals reversed or cancelled, with who and why. | FromDate (Date, optional), ToDate (Date, optional) | When, Kind, Entity, Id, User, Reason | — | — |
| SYS244 | Document Numbering Gaps | Missing runs in each year's journal numbering. | — | Year, From, To, Missing, Description | — | — |
| SYS245 | Unposted & Deferred Batches | Journals still draft, deferred or awaiting approval, with who prepared them. | — | Journal, Posting, Status, Source, Document, PreparedBy, Narrative | — | — |
| SYS248 | Licence Installation History | Licences installed, when and by whom, and which is active (token fingerprint only). | — | Installed, By, Active, Fingerprint | — | — |
| SYS250 | Report Print & Export Log | Every report run in the period: who, what, format, size and copy number. | FromDate (Date, optional), ToDate (Date, optional) | When, Report, User, Purpose, Format, Bytes, Copy, Parameters | — | — |
| SYS258 | Price List Listing | Every price list with its items, quantity breaks and prices. | — | PriceList, Currency, Status, Item, MinimumQuantity, UnitPrice | — | — |
| SYS259 | Exchange Rate History | Every exchange rate captured, by currency pair and type, with its effective date. | — | From, To, Type, Effective, Quotation, Rate | — | — |
| SYS260 | Number Sequence Status | Every document sequence: prefix, padding, yearly reset, gapless flag and the next number per year. | — | Document, Prefix, Branch, Padding, Yearly, Gapless, Next | — | — |
| SYS261 | Company Configuration Listing | Every setting of the company record. | — | Setting, Value | — | — |
| SYS262 | Report Definition Inventory | Every report definition held — system and custom — with scope, version and last change. | — | Code, Name, Module, Scope, Version, Changed | — | — |
| SYS263 | Scheduled Job Status & Delivery Log | Each scheduled job's state and last result, then report deliveries in the period. | FromDate (Date, optional), ToDate (Date, optional) | Kind, Name, When, Status, Detail | — | — |

## Tax

| Code | Name | Purpose | Parameters | Columns | Grouping | Totals |
|---|---|---|---|---|---|---|
| TX216 | VAT Detail / Audit Report | Every posted line carrying a tax code in the period, by code, with its journal and document. | FromDate (Date, optional), ToDate (Date, optional) | Date, Journal, Document, TaxCode, Account, Narrative, Amount | — | Sum(Amount) |
| TX217 | VAT Reconciliation | Per tax code: movement on its output and input tax accounts in the period against the tax-coded lines, with the difference. | FromDate (Date, optional), ToDate (Date, optional) | TaxCode, Name, Output, Input, Coded, Difference | — | Sum(Difference) |
| TX218 | Exempt, Zero-rated & Out-of-scope Supplies | Lines in the period under zero-rated, exempt and out-of-scope tax codes, totalled per code. | FromDate (Date, optional), ToDate (Date, optional) | TaxCode, Name, Type, Lines, Amount | — | Sum(Amount) |
| TX220 | Withholding Tax Return | Withholding certificates by month and tax code: count, gross paid, tax withheld and how much has been remitted. | FromDate (Date, optional), ToDate (Date, optional) | Month, TaxCode, Certificates, Gross, Remitted, Withheld | — | Sum(Withheld) |
