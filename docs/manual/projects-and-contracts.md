# Projects & Contracts

Job contracts, bills of quantities, site costs, subcontracts and certificates.

Works with: Contracts.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Contracts

### Open a job contract

A contract (job) for a customer: its value, budgeted cost, retention % on certificates and the revenue account. The WBS, BOQ, variations, certificates and subcontracts all hang off it. Capture a cost books a manual cost to one of its heads (purchases, stock, labour, plant, subcontract, overhead).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Open, Capture a cost; default Open |
| Contract number | yes | Text |
| Name | no | Text |
| Customer | no | Choice |
| Contract value | yes | Number; default 0 |
| Budgeted cost | yes | Number; default 0 |
| Retention % | yes | Number; default 0 |
| Revenue account | no | Text |
| Cost head (capture) | no | Text |
| Cost from | yes | One of: PurchaseInvoice, StockIssue, Labour, Plant, Subcontractor, Overhead, GoodsReceived; default PurchaseInvoice |
| Cost amount | yes | Number; default 0 |
| Reference | no | Text |
| Date | yes | Date |

The list beside the form shows: Job contracts.

### Job cost position

A job's actual cost (purchases, goods received for it, stock issues, labour, plant, subcontractors, overheads), what is still on order for it, and the forecast. Overhead rates add a percentage on a kind of direct cost (e.g. 35% on labour) as it is captured; 0% removes a rate.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Position, Set overhead rate; default Position |
| Contract number (position) | no | Text |
| Overhead charged on | yes | One of: PurchaseInvoice, StockIssue, Labour, Plant, Subcontractor, GoodsReceived; default PurchaseInvoice |
| Overhead % | no | Number |
| Overhead cost head | no | Text; default Overheads |

The list beside the form shows: Overhead rates.

### Contract revenue, losses and budget

Recognise a contract's revenue by stage of completion (cost to date over budgeted cost) against certified billing - the difference to the contract asset or liability; provide for a foreseeable loss (the whole expected loss at once, the provision trued up); or revise the budgeted cost with the reason.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Recognise revenue, Provide for loss, Revise budget; default Recognise revenue |
| Contract number | yes | Text |
| Date | yes | Date |
| Contract asset account (revenue) | no | Text |
| Contract liability account (revenue) | no | Text |
| Loss expense account (loss) | no | Text |
| Loss provision account (loss) | no | Text |
| Document number | no | Text |
| New budgeted cost (revise) | no | Number |
| Why the budget changes (revise) | no | Text |

The list beside the form shows: Job contracts.

### Client certificates, advance and retention

Certify progress (cumulative work, approved variation work and materials on site; retention held and any client advance recovered at its percentage), receive a client advance, record practical completion with the defects-liability period, and release retention - half after completion, the rest when the period ends.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Certify, Receive advance, Practical completion, Release retention; default Certify |
| Contract number | yes | Text |
| Document number | no | Text |
| Date | yes | Date |
| Work done to date | no | Number |
| Variation work to date | no | Number |
| Materials on site | no | Number |
| Retention receivable account | no | Text |
| Amount (advance / release) | no | Number |
| Advance recovery % | no | Number |
| Advance liability account | no | Text |
| Bank account | no | Text |
| Defects-liability months | no | Number |

The list beside the form shows: Contract positions.

### Submit a variation order

A change to the contract's scope, with its effect on value and budgeted cost.

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| VO number | yes | Text |
| What changes | yes | Text |
| Value impact | yes | Number |
| Cost impact | yes | Number |
| Date | yes | Date |

The list beside the form shows: Variation orders.

### Decide a variation order

Approve (moves the contract value and budget) or reject a submitted variation.

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| VO number | yes | Text |
| Decision | yes | One of: Approve, Reject; default Approve |
| Date | yes | Date |
| Note | no | Text |

The list beside the form shows: Variation orders.

### Print a payment certificate

Prints the payment certificate in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF.

| Field | Required | Notes |
| --- | --- | --- |
| Certificate number | yes | Text |
| PDF file path (when a layout is set) | no | Text |

## Planning

### Work breakdown (WBS)

Phases (no cost head) group activities; an activity carries a budget and the cost head its costs are captured under. Codes are dotted: 1, 1.2, 1.2.3.

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| WBS code | yes | Text |
| Name | yes | Text |
| Budget (activities) | no | Number |
| Cost head (activities) | no | Text |

The list beside the form shows: Work breakdown structure.

### Give an estimate to complete

The site's estimate of what an activity still needs beyond actual and committed cost. Leave blank to fall back to the budget left.

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| Activity code | yes | Text |
| Estimate to complete | no | Number |
| Date | yes | Date |

The list beside the form shows: Work breakdown structure.

### Cost to complete by WBS

Budget, actual, committed (open subcontracts), estimate to complete and forecast final cost per phase and activity.

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |

The list beside the form shows: Work breakdown structure.

### Bill of quantities

A bill-of-quantities line on a contract.

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| BOQ item ref | yes | Text |
| Work | yes | Text |
| Unit | yes | Text; default m3 |
| BOQ quantity | yes | Number |
| Rate | yes | Number |

### Import a BOQ

