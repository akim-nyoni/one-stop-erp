# ADR-0003 - Product name "One4All" (replacing "Meridian")

- **Status:** Accepted
- **Date:** 2026-09-14

## Context

The brief refers to the product throughout as **"Meridian ERP"** with a `Meridian.*` solution and namespace layout. The product owner has named the product **"One4All"**.

## Decision

Use **One4All** everywhere the brief says Meridian:

| Brief (Meridian) | This build (One4All) |
|---|---|
| `Meridian.sln` | `One4All.sln` |
| `Meridian.Core`, `Meridian.Server`, `Meridian.Desktop`, … | `One4All.Core`, `One4All.Server`, `One4All.Desktop`, … |
| `SystemManager` / product string "Meridian ERP" | product string "One4All ERP" |
| database `meridian` | database `one4all` |
| licence `"product": "Meridian ERP"` | `"product": "One4All ERP"` |

The **solution layout, module boundaries, schema names (`sys`, `sec`, `audit`, `gl`, …) and all functional requirements from the brief are unchanged** - only the product brand and root namespace differ.

## Consequences

- Schema names stay short/technical (`gl`, `ar`, `ap`, …) exactly as the brief specifies; they are not rebranded.
- The licensing tool signs payloads with `"product": "One4All ERP"`; validation checks the same string.
