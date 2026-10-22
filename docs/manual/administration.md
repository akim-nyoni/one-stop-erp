# Administration

The company, users and security, importing data and looking after the system.

Works with: System Manager, Documents, Approvals.

Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.

## Company

### Create a company

A new company with a head-office branch, held to the licence's company limit. An existing code changes that company's name and time zone (its base currency stays: its books are kept in it). Companies are kept rather than deleted.

| Field | Required | Notes |
| --- | --- | --- |
| Company code | yes | Text |
| Name | yes | Text |
| Base currency | yes | Text; default USD |
| Time zone | yes | Text; default UTC |

The list beside the form shows: Companies.

### Set up a company for an industry

The Industry Setup Wizard: a new company with the chart of accounts, tax codes, warehouses, an open fiscal year and starter roles for the industries chosen (they add up).

| Field | Required | Notes |
| --- | --- | --- |
| Company code | yes | Text |
| Name | yes | Text |
| Base currency | yes | Text; default USD |
| Industries (Core, Retail, Municipal, Housing, School, Construction, Hospitality, Butchery, BulkMaterials) | yes | Text; default Retail |
| Fiscal year starts | yes | Date |
| VAT rate % | yes | Number; default 15 |
| Add demonstration data | no | Flag; default False |

The list beside the form shows: Companies.

### Add an industry to this company

Adds an industry's accounts, taxes, warehouses, roles, item categories, POS terminal and report set to the current company, keeping everything already there.

| Field | Required | Notes |
| --- | --- | --- |
| Industry | yes | One of: Core, Retail, Municipal, Housing, School, Construction, Hospitality, Butchery, BulkMaterials; default Core |
| VAT rate % | yes | Number; default 15 |
| Add demonstration data | no | Flag; default False |

The list beside the form shows: Chart of accounts.

### Load sample data for testing

Fills a company with realistic sample records so every screen has something to show while you try the system: 8 products with stock, 10 customers with invoices (some overdue) and receipts, 5 suppliers with invoices and a payment, 8 employees on the payroll with a PAYE table, 8 rated properties with water meters and readings, a housing scheme with 12 stands and 8 applicants, a bank account, a cashier station and 3 assets. All codes start with SMP-. Tick "Create a new DEMO company" to set one up first (every industry, standard chart of accounts, fiscal year) and load into it; otherwise it loads into this company.

| Field | Required | Notes |
| --- | --- | --- |
| Create a new DEMO company first | no | Flag; default True |
| New company code | no | Text; default DEMO |
| New company name | no | Text; default Demo Council & Trading |
| Base currency (new company) | no | Text; default USD |
| Fiscal year starts (new company) | yes | Date |

The list beside the form shows: Companies.

### Add a branch

A branch or site of the current company. An existing code changes that branch's name (a deleted branch with the code is brought back). The head office cannot be deleted.

| Field | Required | Notes |
| --- | --- | --- |
| Code | yes | Text |
| Name | yes | Text |

The list beside the form shows: Branches.

### Set the company logo

A PNG or JPEG (up to 512 KB) that report layouts show wherever they place the Logo (an image element with expression CompanyLogo). Leave the path blank to remove it.

| Field | Required | Notes |
| --- | --- | --- |
| Picture file path | no | Text |

The list beside the form shows: Company.

### Company email and SMS settings

