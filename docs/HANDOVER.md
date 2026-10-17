# Handover: continuing One4All development

Written 2026-10-17 by the Fable 5.1 session for whoever continues (intended for Opus 5.5). Read this, then `docs/PROGRESS.md`
(newest rows at the bottom of each table) and the memory files under `C:\Users\Akim\.claude\projects\C--Users-Akim-Videos-ERP\memory\`.
Everything below is what the previous session needed to know to be productive; nothing here replaces the brief (`docs/BRIEF.md`).

## The standing instruction

Develop the system against `docs/BRIEF.md` non-stop, one increment at a time, working through the ranked gap list in the memory file
`one4all-progress-and-layout.md` (section "TWELFTH AUDIT"). Per increment:

1. Build Debug and Release and run every test suite with the verify script (see below).
2. Add a row to `docs/PROGRESS.md` (`python -I <scratchpad>/progress_row.py <rowfile>`: line 1 of the row file is the prefix of the row it goes after, line 2 is the new row).
3. Commit with the trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` (use your own model name).
4. Apply migrations to the dev database, update the memory files, post a brief progress note to the user, and carry on.

Rules that do not change: English only (no language or resource-file work); module-centric Explorer navigation (every new screen goes
into `src/One4All.Desktop/Navigation/NavigationMap.cs` and gets a `ScreenPermissions` rule); forms OK/Cancel; lists CRUD; never kill the
user's running `One4All.Desktop.exe`; never write the database password into a file in the repo (it lives only in the env var the memory
file describes and in the gitignored `appsettings.Development.json`).

## Building and testing without losing time

- Builds can hang MSBuild. The working pattern: run `dotnet build One4All.slnx -c Debug --nologo -v q -nodeReuse:false -p:UseSharedCompilation=false`
  in the background, record the start time, and check DLL timestamps against it. If the build hangs after writing the DLLs, stop it, run
  `taskkill //F //IM dotnet.exe`, then use `--no-build` for `dotnet ef` and `dotnet test`.
- Full verify: `powershell -File <scratchpad>/verify.ps1 > <scratchpad>/verify.out 2>&1` (Debug build, Release build, then all three suites
  with `--no-build`). It takes 20 to 30 minutes. Do not edit source while its builds run: a Debug build that started before a new file
  existed, or a Release build that compiles a half-applied change, fails the run. Edit only after `=== RELEASE OK ===` appears, and never
  build the test projects while the accounting suite is running (testhost locks the DLLs).
- The accounting suite creates a fresh PostgreSQL database per run from the migrations; the first user it creates gets id 1, so never use
  user id 1 for "the cashier" in a test that also creates a user. `Scenario.SeedAsync` seeds the chart (see the account list in
  `tests/One4All.AccountingTests/Scenario.cs` before adding accounts: 2900 and 4950 already exist).
- Console screens snapshot: after adding a screen, copy `src/One4All.Desktop/bin/Debug/net10.0-windows/*` to `<scratchpad>/deskbuild/`,
  run `<scratchpad>/opsdump2` (`dotnet run > <scratchpad>/ops.tsv`), and rewrite `tests/One4All.UnitTests/Security/console-screens.tsv`
  (sorted rows, columns 0,1,2,4). Report catalogue doc: `ONE4ALL_WRITE_CATALOGUE=1 dotnet test tests/One4All.UnitTests --no-build --filter ReportCatalogueDocumentTests`.
- Migrations: scaffold only from freshly built DLLs, and only after the previous increment (with its own migration) is committed: the model
  snapshot is shared, so two uncommitted features end up in one migration. `dotnet ef migrations add/remove` need the connection env var.
- Unit tests pin counts: `ImportTemplatesTests` pins the number of import kinds; `ListingColumnRoundTripTests` and
  `ReportCatalogueDocumentTests` cover listing definitions; `console-screens.tsv` pins screen titles.

## Editing safely

