# MyWorkStation CLI quick context for AI agents

This file is the fast-start context for agents that need to operate MyWorkStation from a terminal.

## Mental model

MyWorkStation now exposes a shared Engine that is used by both the visual App and the CLI.

```text
mws CLI  ->  MyWorkStation Engine  <-  MAUI App UI
```

The CLI does not talk directly to SQLite and does not duplicate sync logic. It sends commands to the running app through a local Named Pipe owned by the Engine Host.

## Requirements

- The visual MyWorkStation app must be running for Engine-backed commands.
- The CLI executable is `mws.exe`.
- The companion Engine Host executable is `mws-engine-host.exe`; it lives beside the app and exits when the owning app exits.
- The app can register the installed app folder in the user PATH from `Configuracion > CLI > Registrar CLI en PATH`.
- Use `--json` when another agent needs structured output.
- Advanced/local testing can target a specific Engine pipe with `MWS_ENGINE_PIPE`.

Local-only CLI commands that do not require the app:

```powershell
mws version
mws alias list
mws alias path
mws alias init
mws alias set <name> <template>
mws alias remove <name>
```

## Health commands

```powershell
mws status
mws status --json
mws doctor
mws doctor --json
```

`status` returns app version, process id, role, Engine pipe, runtime state, configured server URL, service connection state and sync counts.

`doctor` returns a safe diagnostic summary. If the app is not running, the CLI exits with code `2` and tells the caller to open MyWorkStation.

## Config commands

```powershell
mws config list
mws config get server-url
mws config get theme
mws config set theme Dark
mws config set server-url http://localhost:5001
```

Supported keys:

```text
server-url
theme
db-path
signalr-reconnect-minutes
```

## Folder sync commands

```powershell
mws sync create --name Demo --path C:\Work\DemoSender
mws sync invite --id 22
mws sync invites
mws sync accept --sync-id <syncId> --path C:\Work\DemoReceiver
mws sync reject --sync-id <syncId>
mws sync delete --id 22
mws sync list
mws sync list --json
mws sync status --id 22
mws sync status 22
mws sync force --id 22
mws sync force 22
mws sync start --id 22
mws sync stop --id 22
mws sync switch-role --id 22
mws sync logs --id 22 --tail 50
mws sync summary --id 22
```

Notes:

- `--id` and `--sync-id` can be the local numeric link id or the shared `SyncId`.
- `sync create` creates the local sync record.
- `sync invite` broadcasts the pairing request through the existing SignalR hub.
- `sync invites` lists incoming invitations on the receiver.
- `sync accept` creates the receiver-side link and replies to the emitter.
- `sync force` uses the same folder sync service as the visual button.
- `sync logs` prints a human readable tail and supports JSON output.

## Ignore path commands

```powershell
mws sync ignores list --id 22
mws sync ignores add --id 22 --path bin
mws sync ignores remove --id 22 --path bin
```

The app also reads `.gitignore` from the root synchronized folder when present.

## Remote execution commands

```powershell
mws remote exec --sync-id 22 -- "git status"
mws remote exec 22 -- git status
mws remote exec --sync-id 22 --timeout 30 -- "dotnet build"
mws remote shell --sync-id 22
mws remote stop --sync-id 22 --session <sessionId>
```

Remote execution uses the existing remote terminal channel over SignalR. The command runs on the destination PC in the synchronized working folder owned by that remote side.

Important behavior:

- The local side must be the emitter for that folder sync link, matching the visual remote terminal behavior.
- Each execution gets a session id.
- The response contains `stdout`, `stderr`, `exitCode` and `sessionId` in JSON mode.
- If timeout is reached, the Engine sends a remote interrupt and returns exit code `124`.
- Long-running commands should be given an explicit timeout.
- `remote shell` opens a simple command loop over one remote terminal session. It is useful for repeated commands, but use `remote exec --json` for reliable agent automation.
- In `remote shell`, Ctrl+C sends `remote stop` for the active session.

## Git commands

Local Git through the Engine:

```powershell
mws git status
mws git fetch
mws git pull
mws git add .
mws git commit -m "Commit message"
mws git push
mws git log --oneline
mws git diff
mws git branch create feature/demo
```

