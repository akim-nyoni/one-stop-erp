# Budgets

Preparing, approving, changing and monitoring budgets.

Works with: Budgeting.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Set up

### Create or copy a budget

Create a budget for a fiscal year from lines ACCOUNT:period:amount[:cost centre]; or copy an existing budget as the start of a new version (a revision, a supplementary budget or a reforecast), optionally uplifted by a percentage - the copy records which budget it came from.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Copy; default Create |
| Fiscal year code (create) | no | Text |
| Label | no | Text |
| Budget type | yes | One of: OriginalApproved, SupplementaryRevised, Reforecast, DraftSubmission, Capital, Cash, BaseCase, BestCase, WorstCase, ZeroBased, RollingForecast, Project, Establishment; default OriginalApproved |
| Department (blank: company-wide) | no | Text |
| Lines ACCOUNT:period:amount[:cost centre]; … (create) | no | Text |
| Budget id to copy | no | Number |
| Uplift % on the copy | no | Number |
| Why (copy) | no | Text |

The list beside the form shows: Budgets.

### Set a budget's currency and rate

Prepares a budget in a currency at a budget rate held on its header: the lines are planned in that currency and their base equivalents follow the rate, so a new rate re-states them, and the FX variance is measured against it. Blank currency (or the base currency) returns the budget to base.

| Field | Required | Notes |
| --- | --- | --- |
| Budget id | yes | Number |
| Budget currency | no | Choice |
| Budget rate (base per unit) | no | Number |

The list beside the form shows: Budgets in preparation.

### Set an assumption

A named assumption for the year (inflation, fee, headcount cost…). Driver-based lines are quantity × its value; untick Apply to preview what would change.

| Field | Required | Notes |
| --- | --- | --- |
| Fiscal year id | yes | Number |
| Assumption code | yes | Text |
| Value | yes | Number |
| Name | no | Text |
| Unit | no | Text |
| Apply | no | Flag; default False |

The list beside the form shows: Assumptions.

### Multi-year framework, archive and lock

Outer years: indicative amounts for the years after the budget year ("1:6100:1300; 2:6100:1400" - years after:account:amount), set before approval. Show: the framework by account. Roll forward: next year's draft from an approved framework. Archive a superseded version (seal verified), then Lock it for good.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Outer years, Show, Roll forward, Archive, Lock; default Outer years |
| Budget id | yes | Number |
| Outer years (outer years step) | no | Text |
| Next fiscal year (start year, roll forward) | no | Number |
| Label for the new draft | no | Text |

The list beside the form shows: Budget versions.

## Preparation

### Move a budget through preparation

Submit (department) → start review (Finance) → return with reasons or recommend → approve with the board/council resolution (not by the submitter). A returned budget can take Finance's proposals and be resubmitted as a new round.

| Field | Required | Notes |
| --- | --- | --- |
| Budget id | yes | Number |
| Step | yes | One of: Submit, Start review, Return, Recommend, Approve, Accept proposals; default Submit |
| Comment / resolution reference | no | Text |

The list beside the form shows: Budgets in preparation.

### Propose or revise a figure

Finance proposes a figure on a budget under review (kept beside the department's request); the department revises a draft or returned one.

| Field | Required | Notes |
| --- | --- | --- |
| Budget id | yes | Number |
| Account | yes | Text |
| Period | yes | Number; default 1 |
| Amount | yes | Number |
| Cost centre | no | Text |
| Why | no | Text |
| Finance proposal (untick to revise) | no | Flag; default False |

The list beside the form shows: Budgets in preparation.

### Show a budget's negotiation trail

Every step and every proposal, the department's figure beside Finance's, round by round.

| Field | Required | Notes |
| --- | --- | --- |
| Budget id | yes | Number |

The list beside the form shows: Budgets in preparation.

### Add a driver-based line

A line of quantity × an assumption (e.g. 40 learners × FEE), re-computed when the assumption changes.

| Field | Required | Notes |
| --- | --- | --- |
| Budget id | yes | Number |
| Account | yes | Text |
| Period | yes | Number; default 1 |
| Assumption code | yes | Text |
| Quantity | yes | Number |
| Cost centre | no | Text |

The list beside the form shows: Budgets in preparation.

### Spread an annual figure

Spreads an account's annual amount over the year: even, seasonal (last year's actuals), S-curve, working days, or custom weights (w1;w2;… one per period).

| Field | Required | Notes |
| --- | --- | --- |
| Budget id | yes | Number |
| Account | yes | Text |
| Annual amount | yes | Number |
| Profile | yes | One of: Even, Seasonal, Custom, SCurve, WorkingDays; default Even |
| Custom weights | no | Text |
| Cost centre | no | Text |

The list beside the form shows: Budgets in preparation.

### Build the staff budget from the establishment

