# ADR-0001 — Target .NET 10 instead of .NET 8

- **Status:** Accepted
- **Date:** 2026-09-14
- **Deciders:** Product owner + architect

## Context

The brief (§2.2) specifies **.NET 8 LTS**. The build machine has **only the .NET 10 SDK (10.0.401)** installed, with no .NET 8 runtime present. .NET 10 is an LTS release and is the SDK the toolchain resolves by default.

Building `net8.0` here would require installing the .NET 8 SDK and the Windows Desktop runtime purely to satisfy the brief's version number, with no functional benefit — the code uses no API that is exclusive to .NET 8.

## Decision

Target **`net10.0`** for library/host projects and **`net10.0-windows`** for the WPF desktop client. C# `latest`, `nullable` enabled, warnings-as-errors on Core/Application (unchanged from the brief's intent).

`global.json` pins the SDK to `10.0.401` with `rollForward: latestMinor`.

## Consequences

- Builds and runs immediately on the current machine; no runtime install step.
- All other stack choices from §2.2 are honoured (WPF + CommunityToolkit.Mvvm, EF Core, Npgsql, Dapper, etc.); package versions track the `net10.0`-compatible line.
- If a customer environment standardises on .NET 8, retargeting is a one-line `TargetFramework` change per project plus a package downgrade pass — no architectural rework, because no .NET 10-only APIs are used.
- The Avalonia-portability rule (§2.2) is unaffected.
