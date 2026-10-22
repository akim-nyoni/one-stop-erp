# Schools

Students, classes, fees, bursaries and wallets.

Works with: Sales & Schools.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Students

### Students, hostels and routes

Admit a student under their guardian's account (the customer billed); set up a hostel or transport route with its capacity and fee per term; place a student in a hostel and on a route (blank takes them out) within capacity. Admitting an existing admission number changes that student's name and gender; saving an existing hostel or route code changes its capacity and fee.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Admit, Hostel, Route, Place, Sponsor; default Admit |
| Admission number | no | Text |
| Name | no | Text |
| Grade | no | Text |
| Guardian (customer code) | no | Text |
| Sponsor (customer code billed instead of the guardian; blank: none) | no | Text |
| Gender | yes | One of: Unspecified, Female, Male; default Unspecified |
| Hostel / route code | no | Text |
| Capacity | yes | Number; default 0 |
| Fee per term | yes | Number; default 0 |
| Revenue account | no | Text |
| Hostel (place) | no | Text |
| Route (place) | no | Text |
| Date | yes | Date |

The list beside the form shows: Students.

### Staff children and restricted students

Staff discount: the percentage off compulsory fees for staff children while the parent is employed (the better of it and the sibling discount). Staff parent links a student to the employee (blank removes). Do not disclose withholds the student's name from reports for anyone without the restricted-records permission.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Staff discount, Staff parent, Do not disclose, Disclose; default Staff discount |
| Staff discount % | no | Number |
| Admission number | no | Text |
| Employee number (staff parent) | no | Text |
| Why withheld | no | Text |

### A student's fees and balance

One student's fees out of the family's account: every fee invoice, credit and debit note raised for the student (to the guardian or the sponsor), what has been paid on each and what is still owed.

| Field | Required | Notes |
| --- | --- | --- |
| Admission number | yes | Text |

### Top up / spend a student wallet

A top-up is paid into the bank; a spend (tuck shop) is limited to the balance and the student's daily limit.

| Field | Required | Notes |
| --- | --- | --- |
| Admission number | yes | Text |
| Action | yes | One of: Top up, Spend; default Top up |
| Date | yes | Date |
| Amount | yes | Number |
| Wallet liability account | yes | Text |
| Bank account (top up) | no | Text |
| Sales account (spend) | no | Text |
| What was bought | no | Text; default Tuck shop |

The list beside the form shows: Wallet balances.

## Fees

### Fee structures and term billing

A fee structure per grade and term with its items (compulsory ones billed to every student in the grade, optional ones only to those taking them); Bill term invoices every enrolled student of the grade under the prefix (a rerun skips those already billed); Bill student invoices one. Saving an existing structure code changes its name (grade, term and account stay); Delete takes it out of use, leaving the invoices already raised.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Save structure, Add item, Bill term, Bill student; default Save structure |
| Fee structure code | yes | Text |
| Name / item description | no | Text |
| Grade | no | Text |
| Term (e.g. 2026-T1) | no | Text |
| Revenue account | no | Text |
| Item amount | yes | Number; default 0 |
| Compulsory item | no | Flag; default True |
| Invoice prefix (bill term) / number (bill student) | no | Text |
| Admission number (bill student) | no | Text |
| Date | yes | Date |

The list beside the form shows: Fee structures.

### Bursaries

Request a bursary (percentage or value off compulsory fees) - it reduces fees only once approved by someone else with the bursary approval right, or through the approval workflow when one is defined for bursaries. Decide approves or rejects a pending request by its reference. A bursary may expire: fees billed after its last day get no bursary. Expiring lists bursaries ending within the given days, for renewal.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Request, Approve, Reject, Expiring; default Request |
| Admission number (request) | no | Text |
| Type | yes | One of: Percentage, Value; default Percentage |
| Percentage or value (request) | no | Number |
| Reason / decision note | no | Text |
| Reference (decide) | no | Text |
| Last day it applies | no | Date |
| Expiring within (days) | yes | Number; default 60 |
| Date | yes | Date |

The list beside the form shows: Bursary requests.

### Sibling discount

A family's children (sharing a guardian, in admission order) get this percentage off from the given child on, after any bursary. 0 removes the rule.

