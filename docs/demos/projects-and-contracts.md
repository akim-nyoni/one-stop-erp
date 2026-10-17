# Demo: Projects & Contracts

Goal: show that job contracts, bills of quantities, site costs, subcontracts and certificates work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Contracts
1. Open **Open a job contract**. A contract (job) for a customer: its value, budgeted cost, retention % on certificates and the revenue account. Fill in: Step, Contract number, Contract value, Budgeted cost, Retention %, Cost from, Cost amount, Date. Press OK and point out the result line and the list on the right.
2. Open **Job cost position**. A job's actual cost (purchases, goods received for it, stock issues, labour, plant, subcontractors, overheads), what is still on order for it, and the forecast. Fill in: Step, Overhead charged on. Press OK and point out the result line and the list on the right.
3. Open **Contract revenue, losses and budget**. Recognise a contract's revenue by stage of completion (cost to date over budgeted cost) against certified billing — the difference to the contract asset or liability; provide for a foreseeable loss (the whole expected loss at once, the provision trued up); or revise the budgeted cost with the reason. Fill in: Step, Contract number, Date. Press OK and point out the result line and the list on the right.

## Planning
4. Open **Work breakdown (WBS)**. Phases (no cost head) group activities; an activity carries a budget and the cost head its costs are captured under. Fill in: Contract number, WBS code, Name. Press OK and point out the result line and the list on the right.
5. Open **Give an estimate to complete**. The site's estimate of what an activity still needs beyond actual and committed cost. Fill in: Contract number, Activity code, Date. Press OK and point out the result line and the list on the right.
6. Open **Cost to complete by WBS**. Budget, actual, committed (open subcontracts), estimate to complete and forecast final cost per phase and activity. Fill in: Contract number. Press OK and point out the result line and the list on the right.

## Site costs
7. Open **Book timesheet hours to a job**. Hours × rate into the job's labour head (at most 24 hours a day per employee). Fill in: Contract number, Employee, Date, Hours, Cost per hour. Press OK and point out the result line and the list on the right.
8. Open **Issue materials to a job**. Stock out of the warehouse to the contract's WIP account, into the job's materials head. Fill in: Contract number, Product, Warehouse / site store, Quantity, Date, Contract WIP account. Press OK and point out the result line and the list on the right.
9. Open **Log plant hours / running cost**. Hours on a job charge it at the plant's hire rate; leave the contract blank to log a running cost instead. Fill in: Plant code, Date. Press OK and point out the result line and the list on the right.

## Subcontracts
10. Open **Let a subcontract**. A package let to a supplier on a contract, with the retention held on its certificates. Fill in: Contract number, Subcontractor (supplier code), Subcontract reference, Package, Value, Retention %. Press OK and point out the result line and the list on the right.
11. Open **Pay a subcontract advance**. A mobilisation advance (Dr advance account, Cr bank), recovered as a share of each later certificate until cleared. Fill in: Subcontract reference, Advance, Recovered per certificate %, Date, Bank account, Advances to subcontractors account, Payment document number. Press OK and point out the result line and the list on the right.
12. Open **Certify a subcontract**. Invoices the subcontractor for the work since the last certificate, net of retention. Fill in: Subcontract reference, Certificate number, Date, Work done to date, Job cost account, Retention payable account. Press OK and point out the result line and the list on the right.

## Reports to end on
- JC186 Job Cost Report by Cost Head: Per contract and cost head: the budget (from the work breakdown), what is committed on open orders, the cost to date with where it came from, and what is left of the budget after both. Show the drill-through by clicking a document or account.
- JC189 Cost to Complete & Forecast Final Cost: Per contract: value, budget, cost to date, % complete by certification, forecast final cost and the variance to budget. Show the drill-through by clicking a document or account.
- JC190 WIP & Retention Report: Retention held on each contract, what has been released, practical completion and when the defects liability period ends, and what may be released at the date (half at completion, the rest when the defects period ends). Show the drill-through by clicking a document or account.
