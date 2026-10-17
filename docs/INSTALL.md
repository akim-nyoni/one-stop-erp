# Installing One4All

## What ships

`installer/build.ps1 -Version x.y.z` publishes three things into `artifacts/publish/` and, when Inno Setup 6 is installed, builds the
desktop setup program in `artifacts/installer/`:

| Folder | What it is | How it is deployed |
| --- | --- | --- |
| `desktop/` | The Windows desktop client, self-contained (no .NET install needed) | `One4All-x.y.z-setup.exe` (Inno Setup), or copy the folder |
| `server/` | The server (REST API, scheduler, webhooks, e-mail and SMS despatch), self-contained | `server-install.ps1` registers it as a Windows service |
| `licensing/` | The licence generator as a single executable for the licensing desk | copy `One4All.Licensing.Generator.exe` |

## Prerequisite: PostgreSQL

One4All stores everything in PostgreSQL 16 or later (18 is what development runs on). Install it on the server machine or point the
connection string at a server that runs it. Create a login for One4All that may create databases (the first start creates the database)
or create the empty database yourself and give the login rights on it.

## Server as a Windows service

On the server machine, as administrator, in the published `server/` folder:

```powershell
.\server-install.ps1 -Connection "Host=localhost;Port=5432;Database=one4all;Username=one4all;Password=<secret>" -Port 5000
```

The script checks that PostgreSQL answers on the host and port, writes the connection string and the port into `appsettings.json` beside
the executable (readable by SYSTEM and administrators only), registers the service **One4All Server** to start automatically with restart on
failure, and starts it. The first start:

1. creates the database when the server has none of that name (`DatabaseBootstrap`),
2. applies every migration,
3. seeds the permission catalogue, the administrator (`admin` / `Admin@123`, to be changed at first login) and the standard message templates.

Logs go to the `logs` folder beside the executable and to the `sys.app_log` table. `.\server-install.ps1 -Uninstall` removes the service
and leaves the database and files.

## Desktop

Run the setup program on each workstation. The desktop reads its connection string from `appsettings.json` beside the executable (or the
`ONE4ALL_CONNECTION` environment variable); point it at the same database as the server. A workstation with its own local PostgreSQL also
gets first-run database creation.

## Licensing tool

`One4All.Licensing.Generator.exe` issues and binds licences and signs update manifests; it needs no runtime install. See `docs/licensing/`.

## Upgrades

Run the new setup program over the old installation (settings beside the app are kept) and copy the new `server/` folder over the old one,
then restart the service; migrations apply at the next start. Take a backup first (System Manager › Back up or restore the database).