Remote Git through the remote terminal:

```powershell
mws git status --remote --sync-id 22
mws git pull --remote --sync-id 22
mws git add . --remote --sync-id 22
mws git commit -m "Commit message" --remote --sync-id 22
mws git push --remote --sync-id 22
mws git branch create feature/demo --remote --sync-id 22
```

For unusual Git operations, prefer `remote exec`:

```powershell
mws remote exec --sync-id 22 -- "git checkout -b feature/demo"
```

## File transfer commands

```powershell
mws files send C:\Temp\a.txt
mws files send C:\Temp\a.txt C:\Temp\b.txt
```

This uses the existing file transfer service.

## Update commands

```powershell
mws update check
mws update check --json
mws update install
```

`update install` uses the same updater as the visual app. The updater downloads the latest GitHub Release asset, launches an external PowerShell applicator, shows a progress window while the app is closed, copies the files and restarts MyWorkStation.

If GitHub cannot be reached, the app and CLI should not crash; they return that updates cannot be checked right now.

## Workflow commands

```powershell
mws workflow pull-sync --sync-id 22 --branch main
mws workflow pull-sync-send --sync-id 22 --branch main
mws workflow remote-build --sync-id 22
```

`workflow pull-sync` does:

```text
1. Optional git checkout <branch> in the local synchronized folder.
2. git pull in the local synchronized folder.
3. Force folder sync for that link.
```

`workflow remote-build` runs:

```powershell
dotnet build
```

on the remote synchronized folder.

## Alias commands

Aliases live at:

```powershell
%LOCALAPPDATA%\MyWorkStation\cli\aliases.json
```

Useful commands:

```powershell
mws alias list
mws alias path
mws alias init
mws alias set rclean remote exec --sync-id {0} -- git clean -fdx
mws alias remove rclean
```

Default aliases:

```text
lsync   -> sync list
fsync   -> sync force --id {0}
slogs   -> sync logs --id {0} --tail 50
rexec   -> remote exec --sync-id {0} -- {1}
rstatus -> remote exec --sync-id {0} -- git status
rbuild  -> remote exec --sync-id {0} -- dotnet build
rtest   -> remote exec --sync-id {0} -- dotnet test
rgit    -> remote exec --sync-id {0} -- git {1}
```

Examples:

```powershell
mws lsync
mws fsync 22
mws rstatus 22
mws rbuild 22
mws rgit 22 "status --short"
mws rexec 22 "dotnet test"
```

Alias placeholders use zero-based arguments: `{0}`, `{1}`, `{2}`.

## Agent recommendations

- First call `mws status --json`.
- If the app is not running, ask the user to open MyWorkStation or open it if the environment allows GUI apps.
- Use `mws sync list --json` to discover valid sync ids.
- Use numeric link ids for short commands and `SyncId` for logs/auditing if needed.
- Prefer `mws remote exec --sync-id <id> -- "command"` for arbitrary remote CLI tools.
- Prefer `--json` for machine decisions and plain output for user-facing summaries.
- Avoid destructive commands unless the user explicitly asked for them.
- For long-running commands, set `--timeout` and be ready to call `mws remote stop` with the returned session id.

## Local simulation

The repo includes a smoke simulation for AI agents and developers:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Simulate-MwsCli.ps1
```

It builds against Debug outputs already present, copies `mws.exe` and `mws-engine-host.exe` beside `MyWorkStation.exe`, starts two local app instances with isolated pipes, creates dummy sender/receiver folders, pairs them, forces sync in both directions, validates `.gitignore`, executes remote commands, interrupts a long-running command by timeout, verifies remote execution still works after interruption, and confirms the spawned app/host processes exit cleanly.

For manual multi-instance testing:

```powershell
$env:MWS_ENGINE_PIPE = "Elysium.WorkStation.Engine.Test.Server"
$env:MWS_DEBUG_ROLE = "server"
$env:MWS_SERVER_URL = "http://localhost:5197"
```

Use a different `MWS_ENGINE_PIPE` for the client instance, then set the same variable before each `mws` command to target that instance.
