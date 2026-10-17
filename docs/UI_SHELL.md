# Desktop Shell - UI Reference (module-centric, built for non-technical users)

Since 2026-10-14 the shell is organised **by business module, in plain language**, after user testing showed the Sage-style
"kind of work first" menus were hard to follow. The source of truth is `One4All.Desktop/Navigation/NavigationMap.cs`.

## Layout regions (Explorer, modelled on Sage 200 Evolution - user request 2026-10-16)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ File  View  Modules  Maintenance  Transactions  Reports  Enquiries  Utilities  Help │
│ ⏻ Exit  ⬆ Home │ ◀ Back ▶ Forward │ ⭐ Favourites ▾            Search [________] 🔎 │
├────────────────┬────────────────────────────────────────────────────────────┤
│ Filter: [____] │ [Home] [Sales & Customers ×] [Customers ×]   ← document tabs │
│ ▸ Sales & Cust.│ ┌ Related Tasks ┐ ┌ Maintenance (green)  Transactions (red) ┐ │
│   Maintenance  │ │ tile  tile    │ │ tile tile tile       tile tile tile    │ │
│   Transactions │ │ tile  tile    │ │ tile tile            tile tile         │ │
│   Enquiries    │ └───────────────┘ └────────────────────────────────────────┘ │
│   Utilities    │  …or a list tab: [➕ Add a customer…] [🔄 Refresh] [📝 History] 🔍 │
│ ▸ Accounting   │   grid of records ……………………………………………  10 records              │
├────────────────┴────────────────────────────────────────────────────────────┤
│ Ready.                                        Demo Council & Trading  👤 Admin │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Behaviour rules

- **20 modules** with plain-language groups of screens (`NavigationMap`), each screen with a kind of work: Maintenance (green), Transactions (red),
  Enquiries (blue), Utilities (purple) - `NavCategory.BrushFor`.
- **Tree**: module ▸ kind of work ▸ screen. The Filter box narrows it to screens whose name (or group) contains the text. Selecting a module or a
  kind opens the module's tab; double-click / Enter on a screen opens it.
- **Tabs**: Home (Quick start tiles, module tiles); a module page (`ModulePage`: Related Tasks = its everyday screens; the kinds of work side by
  side, tiles icon-above-label, then a purple **Reports** section of that module's catalogue reports); a screen's **list** (`OperationsListView`:
  Add…, Edit… (form prefilled from the row; double-click too), Delete (asks; soft delete), Refresh, History, search, grid, record count). × / middle-click /
  Ctrl+W closes a tab; Back / Forward walk the tabs visited; Favourites pins list screens (per user, `%AppData%\One4Allavourites.txt`).
- **Forms open in their own window** (`OperationsWindow` hosting `OperationsView`): Add… / Edit… on a list, or a screen without a list, or a dedicated
  window (till, teller, approvals…). A form and its list share one `OperationsViewModel`, so the list refreshes when the form saves. Buttons are
  **Cancel** and **OK** (Run for a document / enquiry): a one-line success closes the form; errors and output stay to be read; the output pane
  shows only when there is output.
- **Reports are not a module**: the Reports menu has a submenu per module (the catalogue's `Module` → Explorer module via `NavigationMap.ReportModules`),
  each entry opening the Report Viewer on that report; the viewer, designer and subscription tools live under Administration.
- Learnability: the till and teller show their first step in place (guide strip); code boxes are lookups (`Ops.CustomerPick` etc.); required boxes
  are starred; the status bar shows the chosen screen's purpose.
- Data-driven and gated: licence and permissions hide what the user may not use; `NavigationBuilder.Unresolved` must stay empty. Every screen is
  gated by a catalogue feature (`Application.Security.ScreenPermissions`: console module + title → `Module.Feature`; `Operation.Permission` overrides);
  a user sees a screen when they have anything on its feature, runs it with the flags it needs (Print / Approve / VoidReverse / View / any change),
  and deletes with Delete. Add a rule there when a new screen's title would not fall under the right feature.
- Adding a screen: place it in `NavigationMap` under the right module and group; a new window also needs a key in `ShellWindow.OpenEntry`.

## MVVM constraint (brief §2.2)

Zero business logic in code-behind; ViewModels reference only `ICommand` from WPF so an Avalonia port
stays possible. The shell composes module descriptors (`IModuleDescriptor`) discovered via DI.
