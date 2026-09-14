# Desktop Shell — UI Reference (follow Sage Evolution)

The One4All back-office shell (`One4All.Desktop/Shell`) follows the **Sage Evolution** layout.

## Layout regions

```
┌───────────────────────────────────────────────────────────────────────────┐
│ Menu bar:  File  View  Dashboard  My Desktop  Administration  Maintenance   │
│            Reports  Transactions  Enquiries  Visual Reports  Charts …  Help │
├───────────────────────────────────────────────────────────────────────────┤
│ Toolbar:  Exit | ◀ Back  ▶ Forward | ★ Favourites | [quick icons…] | 🔍Search│
├──────────────────┬────────────────────────────────────────────────────────┤
│ System Tree      │  Content area (tabbed)                                   │
│ (left navigator) │                                                          │
│                  │   ┌─ Module home: grid of large module icons ─────────┐ │
│ ▸ One4All (root) │   │  [GL] [AR] [AP] [Cash] [Inventory] [Procurement]  │ │
│  ▾ My Desktop    │   │  [POS] [Vouchers] [Municipal] [Housing] [Payroll] │ │
│     My Authorisns│   │  [Budget] [Job Costing] [Fixed Assets] [Reports]  │ │
│     My Calendar  │   └───────────────────────────────────────────────────┘ │
│     Notifications│                                                          │
│  ▸ General Ledger│   Selecting a module opens its workspace as a tab.       │
│  ▸ Accounts Rec. │                                                          │
│  ▸ Inventory     │                                                          │
│    ▾ Maintenance │                                                          │
│      Transactions│                                                          │
│      Enquiries   │                                                          │
│      Reports     │                                                          │
└──────────────────┴────────────────────────────────────────────────────────┘
```

## Behaviour rules (from the brief)

- **Navigation is data-driven**: the System Tree and the module-icon grid are built at runtime from
  the modules that are **licensed** (`ILicenceService.IsModuleLicensed`) **and permitted** for the
  signed-in user (`View` on at least one feature in the module). A locked module is *hidden*, not greyed.
- Each module node expands to the standard child groups **Maintenance / Transactions / Enquiries / Reports**
  (Sage's convention), themselves filtered by permission.
- The content area is **tabbed**; the module home tab shows the icon grid.
- A **company + period + currency indicator** sits in the status bar; a company/period switcher is on the toolbar.
- **Favourites** and **My Desktop** (authorisations due, notifications, requisitions) are per-user.
- Colour accent: green on white, dense back-office styling (POS uses a separate touch-first theme).

## MVVM constraint (brief §2.2)

Zero business logic in code-behind; ViewModels reference only `ICommand` from WPF so an Avalonia port
stays possible. The shell composes module descriptors (`IModuleDescriptor`) discovered via DI.