| Field | Required | Notes |
| --- | --- | --- |
| From child (2 = second) | yes | Number; default 2 |
| Discount % | yes | Number |

The list beside the form shows: Sibling discounts.

### Send fee reminders

Emails every guardian or sponsor paying for an enrolled student who owes at least the minimum their statement; those without an email address are counted for a printed letter.

| Field | Required | Notes |
| --- | --- | --- |
| As at | yes | Date |
| Email template | yes | Text; default FEES |
| Smallest balance | yes | Number; default 1 |

### Print a fee invoice

Prints the fee invoice in the standard business document layout, or through the company's own layout (System Manager › Document layouts) as a PDF.

| Field | Required | Notes |
| --- | --- | --- |
| Invoice number | yes | Text |
| PDF file path (when a layout is set) | no | Text |

## School year

### Academic years and terms

Years and their terms (no overlaps). A term's code is what fee structures name as their term: saving it gives them its dates for pro-rata billing and deferred income.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Save year, Save term, Which term is it?; default Save year |
| Year code | yes | Text |
| Term code (e.g. 2026-T1) | no | Text |
| Term name | no | Text |
| Term number | yes | Number; default 1 |
| First day | yes | Date |
| Last day | yes | Date |

The list beside the form shows: Terms.

### Term dates, optional fees and leavers

Term dates (with pro rata, a joiner pays from the admission date and a leaver is credited the unused part of the term invoice); a student taking or dropping an optional item (lunches, clubs, trips - billed with the term, not discounted); a student's admission date; withdrawing a student.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Term dates, Take option, Drop option, Admission date, Withdraw; default Term dates |
| Fee structure code | no | Text |
| Admission number | no | Text |
| Optional fee item id (from the list) | no | Number |
| Term start / admission date | no | Date |
| Term end | no | Date |
| Pro rata joiners and leavers | no | Flag; default True |
| Term invoice prefix (withdraw) | no | Text |
| Left on (withdraw) | yes | Date |

The list beside the form shows: Optional fee items.

### Classes and streams

Classes by grade and stream with capacity and the grade they move up to (blank: final year). Place a student within capacity; at year end, promote the school once. Saving an existing grade and stream changes that class (and puts a closed one back in use); Delete closes a class nobody is enrolled in.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Save class, Place a student, Promote the year; default Save class |
| Grade / form | no | Text |
| Stream | no | Text |
| Class name | no | Text |
| Capacity | no | Number |
| Next grade | no | Text |
| Admission number | no | Text |
| Year ending (promote) | no | Text |

The list beside the form shows: Class rolls.

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| SC173 | Student Fee Statement | Per student (and the guardian or sponsor who pays): fees billed by term, payments allocated, arrears brought forward and carried, aged; one student or the whole school. |
| SC175 | Fee Billing Run Summary | Fees billed in the period by term and grade: students billed and the amount. |
| SC176 | Fee Arrears by Grade | Unpaid fee invoices as at the date per student, aged, grouped by grade. |
| SC177 | Fee Collection Report | Receipts from guardians in the period, with the students they pay for. |
| SC178 | Bursary & Discount Report | Students on bursaries with type and value, and what that is worth against their grade's fees. |
| SC179 | Sponsor Account Statement | Per sponsor: the students they sponsor, fees billed to them and receipts in the period, and the balance they owe at the end of it. |
| SC180 | Enrolment & Headcount | Per grade: enrolled, boarders, day scholars, bursary holders, students no longer enrolled, and the enrolled split by gender. |
| SC181 | Deferred Fee Income Schedule | Fees billed in advance per invoice: billed, recognised to date as the term runs, and still deferred. |
| SC182 | Student Balances Listing | Every student with guardian, grade, enrolment and what is owed on their fee invoices at the date. |
| SC183 | Transport Route Billing & Utilisation | Per route: seats, enrolled riders, utilisation, the fee and what was billed for transport in the period. |
| SC184 | Boarding / Hostel Occupancy & Billing | Per hostel: beds, enrolled boarders, occupancy, the fee and what was billed for boarding in the period. |
| SC185 | Tuck Shop Sales & Student Account Spend | Per student over the period: wallet balance brought forward, top-ups, tuck-shop spend and the balance carried forward. |
| SCH-STU | Student Register | Enrolled students with grade, boarding status and bursary. |
