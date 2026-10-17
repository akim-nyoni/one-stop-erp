# ADR-0005 — The desktop client connects to PostgreSQL directly; the server hosts the API, scheduler and webhooks

- **Status:** Accepted
- **Date:** 2026-10-17
- **Deciders:** Product owner + architect

## Context

The brief (§2.2) describes an application server ("Meridian.Server", ASP.NET Core Web API) as the only thing that touches PostgreSQL, with the WPF desktop talking to it over JWT-secured HTTP.

What was built: every business rule lives in `One4All.Application` / `One4All.Infrastructure` and is hosted twice — by the desktop (`App.xaml.cs` builds a `ServiceCollection` with `AddOne4AllInfrastructure` and opens the database itself) and by `One4All.Server` (the same registration behind a Web API with JWT and API-key authentication, the scheduler host and the webhook publisher). The desktop never calls the server; the server exists for third parties, mobile and integration (§6), scheduled jobs and webhooks.

This happened because the desktop was built first against the service layer, and the LAN deployments the product targets (a council office, a shop, a school) run the client and the database on one network with the database a few metres away. Every feature since has been written and tested once, against the service layer, and runs unchanged under both hosts.

## Decision

Keep the direct connection as the **default client mode**, and keep the Web API as a **second host of the same services** rather than the desktop's data path.

- The desktop opens PostgreSQL with the connection string in `appsettings.json` (per-user credentials and roles are enforced by One4All's own permission model; the database role is the application's).
- The server hosts the same services for the API (`/api/...` with JWT or API keys), the scheduler (`SchedulerHost`) and webhooks (`WebhookHost`). Anything the desktop can do, the API can do, because both call the same `IXxxService`.
- A **client mode that routes the desktop through the server** (an `IXxxService` implementation per interface that calls the API, selected by configuration) is the planned way to serve a branch over a WAN or a locked-down site. The service interfaces are the seam; no screen or view model knows which host it is talking to.

## Consequences

- One code path to test: the accounting test suite runs every rule once, against the service layer, and both hosts inherit it.
- A LAN install needs no application server process to be running for the desktop to work; the server is installed where scheduled jobs, the API or webhooks are wanted (usually one per site).
- Database credentials are present on each desktop machine's `appsettings.json` (protected by the machine's file permissions); a site that cannot accept that is the case for the API client mode above.
- The offline POS design (local queue, sealed snapshot, outbox and sync) is unaffected: it already assumes the till may lose its connection, whichever host it talks to.
- When the API client mode is built, the API surface must cover every service the desktop uses; `ApiReportDataProvider` and the operations console's dependence on `One4AllDbContext` for its lists (`OperationContext.Db`) are the two places that would need an API-backed equivalent.
