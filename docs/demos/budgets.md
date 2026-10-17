# Demo: Budgets

Goal: show that preparing, approving, changing and monitoring budgets work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Set up
1. Open **Create or copy a budget**. Create a budget for a fiscal year from lines ACCOUNT:period:amount[:cost centre]; or copy an existing budget as the start of a new version (a revision, a supplementary budget or a reforecast), optionally uplifted by a percentage - the copy records which budget it came from. Fill in: Step, Budget type. Press OK and point out the result line and the list on the right.
2. Open **Set a budget's currency and rate**. Prepares a budget in a currency at a budget rate held on its header: the lines are planned in that currency and their base equivalents follow the rate, so a new rate re-states them, and the FX variance is measured against it. Fill in: Budget id. Press OK and point out the result line and the list on the right.
3. Open **Set an assumption**. A named assumption for the year (inflation, fee, headcount cost…). Fill in: Fiscal year id, Assumption code, Value. Press OK and point out the result line and the list on the right.

## Preparation
4. Open **Move a budget through preparation**. Submit (department) → start review (Finance) → return with reasons or recommend → approve with the board/council resolution (not by the submitter). Fill in: Budget id, Step. Press OK and point out the result line and the list on the right.
5. Open **Propose or revise a figure**. Finance proposes a figure on a budget under review (kept beside the department's request); the department revises a draft or returned one. Fill in: Budget id, Account, Period, Amount. Press OK and point out the result line and the list on the right.
6. Open **Show a budget's negotiation trail**. Every step and every proposal, the department's figure beside Finance's, round by round. Fill in: Budget id. Press OK and point out the result line and the list on the right.

## Changes
7. Open **Move money between budget lines (virements)**. Moves budget from one vote to another - between cost centres, or to another department's budget of the same year - requested with a justification, approved or rejected by another user whose authorisation level covers the amount's band (Bands: limit:level; …, blank limit for the top band); the register shows each vote's original, virements in and out, and current amount. Fill in: Step, Period, Amount. Press OK and point out the result line and the list on the right.
8. Open **Re-phase an approved budget**. Moves an amount between periods of an approved budget (the annual total does not change). Press OK and point out the result line and the list on the right.
9. Open **Carry unspent capital forward**. Year-end: Propose lists each capital vote's approved budget, what was spent and what may still be carried; Approve carries the given amounts (VOTE:amount; …) into a later year's capital budget under the approval reference - a separate act, recorded for audit. Fill in: Step, From fiscal year (code), As at. Press OK and point out the result line and the list on the right.

## Monitoring
10. Open **Budget vs actual by fund, project or branch**. The active budget against posted actuals for each fund, project or branch and account, up to a period (one value only, or all). Fill in: Fiscal year (start year), Budget type, By, Up to period. Press OK and point out the result line and the list on the right.
11. Open **Vote book and utilisation alerts**. The vote book to a period (original, supplementary, virements, expenditure, commitments and what is unexpended per vote), the votes past utilisation thresholds (e.g. Fill in: Step, Financial year (starting in), As at period. Press OK and point out the result line and the list on the right.
12. Open **Variance commentary**. The material variances between budget and actual to a period (over a value or a % threshold) with their explanations; a budget holder explains one by account. Fill in: Step, Financial year (starting in), As at period, Budget type, Material over (value), or over (%). Press OK and point out the result line and the list on the right.

## Reports to end on
- B1 Budget Preparation Status Board: Every department's budget preparation for the year of the date: not started, in progress, submitted or approved, against the deadline. Show the drill-through by clicking a document or account.
- B10 Revenue Budget Build-up: Driver-based revenue lines: the volume each is built on, the implied rate and the amount. Show the drill-through by clicking a document or account.
- B11 Budget Version Register: Every budget version of the year ever created: number, type, status, author and date, why it was raised, who approved it under which resolution, and its seal hash with whether it still verifies. Show the drill-through by clicking a document or account.
