# Handover: continuing One4All development

Written 2026-10-17 by the Fable 5.1 session for whoever continues (intended for Opus 5.5). Read this, then `docs/PROGRESS.md`
(newest rows at the bottom of each table) and the memory files under `C:\Users\Akim\.claude\projects\C--Users-Akim-Videos-ERP\memory\`.
Everything below is what the previous session needed to know to be productive; nothing here replaces the brief (`docs/BRIEF.md`).

## The standing instruction

Develop the system against `docs/BRIEF.md` non-stop, one increment at a time, working through the ranked gap list in the memory file
`one4all-progress-and-layout.md` (section "THIRTEENTH AUDIT"). Per increment:

1. Build Debug and Release and run every test suite with the verify script (see below).
2. Add a row to `docs/PROGRESS.md` (`python -I tools/dev/progress_row.py <rowfile>`: line 1 of the row file is the prefix of the row it goes after, line 2 is the new row).
3. Commit locally with the trailer the harness gives for the current model (Opus 5.5 since 2026-10-17: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`).
   The repository is on GitHub (`origin` = github.com/akim-nyoni/onestoperp, branch `main`); the user pushes. Do not push unless asked, and never
   rewrite commits already on origin (the user's d032d334 "initial commit" is pushed).
4. Apply migrations to the dev database, update the memory files, post a brief progress note to the user, and carry on.

Rules that do not change: English only (no language or resource-file work); module-centric Explorer navigation (every new screen goes
into `src/One4All.Desktop/Navigation/NavigationMap.cs` and gets a `ScreenPermissions` rule); forms OK/Cancel; lists CRUD; never kill the
user's running `One4All.Desktop.exe`; never write the database password into a file in the repo (it lives only in the env var the memory
file describes and in the gitignored `appsettings.Development.json`); never write the em-dash character anywhere (code, strings,
comments, docs, commit messages): use a plain hyphen. a search for U+2014 across the repository should find none; report codes must stay unique
(`ReportCodeUniquenessTests`).

## Building and testing without losing time

- Builds can hang MSBuild. The working pattern: run `dotnet build One4All.slnx -c Debug --nologo -v q -nodeReuse:false -p:UseSharedCompilation=false`
  in the background, record the start time, and check DLL timestamps against it. If the build hangs after writing the DLLs, stop it, run
  `taskkill //F //IM dotnet.exe`, then use `--no-build` for `dotnet ef` and `dotnet test`.
- Full verify: `powershell -File tools/dev/verify.ps1 > verify.out 2>&1` (keep verify.out outside the repo or delete it; Debug build, Release build, then all three suites
  with `--no-build`). It takes 20 to 30 minutes. Do not edit source while its builds run: a Debug build that started before a new file
  existed, or a Release build that compiles a half-applied change, fails the run. Edit only after `=== RELEASE OK ===` appears, and never
  build the test projects while the accounting suite is running (testhost locks the DLLs).
- The accounting suite creates a fresh PostgreSQL database per run from the migrations; the first user it creates gets id 1, so never use
  user id 1 for "the cashier" in a test that also creates a user. `Scenario.SeedAsync` seeds the chart (see the account list in
  `tests/One4All.AccountingTests/Scenario.cs` before adding accounts: 2900 and 4950 already exist).
- Console screens snapshot: after adding a screen, add its line to `tests/One4All.UnitTests/Security/console-screens.tsv` by hand
  (module, title, IsDocument, area; tab-separated, kept in sorted order); `ScreenPermissionsTests` fails until the screen has a permission rule. Report catalogue doc: `ONE4ALL_WRITE_CATALOGUE=1 dotnet test tests/One4All.UnitTests --no-build --filter ReportCatalogueDocumentTests`.
- Generated documentation: `dotnet run --project tools/One4All.DocGen -- .` rewrites `docs/manual/`, `docs/demos/`, `docs/acceptance/ACCEPTANCE-PACK.md`
  and `docs/TRAINING-DATA.md` from the screens, reports and test sources; run it after adding screens, reports or tests and commit the result.
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

The twelfth list is complete. The thirteenth audit (three Explore agents, 2026-10-17) is ranked in the memory file under "THIRTEENTH AUDIT":
#1 to #13 and #15 to #17 are done, #14 partly (offer exceptions queue and stand ward; re-pointing an existing property's ratepayer and more than
one stand per beneficiary are still open). Continue with #18 (the missing and thin section-14 reports) and down the list. The last rows of
`docs/PROGRESS.md` say exactly what each increment did.