The company's own email delivery (its SMTP server, or Microsoft 365 through Microsoft Graph with an app registration allowed Mail.Send) and its SMS provider, used instead of the server's settings. Passwords, client secrets and API keys are stored encrypted and never shown; leave them blank to keep the stored ones. Show: what is set.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Show, Email, SMS; default Show |
| Email provider | yes | One of: Default, Smtp, MicrosoftGraph; default Default |
| Sender address / Graph mailbox | no | Text |
| Sender name | no | Text |
| SMTP host | no | Text |
| SMTP port | yes | Number; default 587 |
| SMTP uses TLS | no | Flag; default True |
| SMTP login | no | Text |
| SMTP password (blank: keep) | no | Text |
| Graph tenant id | no | Text |
| Graph client id | no | Text |
| Graph client secret (blank: keep) | no | Text |
| SMS endpoint (blank: the server's) | no | Text |
| SMS body template with {to} {body} {from} | no | Text |
| SMS auth header | no | Text |
| SMS auth value (blank: keep) | no | Text |
| SMS sender id | no | Text |

The list beside the form shows: Import templates.

### Message templates

The email, SMS and WhatsApp templates bulk sends use (statements STATEMENT, fee statements FEES, reminders REMINDER, bills BILL), with {{Field}} merge fields. Save: creates or rewords a template (\n in the body starts a new line). Preview: renders it with sample values "Name=Thandi;Account=C001" and lists fields still blank. Seed standard: adds the standard templates this company is missing without touching reworded ones. An existing code, channel and language rewords that template; templates are kept rather than deleted (sent messages name them).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Save, Preview, Seed standard, List; default Save |
| Template code | no | Text |
| Channel | yes | One of: Email, Sms, WhatsApp, InApp; default Email |
| Language | no | Text; default en |
| Document type (save) | no | Text |
| Subject (email) | no | Text |
| Body (save; \n for a new line) | no | Text |
| Sample values (preview), Field=value; … | no | Text |

The list beside the form shows: Message templates.

### Message batches

Every bulk send (statements, bills, reminders, fee statements, payslips) with how many messages are queued, sent and failed. Status: one batch's failures by recipient. Despatch now: sends everything queued (retrying failures up to the attempts given).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: List, Status, Despatch now; default List |
| Batch id (status) | no | Number |
| Attempts before a message is failed (despatch) | yes | Number; default 3 |

The list beside the form shows: Message batches.

### Language and terminology

The company's locale (screen language where translated, and the date and number formats of its reports - e.g. en-ZW, pt-MZ) and its own words for standard terms (Stand → Erf) on screens and report labels. Sign in again to see the screens change. A term already replaced is changed by saving it again; Delete puts the standard word back.

| Field | Required | Notes |
| --- | --- | --- |
| Locale (blank: keep; '-' for English / invariant) | no | Text |
| Standard word | no | Text |
| The company's word (blank removes it) | no | Text |

The list beside the form shows: The company's terms.

### Attachment rules

Scans, contracts and delivery notes on a record (journal, customer, supplier, ledger document, purchase order, asset, employee, property, student, contract): PDF, images, Word / Excel, CSV, text or e-mail up to 10 MB. Each file is checksummed and checked on opening; removing one keeps the file with the reason, and files on posted journals and ledger documents cannot be removed.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: List, Attach, Save a copy, Remove; default List |
| Record type | yes | One of: Journal, Customer, Supplier, LedgerDocument, PurchaseOrder, FixedAsset, Employee, Property, Student, Contract, WorkflowDocument, AccountingVoucher; default Journal |
| Record number / code | no | Text |
| File path (attach) or folder (save a copy) | no | Text |
| Description / removal reason | no | Text |
| Attachment id (save / remove) | no | Number |

The list beside the form shows: Recent attachments.

## Reports

### Run a report

Choose a report, fill its options and view, print or export it.

### Design a report

Change a report's layout or make a new one.

### Quick column layout

Pick and order the columns of a simple listing.

### Report house style

The company's house style for every report in one click: the font and size for standard text, the colour of report titles, the colour of column-heading strips, the colour of lines, a footer line on every page, and whether the logo is placed top-right on reports that do not place it themselves. Colours are #RRGGBB. A report's own choices stay; Clear returns every report to its own design.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Apply, Clear; default Apply |
| Font family (e.g. Calibri) | no | Text |
| Standard text size (pt) | no | Number |
| Title colour | no | Text |
| Column-heading background | no | Text |
| Column-heading text colour | no | Text |
| Line colour | no | Text |
| Footer line on every page | no | Text |
| Place the logo top-right | no | Flag; default False |

The list beside the form shows: House style.

### Report version history

Every saved version of a company's report layout: list them, see what changed between two versions (field by field), or roll back to an earlier one (saved as a new version, so nothing is lost).

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: History, Compare, Roll back; default History |
| Report code | yes | Text |
| Earlier version (compare) / version to restore | no | Number |
| Later version (compare) | no | Number |

The list beside the form shows: Saved report versions.

### Email a report on a schedule

A report emailed every time the report-subscriptions job runs: to a list of addresses, or burst by a field to each customer's or supplier's contacts (or the address in a field); and/or saved to a folder or printed on a printer. Parameters may use date tokens (@Today, @MonthStart, @PrevMonthEnd ...). An existing code changes that subscription (and starts it again if it was stopped); Delete stops it, keeping its delivery log.

| Field | Required | Notes |
| --- | --- | --- |
| Subscription code | yes | Text |
| Report code | yes | Text |
| Format | yes | One of: Pdf, Xlsx, Csv, Html; default Pdf |
| Email addresses (; separated) | no | Text |
| Burst by field | no | Text |
| Field holding the email (burst) | no | Text |
| Or burst to the contacts of | yes | One of: (email field), Customers, Suppliers; default (email field) |
| Parameters (Name=value; ...) | no | Text |
| Also save to this folder (inside the delivery root) | no | Text |
| Also print on this printer | no | Text |

The list beside the form shows: Report subscriptions.

### Email a report to each customer or supplier

Run a report once, split it by a field (e.g. Customer code) and email each part as its own PDF - to the address in a field of the rows, or to the contacts of the customer or supplier the key names. Parameters as Name=value; ... (dates as yyyy-MM-dd). Parts with nobody to send to are listed.

| Field | Required | Notes |
| --- | --- | --- |
| Report code | yes | Text |
| Split by field | yes | Text |
| Field holding the email (optional) | no | Text |
| Or send to the contacts of | yes | One of: (email field), Customers, Suppliers; default (email field) |
| Parameters | no | Text |

The list beside the form shows: Report subscriptions.

### Refresh reporting snapshots

Re-takes the aged debt, stock valuation and sales-by-day snapshots now (normally the scheduled task reporting.refresh-snapshots does this); reports keep reading the previous snapshot meanwhile.

The list beside the form shows: Snapshot refreshes.

### Stock valuation and sales snapshots

Revenue by day and department between two dates from the sales snapshot (the list shows stock value by warehouse and category).

| Field | Required | Notes |
| --- | --- | --- |
| From | yes | Date |
| To | yes | Date |

The list beside the form shows: Stock valuation (snapshot).

## Users and security

### Create a user

A login who must change the initial password at first sign-in (8+ characters, letters and digits, not the user name). An existing login name changes that user's display name and e-mail (a password given is set as a new one they must change). Delete deactivates the user: they can no longer sign in, and their history stays; the last system administrator stays active.

| Field | Required | Notes |
| --- | --- | --- |
| Login name | yes | Text |
| Display name | yes | Text |
| E-mail | no | Text |
| Initial password (blank when changing a user) | no | Text |

The list beside the form shows: Users and their roles.

### Reset a password

Sets a new password the user must change at next sign-in, and clears any lockout.

| Field | Required | Notes |
| --- | --- | --- |
| Login name | yes | Text |
| New password | yes | Text |

The list beside the form shows: Users and their roles.

### Activate or deactivate a user

A deactivated user cannot sign in; the last system administrator stays active.

| Field | Required | Notes |
| --- | --- | --- |
| Login name | yes | Text |
| Active | no | Flag; default False |

The list beside the form shows: Users and their roles.

### Create a role

A role, optionally starting from a copy of another role's permissions (edit grants in the permission grid). An existing role name changes its description (a deleted role with the name is brought back). Delete is refused for a system role and for a role users or groups still hold.

| Field | Required | Notes |
| --- | --- | --- |
| Role name | yes | Text |
| Description | no | Text |
| Copy permissions from role | no | Text |

The list beside the form shows: Roles.

### Assign a role

Gives a user a role, limited by scopes such as Company:ACME;Branch:HO,NORTH (blank: unrestricted). Warns of segregation-of-duties conflicts.

| Field | Required | Notes |
| --- | --- | --- |
| Login name | yes | Text |
| Role name | yes | Text |
| Scopes (Dimension:values;...) | no | Text |
| Remove the role instead | no | Flag; default False |

The list beside the form shows: Users and their roles.

### Roles and permissions

What each role and user may do, ticked on a grid.

### Compare roles or change many at once

Compare shows where two roles differ, permission by permission. Bulk change adds, removes or sets operations (View, Create, Edit, Delete, Print, Export, Post, Approve, VoidReverse - comma separated) on a permission code or a pattern such as Payroll.* across several roles (comma separated) at once. System roles are left alone.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Compare, Bulk change; default Compare |
| Role(s) (compare: two) | yes | Text |
| Permission code or pattern (bulk) | no | Text |
| Operations (bulk) | no | Text |
| Bulk change | yes | One of: Add, Remove, Set; default Add |

The list beside the form shows: Roles.

### Create a group or add a role to it

Groups bundle roles; users who join inherit them. Give a role to add it to the group (creating the group if new).

| Field | Required | Notes |
| --- | --- | --- |
| Group name | yes | Text |
| Role to add | no | Text |

The list beside the form shows: Groups.

### Join or leave a group

Puts a user in a group (inheriting its roles) or takes them out.

| Field | Required | Notes |
| --- | --- | --- |
| Login name | yes | Text |
| Group name | yes | Text |
| Member | no | Flag; default False |

The list beside the form shows: Users and their roles.

### API keys for integrations

An integration signs in with the X-Api-Key header and acts with the permissions of the service user named (create one with only what it needs). The key is shown once - copy it then; rotating issues a new one and the old stops working; revoking (or Delete on the list) stops the client.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Create, Rotate, Revoke; default Create |
| Client name (create) | no | Text |
| Service user it acts as (create) | no | Text |
| Expires on (create) | no | Date |
| Requests per minute (create; blank: the server's) | no | Number |
| Client id (rotate / revoke) | no | Text |

The list beside the form shows: API clients.

### Sign-in security (two-factor)

Turn on a second step at sign-in for your account.

## Importing and exporting data

### Import data or opening balances

Loads a CSV template (header row; dates yyyy-MM-dd). Untick Commit to preview: nothing is written while any row has an error. Accounts: code,name,type[,category,manual,budget]. Warehouses: code,name. RentalUnits: code,description,type,rooms,rent,rent_account[,vacant_since,suburb,max_occupants] (no vacant date: let). Customers/Suppliers: code,name,currency[,credit_limit]. Items: code,description[,type,revenue_account,cost_account,category]. Stock: item,warehouse,quantity,unit_cost. Receivables/Payables: trader,document,date,amount (negative = credit). TrialBalance: account,debit,credit. FixedAssets: asset_number,description,class,acquired,cost,accumulated,life_months,cost_account,accum_account,expense_account[,residual,method,rate]. Employees: employee_number,name,basic_salary,engaged[,national_id,department,cost_centre,grade]. StaffLoans: employee_number,reference,outstanding,instalment,loan_account. PayrollYtd: employee_number,tax_year,code,amount (TAXABLE for taxable pay so far, or a pay element such as PAYE for tax already deducted). LeaveBalances: employee_number,leave_type,days[,taken] (the leave owed at take-on). CostCentres/Departments: code,name. TaxCodes: code,name,type,rate,effective_from[,output_account,input_account]. Properties: stand,ward,ratepayer[,valuation,size,connected,services e.g. Water:WAT1;Refuse:REF1]. Meters: serial,stand,service,reading,date[,digits,multiplier]. Students: admission,name,grade,guardian[,boarder,admitted]. Stands: stand,parcel,size[,cost,scheme,status] (a parcel not yet registered is created from its stands). OwnershipHistory: stand,date,change,holder[,customer,consideration] (change: Allocation, Sale, Cession, Surrender, Repossession; joins the stand's hash chain in date order). Applicants: id_number,name,number,registered[,category,date_of_birth,resident_since,dependants,disability,income,tenure,status,marital,regime,spouse_id,spouse_name,verified] (legacy waiting-list numbers kept). Tariffs: code,name,service,type,effective,revenue_account[,fixed,rate,minimum,rate_per_valuation_cents,rate_per_sqm,category,ward,blocks e.g. 0-10:0.5;10-30:0.8;30-:1.2]. PriceLists: price_list,item,price[,name,currency,min_qty,effective] (a list not yet on file is created). Budget: account,period,amount[,cost_centre,quantity] for the year of the date. InstalmentAgreements: agreement,stand,buyer,cash_price,deposit,rate,method,term_months,signed,first_due,interest_account[,tenure,escalation,arrears] (instalments due by the opening date are history; arrears become an open item against the suspense account). MunicipalArrears: stand,service,invoice,amount[,date,narrative] (an unpaid bill per property and service against the suspense account, on an opening billing run for the service). The list also shows the fixed asset and staff loan registers against their accounts.

| Field | Required | Notes |
| --- | --- | --- |
| What | yes | One of: Accounts, Warehouses, Customers, Suppliers, Items, Stock, Receivables, Payables, TrialBalance, FixedAssets, Employees, StaffLoans, PayrollYtd, LeaveBalances, CostCentres, Departments, TaxCodes, Properties, Meters, Students, Stands, OwnershipHistory, Applicants, Tariffs, PriceLists, RentalUnits, Budget, InstalmentAgreements, MunicipalArrears; default Accounts |
| CSV or Excel (.xlsx, first sheet) file path | yes | Text |
| Opening date | yes | Date |
| Migration suspense account | yes | Text; default 2900 |
| Budget label (budget) | no | Text |
| Budget type (budget) | yes | One of: OriginalApproved, SupplementaryRevised, Reforecast, DraftSubmission, Capital, Cash, BaseCase, BestCase, WorstCase, ZeroBased, RollingForecast, Project, Establishment; default OriginalApproved |
| Budget department (budget; blank: whole company) | no | Text |
| Commit | no | Flag; default False |

The list beside the form shows: Opening balance reconciliation.

### Import templates and data export

Template: saves the CSV template for an import (its header row and an example row) to fill in. Export: saves a register (accounts, warehouses, customers, suppliers, items, employees, stock on hand, fixed assets, staff loans, cost centres, departments, students, stands) in its import layout, to correct in a spreadsheet or load into another company.

| Field | Required | Notes |
| --- | --- | --- |
| Action | yes | One of: Template, Export; default Template |
| What | yes | One of: Accounts, Warehouses, Customers, Suppliers, Items, Stock, Receivables, Payables, TrialBalance, FixedAssets, Employees, StaffLoans, PayrollYtd, LeaveBalances, CostCentres, Departments, TaxCodes, Properties, Meters, Students, Stands, OwnershipHistory, Applicants, Tariffs, PriceLists, RentalUnits, Budget, InstalmentAgreements, MunicipalArrears; default Accounts |
| Save to (.csv path) | yes | Text |

The list beside the form shows: Import templates.

### Quick search

Finds items, customers, suppliers, leads and accounts by name or code, forgiving a mistyped letter or two.

| Field | Required | Notes |
| --- | --- | --- |
| Search for | yes | Text |

The list beside the form shows: Aged debt (snapshot).

## Looking after the system

### Back up or restore the database

Takes a backup now (pg_dump, SHA-256 recorded, copied off-site when configured), checks one, or restores a checked backup into a NEW database (type RESTORE <name> to confirm) - the live database is never overwritten. Restore a file: an off-site copy or another server's backup with no record here, into a new database. Switch over: the restored database takes the live name and the old live one is kept as <live>_retired_<stamp> (type SWITCH TO <name>); restart the server and every desktop afterwards.

| Field | Required | Notes |
| --- | --- | --- |
| Action | yes | One of: Back up now, Check a backup, Restore into a new database, Restore a file into a new database, Switch over to a restored database; default Back up now |
| Backup id (check / restore) | no | Number |
| Backup file path (restore a file) | no | Text |
| New / restored database name | no | Text |
| Confirmation (RESTORE name / SWITCH TO name) | no | Text |

The list beside the form shows: Backups.

### Licence

Shows the licence; installs a new licence or the vendor's revocation list (paste the text or give the file path); or gives this machine's activation request code to send to the vendor.

| Field | Required | Notes |
| --- | --- | --- |
| Action | yes | One of: Show status, Install licence, Install revocation list, Activation request code, Activate online, Renew online; default Show status |
| Licence / list text or file path | no | Text |
| Customer reference (request code) | no | Text |

The list beside the form shows: Licensed modules.

### Scheduled jobs

Jobs that run by themselves, when they last ran and what they did.

### Audit trail

Who changed what, and when.

### Who touched this record

The full history of one record.

### Check data

Look for problems in the data and fix them.

### Data health

How complete and tidy the data is.

### Review direct database changes

Database triggers record every change to users, roles, permissions, prices, tariffs, meters and the licence - including changes made with SQL tools, listed here.

| Field | Required | Notes |
| --- | --- | --- |
| Look back (days) | yes | Number; default 30 |
| Include the application's own changes | no | Flag; default False |

The list beside the form shows: Sensitive-table changes made outside One4All (last 30 days).

### Archive an audit year

Moves a closed year of the audit log out of the live table into the archive schema (still readable, never deleted). It needs the licence holder's signed authorisation for that year (MA1. token), years go oldest first, and the hash chain carries on across the gap.

| Field | Required | Notes |
| --- | --- | --- |
| Year to archive | yes | Number |
| Licence holder's authorisation (MA1. token) | yes | Text |

The list beside the form shows: Audit log by year.

### Failed offline operations

Work captured offline that failed to synchronise: requeue one (after fixing the cause) or discard it, by its operation id.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Requeue, Discard; default Requeue |
| Operation id | yes | Text |

The list beside the form shows: Failed offline operations.

### Printers per workstation

The printers a workstation uses for each kind of printing (reports, documents, receipts, cheques, labels, payslips): a Windows printer (USB or shared) or a network receipt printer by its IP address and port (usually 9100). Set makes it the purpose's only printer; Add another prints to it as well (a customer and a shop copy, a second counter); Remove all takes every printer of the purpose away; Test prints a test slip. Workstation * is the default for every computer without its own; this computer's name is filled in. Tills print receipts on their workstation's receipt printers (copies from the receipt layout); the report viewer's print dialog opens on the chosen printer. Edit opens a printer in the form (Set or Add another saves the change); Delete on the list removes that one printer.

| Field | Required | Notes |
| --- | --- | --- |
| Step | yes | One of: Set, Add another, Remove all, Test; default Set |
| Workstation (computer name, or *) | yes | Text; default BLACKBOX |
| Purpose | yes | One of: Reports, Documents, Receipts, Cheques, Labels, Payslips; default Reports |
| Connection | yes | One of: Windows printer, Network (IP address); default Windows printer |
| Printer (Windows name, or a name for a network printer) | no | Choice |
| Network printer IP address | no | Text |
| Network printer port | yes | Number; default 9100 |
| Tray (Auto, Manual, Cassette, Tractor) | no | Text |
| Copies | yes | Number; default 1 |

The list beside the form shows: Printers.

### Manage webhooks

Subscribe an endpoint to sale.completed, invoice.posted, payment.received, bill.generated or *; deliveries are signed (X-One4All-Signature: sha256=HMAC). Give a subscription id to stop it, a failed delivery id to retry it, or tick Send now to dispatch what is due.

| Field | Required | Notes |
| --- | --- | --- |
| Event (to subscribe) | no | Text |
| Endpoint URL | no | Text |
| Signing secret (16+ characters) | no | Text |
| Subscription id to stop | no | Number |
| Failed delivery id to retry | no | Number |
| Send now | no | Flag; default False |

The list beside the form shows: Webhook deliveries (latest 200).

## Reports

| Code | Report | What it shows |
| --- | --- | --- |
| DB251 | Executive Dashboard | Year to date at the date: revenue, expenses, profit, cash, debtors, creditors, stock, working capital and the cash cycle. |
| DB252 | Daily Flash Report | The day's sales, receipts and payments against the same day last week and the month to date, with cash at close. |
| DB253 | KPI Scorecard | This month against last: revenue, profit, gross margin, debtor, creditor and stock days, cash cycle - with the direction of travel. |
| DB254 | Branch Comparison League Table | Branches ranked by POS net sales in the period, with transactions, average basket and share. |
| DB255 | Trend & Seasonality Analysis | Revenue, expenses and profit month by month for the 24 months to the date, with each month against the same month a year before. |
| SYS-PERIOD | Period Status | Accounting periods with their open/closed status for the key ledgers (GL, AR, AP) - a month-end close aid. |
| SYS235 | Audit Trail Report | Every audit-logged action in the period - who, when, what record, which fields and why - optionally for one record type or user. |
| SYS236 | User Activity Report | Per user over the period: sign-ins, failed attempts, lockouts, first and last sign-in, hours active (closed sessions, and open ones to their last activity), terminals used, screens used and actions run, and reports run (SYS236-S lists the screens). |
| SYS236-S | Screen Usage | Every desktop screen used in the period, per user: how often it was opened and run, and when it was last used. |
| SYS237 | Permission Matrix | Every role with each permission it grants and the operations allowed. |
| SYS237-U | Permission Matrix by User | Every active user with each permission they hold through their roles, groups and individual overrides, and the operations allowed; system administrators are shown as holding everything. The auditor's users-by-permissions view. |
| SYS238 | User, Role & Group Membership | Every user with direct roles, groups, roles held through groups and individual overrides. |
| SYS239 | Segregation of Duties Violations (all modules) | Every active user whose combined permissions break a separation-of-duties rule. |
| SYS240 | Master Data Change Report | Changes in the period to customers, suppliers, items, accounts, tariffs, properties, employees, price lists, users and roles. |
| SYS241 | Supervisor Override Report | Every override authorised by a supervisor in the period across the modules: POS price overrides, refunds and no-sales, and manual weighbridge weights. |
| SYS242 | Back-dated Transactions | Journals entered in the period with a posting date more than the given number of days before they were entered. |
| SYS243 | Deleted & Voided Documents | Records deleted or retired in the period, and journals reversed or cancelled, with who and why. |
| SYS244 | Document Numbering Gaps | Missing runs in each year's journal numbering. |
| SYS245 | Unposted & Deferred Batches | Journals still draft, deferred or awaiting approval, with who prepared them. |
| SYS246 | Data Integrity & System Health | The data-health checks at a date - orphan records, unbalanced batches, control-account variances, negative stock and the rest - each with its status, count, value and first findings. |
| SYS247 | Audit Trail Hash-Chain Verification | Recomputes the audit trail's hash chain for the company and for system-wide entries and says whether it is intact or where it first breaks. |
| SYS248 | Licence Installation History | Licences installed, when and by whom, and which is active (token fingerprint only). |
| SYS249 | Login Session & Concurrency Report | Every session opened in the period: user, terminal, start, end and duration, flagging sessions open at the same time as another of the same user on a different terminal. |
| SYS250 | Report Print & Export Log | Every report run in the period: who, what, format, size and copy number. |
| SYS258 | Price List Listing | Every price list with its items, quantity breaks and prices. |
| SYS259 | Exchange Rate History | Every exchange rate captured, by currency pair and type, with its effective date. |
| SYS260 | Number Sequence Status | Every document sequence: prefix, padding, yearly reset, gapless flag and the next number per year. |
| SYS261 | Company Configuration Listing | Every setting of the company record. |
| SYS262 | Report Definition Inventory | Every report definition held - system and custom - with scope, version and last change. |
| SYS263 | Scheduled Job Status & Delivery Log | Each scheduled job's state and last result, then report deliveries in the period. |
