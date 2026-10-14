# Desktop Shell — UI Reference (module-centric, built for non-technical users)

Since 2026-10-14 the shell is organised **by business module, in plain language**, after user testing showed the Sage-style
"kind of work first" menus were hard to follow. The source of truth is `One4All.Desktop/Navigation/NavigationMap.cs`.

## Layout regions

```
┌───────────────────────────────────────────────────────────────────────────┐
│ File  Modules  Maintenance  Transactions  Enquiries  Utilities  Reports  View  Help │
├───────────────────────────────────────────────────────────────────────────┤
│ 🏠 Home   🔍 [ What do you want to do?  e.g. "add a product" ]  (Ctrl+F)        │
├──────────────────┬────────────────────────────────────────────────────────┤
│ Module list      │  Home: one card per module (icon, name, what it is for) │
│ ▸ Sales & Cust.  │                                                          │
│ ▾ Stock & Prod.  │  Module page: that module's screens only, in group cards │
│   ▾ Products     │   ┌ Products ──────────┐ ┌ Stock levels ──────────┐       │
│      Add a prod… │   │ Add a product…     │ │ Stock take…            │       │
│   ▸ Stock levels │   └────────────────────┘ └────────────────────────┘       │
│ ▸ Housing        │                                                          │
└──────────────────┴────────────────────────────────────────────────────────┘
```

## Behaviour rules

- **20 modules** (Sales & Customers, Purchases & Suppliers, Stock & Products, Point of Sale, Restaurant, Accounting, Cash & Banking,
  Cashiering, Budgets, Fixed Assets, HR & Payroll, Municipality, Housing, Projects & Contracts, Weighbridge, Schools, CRM, Approvals,
  Reports, Administration), each with plain-language **groups** of screens.
- Opening a module shows **only that module's screens**. Menus are multi-level: **Modules ▸ module ▸ group ▸ screen**, and
  **Maintenance / Transactions / Enquiries / Utilities ▸ module ▸ (group ▸) screen** — Transactions ▸ Housing lists Housing only.
- **Every screen opens in its own window** (`OperationsWindow` for a single action, or the dedicated window), so several can be open.
- The **search box** finds any screen by what it does (every typed word must match; plurals match singulars); Enter opens the first hit.
- Data-driven and gated: entries a user's **licence and permissions** do not cover are hidden; empty groups and modules disappear.
- Any console action the map does not name falls into its module's **More** group, so nothing is unreachable;
  `NavigationBuilder.Unresolved` lists map entries whose action no longer exists (keep it empty).
- Adding a screen: place it in `NavigationMap` under the right module and group with a plain title; a new window also needs a key in
  `ShellWindow.OpenEntry`.

## MVVM constraint (brief §2.2)

Zero business logic in code-behind; ViewModels reference only `ICommand` from WPF so an Avalonia port
stays possible. The shell composes module descriptors (`IModuleDescriptor`) discovered via DI.
