# Weighbridge

Weighing loads in and out, invoicing loads and concrete tests.

Works with: Weighbridge.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Weighing

### First weigh (open a ticket)

Outbound loads weigh empty first (leave the weight blank to use the vehicle's stored tare); inbound loads weigh loaded first. A keyed weight needs a reason and another person's authority.

| Field | Required | Notes |
| --- | --- | --- |
| Direction | yes | One of: Outbound, Inbound; default Outbound |
| Vehicle registration | yes | Text |
| Product (item code) | yes | Text |
| m³ per tonne | yes | Number; default 0.6 |
| Date | yes | Date |
| Weight kg (blank = stored tare) | no | Number |
| Customer | no | Choice |
| Supplier | no | Choice |
| Driver | no | Text |
| Delivery site | no | Text |
| Stockpile (warehouse code) | no | Text |
| Haulage distance km | no | Number |
| Concrete batch run | no | Text |
| Weight keyed by hand | no | Flag; default False |
| Why keyed | no | Text |
| Authorised by (user name) | no | Text |
| Sales order (outbound) or purchase order (inbound) | no | Text |
| Read the weight from the scale | no | Flag; default False |

The list beside the form shows: Tickets still on the bridge.

### Second weigh (complete a ticket)

Completes the ticket: net weight, haulage by zone and - for an outbound load from a stockpile - the stock issue.

| Field | Required | Notes |
| --- | --- | --- |
| Ticket number | yes | Text |
| Weight kg | no | Number |
| Deduction kg (moisture etc.) | yes | Number; default 0 |
| Weight keyed by hand | no | Flag; default False |
| Why keyed | no | Text |
| Authorised by (user name) | no | Text |
| Read the weight from the scale | no | Flag; default False |
| Show the printed ticket | no | Flag; default False |

The list beside the form shows: Tickets still on the bridge.

### Invoice weighbridge loads

Bills a customer for every completed outbound load up to the invoice date that has not been invoiced: net tonnes at the price, plus each load's haulage, with tax on top. Each ticket is marked with the invoice so it is never billed twice.

| Field | Required | Notes |
| --- | --- | --- |
| Customer | yes | Choice |
| Invoice number | yes | Text |
| Date | yes | Date |
| Price per tonne (blank: the order's or the price book's) | no | Number |
| Sales account | yes | Text; default 4000 |
| Haulage revenue account (blank = sales) | no | Text |
| Tax code | no | Text |
| Authorised by (user name; needed when the price differs from the order's or price book's) | no | Text |

The list beside the form shows: Completed loads not yet invoiced.

### Concrete slump and strength tests

Records a slump test (mm, passes within the tolerance of the target) or a 7- or 28-day cube strength (MPa, passes at or above the specified strength) against a batch run; Show lists a run's tests.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Record, Show; default Record |
| Batch run number | yes | Text |
| Sample / cube reference | no | Text |
| Test | yes | One of: Slump, Cube7Day, Cube28Day; default Slump |
| Result (mm or MPa) | yes | Number; default 0 |
| Target slump or specified strength | yes | Number; default 0 |
| Slump tolerance (mm) | yes | Number; default 25 |
| Tested by | no | Text |
| Date | yes | Date |

The list beside the form shows: Failed tests (last 90 days).

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| WB198 | Weighbridge Ticket | Each ticket in the period: vehicle, driver, customer or supplier, product, time in and out, gross, tare, deduction and net. |
| WB199 | Daily Weighbridge Register & Tonnage Summary | Completed loads per day, product, customer, vehicle and driver: loads, tonnes and m³. |
| WB200 | Vehicle Movement & Turnaround | Per vehicle: loads, average and longest time between weighs, open tickets and overloads against its legal maximum. |
| WB201 | Deliveries by Customer / Site | Outbound loads per customer and site: loads, tonnes, m³ and the haulage charged. |
| WB202 | Manual Weight Override Report | Tickets where a weight was keyed by hand: operator, authorising supervisor, reason and the net recorded. |
| WB203 | Stockpile Reconciliation | Per weighed product and stockpile: opening, produced, received, sold over the weighbridge, adjusted and closing tonnes. |
| WB204 | Concrete Batch Report | Each batch run in the period: mix, m³ produced and delivered over the weighbridge, materials cost and the variance to the recipe. |
| WB205 | Haulage Charge Analysis | Haulage charged in the period per zone: loads, tonnes, kilometres and the charge, with the rate achieved per tonne-km. |
