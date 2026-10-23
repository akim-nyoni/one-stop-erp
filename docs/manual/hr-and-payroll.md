# HR & Payroll

Employees, leave, time and attendance, and the payroll.

Works with: HR & Attendance.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Employees

### Add or change an employee

Engage an employee, or update one (blank fields stay as they are); an existing employee number always updates that employee. Bank details are set at engagement; later changes go through 'Bank detail change' and a second person's approval. Where payroll needs approval ('Set payroll approval'), the bank details given at engagement and any change of basic salary or payment method also wait there for another user; a salary or payment change dated later takes effect from the first payroll run paid on or after that date ('Pay change history' keeps every one). Delete removes only an employee never paid, with no loans or leave on record; anyone else leaves through 'Termination and final pay'.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Engage, Update; default Engage |
| Employee number | yes | Text |
| Full name | no | Text |
| Basic salary | no | Number |
| Engagement date | yes | Date |
| Employment | yes | One of: Permanent, Contract, Casual, FixedTerm, Seasonal, Intern, Consultant; default Permanent |
| Department code (3) | no | Text |
| Cost centre code (3) | no | Text |
| Pay group | no | Text |
| Grade | no | Text |
| National ID | no | Text |
| Tax number | no | Text |
| Contract end | no | Date |
| Paid by | yes | One of: BankTransfer, Cash, MobileMoney, Cheque; default BankTransfer |
| Bank (engage only) | no | Text |
| Bank branch (engage only) | no | Text |
| Account number (engage only) | no | Text |
| Mobile money (engage only) | no | Text |
| Email | no | Text |
| Paid | yes | One of: Monthly, Fortnightly, Weekly; default Monthly |
| Works at branch (code) | no | Text |
| Charged to project | no | Text |
| Charged to fund | no | Text |
| Reports to (employee number) | no | Text |
| Marital status | yes | One of: NotRecorded, Single, Married, Divorced, Widowed; default NotRecorded |
| Nationality | no | Text |
| Social security number | no | Text |
| Trade union | no | Text |
| Next of kin | no | Text |
| Next of kin's phone | no | Text |
| Next of kin is (relationship) | no | Text |
| Housing tenancy number (update; rent recovered from pay; '-' clears) | no | Text |
| Salary / payment-method change effective from (update; blank: today) | no | Date |

The list beside the form shows: Employees.

### Employee contacts, photo and dependants

The rest of the employee master: addresses, phone, job title, notice period and workers' compensation class (blank stays, a space clears); the photograph (a JPEG, PNG or BMP file; blank removes it); and beneficiaries (with their share, all shares at most 100%) and medical-aid dependants.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Details, Photo, Add dependant, Remove dependant; default Details |
| Employee number | no | Text |
| Physical address | no | Text |
| Postal address | no | Text |
| Phone | no | Text |
| Job title / position | no | Text |
| Notice period (days) | no | Number |
| Workers' compensation class | no | Text |
| Photograph file | no | Text |
| Dependant is | yes | One of: Beneficiary, MedicalAidDependant, Both; default Beneficiary |
| Dependant's name | no | Text |
| Relationship | no | Text |
| Born | no | Date |
| ID number | no | Text |
| Beneficiary share % | no | Number |
| Medical-aid number | no | Text |
| Dependant id | no | Number |

The list beside the form shows: Beneficiaries and medical-aid dependants.

### File an employee document

A contract, permit, licence or certificate on the employee's file.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Document type | yes | Text |
| Reference | no | Text |
| Expires on | no | Date |

The list beside the form shows: Documents expiring in 90 days.

### Bank detail change

Ask for a change of an employee's bank or mobile-money details; a different person approves or rejects it before it takes effect (blank fields are left as they are). Net-pay splits, and - where payroll needs approval - salary and payment-method changes made on the employee record, wait in the same list and are approved or rejected here by someone other than whoever asked (and not by the employee themselves).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Request, Approve, Reject; default Request |
| Employee number | no | Text |
| Bank | no | Text |
| Branch | no | Text |
| Account number | no | Text |
| Mobile money number | no | Text |
| Request id (approve / reject) | no | Number |
| Note | no | Text |

The list beside the form shows: Bank and pay changes awaiting approval.

### Pay change history

Every change of an employee's basic salary or payment method: who asked and when, from what to what, from when, who approved or rejected it, and when it was applied. Pick an employee for their history; the list shows the company's.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |

The list beside the form shows: Salary and payment-method changes.

### Set an employee's pay group

Moves an employee to a pay group (MONTHLY, WEEKLY, CASUAL…); blank returns them to the default payroll.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Pay group | no | Text |

### Appoint to act