- Apply multi-line edits with small Python scripts written with the Write tool (Bash heredocs mangle backslashes). Make each script
  idempotent (`if new in text: skip`) so a failed run can be re-run after fixing one anchor.
- Anchors fail on doc comments and duplicated text; read the exact lines first. `from`, `to`, `group` are query keywords inside LINQ
  query expressions: never use them as variable names in methods that contain `from ... in` queries.
- The `One4All.Reporting` project cannot reference `One4All.Application` (duplicate small constants are fine; see `SystemReports.CommonParameterModules`).
- EF conventions: strings are varchar(256) unless `.HasColumnType("text")`; decimals are (19,4). Nullable enums use `HasConversion<int>()`.
- `Result`/`Error` pattern: `Error.Validation/Rule/NotFound/Conflict/Forbidden`, implicit conversions; `_db.AtomicAsync` nests (joins the
  current transaction). The posting engine looks up a daily rate when a line's currency is not base and no rate is keyed.
- Committing several increments that touch one file: stage a temporary version of the file with the other increment's hunk removed
  (write, `git add`, write the full content back). See the Python staging snippets in this session's transcript if available.

## Where things are

- Services: interfaces in `src/One4All.Application/Abstractions/<Module>/`, implementations in `src/One4All.Infrastructure/<Module>/`,
  registration in `src/One4All.Infrastructure/DependencyInjection.cs` (constructor optional parameters resolve to null when unregistered).
- Desktop screens: `src/One4All.Desktop/Operations/OperationCatalogue.<Area>.cs` (`Operation` with `Fields`, `Run`, `List`; helpers
  `Pick/Req/Opt/Num/OptNum/Tick/Today/SupplierPick/CustomerPick`, `Check(result)`, `M(amount)`, `D(date)`; `i.Text/Optional/Number/OptionalNumber/Flag/Date`).
- Reports: listings in `src/One4All.Reporting/<Module>Reports.cs` (`Listing(code, name, module, description, command, columns, parameters)`),
  rows from `src/One4All.Infrastructure/Reporting/<Module>ReportDataProvider.cs` (`CanHandle` by command prefix). Sales, Purchasing,
  CashBook and Tax listings get the common parameters automatically; the runner translates money columns (`ReportCurrencyTranslation`).
- Drill-through: `DrillThrough.Infer` (field name to target) and `IDrillThroughService` (text for the record) feed the viewer's drill window.
- REST API: `src/One4All.Server/Program.cs` (one endpoint per feature, `ValueResponse/ActionResponse`) plus `TradingApiEndpoints.cs`;
  API documents in `src/One4All.Application/Api/TradingApiModels.cs`.
- Imports and take-on: `IOpeningBalanceService` and `IVerticalImportService` with `ImportTemplates` and the System Manager Import screen.
- Backups: `IBackupService` (`BackupService` wraps pg_dump/pg_restore; restore by record, by file, and switch-over).

## State at handover

See the last rows of `docs/PROGRESS.md` and the "TWELFTH AUDIT" line in the memory file for exactly which items are committed. Remaining
from the twelfth list: the item 19 bundle (server packaging as a Windows service with the PostgreSQL prerequisite and first-run database
setup and a single-file licensing tool publish; documentation deliverables: user manual per vertical, acceptance test pack, demo scripts,
training data set; performance tests still open: POS start-to-ready under 10 s, 20 terminals, payslip e-mailing, HTTP concurrency;
designer gaps: record sorting separate from grouping, element group/ungroup, images from URL or path, anchoring, gradients, tooltips;
about 70 more §S document layouts to make designer-editable). After that, run a fresh (thirteenth) audit of the brief with three parallel
Explore agents, merge and rank the gaps, and continue from the top.

Estimate given to the user on 2026-10-17: roughly 15 to 18 hours for the rest of the twelfth list, then 8 to 10 hours for a thirteenth pass.
