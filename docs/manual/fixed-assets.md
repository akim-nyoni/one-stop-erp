# Fixed Assets

The asset register, depreciation, counts, maintenance and capital projects.

Works with: Fixed Assets.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Asset register

### Assets: register, depreciate, dispose, move

Register an asset (Dr cost, Cr the funding account; the class fills in method, life, residual and accounts left blank), run the month's depreciation for every active asset (a month already charged is skipped), dispose of an asset with the profit or loss on disposal, revalue it (IAS 16: an uplift to the revaluation reserve, a write-down to expense), move it to a new location or custodian, or record maintenance work on it.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Register, Run depreciation, Dispose, Revalue, Move, Maintenance; default Register |
| Asset number (all but the run) | no | Text |
| Date | yes | Date |
| Description (register, maintenance) | no | Text |
| Asset class (register) | no | Text |
| Cost / proceeds / new carrying amount / maintenance cost | no | Number |
| Funding account (register: credited; dispose: debited with proceeds) | no | Text |
| Parent asset (register a component) | no | Text |
| Cost centre (register) | no | Text |
| Department (register) | no | Text |
| Gain on disposal account | no | Text |
| Loss on disposal account | no | Text |
| Sold to customer (dispose: invoiced; Funding is then the proceeds account) | no | Text |
| Tax code on the sale (dispose to a customer) | no | Text |
| Revaluation reserve account | no | Text |
| Revaluation write-down expense account | no | Text |
| New location (move) | no | Text |
| New custodian (move) | no | Text |
| Service provider (maintenance) | no | Text |
| Document number | no | Text |
| Barcode / tag number (register, move) | no | Text |

The list beside the form shows: Fixed asset register.

### Asset classes and impairment

Classes hold the defaults an asset registered in them takes (method, life, residual %, rate and accounts). An impairment test (IAS 36) books a recoverable amount below the carrying amount as a loss, or reverses an earlier loss when it recovers - never above what it would have been unimpaired.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Save class, Impairment test; default Save class |
| Class code | no | Text |
| Class name | no | Text |
| Method | yes | One of: StraightLine, ReducingBalance, UnitsOfProduction; default StraightLine |
| Useful life (months) | no | Number |
| Residual % of cost | no | Number |
| Reducing-balance rate % | no | Number |
| Cost account | no | Text |
| Accumulated depreciation account | no | Text |
| Depreciation expense account | no | Text |
| Impairment account | no | Text |
| Asset number (test) | no | Text |
| Recoverable amount (test) | no | Number |
| Indicator / reason (test) | no | Text |
| Document number (test) | no | Text |
| Depreciation convention | yes | One of: FullMonth, MidMonth, NextMonth, DailyProRata, MidYear; default FullMonth |
| Date | yes | Date |

The list beside the form shows: Asset classes.

### Asset insurance and warranty

Who insures an asset, the policy and the sum insured with its expiry, and when the manufacturer's warranty ends (report M213 and the expiry alerts read them). Saving an asset again changes its details (a blank keeps what is there); Delete clears the asset's insurance and warranty details (the asset itself stays).

| Field | Required | Notes |
| --- | --- | --- |
| Asset number | yes | Text |
| Insurer | no | Text |
| Policy number | no | Text |
| Sum insured | no | Number |
| Insurance expires | no | Date |
| Warranty ends | no | Date |

The list beside the form shows: Insurance and warranty.

### Capitalise from a supplier invoice