A CSV file (or lines separated by ';'): item, description, unit, quantity, rate, cost head (optional); a header row and quoted descriptions are fine. Untick 'Import' to preview the errors first; any error stops the import. 'By cost head' shows the BOQ budget against measured value and cost.

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| CSV file path | no | Text |
| Or lines: ITEM,description,unit,qty,rate,head; … | no | Text |
| Import (otherwise preview) | no | Flag; default False |
| By cost head instead | no | Flag; default False |

### Measure a BOQ line

Records the quantity measured to date (never earlier than the last measurement).

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| BOQ item ref | yes | Text |
| Measured to date | yes | Number |
| Date | yes | Date |

## Site costs

### Book timesheet hours to a job

Hours × rate into the job's labour head (at most 24 hours a day per employee).

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| Employee | yes | Choice |
| Date | yes | Date |
| Hours | yes | Number |
| Cost per hour | yes | Number |
| Cost head | no | Text; default Labour |

### Issue materials to a job

Stock out of the warehouse to the contract's WIP account, into the job's materials head.

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| Product | yes | Choice |
| Warehouse / site store | yes | Text |
| Quantity | yes | Number |
| Date | yes | Date |
| Contract WIP account | yes | Text |

### Log plant hours / running cost

Hours on a job charge it at the plant's hire rate; leave the contract blank to log a running cost instead.

| Field | Required | Notes |
| --- | --- | --- |
| Plant code | yes | Text |
| Contract number (hours) | no | Text |
| Date | yes | Date |
| Hours | no | Number |
| Running cost | no | Number |
| Running cost description | no | Text |

## Subcontracts

### Let a subcontract

A package let to a supplier on a contract, with the retention held on its certificates.

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| Subcontractor (supplier code) | yes | Text |
| Subcontract reference | yes | Text |
| Package | yes | Text |
| Value | yes | Number |
| Retention % | yes | Number; default 10 |
| Job cost head (default Subcontract) | no | Text |

The list beside the form shows: Subcontracts.

### Pay a subcontract advance

A mobilisation advance (Dr advance account, Cr bank), recovered as a share of each later certificate until cleared.

| Field | Required | Notes |
| --- | --- | --- |
| Subcontract reference | yes | Text |
| Advance | yes | Number |
| Recovered per certificate % | yes | Number; default 20 |
| Date | yes | Date |
| Bank account | yes | Text; default 1100 |
| Advances to subcontractors account | yes | Text |
| Payment document number | yes | Text |

The list beside the form shows: Subcontracts.

### Certify a subcontract

Invoices the subcontractor for the work since the last certificate, net of retention.

| Field | Required | Notes |
| --- | --- | --- |
| Subcontract reference | yes | Text |
| Certificate number | yes | Text |
| Date | yes | Date |
| Work done to date | yes | Number |
| Job cost account | yes | Text; default 5000 |
| Retention payable account | yes | Text |

The list beside the form shows: Subcontracts.

### Release subcontract retention

Invoices held retention to the subcontractor (never more than held).

| Field | Required | Notes |
| --- | --- | --- |
| Subcontract reference | yes | Text |
| Amount | yes | Number |
| Date | yes | Date |
| Retention payable account | yes | Text |
| Release document number | yes | Text |

The list beside the form shows: Subcontracts.

### Set subcontract withholding tax

Withholding tax deducted from each certificate unless the subcontractor's tax clearance is valid. Leave blank to stop withholding.

| Field | Required | Notes |
| --- | --- | --- |
| Subcontract reference | yes | Text |
| Withholding tax code | no | Text |

The list beside the form shows: Subcontracts.

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| JC186 | Job Cost Report by Cost Head | Per contract and cost head: the budget (from the work breakdown), what is committed on open orders, the cost to date with where it came from, and what is left of the budget after both. |
| JC189 | Cost to Complete & Forecast Final Cost | Per contract: value, budget, cost to date, % complete by certification, forecast final cost and the variance to budget. |
| JC190 | WIP & Retention Report | Retention held on each contract, what has been released, practical completion and when the defects liability period ends, and what may be released at the date (half at completion, the rest when the defects period ends). |
| JC191 | Variation Order Register | Every variation submitted in the period: contract, status, decision, and the value and cost impact (approved impacts move the contract). |
| JC192 | Subcontractor Ledger, Certificates & Retention | Each subcontract's certificates in the period (gross, retention, payable) and retention released, with value, certified to date and retention held. |
| JC193 | Labour & Timesheet Cost by Job | Hours and labour cost booked to each contract in the period, per employee and cost head. |
| JC194 | Plant Utilisation & Hire Recovery | Per item of plant over the period: hours worked against hours available, hire recovered from jobs, running costs, cost per hour and the net recovery. |
| JC195 | Materials Issued to Job | Stock issued to each contract in the period: date, item, quantity and cost, from the stock movements referenced to the contract. |
| JC196 | BOQ vs Measured Quantities | Each bill-of-quantities line: billed quantity, measured to date, the over / under measure and the value of work measured. |
| JC197 | Contract Cash Flow | Per contract over the period: net certified (after retention) against costs incurred, and the net cash position. |
| JOB-IPC | Progress Certificates | Interim payment certificates with gross, retention and net payable for a period. |
| JOB-PROF | Job Profitability | Contract value, budget, actual cost, certified-to-date and percentage complete per contract. |
| JOB-WIP | Job WIP | Per contract: revenue recognised on cost-to-cost completion versus certified-to-date, giving the WIP position (contract asset if positive, liability if negative). |
