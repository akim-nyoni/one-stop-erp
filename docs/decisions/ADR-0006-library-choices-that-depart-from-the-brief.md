# ADR-0006 - Library choices that depart from the brief

- **Status:** Accepted
- **Date:** 2026-10-17
- **Deciders:** Product owner + architect

## Context

The brief (§2.2) names MediatR (command/query separation), FluentValidation, Mapster, Hangfire (Postgres-backed background jobs), Testcontainers.PostgreSql (integration tests) and Bogus (seeding). None of them is referenced. Each was left out for a reason that still holds; this record keeps the reasons where the next person can find them.

## Decisions

**MediatR - not used.** Each module exposes an interface (`IPostingEngine`, `IBillingRunService`, `IAllocationService` …) whose methods are the commands and queries, returning `Result` / `Result<T>`. A screen, an API endpoint, a scheduled job and a test call the same method. A mediator would add a request class, a handler class and a pipeline per method without separating anything that is not already separated by the interface; the Result pattern and `AtomicAsync` units of work give the cross-cutting behaviour (failure, transactions) the brief wanted from pipeline behaviours.

**FluentValidation - not used.** Validation lives in the entities (constructors and methods throw or return `Error.Validation`) and in the services (`Error.Validation` / `Error.Rule` results), so the same rules apply whichever host calls. Request objects are simple records; a validator class per request would duplicate rules that must also hold for callers that bypass the request (importers, replayed offline operations, scheduled jobs).

**Mapster - not used.** Report rows, document models and API responses are built by hand (`Row(...)`, records), which keeps the shape of every output explicit and greppable; there are no entity-to-DTO mappings large enough to justify a mapper.

**Hangfire - not used; `ScheduledJob` + `IJobScheduler` + `SchedulerHost` instead.** Scheduled work (billing runs, statement runs, nightly sweeps, notices, partition maintenance) is a table of `ScheduledJob` rows (`IJobScheduler`: define, enable, run due, run now, history) run by `SchedulerHost` in the server process, each job an `IScheduledTask` that is idempotent and resumable as §15 requires, with its own run log. Hangfire's dashboard, storage schema and retry semantics would sit beside a scheduler the product already explains to its users; a site that wants Hangfire's dashboard can host the same `IScheduledTask`s under it later.

**Testcontainers.PostgreSql - not used; a local PostgreSQL instead.** The accounting and integration tests run against the PostgreSQL named by `ONE4ALL_TEST_CONNECTION` / `ONE4ALL_CONNECTION` (the fixture migrates it and every test seeds its own company, so tests are independent and the database is reused). Docker is not present on the build machine and the tests number over seven hundred; a container per run would add minutes to every verify for no isolation the per-company seeding does not already give. A CI job that has Docker can point the same variable at a container.

**Bogus - not used; `SampleDataService` and the `Scenario` seeders instead.** Demo and test data are written by hand so the figures in the demos and the tests are meaningful and stable (a worked example, not random noise), and the industry setup wizard seeds its demo company from the same code.

**Serilog - adopted (ADR-0007).** The brief's logging stack is now in place; it had been left at `Microsoft.Extensions.Logging` defaults.

## Consequences

- Fewer moving parts and dependencies; the service interfaces are the architecture, and every host (desktop, API, scheduler, tests) uses them the same way.
- Anyone expecting MediatR handlers or FluentValidation validators will find the equivalent in the service method and the entity; this record is the signpost.
- If a future requirement needs what one of these libraries gives (a pipeline behaviour for every call, a job dashboard), it can be added at the host without touching the modules.