An employee acting in a higher grade for a time is paid ACTINGALLOWANCE (that grade's scale less their basic) through an element whose formula is ACTINGALLOWANCE.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Acting grade | yes | Text |
| Notch | yes | Number |
| From | yes | Date |
| To | no | Date |

The list beside the form shows: Acting appointments.

### An employee leaves (final pay)

Preview the final pay (last month pro rata, notice, leave, gratuity, severance by the termination type; PAYE on ordinary pay; on benefits above the tax-free amount, the terminal tax table's bands when one is given, else the terminal rate; loans recovered as far as the pay allows), then terminate - the final pay posts in one journal and later runs leave the employee out. Clear checklist items by id; once all are cleared, release the final pay from the bank (any part the slip shows in another currency from that currency's account, at the final pay's rate); print the certificate.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Preview, Terminate, Clear item, Release final pay, Final pay slip, Certificate of service; default Preview |
| Employee number | yes | Text |
| Type | yes | One of: Resignation, Dismissal, Retrenchment, Retirement, Death, EndOfContract, Absconding; default Resignation |
| Last day | yes | Date |
| Reason | no | Text |
| Notice (months) | yes | Number; default 1 |
| Working days a month | yes | Number; default 22 |
| Gratuity weeks a year | yes | Number; default 2 |
| Severance weeks a year | yes | Number; default 4 |
| Terminal benefits tax % | yes | Number; default 0 |
| Salary expense account | yes | Text; default 6100 |
| Terminal benefits account | yes | Text; default 6100 |
| PAYE liability account | yes | Text; default 2400 |
| Net pay account | yes | Text; default 2420 |
| Checklist item id | no | Number |
| Bank account paying (release) | no | Text |
| Foreign currency 1 (release) | no | Text |
| Paying account for it (release) | no | Text |
| Foreign currency 2 (release) | no | Text |
| Paying account for that (release) | no | Text |
| Year's bonus (paid pro rata) | no | Number |
| Overpayment to recover | no | Number |
| Recoveries account (property, overpayments) | no | Text |
| Tax-free terminal benefits | no | Number |
| Terminal tax table (bands, instead of the rate) | no | Text |

The list beside the form shows: Clearance checklist.

### HR expiry alerts

What lapses for active employees - work permits, contract ends, probation, filed documents and training certificates - within the days given; Notify sends HR (users whose roles cover employee records) one notification listing them. The scheduled task hr.expiry-alerts does this daily for 30 days ahead.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: List, Notify HR; default List |
| Within (days) | yes | Number; default 60 |
| Date | yes | Date |

The list beside the form shows: Expiring within 60 days.

## Records

### Record a disciplinary action

The offence and the sanction issued.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Date | yes | Date |
| Offence | yes | Text |
| Sanction | yes | One of: VerbalWarning, WrittenWarning, FinalWarning, Suspension, Dismissal; default VerbalWarning |

### Record training

A completed course, its cost and when it must be renewed.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Course | yes | Text |
| Completed on | yes | Date |
| Expires on | no | Date |
| Provider | no | Text |
| Cost | yes | Number; default 0 |

### Record an appraisal

Rating 1–5 and the next appraisal date.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Date | yes | Date |
| Rating (1–5) | yes | Number; default 3 |
| Next due | yes | Date |

### Grievances and property issued

Lodge a grievance and follow it (investigating, resolved or withdrawn with the outcome); issue company property to an employee and record its return - anything still out is listed on the leaver's clearance. Medical records are kept under Medical records (restricted).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Lodge grievance, Move grievance, Issue property, Return property; default Lodge grievance |
| Employee number | no | Text |
| Subject / item | no | Text |
| Details | no | Text |
| Against (grievance) | no | Text |
| Grievance is now | yes | One of: Investigating, Resolved, Withdrawn; default Investigating |
| Outcome / return condition | no | Text |
| Serial or asset number | no | Text |
| Value | yes | Number; default 0 |
| Grievance or issue id | no | Number |
| Date | yes | Date |

The list beside the form shows: Open grievances and property out.

### Medical records (restricted)

An employee's medical records - fitness assessments, illness, injury, disability - seen and kept only by users with the Hr.Medical permission; notes are encrypted at rest.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Show, Record; default Show |
| Employee number | yes | Text |
| Record is | yes | One of: Fitness, Illness, Injury, Disability, Other; default Fitness |
| Note | no | Text |
| Fit for work | yes | One of: Not assessed, Fit, Not fit; default Not assessed |
| Review on | no | Date |
| Date | yes | Date |

## Organisation

### Posts and establishment

Approved posts and annual cost per post for a department and grade.

| Field | Required | Notes |
| --- | --- | --- |
| Department | yes | Text |
| Grade | yes | Text |
| Approved posts | yes | Number |
| Annual cost per post | yes | Number |

The list beside the form shows: Posts filled and vacant.

### Salary scales

A grade's monthly salary at a notch, from a date. Once a company keeps posts, staff can only be engaged or moved into a vacant approved post.

| Field | Required | Notes |
| --- | --- | --- |
| Grade | yes | Text |
| Notch (1 is the bottom) | yes | Number |
| Monthly salary | yes | Number |
| Effective from | yes | Date |

The list beside the form shows: Salary scales.

### Run the annual increment

Moves every active employee on a scaled grade up one notch, to that notch's salary, unless at the top or incremented within the year.

| Field | Required | Notes |
| --- | --- | --- |
| Date | yes | Date |

The list beside the form shows: Staff on scales.

## Time and attendance

### Record an attendance day

One record per employee per day: status, clock times, lateness and overtime.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Date | yes | Date |
| Status | yes | One of: Present, Absent, Sick; default Present |
| Clock in (HH:mm) | no | Text |
| Clock out (HH:mm) | no | Text |
| Minutes late | yes | Number; default 0 |
| Overtime hours | yes | Number; default 0 |
| Overtime multiplier | yes | Number; default 1.5 |

The list beside the form shows: Overtime awaiting approval.

### Approve overtime

Overtime only reaches payroll once approved.

| Field | Required | Notes |
| --- | --- | --- |
| Attendance id (from the list) | yes | Number |

The list beside the form shows: Overtime awaiting approval.

### Shift rosters

Shift: a code, times (an end before the start runs past midnight), grace minutes and allowance per shift. Pattern: a cycle like "D,D,N,N,OFF,OFF". Assign puts an employee on a pattern from an anchor date. Generate writes the roster for a period (days changed by hand are kept); Set day changes one day by hand. Allowances sets each employee's shift allowance for a pay period from the rostered shifts they worked. Attendance lateness is measured against the rostered start.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Shift, Pattern, Assign, Generate, Set day, Allowances; default Shift |
| Shift / pattern code (or OFF) | no | Text |
| Shift name | no | Text |
| Start (HH:mm) | no | Text |
| End (HH:mm) | no | Text |
| Grace minutes | yes | Number; default 0 |
| Allowance per shift | yes | Number; default 0 |
| Pattern steps | no | Text |
| Employee | no | Choice |
| From / day | yes | Date |
| To | yes | Date |
| Pay period (allowances) | no | Text |
| Allowance element | no | Text |

The list beside the form shows: Roster (next 14 days).

### Import time-clock punches

Reads the clock's export (badge,date time per line; CSV or Excel): each employee's first punch in and last punch out make the day, late against the rostered shift, with the hours beyond the shift as overtime awaiting approval. Days already recorded are left alone.

| Field | Required | Notes |
| --- | --- | --- |
| Export file (CSV or .xlsx) | yes | Text |
| Overtime rate | yes | Number; default 1.5 |

The list beside the form shows: Leave balances.

## Leave

### Take leave

Records leave taken over a period (never more than the balance).

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Leave type | yes | Text; default ANNUAL |
| From | yes | Date |
| To | yes | Date |
| Days | yes | Number |

The list beside the form shows: Leave balances.

### Leave calendar and recall

Shows who is on leave between two dates (optionally one department); cancels leave not yet begun, or cuts it short when someone is recalled - the days not taken go back to the balance (pro rata unless given).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Calendar, Cancel or recall; default Calendar |
| From | yes | Date |
| To | yes | Date |
| Department (calendar) | no | Text |
| Leave record id (cancel) | no | Number |
| Back at work on (blank: cancel outright) | no | Date |
| Days given back (blank: pro rata) | no | Number |

The list beside the form shows: Leave this month.

### Encash leave

Pays out leave days at a daily rate.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Leave type | yes | Text; default ANNUAL |
| Date | yes | Date |
| Days | yes | Number |
| Daily rate | yes | Number |

The list beside the form shows: Leave balances.

### Leave policies

Saving an existing leave type changes its policy (and puts a retired one back in use); Delete stops it accruing. Per leave type: yearly days (service bands "5:24;10:30" raise them after so many completed years), earned monthly or granted at the leave-year start, joiners pro rata, and a carry-over cap applied as the leave year turns (the excess is forfeited). A balance ceiling stops accrual; carried days can lapse so many months into the year; leave can be taken in advance down to so many days; sick tiers "11:50;21:0" pay so much from the nth sick day of the year. Run month accrues for a month (the scheduled task does the month just ended); Apply sick pay sets the period's deductions. Advance leave pay pays the leave starting in the month ahead into the pay period (LEAVEPAY) and recovers it in the recovery period (LEAVEADV). Send attendance to payroll puts the month's approved overtime on OVERTIME / OVERTIME2 and deducts unpaid days (absences no leave covers, and the leave type as unpaid leave) on the element.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Save policy, Run month, Apply sick pay, Send attendance to payroll, Advance leave pay, Credit time off in lieu; default Save policy |
| Leave type | yes | Text; default ANNUAL |
| Days a year | no | Number |
| Earned monthly | no | Flag; default True |
| Leave year starts (month) | yes | Number; default 1 |
| Carry-over cap (days, blank: none) | no | Number |
| Joiners pro rata | no | Flag; default True |
| Service bands (years:days;...) | no | Text |
| Balance ceiling (days, blank: none) | no | Number |
| Carried days lapse after (months, blank: never) | no | Number |
| May go negative by (days) | yes | Number; default 0 |
| Sick tiers (from day:pay %;...) | no | Text |
| Month to run | yes | Date |
| Pay period (sick pay) | no | Text |
| Deduction element (sick pay) | no | Text |
| Working days a month (sick pay) | yes | Number; default 21.67 |
| Recovery period (advance) | no | Text |
| Days by grade GRADE=days; … (policy) | no | Text |
| Most of a department away at once (policy) | no | Number |
| Rolling sick cycle in months (policy; blank: the leave year) | no | Number |
| Unpaid leave: loan instalments held back while on it (policy) | no | Flag; default False |
| Employee number (time off in lieu) | no | Text |
| Overtime hours (time off in lieu) | no | Number |
| Hours in a working day | yes | Number; default 8 |
| Hours off per hour worked | yes | Number; default 1 |

The list beside the form shows: Leave policies.

### Accrue leave

Adds days to an employee's leave balance.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Leave type | yes | Text; default ANNUAL |
| Days | yes | Number |

The list beside the form shows: Leave balances.

### Leave provision (IAS 19)

Trues the provision for leave owed to staff up (or down) to its value at the date: each employee's untaken days at their own daily rate of basic (annual ÷ 260), or one rate for everyone; only the movement posts. The scheduled task payroll.leave-provision does this at each month end.

| Field | Required | Notes |
| --- | --- | --- |
| Date | yes | Date |
| One daily rate for everyone (blank: each employee's own) | no | Number |
| Leave expense account | yes | Text; default 6600 |
| Leave provision account | yes | Text; default 2460 |
| Document number | no | Text |

## Running the payroll

### Enter overtime, allowances and deductions

Overtime hours (units on a rate-per-unit element) or a once-off amount for one employee and period.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Element code | yes | Text |
| Period | yes | Text; default 2026-10 |
| Units | no | Number |
| Amount | no | Number |

### Import pay variables

Lines 'employee, element, units, amount' separated by ';'. Untick 'Import' to preview the errors first; any error stops the import.

| Field | Required | Notes |
| --- | --- | --- |
| Period | yes | Text; default 2026-10 |
| Lines: EMP,ELEMENT,units,amount; … | yes | Text |
| Import (otherwise preview) | no | Flag; default False |

### 1. Calculate the payroll

Step 1 of the payroll: calculates a pay group's payroll without posting it, for someone else to approve. The run number given here (the next in sequence when left blank) is the one picked to approve, commit and pay it. Recalculating the same run replaces the earlier draft.

| Field | Required | Notes |
| --- | --- | --- |
| Pay group (blank = default) | no | Text |
| Period | yes | Text; default 2026-10 |
| Date | yes | Date |
| Run number (blank: next in sequence) | no | Text |
| Net pay control account | yes | Text; default 2420 |

The list beside the form shows: Payroll drafts.

### 2. Approve the payroll

Step 2: approves a calculated payroll run, picked by its run number (not your own; a large movement needs two different approvers).

| Field | Required | Notes |
| --- | --- | --- |
| Payroll run to approve | yes | Choice |

The list beside the form shows: Payroll drafts.

### 3. Commit the payroll

Step 3: saves the approved run's payslips and posts its journal - refused if anything changed since approval. Then pay it under step 4 with the same run number.

| Field | Required | Notes |
| --- | --- | --- |
| Approved payroll run to commit | yes | Choice |

The list beside the form shows: Payroll drafts.

### 4. Pay and close the payroll

After commit: Pay posts the run's net pay from the account for each pay method (bank transfers, cash, mobile money, cheques) - earnings paid in another currency go out of that currency's account at the pay date's rate - and clears the net pay control; Close closes the run once its net pay control is clear; Stage shows where a run stands.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Pay, Close, Stage; default Pay |
| Payroll run | yes | Choice |
| Bank account for transfers | no | Text; default 1100 |
| Cash account | no | Text |
| Mobile-money float account | no | Text |
| Bank account cheques are drawn on | no | Text |
| Foreign currency 1 (pay) | no | Text |
| Paying account for it (pay) | no | Text |
| Foreign currency 2 (pay) | no | Text |
| Paying account for that (pay) | no | Text |
| Date | yes | Date |

The list beside the form shows: Payroll runs.

### Payslips

Saves one employee's payslip for a period as a (password-protected) PDF, or emails every payslip of a payroll run to the employees, listing who has no email address or payslip password. Payslip settings choose what opens the PDF (national ID, date of birth, employee number, a password the employee sets, or none) and the report whose layout prints it (the company's own design in the report designer, e.g. a copy of PR74; blank for the standard text payslip). Set password keeps a password for an employee who cannot set their own in self-service.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Save PDF, Email a run, Payslip settings, Set password; default Save PDF |
| Employee number (PDF / password) | no | Text |
| Period, e.g. 2026-09 (PDF) | no | Text |
| Save to file path (PDF) | no | Text |
| Payroll run number (email) | no | Text |
| Opens the PDF with (settings) | yes | One of: National ID, Date of birth, Employee number, Password the employee sets, No password; default National ID |
| Payslip layout report code (settings; blank: standard text) | no | Text |
| Payslip password (set password; 8+ characters) | no | Text |

### Supplementary payroll run

Corrects a committed period with extra (or negative) element amounts - a visible run with its own payslips and journal.

| Field | Required | Notes |
| --- | --- | --- |
| Committed period | yes | Text |
| Date | yes | Date |
| Run document number | yes | Text |
| Reason | yes | Text |
| Net pay control account | yes | Text; default 2420 |
| Lines: EMPLOYEE:ELEMENT:amount; … | yes | Text |
| Approved by (user name; required where payroll needs approval) | no | Text |

The list beside the form shows: Payroll runs.

### Reverse a payroll run

A formally approved reversal: reversing journal and negative payslips. The approver cannot be whoever ran the payroll.

| Field | Required | Notes |
| --- | --- | --- |
| Payroll run | yes | Choice |
| Date | yes | Date |
| Reason | yes | Text |
| Approved by (user name) | yes | Text |

The list beside the form shows: Payroll runs.

### Backpay for a salary change

Recalculates every period paid since the effective date at the new basic salary and pays the difference. Untick 'Pay now' to preview.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| New basic salary | yes | Number |
| Effective from | yes | Date |
| Pay now | no | Flag; default False |
| Pay in period | yes | Text |
| Pay date | yes | Date |
| Run document number | no | Text |
| Reason | no | Text |
| Net pay control account | yes | Text; default 2420 |
| Approved by (user name; required where payroll needs approval) | no | Text |

The list beside the form shows: Payroll runs.

### Split net pay

Asks to send part of an employee's net pay to another bank account or wallet - a fixed amount, or a percentage (e.g. 40% to mobile money); the rest goes to their main account. A split can be paid in another currency: its share is worked out in base (a fixed amount is a base amount) and converted at the pay date's rate. Remove asks to take a split off. Like any bank-detail change, a different person approves it under Bank detail change before pay goes anywhere new.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Paid by | yes | One of: BankTransfer, Cash, MobileMoney, Cheque; default BankTransfer |
| Account or wallet number | yes | Text |
| Fixed amount | no | Number |
| Percent of net pay | no | Number |
| Bank of that account (blank: the employee's bank) | no | Text |
| Branch of that account | no | Text |
| Paid in currency (blank: base) | no | Text |
| Remove this split | no | Flag; default False |

The list beside the form shows: Pay splits.

### Run the payroll in one step (no approval)

Calculates and commits a pay group's payroll for the period (blank group = the default payroll): payslips and one balanced journal. A period is paid once per group.

| Field | Required | Notes |
| --- | --- | --- |
| Pay group (blank = default) | no | Text |
| Period | yes | Text; default 2026-10 |
| Date | yes | Date |
| Run number (blank: next in sequence) | no | Text |
| Net pay control account | yes | Text; default 2420 |

The list beside the form shows: Payroll runs.

## Loans, claims and provisions

### Staff loans

Grants a staff loan (paid out, recovered through payroll by the instalment) or records a repayment made outside payroll.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Grant, Repayment; default Grant |
| Employee number (grant) | no | Text |
| Loan reference | no | Text |
| Principal / repayment | yes | Number |
| Monthly instalment (grant) | yes | Number; default 0 |
| Loan receivable account (grant) | no | Text |
| Bank / funding account | yes | Text |
| Document number | yes | Text |
| Date | yes | Date |

The list beside the form shows: Staff loans.

### Expense claims

A staff member's claim for out-of-pocket expenses (lines DESCRIPTION:ACCOUNT:amount; …), approved by a user other than the claimant, rejected, or reimbursed from a bank or cash account.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Approve, Reject, Reimburse; default Create |
| Claim reference | yes | Text |
| Employee number (create) | no | Text |
| Lines: description:ACCOUNT:amount; … (create) | no | Text |
| Paid from account (reimburse) | no | Text |
| Payment document number (reimburse) | no | Text |
| Date | yes | Date |

The list beside the form shows: Claims.

### Raise third-party invoices

Raises a run's AP invoices for the tax authority, schemes and garnishee creditors that have a supplier - clearing the payroll liabilities into payables.

| Field | Required | Notes |
| --- | --- | --- |
| Payroll run | yes | Choice |

The list beside the form shows: Payroll runs.

### Bonus and gratuity provisions

Trues up the 13th-cheque bonus provision (earned through the bonus year), the gratuity provision (days' pay per year of service, discounted to retirement when a discount rate is given) or the long-service award provision (milestones YEARS:months' pay; …, earned towards the next) to what active employees have earned to the date, posting the movement between the expense and the provision.

| Field | Required | Notes |
| --- | --- | --- |
| Provision | yes | One of: Bonus, Gratuity, LongService; default Bonus |
| Date | yes | Date |
| Bonus year starts | yes | Date |
| Months' pay a year (bonus) | yes | Number; default 1 |
| Days' pay per year of service (gratuity) | yes | Number; default 15 |
| Working days a month (gratuity) | yes | Number; default 26 |
| Expense account | yes | Text |
| Provision account | yes | Text |
| Document number | yes | Text |
| Long-service milestones YEARS:months' pay; … (e.g. 10:1; 20:2) | no | Text |
| Discount rate % (gratuity; 0: none) | yes | Number; default 0 |
| Salary growth % (gratuity) | yes | Number; default 0 |
| Retirement age (gratuity) | yes | Number; default 65 |

The list beside the form shows: Leave balances.

### Accrue unpaid salaries

Period end: accrues basic salary for everyone employed but not yet paid for the month, reversed on the 1st of next month.

| Field | Required | Notes |
| --- | --- | --- |
| Period end | yes | Date |
| Salary expense account | yes | Text; default 6100 |
| Accrued salaries account | yes | Text |
| Document number | yes | Text |

### Capitalise timesheet labour to WIP

Moves a contract's timesheet labour up to a date into its WIP account, each timesheet once.

| Field | Required | Notes |
| --- | --- | --- |
| Contract number | yes | Text |
| Up to | yes | Date |
| Contract WIP account | yes | Text |
| Payroll cost account credited | yes | Text; default 6100 |
| Document number | yes | Text |

### Decide self-service requests

Leave applications and payment-detail changes employees made through self-service. Approving takes the leave or applies the change; nobody decides their own.

| Field | Required | Notes |
| --- | --- | --- |
| Kind | yes | One of: Leave, Detail; default Leave |
| Request id (from the list) | yes | Number |
| Decision | yes | One of: Approve, Reject; default Approve |
| Note | no | Text |

The list beside the form shows: Requests awaiting HR.

## Payroll setup

### Pay elements and tax tables

Create a pay element or edit one (every field but its code and type, entered in full as for a new one; creating an existing code edits it), assign one to (or end one for) an employee, delete one no employee is on (past payslips keep it), create a tax table - bands as 'from-to:rate' separated by ';', the last open-ended, e.g. '0-100:0; 100-300:20; 300-:30' - or add the standard elements (BASIC, OVERTIME, HOUSING, TRANSPORT, PAYE on the tax table).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create element, Edit element, Set element options, Assign to employee, End for employee, Create tax table, Add standard elements, Preview formula; default Create element |
| Element / table code | no | Text |
| Element name | no | Text |
| Element type | yes | One of: Earning, Deduction, EmployerContribution, Memo, Provision; default Earning |
| Calculation | yes | One of: FixedAmount, PercentOfBasic, PercentOfGross, RatePerUnit, Formula, TaxTable, PercentOfGroup, TableLookup, BalanceDriven; default FixedAmount |
| Sequence | no | Number |
| Account (salaries expense for the standard set) | no | Text |
| PAYE liability account (standard set) | no | Text |
| Fixed / override amount | no | Number |
| Rate / override rate | no | Number |
| Units | no | Number |
| Formula | no | Text |
| Taxable | no | Flag; default False |
| Tax table code | no | Text |
| Employee | no | Choice |
| Tax table effective from | yes | Date |
| Tax bands | no | Text |
| Pensionable | no | Flag; default False |
| Subject to social security | no | Flag; default False |
| In the overtime base | no | Flag; default False |
| In the leave-pay base | no | Flag; default False |
| Adds to group | no | Text |
| Pro-rated for a part period | no | Flag; default False |
| Never goes into arrears (deduction) | no | Flag; default False |
| Counts towards terminal benefits | no | Flag; default False |
| Subject to union dues | no | Flag; default False |
| Balance to recover (assign; balance-driven element) | no | Number |
| Recovers (deduction) | yes | One of: None, StaffPurchases, HousingRent; default None |
| Taxable-benefit category (PR34), e.g. Housing | no | Text |
| Paid in currency (blank: base) | no | Text |
| Amounts keyed in the paid-in currency | no | Flag; default False |
| Cumulative tax (year to date) | no | Flag; default False |
| Tax per currency (split-currency pay) | no | Flag; default False |
| Annual payment (bonus tax spread) | no | Flag; default False |
| Statutory return line (PAYE, AIDS levy, social security, medical aid; None: by its name) | yes | One of: None, Paye, AidsLevy, SocialSecurity, MedicalAid; default None |
| Tax table currency (blank: base; one table per currency for split-currency tax) | no | Text |
| Percentage of group (percent-of-group) | no | Text |
| Floor | no | Number |
| Ceiling | no | Number |
| Round to (unit) | no | Number |
| Rounding | yes | One of: Nearest, Up, Down; default Nearest |
| Expense account (contribution / provision) | no | Text |
| Accounts by department: DEPT=ACCOUNT; … | no | Text |
| Lookup variable (table lookup; blank: BASIC) | no | Text |
| Lookup bands from-to:amount or rate%; … (table lookup) | no | Text |
| Preview values NAME=value; … | no | Text |

The list beside the form shows: Pay elements.

### Tax credits

Credits taken off the tax the table works out, never below nil: a fixed amount a month (say an elderly credit from 65) or a percentage of a pay element (say half the medical-aid deduction), for everyone or only the employees given it. Saving an existing code changes that credit (and puts it back in use); Delete stops it being given.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Save credit, Give to employee, Take from employee; default Save credit |
| Credit code | yes | Text |
| Name | no | Text |
| Basis | yes | One of: Fixed, PercentOfElement; default Fixed |
| Monthly amount (fixed) | yes | Number; default 0 |
| Percent (of an element) | yes | Number; default 0 |
| Pay element | no | Text |
| From age | no | Number |
| Applies to every employee | no | Flag; default False |
| Employee number (give / take) | no | Text |

The list beside the form shows: Tax credits.

### Set the tax year

The month the tax year starts in (1 = January; 3 for a March–February year). Year-to-date taxable pay, cumulative tax and the YTD openings follow it.

| Field | Required | Notes |
| --- | --- | --- |
| First month of the tax year (1–12) | yes | Number; default 1 |

The list beside the form shows: Deductions and their suspension priority.

### Set payroll approval

Whether payroll needs a second user: regular runs then go calculate, approve (by someone else), commit, and supplementary and backpay runs name their approver. Nobody ever approves a run that pays themselves.

| Field | Required | Notes |
| --- | --- | --- |
| Payroll needs approval by a second user | no | Flag; default False |
| Invoice the third parties (tax, funds, court orders) with every committed run | no | Flag; default False |

### Set net-pay protection

The minimum take-home (an amount and / or % of gross) and, optionally, one deduction's suspension priority (0 = never suspended; highest is suspended first).

| Field | Required | Notes |
| --- | --- | --- |
| Minimum take-home amount | no | Number |
| Minimum take-home % of gross | no | Number |
| Deduction code | no | Text |
| Its suspension priority | no | Number |

The list beside the form shows: Deductions and their suspension priority.

### Register a garnishee order

A court order deducted from pay through a deduction element until the total is settled.

| Field | Required | Notes |
| --- | --- | --- |
| Employee | yes | Choice |
| Order reference | yes | Text |
| Creditor | yes | Text |
| Priority | yes | Number; default 1 |
| Instalment per period | yes | Number |
| Total amount | yes | Number |
| Deduction element code | yes | Text |
| Issued on | yes | Date |

### Benefits in kind

Taxable benefits (a company car valued from the vehicle scale by engine size or cost, housing, school fees, medical): taxed each pay period they apply and shown on the payslip as memo lines. Also the vehicle scale's bands, the deemed interest rate cheap staff loans are taxed at, and a loan's own rate. Add benefit with a benefit id changes that benefit's engine size, cost and monthly value; Delete on the list ends a benefit today (it stays in history).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Add benefit, End benefit, Vehicle scale band, Deemed loan rate, Loan interest rate, Suspend loan deductions, Resume loan deductions, Loan interest income account; default Add benefit |
| Employee | no | Choice |
| Interest income account | no | Text |
| Benefit | yes | One of: MotorVehicle, Housing, SchoolFees, Medical, Other; default MotorVehicle |
| What it is (registration, property) | no | Text |
| Engine capacity (cc) | no | Number |
| Vehicle cost | no | Number |
| Monthly value (blank: from the vehicle scale) | no | Number |
| Scale measured on | yes | One of: EngineCapacity, Cost; default EngineCapacity |
| Scale band from | no | Number |
| Scale band to (blank: open) | no | Number |
| Interest rate % a year | no | Number |
| Loan reference | no | Text |
| Benefit id (end) | no | Number |
| Date | yes | Date |

The list beside the form shows: Benefits.

### Payroll encryption

Salary, bank details, national ID and tax numbers are encrypted at rest. This shows what is still in plaintext (written before encryption) or under a retired key, and Encrypt now rewrites it with the active key (the scheduled task security.reencrypt-fields does the same).

| Field | Required | Notes |
| --- | --- | --- |
| Encrypt now | no | Flag; default False |

The list beside the form shows: Values not yet under the active key.

### Import legacy payroll (parallel run)

The old system's figures for a period, compared line by line in report PR8. Replaces an earlier import of the period.

| Field | Required | Notes |
| --- | --- | --- |
| Period | yes | Text |
| Lines: EMPLOYEE,ELEMENT,amount; … (NET for net pay) | yes | Text |

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| EXP-CLAIMS | Expense Claims | Employee expense claims with reference, date, total, status and reimbursement journal. |
| HR-EMP | Employee Register | All employees with number, name, department, cost centre, status and basic salary. |
| PAY-DED | Payroll Deductions | Deductions aggregated by element across payslips in the period, with the number of employees affected. |
| PAY-LEAVE | Leave Balances | Employee leave balances by type - days accrued, taken and available. |
| PAY-LOANS | Employee Loans | Outstanding employee loans - principal, balance, installment and status. |
| PAY-SUM | Payroll Summary | Gross, deductions and net pay per employee for a period. |
| PR10 | Backpay & Retrospective Adjustments | Backpay paid in the period: per employee, the period corrected and each element's paid, due and difference. |
| PR11 | Bank Transfer (EFT) Schedule | Net pay for the period to employees paid by bank transfer (held salaries excluded), with bank, branch and account. |
| PR12 | Cash Pay Denomination Breakdown | Net pay of the employees paid in cash, broken into the base currency's notes and coins, by pay point (branch, else department): each envelope, then the notes and coins the pay point draws from the bank. |
| PR13 | Mobile Money Payment Schedule | Net pay for the period to employees paid by mobile money (held salaries excluded), with the wallet. |
| PR14 | Split-Pay Report | Employees whose net pay is split across accounts or methods: each fixed part and what is left for the primary account from the latest net pay in the period. |
| PR15 | Held Salaries | Employees whose pay is held back, why, and the net pay held for the period. |
| PR16 | Net Pay Control Reconciliation | Per pay month: gross, deductions and net from the payslips against the net pay credited in the payroll journals. |
| PR17 | Earnings Analysis | Earnings in the period by department and element. |
| PR18 | Deductions Analysis | Each deduction over the period: how many employees had it, the total, the average and the largest, by department. |
| PR19 | Third-Party Remittance Schedules | Per scheme and member for the period: the employee's deduction and the employer's contribution - the schedule each provider needs. |
| PR2 | Payroll Summary by Element | Totals for the period per pay group by earning, deduction and employer contribution, with how many employees each. |
| PR20 | Deductions vs Remittances | Per scheme and period: what the payroll deducted and contributed against what was paid over, with the gap. |
| PR21 | Garnishee & Court Order Register | Every court order by employee and priority: creditor, instalment, total ordered, deducted through payroll to the date and the balance still to collect. |
| PR22 | Loan & Advance Register and Balances | Every employee loan and advance: granted, principal, instalment, repaid, instalments left, status and the balance outstanding. |
| PR23 | Staff Purchase Deductions Reconciliation | Per employee: staff-account purchases at the till against the staff-purchase deductions from pay, with the balance still to recover. |
| PR24 | Housing Rent Deduction Reconciliation | Per employee tenant: rent billed, rent deducted from pay and receipted to the tenancy account, the account balance, and any deduction not receipted. |
| PR25 | Overtime Analysis | Overtime in the period by department, employee and multiplier: hours, approved and unapproved hours, and the cost at the basic hourly rate. |
| PR26 | Allowance Analysis | Earnings other than basic salary in the period, per employee and allowance. |
| PR27 | PAYE Remittance Return | Per month and currency the earnings were paid in: employees, taxable earnings, PAYE by the tax table in force and as deducted with any difference, and the AIDS levy - in base, and in the currency itself at the pay date's rate. |
| PR28 | Annual Tax Certificates | Per employee for the tax year: ID and tax numbers, gross, taxable, pension, medical aid, social security (employee and employer), AIDS levy and PAYE - the certificate (P2 / ITF16) data. |
| PR29 | Tax Reconciliation | Per employee over the period: taxable earnings, PAYE deducted and the effective rate. |
| PR3 | Payroll Journal | The payroll postings of the period by account, with debits and credits. |
| PR30 | Social Security Contribution Schedule | Social security members with the period's employee and employer contributions. |
| PR31 | Pension Fund Contribution Schedule | Pension fund members with the period's employee and employer contributions. |
| PR32 | Medical Aid Contribution Schedule | Medical aid members with the period's employee and employer contributions. |
| PR33 | Workers' Compensation & Skills Levy Schedule | Workers' compensation and skills levy contributions for the period by member. |
| PR34 | Taxable Benefits Schedule | Benefits in kind paid in the period per employee: category, value and the PAYE they add at the table in force. |
| PR35 | Multi-Currency Tax Split | Split-currency working paper: per employee and period, each currency's taxable earnings and share, and the tax attributable to it (own currency table, or the base table apportioned), with the tax deducted apportioned. |
| PR36 | Tax Table in Force | The PAYE bands that apply at the date, with the tax at the top of each band. |
| PR37 | Leave Balances | Each current employee's leave by department and type: the year's entitlement, accrued, taken, carried forward, carried days about to lapse, and the balance. |
| PR38 | Leave Liability (IAS 19) | Leave days owed at the date per employee and type, valued at the daily rate of basic salary (annual ÷ 260). |
| PR39 | Leave Taken Analysis | Leave taken in the period by type, department and month: employees and days. |
| PR4 | Pre-Payroll Validation | Active employees whose records would stop a clean payment or return: missing ID, tax number, bank or wallet details, date of birth. |
| PR40 | Absence, Lateness & Sick Leave Pattern | Per employee over the period: absent days, late days and minutes, sick days (Mondays / Fridays) and where they stand on sick pay over the last 12 months. |
| PR41 | Leave Encashment Report | Leave paid out in the period instead of taken: employee, type, days and amount. |
| PR42 | Timesheet Hours & Labour Cost Allocated to Jobs | Per employee over the period: hours and cost booked to jobs against gross pay, and the pay left unallocated. |
| PR43 | Attendance Exception Report | Days in the period with no clock-in or clock-out, unapproved overtime, or hours over the daily maximum. |
| PR44 | Employee Master Listing | Every employee by department: grade, contract type, status, engagement, job title and pay group. |
| PR45 | Headcount Reconciliation | Per department over the period: opening headcount, starters, leavers and closing. |
| PR46 | Starters & Leavers | Employees engaged or leaving in the period, with the date and the reason for leaving. |
| PR47 | Establishment vs Actual | Per department and grade at the date: approved posts, filled, vacant and over establishment, with the annual saving on vacant posts. |
| PR48 | Grade & Gender Distribution | Active headcount per grade with the gender split and the basic salary range. |
| PR49 | Salary Change Audit | Every change to an employee's basic salary in the period, with who changed it. |
| PR5 | Payroll Variance / Movement | Each employee's net pay this pay month against last, with starters, leavers and changes over 10% flagged. |
| PR50 | Employee Bank Detail Change Audit | Every change to how or where an employee is paid, with who changed it (account numbers masked). |
| PR51 | Contract, Probation & Permit Expiry | Fixed-term contracts, probations and work permits ending within 90 days of the date, or already passed. |
| PR52 | Termination & Final Settlement | Leavers in the period with their final settlement computed: notice, leave, gratuity, severance (retrenchment), recoveries, tax on terminal benefits and the net. |
| PR53 | Clearance Checklist Outstanding | Leavers by the date who still hold company assets, owe on loans or have leave not yet settled. |
| PR54 | Gratuity & Long Service Provision | Per employee in service: completed years, the gratuity earned to date and its value discounted from normal retirement (IAS 19 other long-term benefits). |
| PR55 | Employee Documents Expiring / Missing | Employees in service whose documents have expired or expire within the window, or who lack a required document type. |
| PR56 | Disciplinary & Warning Register | Disciplinary records with the sanction, when warnings lapse and whether they are still live at the date. |
| PR57 | Training & Qualification Register; Appraisal Due | Training and qualifications held with renewal status, then each employee's last appraisal and when the next is due. |
| PR58 | Total Cost to Company | Per employee over the period: gross pay plus employer contributions, and the annualised cost. |
| PR59 | Labour Cost by Department & Cost Centre | Gross pay plus employer contributions in the period by department and cost centre. |
| PR6 | Payroll Calculation Trace | Every payslip line in the period with how its element is calculated and the employee's basic salary. |
| PR60 | Payroll Cost vs Budget | Year to date on each payroll expense account: budget, actual and the variance. |
| PR61 | Payroll as % of Revenue | Month by month: payroll cost (gross + employer contributions) against revenue. |
| PR62 | Overtime & Casual Labour vs Permanent Cost | Per department over the period: pay to permanent and contract staff, to casuals, and overtime, with casual plus overtime as a share of the permanent cost. |
| PR63 | Payroll Accrual Schedule | Employees in service at the period end not yet paid for that month (including salaries on hold), with the basic salary to accrue. |
| PR64 | Payroll GL Reconciliation | Per element account: what the payslips say against what the ledger holds for the period. |
| PR65 | Payroll Audit Trail | Every recorded change in the period to employees, elements, assignments, loans, leave and payslips. |
| PR66 | Segregation of Duties Violations - Payroll | Users who processed a payroll run in the period after changing employee pay or payment details, and anyone who processed a run that paid themselves. |
| PR67 | Payroll Report Access & Export Log | Every run of a payroll report in the period - who, what, format and copy. |
| PR68 | Ghost Worker Investigation Pack | Active employees sharing a bank account, wallet or national ID with another, with no ID, or with no leave in over a year. |
| PR69 | Payslip Reprint Log | Payslips printed more than once: each print after the first with when and by whom. |
| PR7 | Payroll Run Control Register | Every payroll run in the period: employees, gross, deductions, net, the journal and who ran it. |
| PR70 | Year-to-Date Payroll Register | Per employee for the year to the end of the period: gross, taxable, deductions, employer contributions and net. |
| PR71 | Net-Pay Protection & Deduction Arrears | Deductions suspended to keep net pay at the minimum take-home, and arrears recovered later: attempted, deducted, why, and the arrears still carried forward. |
| PR72 | Variable Input Register | Overtime, absences and once-off earnings and deductions keyed or imported for each period, and what each was paid at. |
| PR73 | Third-Party Invoices Raised | The AP invoices raised for the tax authority, schemes and garnishee creditors from payroll runs, with what is still outstanding on each. |
| PR74 | Payslip (designable) | Every payslip line in the period - employee, section, element and amount with the slip's gross, deductions and net - as the data source for a company's own payslip layout in the report designer (one employee, or all). |
| PR75 | Payroll Register by Currency | Per run and currency: the gross earnings paid in it (base and in the currency at the run's pay date rate), PAYE and other deductions falling on it, and the net pay paid in it as the settlement pays it (one run, or every run in the period). |
| PR8 | Parallel Run Comparison | Go-live parallel run: One4All's payslips against the imported legacy payroll, per period, employee and element (NET for net pay). |
| PR9 | Supplementary Runs & Reversal Register | Every supplementary, backpay and reversal run in the period, and every run reversed: reason, approval and journals. |
