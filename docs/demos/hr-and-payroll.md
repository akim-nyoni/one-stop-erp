# Demo: HR & Payroll

Goal: show that employees, leave, time and attendance, and the payroll work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Employees
1. Open **Add or change an employee**. Engage an employee, or update one (blank fields stay as they are); an existing employee number always updates that employee. Fill in: Step, Employee number, Engagement date, Employment, Paid by, Paid, Marital status. Press OK and point out the result line and the list on the right.
2. Open **Employee contacts, photo and dependants**. The rest of the employee master: addresses, phone, job title, notice period and workers' compensation class (blank stays, a space clears); the photograph (a JPEG, PNG or BMP file; blank removes it); and beneficiaries (with their share, all shares at most 100%) and medical-aid dependants. Fill in: Step, Dependant is. Press OK and point out the result line and the list on the right.
3. Open **File an employee document**. A contract, permit, licence or certificate on the employee's file. Fill in: Employee, Document type. Press OK and point out the result line and the list on the right.

## Records
4. Open **Record a disciplinary action**. The offence and the sanction issued. Fill in: Employee, Date, Offence, Sanction. Press OK and point out the result line and the list on the right.
5. Open **Record training**. A completed course, its cost and when it must be renewed. Fill in: Employee, Course, Completed on, Cost. Press OK and point out the result line and the list on the right.
6. Open **Record an appraisal**. Rating 1–5 and the next appraisal date. Fill in: Employee, Date, Rating (1–5), Next due. Press OK and point out the result line and the list on the right.

## Organisation
7. Open **Posts and establishment**. Approved posts and annual cost per post for a department and grade. Fill in: Department, Grade, Approved posts, Annual cost per post. Press OK and point out the result line and the list on the right.
8. Open **Salary scales**. A grade's monthly salary at a notch, from a date. Fill in: Grade, Notch (1 is the bottom), Monthly salary, Effective from. Press OK and point out the result line and the list on the right.
9. Open **Run the annual increment**. Moves every active employee on a scaled grade up one notch, to that notch's salary, unless at the top or incremented within the year. Fill in: Date. Press OK and point out the result line and the list on the right.

## Time and attendance
10. Open **Record an attendance day**. One record per employee per day: status, clock times, lateness and overtime. Fill in: Employee, Date, Status, Minutes late, Overtime hours, Overtime multiplier. Press OK and point out the result line and the list on the right.
11. Open **Approve overtime**. Overtime only reaches payroll once approved. Fill in: Attendance id (from the list). Press OK and point out the result line and the list on the right.
12. Open **Shift rosters**. Shift: a code, times (an end before the start runs past midnight), grace minutes and allowance per shift. Fill in: Step, Grace minutes, Allowance per shift, From / day, To. Press OK and point out the result line and the list on the right.

## Leave
13. Open **Take leave**. Records leave taken over a period (never more than the balance). Fill in: Employee, Leave type, From, To, Days. Press OK and point out the result line and the list on the right.
14. Open **Leave calendar and recall**. Shows who is on leave between two dates (optionally one department); cancels leave not yet begun, or cuts it short when someone is recalled - the days not taken go back to the balance (pro rata unless given). Fill in: Step, From, To. Press OK and point out the result line and the list on the right.
15. Open **Encash leave**. Pays out leave days at a daily rate. Fill in: Employee, Leave type, Date, Days, Daily rate. Press OK and point out the result line and the list on the right.

## Running the payroll
16. Open **Enter overtime, allowances and deductions**. Overtime hours (units on a rate-per-unit element) or a once-off amount for one employee and period. Fill in: Employee, Element code, Period. Press OK and point out the result line and the list on the right.
17. Open **Import pay variables**. Lines 'employee, element, units, amount' separated by ';'. Fill in: Period, Lines: EMP,ELEMENT,units,amount; …. Press OK and point out the result line and the list on the right.
18. Open **1. Calculate the payroll**. Step 1 of the payroll: calculates a pay group's payroll without posting it, for someone else to approve. Fill in: Period, Date, Net pay control account. Press OK and point out the result line and the list on the right.

## Loans, claims and provisions
19. Open **Staff loans**. Grants a staff loan (paid out, recovered through payroll by the instalment) or records a repayment made outside payroll. Fill in: Step, Principal / repayment, Monthly instalment (grant), Bank / funding account, Document number, Date. Press OK and point out the result line and the list on the right.
20. Open **Expense claims**. A staff member's claim for out-of-pocket expenses (lines DESCRIPTION:ACCOUNT:amount; …), approved by a user other than the claimant, rejected, or reimbursed from a bank or cash account. Fill in: Step, Claim reference, Date. Press OK and point out the result line and the list on the right.
21. Open **Raise third-party invoices**. Raises a run's AP invoices for the tax authority, schemes and garnishee creditors that have a supplier - clearing the payroll liabilities into payables. Fill in: Payroll run. Press OK and point out the result line and the list on the right.

## Payroll setup
22. Open **Pay elements and tax tables**. Create a pay element or edit one (every field but its code and type, entered in full as for a new one; creating an existing code edits it), assign one to (or end one for) an employee, delete one no employee is on (past payslips keep it), create a tax table - bands as 'from-to:rate' separated by ';', the last open-ended, e.g. Fill in: Step, Element type, Calculation, Tax table effective from, Recovers (deduction), Rounding. Press OK and point out the result line and the list on the right.
23. Open **Tax credits**. Credits taken off the tax the table works out, never below nil: a fixed amount a month (say an elderly credit from 65) or a percentage of a pay element (say half the medical-aid deduction), for everyone or only the employees given it. Fill in: Step, Credit code, Basis, Monthly amount (fixed), Percent (of an element). Press OK and point out the result line and the list on the right.
24. Open **Set the tax year**. The month the tax year starts in (1 = January; 3 for a March–February year). Fill in: First month of the tax year (1–12). Press OK and point out the result line and the list on the right.

## Reports to end on
- EXP-CLAIMS Expense Claims: Employee expense claims with reference, date, total, status and reimbursement journal. Show the drill-through by clicking a document or account.
- HR-EMP Employee Register: All employees with number, name, department, cost centre, status and basic salary. Show the drill-through by clicking a document or account.
- PAY-DED Payroll Deductions: Deductions aggregated by element across payslips in the period, with the number of employees affected. Show the drill-through by clicking a document or account.
