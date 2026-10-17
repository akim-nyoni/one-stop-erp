# Demo: Fixed Assets

Goal: show that the asset register, depreciation, counts, maintenance and capital projects work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Asset register
1. Open **Assets: register, depreciate, dispose, move**. Register an asset (Dr cost, Cr the funding account; the class fills in method, life, residual and accounts left blank), run the month's depreciation for every active asset (a month already charged is skipped), dispose of an asset with the profit or loss on disposal, revalue it (IAS 16: an uplift to the revaluation reserve, a write-down to expense), move it to a new location or custodian, or record maintenance work on it. Fill in: Step, Date. Press OK and point out the result line and the list on the right.
2. Open **Asset classes and impairment**. Classes hold the defaults an asset registered in them takes (method, life, residual %, rate and accounts). Fill in: Step, Method, Depreciation convention, Date. Press OK and point out the result line and the list on the right.
3. Open **Asset insurance and warranty**. Who insures an asset, the policy and the sum insured with its expiry, and when the manufacturer's warranty ends (report M213 and the expiry alerts read them). Fill in: Asset number. Press OK and point out the result line and the list on the right.

## Asset counts
4. Open **Start an asset count**. Lists every held asset the register places at the location (blank for everywhere) for verification. Fill in: Date. Press OK and point out the result line and the list on the right.
5. Open **Record an asset sighting**. Records an asset (or unknown tag) seen during a count, where and in what condition. Fill in: Count number, Asset number / tag, Seen at. Press OK and point out the result line and the list on the right.
6. Open **Close an asset count**. Closes the count: anything not seen is missing. Fill in: Count number. Press OK and point out the result line and the list on the right.

## Maintenance
7. Open **Plan recurring maintenance**. A task repeated every N months for an asset. Fill in: Asset number, Task, Every (months), First due. Press OK and point out the result line and the list on the right.
8. Open **Complete planned maintenance**. Logs the planned task as done and rolls the next due date forward. Fill in: Plan id (from the list), Date, Cost. Press OK and point out the result line and the list on the right.

## Capital projects
9. Open **Open a capital project (CWIP)**. A project collecting cost in a capital work-in-progress account until capitalised. Fill in: Project code, Name, CWIP account, Budget, Opened. Press OK and point out the result line and the list on the right.
10. Open **Add a cost to a capital project**. Dr CWIP, Cr the funding account. Fill in: Project code, Date, Amount, Funding account, Description. Press OK and point out the result line and the list on the right.
11. Open **Capitalise a capital project**. Registers the asset at the project's whole cost, clearing CWIP. Fill in: Project code, New asset number, Asset description, Asset class, In use from, Useful life (months), Asset cost account, Accumulated depreciation account, Depreciation expense account. Press OK and point out the result line and the list on the right.

## Reports to end on
- FA-DISP Asset Disposals: Disposed assets with proceeds, net book value at disposal, the gain or loss, and the journal — newest first. Show the drill-through by clicking a document or account.
- FA-FCST Depreciation Forecast: Projected monthly depreciation across active assets and the aggregate closing net book value. Show the drill-through by clicking a document or account.
- FA-MAINT Asset Maintenance Log: Maintenance and service events logged against assets, newest first, with the informational cost. Show the drill-through by clicking a document or account.