Approved posts × annual cost per post, plus the increment, less the vacancy factor, spread evenly; employer contributions to their own account. Re-run after a salary change.

| Field | Required | Notes |
| --- | --- | --- |
| Budget id | yes | Number |
| Salaries account | yes | Text; default 6000 |
| Increment % | yes | Number; default 0 |
| Vacancy factor % | yes | Number; default 0 |
| Employer contributions % | yes | Number; default 0 |
| Contributions account | no | Text |

The list beside the form shows: Budgets in preparation.

### Preparation deadlines and budget alerts

Set a department's submission deadline; remind budget holders whose submission is due within the days given (or overdue); set where utilisation alerts go and their thresholds - with a department named, that department's budget holder e-mail and own thresholds; send the alerts now (each vote and threshold once). The scheduled task budget.notices does both daily.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Set deadline, Send reminders, Alert settings, Send alerts; default Set deadline |
| Department | no | Text |
| Deadline | no | Date |
| Fiscal year id (board / reminders) | yes | Number; default 0 |
| Remind this many days before | yes | Number; default 7 |
| Alert email | no | Text |
| Thresholds % (e.g. 75, 90, 100) | no | Text |
| Date | yes | Date |

The list beside the form shows: Preparation board.

### Export a budget to Excel

Writes the budget as an .xlsx workbook (a row per account and cost centre, P1 to P12 and a total) into the folder given, for editing offline. Keep the first two rows as they are.

| Field | Required | Notes |
| --- | --- | --- |
| Budget id | yes | Number |
| Add a zero row for every budget account | no | Flag; default False |
| Folder | yes | Text; default C:\Users\Akim\Documents |

The list beside the form shows: Budgets in preparation.

### Import a budget workbook

Reads an edited budget workbook back: every row is checked and the differences listed. Tick Apply to write them (draft or returned budgets only; nothing is written while any row is wrong). Rows deleted from the workbook leave those figures unchanged.

| Field | Required | Notes |
| --- | --- | --- |
| Budget id | yes | Number |
| Workbook (.xlsx) path | yes | Text |
| Apply the changes | no | Flag; default False |

The list beside the form shows: Budgets in preparation.

## Changes

### Move money between budget lines (virements)

Moves budget from one vote to another - between cost centres, or to another department's budget of the same year - requested with a justification, approved or rejected by another user whose authorisation level covers the amount's band (Bands: limit:level; …, blank limit for the top band); the register shows each vote's original, virements in and out, and current amount.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Request, Approve, Reject, Register, Bands; default Request |
| Budget id (request, register) | no | Number |
| From account | no | Text |
| To account | no | Text |
| From cost centre | no | Text |
| To cost centre | no | Text |
| To budget id (another department) | no | Number |
| Period | yes | Number; default 1 |
| Amount | yes | Number; default 0 |
| Justification / rejection reason | no | Text |
| Virement id (approve, reject) | no | Number |
| Bands: limit:level; … (bands) | no | Text |

The list beside the form shows: Virements.

### Re-phase an approved budget

Moves an amount between periods of an approved budget (the annual total does not change). Ask, then someone else approves or rejects (give the request id).

| Field | Required | Notes |
| --- | --- | --- |
| Request id to decide (blank: new request) | no | Text |
| Approve (untick to reject) | no | Flag; default False |
| Budget id | no | Number |
| Account | no | Text |
| From period | no | Number |
| To period | no | Number |
| Amount | no | Number |
| Reason | no | Text |

The list beside the form shows: Re-phasing requests.

### Carry unspent capital forward

Year-end: Propose lists each capital vote's approved budget, what was spent and what may still be carried; Approve carries the given amounts (VOTE:amount; …) into a later year's capital budget under the approval reference - a separate act, recorded for audit.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Propose, Approve; default Propose |
| From fiscal year (code) | yes | Text |
| Into capital budget (label, approve) | no | Text |
| VOTE:amount; … (approve) | no | Text |
| Approval reference | no | Text |
| As at | yes | Date |

The list beside the form shows: Capital budgets.

### Budget methods and forecasts

Builds a draft budget incrementally from a prior year's actuals or budget with an uplift (accounts the draft already has are left alone); reforecasts a budget with the actuals to a period and the budget for the rest (a new Reforecast version); and shows its cash budget - each line turned into cash by its timing (prefix:p0/p1/…; e.g. 4:50/30/20 collects sales over three months) from the opening cash.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Build from prior-year actuals, Build from prior-year budget, Reforecast, Cash budget; default Build from prior-year actuals |
| Budget id | yes | Number |
| Prior fiscal year id (build) | no | Number |
| Uplift % (build) | yes | Number; default 0 |
| Accounts starting (build; blank: all) | no | Text |
| Actuals to period (reforecast) | yes | Number; default 6 |
| Reforecast label | no | Text |
| Cash timings: prefix:p0/p1/…; … | no | Text |
| Opening cash | yes | Number; default 0 |

