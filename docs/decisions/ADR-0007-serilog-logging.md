# ADR-0007 - Serilog to a daily file and the sys.app_log table

- **Status:** Accepted
- **Date:** 2026-10-17
- **Deciders:** Product owner + architect

## Context

The brief (§2.2) asks for Serilog writing to a rolling file, a Postgres table and optionally Seq. Until now both hosts used `Microsoft.Extensions.Logging` with no provider configured beyond the defaults, so nothing was kept anywhere a support person could read.

## Decision

`One4AllLogging.AddOne4AllLogging(connectionString, host, configuration, logDirectory)` (in `One4All.Infrastructure.Logging`) builds the Serilog logger and makes it the only logging provider for the desktop and the server:

- **Rolling file** (`Serilog.Sinks.File`): one file per day per host, `one4all-<host>-<date>.log`, 31 days kept, shared so several desktop processes on one machine can write. The folder is `Logging:Directory` when set, else the user's local One4All folder (desktop) or `logs` beside the server.
- **Postgres table** `sys.app_log` (`AppLogEntry`): the product's own sink, `PostgresLogSink`, queues events and writes them in batches from a background drain through Npgsql, so logging never blocks a screen or a request and a database that is down or not yet migrated loses log lines (noted in Serilog's self-log) rather than failing the host. Information and above go to the table; Microsoft and EF Core noise is kept to warnings. The System Manager screen **Application log** lists the latest lines by level and text and removes lines older than the days kept.
- **Seq, console or anything else**: the host's `Serilog` configuration section is applied over the defaults (`Serilog.Settings.Configuration`), so a site that references `Serilog.Sinks.Seq` in the host and adds a `WriteTo` entry gets Seq without a code change. The Seq package is not shipped by default.

## Consequences

- One table to read across every machine on a site; the files remain as the per-machine fallback when the database is unreachable.
- The same `ILogger<T>` calls everywhere; nothing in Application or the modules references Serilog.
- The table grows with use: the Application log screen's Run removes old lines (default 90 days), and a scheduled job can call the same removal nightly.
