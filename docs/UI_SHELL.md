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
│ ▾ Accounting ◀lit│  Module page — Home › Accounting                         │
│   ▸ Maintenance  │  Open screens: [📒 Add an account ✕] [🧾 Journal ✕]  Close all │
│   ▸ Transactions │   ┌ ⚙ Maintenance — setting up ─────────── 11 screens ┐ │
│   ▸ Enquiries    │   │ [Add an account] [Add a cost centre] [Tax code] … │ │
│   ▸ Utilities    │   ┌ 📝 Transactions — day-to-day ────────── 7 screens ┐ │
│ ▸ Housing        │   │ [Post a journal] [Recurring journals] …           │ │
└──────────────────┴────────────────────────────────────────────────────────┘
```

## Behaviour rules

- **20 modules** (Sales & Customers, Purchases & Suppliers, Stock & Products, Point of Sale, Restaurant, Accounting, Cash & Banking,
  Cashiering, Budgets, Fixed Assets, HR & Payroll, Municipality, Housing, Projects & Contracts, Weighbridge, Schools, CRM, Approvals,
  Reports, Administration), each with plain-language **groups** of screens (`NavigationMap`).
- **A module page shows every one of its screens, labelled by kind of work**: a section each for Maintenance (setting up), Transactions
  (day-to-day), Enquiries (looking up) and Utilities (period end and housekeeping) — `NavModule.Categories` — with the group as a chip on each
  screen card. Choosing a kind of work (sidebar, menu, search) scrolls the page to that section.
- **The sidebar** is Module ▸ kind of work ▸ screen; the module shown is lit (bar in its colour, bold title) and rows highlight on hover.
- **Every screen opens in its own window** (`OperationsWindow` hosting `OperationsView`, or the dedicated window), so several can be worked on
  at once; the **Open screens** bar on the page lists them — click a chip to bring that window to the front, ✕ to close it, "Close all".
- Menus are multi-level: **Modules ▸ module ▸ kind of work ▸ (group ▸) screen**, and **Maintenance / Transactions / Enquiries / Utilities ▸ module ▸ screen**.
- The **search box** finds any screen by what it does (every typed word must match; plurals match singulars); Enter opens the first hit.
- Data-driven and gated: entries a user's **licence and permissions** do not cover are hidden; empty sections and modules disappear.
- Any console action the map does not name falls into its module's **More** group; `NavigationBuilder.Unresolved` lists map entries whose
  action no longer exists (keep it empty).
- Adding a screen: place it in `NavigationMap` under the right module and group with a plain title; a new window also needs a key in
  `ShellWindow.OpenEntry`.

## MVVM constraint (brief §2.2)

Zero business logic in code-behind; ViewModels reference only `ICommand` from WPF so an Avalonia port
stays possible. The shell composes module descriptors (`IModuleDescriptor`) discovered via DI.