The invoice's amount on the account it was coded to becomes the asset's cost (moved to the class's cost account when coded elsewhere); never more than is left.

| Field | Required | Notes |
| --- | --- | --- |
| Supplier invoice number | yes | Text |
| Supplier code (if the number repeats) | no | Text |
| Asset number | yes | Text |
| Description | yes | Text |
| Asset class | yes | Text |
| Cost (blank: all that is left) | no | Number |
| Account the invoice was coded to (blank: the class's cost account) | no | Text |
| Reclassification document number | no | Text |

The list beside the form shows: Assets capitalised from invoices.

### Move an asset to another cost centre

Moves the asset's cost and accumulated depreciation to another cost centre / department; future depreciation follows.

| Field | Required | Notes |
| --- | --- | --- |
| Asset number | yes | Text |
| Cost centre code | no | Text |
| Department code | no | Text |
| Date | yes | Date |
| Document number | yes | Text |

### Record asset usage

Units (hours, km, tonnes) a units-of-production asset was used for; the month's depreciation run charges them.

| Field | Required | Notes |
| --- | --- | --- |
| Asset number | yes | Text |
| Date | yes | Date |
| Units used | yes | Number |
| Reading reference | no | Text; default Meter |

## Asset counts

### Start an asset count

Lists every held asset the register places at the location (blank for everywhere) for verification.

| Field | Required | Notes |
| --- | --- | --- |
| Location | no | Text |
| Date | yes | Date |

The list beside the form shows: Open counts.

### Record an asset sighting

Records an asset (or unknown tag) seen during a count, where and in what condition.

| Field | Required | Notes |
| --- | --- | --- |
| Count number | yes | Text |
| Asset number / tag | yes | Text |
| Seen at | yes | Text |
| Condition | no | Text |

The list beside the form shows: Open counts.

### Close an asset count

Closes the count: anything not seen is missing. Optionally moves assets found elsewhere to where they were seen.

| Field | Required | Notes |
| --- | --- | --- |
| Count number | yes | Text |
| Update locations of assets found elsewhere | no | Flag; default True |

The list beside the form shows: Open counts.

## Maintenance

### Plan recurring maintenance

A task repeated every N months for an asset. Giving an existing plan id changes its task, interval and next due date (the asset stays); Delete stops the plan (its maintenance log is kept).

| Field | Required | Notes |
| --- | --- | --- |
| Plan id (blank: a new plan) | no | Number |
| Asset number | yes | Text |
| Task | yes | Text |
| Every (months) | yes | Number; default 6 |
| Next due | yes | Date |

The list beside the form shows: Maintenance plans.

### Complete planned maintenance

Logs the planned task as done and rolls the next due date forward.

| Field | Required | Notes |
| --- | --- | --- |
| Plan id (from the list) | yes | Number |
| Date | yes | Date |
| Cost | yes | Number; default 0 |
| Done by | no | Text |

The list beside the form shows: Maintenance plans.

## Capital projects

### Open a capital project (CWIP)

A project collecting cost in a capital work-in-progress account until capitalised.

| Field | Required | Notes |
| --- | --- | --- |
| Project code | yes | Text |
| Name | yes | Text |
| CWIP account | yes | Text |
| Budget | yes | Number |
| Opened | yes | Date |

The list beside the form shows: Capital projects.

### Add a cost to a capital project

Dr CWIP, Cr the funding account.

| Field | Required | Notes |
| --- | --- | --- |
| Project code | yes | Text |
| Date | yes | Date |
| Amount | yes | Number |
| Funding account | yes | Text; default 1100 |
| Description | yes | Text |

The list beside the form shows: Capital projects.

### Capitalise a capital project

Registers the asset at the project's whole cost, clearing CWIP.

| Field | Required | Notes |
| --- | --- | --- |
| Project code | yes | Text |
| New asset number | yes | Text |
| Asset description | yes | Text |
| Asset class | yes | Text |
| In use from | yes | Date |
| Useful life (months) | yes | Number; default 240 |
| Asset cost account | yes | Text; default 1600 |
| Accumulated depreciation account | yes | Text; default 1650 |
| Depreciation expense account | yes | Text; default 6000 |

The list beside the form shows: Capital projects.

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| FA-DISP | Asset Disposals | Disposed assets with proceeds, net book value at disposal, the gain or loss, and the journal - newest first. |
| FA-FCST | Depreciation Forecast | Projected monthly depreciation across active assets and the aggregate closing net book value. |
| FA-MAINT | Asset Maintenance Log | Maintenance and service events logged against assets, newest first, with the informational cost. |
| FA-REG | Fixed Asset Register | Assets with cost, accumulated depreciation and net book value. |
| FA207 | Depreciation Schedule (actual) | Depreciation charged in the period, by month and asset, from the posted depreciation runs. |
| FA208 | Asset Additions & Transfers | Assets acquired in the period, and changes of location or custodian recorded in the audit trail. |
| FA209 | Asset Movement Schedule (note) | Per asset class: cost and accumulated depreciation brought forward, additions, disposals, charge and carried forward, and the net book value. |
| FA210 | Asset Verification / Count Variance | Each verification in the period: every asset expected or seen, where the register has it and where it was found, its condition and result, with the net book value at risk. |
| FA211 | Capital Work in Progress | Each capital project: budget, cost brought forward, added and capitalised in the period, the CWIP balance carried forward and the share of budget spent. |
| FA212 | Fully Depreciated Assets Still in Use | Assets written down to residual value that are still held, with where they are and who holds them. |
| FA213 | Insurance & Warranty Expiry | Held assets whose insurance or warranty has lapsed or ends within the window, uninsured assets, and cover below net book value. |
| FA214 | Maintenance Schedule & History | Planned tasks with when they were last done and are next due (overdue flagged), then the maintenance logged in the period. |