The list beside the form shows: Budgets.

## Monitoring

### Budget vs actual by fund, project or branch

The active budget against posted actuals for each fund, project or branch and account, up to a period (one value only, or all).

| Field | Required | Notes |
| --- | --- | --- |
| Fiscal year (start year) | yes | Number; default 2026 |
| Budget type | yes | One of: OriginalApproved, SupplementaryRevised, Reforecast, DraftSubmission, Capital, Cash, BaseCase, BestCase, WorstCase, ZeroBased, RollingForecast, Project, Establishment; default OriginalApproved |
| Department (blank: company-wide) | no | Text |
| By | yes | One of: Fund, Project, Branch; default Fund |
| Only this code | no | Text |
| Up to period | yes | Number; default 12 |

The list beside the form shows: Budgets in preparation.

### Vote book and utilisation alerts

The vote book to a period (original, supplementary, virements, expenditure, commitments and what is unexpended per vote), the votes past utilisation thresholds (e.g. 75, 90, 100 %), or a check that an approved budget's seal still matches its figures.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Vote book, Utilisation alerts, Verify seal; default Vote book |
| Financial year (starting in) | yes | Number; default 2026 |
| As at period | yes | Number; default 12 |
| Thresholds % | no | Text; default 75, 90, 100 |
| Budget id (seal) | no | Number |

### Variance commentary

The material variances between budget and actual to a period (over a value or a % threshold) with their explanations; a budget holder explains one by account.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Material variances, Explain; default Material variances |
| Financial year (starting in) | yes | Number; default 2026 |
| As at period | yes | Number; default 12 |
| Budget type | yes | One of: OriginalApproved, SupplementaryRevised, Reforecast, DraftSubmission, Capital, Cash, BaseCase, BestCase, WorstCase, ZeroBased, RollingForecast, Project, Establishment; default OriginalApproved |
| Department | no | Text |
| Material over (value) | yes | Number; default 1000 |
| or over (%) | yes | Number; default 10 |
| Account (explain) | no | Text |
| Explanation | no | Text |

### Budget-holder statements

Each department's active budget against actuals to the period of the date - budget and actual to date, the variance and the share of the year's budget used, with votes past the utilisation thresholds called out. Preview shows one department's; Send emails every budget holder with an address (the scheduled budget notices send them on the first of each month for the month just ended).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Preview, Send; default Preview |
| Department (preview) | no | Text |
| As at | yes | Date |

The list beside the form shows: Budgets in preparation.

### Record actual volumes

The quantity behind an actual (units sold, kilolitres billed, hours worked) on a budget line's account, so budget variances split into volume and price (report B35).

| Field | Required | Notes |
| --- | --- | --- |
| Account | yes | Text |
| Department | no | Text |
| Date | yes | Date |
| Quantity | yes | Number |
| Reference | yes | Text |

The list beside the form shows: Recorded volumes.

### Year-end commitments

Closes the year's open commitments (orders not yet invoiced): those listed (ids) are carried forward into the next year's budget, the rest lapse.

| Field | Required | Notes |
| --- | --- | --- |
| Closing financial year (starting in) | yes | Number; default 2026 |
| Carry forward: commitment ids, comma-separated | no | Text |

