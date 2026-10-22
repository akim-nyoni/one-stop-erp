# Demo: Administration

Goal: show that the company, users and security, importing data and looking after the system work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.

## Company
1. Open **Create a company**. A new company with a head-office branch, held to the licence's company limit. Fill in: Company code, Name, Base currency, Time zone. Press OK and point out the result line and the list on the right.
2. Open **Set up a company for an industry**. The Industry Setup Wizard: a new company with the chart of accounts, tax codes, warehouses, an open fiscal year and starter roles for the industries chosen (they add up). Fill in: Company code, Name, Base currency, Industries (Core, Retail, Municipal, Housing, School, Construction, Hospitality, Butchery, BulkMaterials), Fiscal year starts, VAT rate %. Press OK and point out the result line and the list on the right.
3. Open **Add an industry to this company**. Adds an industry's accounts, taxes, warehouses, roles, item categories, POS terminal and report set to the current company, keeping everything already there. Fill in: Industry, VAT rate %. Press OK and point out the result line and the list on the right.

## Reports
4. Open **Run a report**. Choose a report, fill its options and view, print or export it.
5. Open **Design a report**. Change a report's layout or make a new one.
6. Open **Quick column layout**. Pick and order the columns of a simple listing.

## Users and security
7. Open **Create a user**. A login who must change the initial password at first sign-in (8+ characters, letters and digits, not the user name). Fill in: Login name, Display name. Press OK and point out the result line and the list on the right.
8. Open **Reset a password**. Sets a new password the user must change at next sign-in, and clears any lockout. Fill in: Login name, New password. Press OK and point out the result line and the list on the right.
9. Open **Activate or deactivate a user**. A deactivated user cannot sign in; the last system administrator stays active. Fill in: Login name. Press OK and point out the result line and the list on the right.

## Importing and exporting data
10. Open **Import data or opening balances**. Loads a CSV template (header row; dates yyyy-MM-dd). Fill in: What, CSV or Excel (.xlsx, first sheet) file path, Opening date, Migration suspense account, Budget type (budget). Press OK and point out the result line and the list on the right.
11. Open **Import templates and data export**. Template: saves the CSV template for an import (its header row and an example row) to fill in. Fill in: Action, What, Save to (.csv path). Press OK and point out the result line and the list on the right.
12. Open **Quick search**. Finds items, customers, suppliers, leads and accounts by name or code, forgiving a mistyped letter or two. Fill in: Search for. Press OK and point out the result line and the list on the right.

## Looking after the system
13. Open **Back up or restore the database**. Takes a backup now (pg_dump, SHA-256 recorded, copied off-site when configured), checks one, or restores a checked backup into a NEW database (type RESTORE <name> to confirm) - the live database is never overwritten. Fill in: Action. Press OK and point out the result line and the list on the right.
14. Open **Licence**. Shows the licence; installs a new licence or the vendor's revocation list (paste the text or give the file path); or gives this machine's activation request code to send to the vendor. Fill in: Action. Press OK and point out the result line and the list on the right.
15. Open **Scheduled jobs**. Jobs that run by themselves, when they last ran and what they did.

## Reports to end on
- DB251 Executive Dashboard: Year to date at the date: revenue, expenses, profit, cash, debtors, creditors, stock, working capital and the cash cycle. Show the drill-through by clicking a document or account.
- DB252 Daily Flash Report: The day's sales, receipts and payments against the same day last week and the month to date, with cash at close. Show the drill-through by clicking a document or account.
- DB253 KPI Scorecard: This month against last: revenue, profit, gross margin, debtor, creditor and stock days, cash cycle - with the direction of travel. Show the drill-through by clicking a document or account.