The list beside the form shows: Open commitments.

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| B1 | Budget Preparation Status Board | Every department's budget preparation for the year of the date: not started, in progress, submitted or approved, against the deadline. |
| B10 | Revenue Budget Build-up | Driver-based revenue lines: the volume each is built on, the implied rate and the amount. |
| B11 | Budget Version Register | Every budget version of the year ever created: number, type, status, author and date, why it was raised, who approved it under which resolution, and its seal hash with whether it still verifies. |
| B12 | Budget Version Comparison | For each budget with more than one version: the latest against the one before, per account. |
| B13 | Original vs Recommended vs Approved | Per department and account: what was first requested, the last draft recommended, what was approved, and the cut. |
| B14 | Budget Approval Audit | Every status change of the year's budgets - submitted, approved, activated, superseded - with who made it and the days since the previous step. |
| B15 | Multi-Year Budget & Actual Comparison | Each budgeted account's budget and actual for this year and the two before. |
| B16 | Archived Budget Retrieval | Any sealed budget of the date's year in full, exactly as approved - every account by quarter - headed by its seal hash re-verified against the lines. Choose the budget by label and version (latest sealed by default). |
| B17 | Budget Versions, Seals & Superseded Budgets | Every budget held: type, version, status, why it was revised, and whether its seal still verifies. |
| B18 | Budget Line Change Log | Every recorded change to budget lines in the year, with who made it and the fields changed. |
| B19 | Approved Budget Register by Vote & Department | The appropriation of record: every in-force budget line for the year by account and department, by quarter. |
| B2 | Departmental Budget Submission | Every budget of the year by department and version: each account's quarters and annual total. |
| B20 | Virement Register | Transfers between budget lines in the year: from, to, period, amount, justification, who asked and who reviewed. |
| B21 | Budget Reconciliation (Original to Current) | Per vote: original appropriation, plus supplementary, plus or minus virements and rephasing, equals the current approved budget. |
| B22 | Supplementary Appropriation Schedule | Each supplementary or revised budget of the year: per vote, the amount against the original approved, the change, and the reason. |
| B23 | Rephasing Report | Between consecutive versions of a budget: votes whose annual total is unchanged but whose months moved, with the value moved. |
| B24 | Vote Book | Per budgeted account and department: the year's budget, actual to date, open commitments and what remains. |
| B25 | Commitment / Encumbrance Register | Every commitment of the year with reference, account, department, period and status. |
| B26 | Commitments vs Open Purchase Orders | Open commitments against the open value of the purchase order they reference, with any difference. |
| B27 | Budget Block & Override Report | Postings fund control refused for want of an appropriation, and those let through on a supervisor's override with the stated reason. |
| B28 | Expenditure Against Unappropriated Votes | Expense accounts with spending this year but no line in an approved budget. |
| B29 | Year-End Commitment Carry-Forward Schedule | Commitments of the year that lapsed or were carried into the next year at year end, with who approved the carry-forward. |
| B3 | Target vs Submission Gap | Per department: the ceiling issued, what its latest draft requests, and the gap still to reconcile. |
| B30 | Unspent Capital Carry-Forward Schedule | Per capital vote: the year's capital budget, spent to the date, and the unspent balance proposed to carry forward. |
| B31 | Budget vs Actual vs Commitment vs Available | At account, department or division level, for the date's period, the year to date or the full year: budget, actual, open commitments, what is available and the share used, with the number of postings behind each actual. |
| B32 | Budget Holder Statement | Per budget holder: their department's votes - budget, spent and committed to date, available and utilisation. |
| B33 | Budget Utilisation (RAG) | Year-to-date use of each budgeted account's year-to-date budget: green under 90%, amber to 100%, red over. |
| B34 | Over-Budget & Threshold Exceptions | Only the budgeted accounts at amber or red, worst first. |
| B35 | Variance Decomposition | Year-to-date variance per budget line split into volume (actual vs budgeted quantity at budget price), FX (foreign amounts at actual vs planned rate) and price / rate. |
| B36 | Variance Commentary Pack | The year's variance explanations by period and account, beside the budget and actual they explain. |
| B37 | Departmental Management Accounts | Per department, year to date: income and expenditure against budget, and the surplus or deficit. |
| B38 | Full-Year Forecast / Outturn Projection | Per vote: the approved budget, actual to date, the forecast for the rest of the year (latest reforecast, else the remaining budget) and the projected outturn against budget. |
| B39 | Forecast vs Forecast Movement | Between the last two reforecast rounds of the year: each vote's forecast before and after, the movement and the reason given. |
| B4 | Consolidated Draft Budget by Hierarchy | The latest drafts rolled up department → division → directorate → company, at the level asked for. |
| B40 | Rolling Forecast | The next N months from the date: per vote, the latest reforecast (else the budget) month by month. |
| B41 | Cash Budget vs Actual Cash Flow | Month by month to the date: the cash budget's net flow against the actual net movement on the bank accounts, and the variance. |
| B42 | Capital Project Budget vs Actual vs Commitment | Each capital project: budget, cost to date, commitments, the share spent and the cost to complete. |
| B43 | Revenue Budget Performance | Per revenue source (income account), year to date: budget, billed, collected (the share of the billed invoices since paid), billed against budget and the collection rate. |
| B44 | Budget Accuracy Review | For the year before the as-at date's year: each account's full-year budget against actual, the variance and how accurate the budget was. |
| B45 | Budget Audit Trail | Every recorded change in the year to budgets, lines, virements, commitments and variance comments. |
| B5 | Assumption & Driver Register | The year's planning drivers with their value and the budget lines each one feeds. |
| B6 | Assumption Sensitivity / What-If | Change one driver: each dependent line scaled to the new value, the change per line and on the total. |
| B7 | Budget Preparation Basis | How every line of the year's budgets was arrived at - zero-based, incremental, actuals-based or driver-based - with the driver. |
| B8 | Establishment-Driven Staff Budget | Per department: approved posts at their annual cost less the vacancy factor, against the staff budget held for the department. |
| B9 | Capital Budget Submission & Phasing | The year's capital budgets per vote by month, with the cumulative share spent of the year - the S-curve. |
| BUD-BVA | Budget vs Actual | Active budget compared to GL actuals by account, with the variance. |
| BUD-LIST | Budget Listing | All budget versions with type, department, status, active flag and annual total. |
