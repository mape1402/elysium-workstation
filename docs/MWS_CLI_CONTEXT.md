# MyWorkStation CLI quick context for AI agents

This file is the fast-start context for agents that need to operate MyWorkStation from a terminal.

## Mental model

MyWorkStation exposes a shared Engine used by both the visual App and the CLI.

```text
mws CLI  ->  MyWorkStation Engine  <-  MAUI App UI
```

The CLI does not talk directly to SQLite and does not duplicate sync logic. It sends commands to the running app through a local Named Pipe owned by the Engine Host.

## Vocabulary

Use these names exactly to avoid confusing MyWorkStation operations with Git operations:

- `folder sync`: file synchronization between two configured folders.
- `MWS terminal`: command execution on another MyWorkStation PC.
- `sync-linked terminal`: MWS terminal that uses an accepted folder sync link only to pick the remote working directory. File transfer does not need to be started.
- `peer terminal`: MWS terminal that targets another connected MyWorkStation PC without a folder sync link.
- `Git remote`: only the Git concept/origin/upstream. Do not call MWS terminal commands "git remote".

Legacy `mws remote ...` commands still exist for compatibility, but new automation should prefer `mws terminal ...`.

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
- `sync start` and `sync stop` control continuous file transfer only.
- `sync force` uses the same folder sync service as the visual force button.
- `sync logs` prints a human readable tail and supports JSON output.
- The app also reads `.gitignore` from the root synchronized folder when present.

## MWS terminal commands

The MWS terminal executes commands on another connected MyWorkStation PC.

### Sync-linked terminal

Use this when you want the command to run in the destination folder of an accepted folder sync link. The folder sync link does not need continuous file transfer to be started.

```powershell
mws terminal exec --sync-id 22 -- git status
mws terminal exec 22 -- git status
mws terminal exec --sync-id 22 --timeout 60 -- dotnet build
mws terminal shell --sync-id 22
mws terminal stop --sync-id 22 --session <sessionId>
```

Rules:

- The local side must be the emitter for that accepted folder sync link.
- `sync start` is not required. The link is used only to know the remote working folder.
- Prefer `terminal exec` for AI automation.
- Use `--timeout <seconds>` for long-running commands.
- If timeout is reached, the Engine sends an interrupt and returns exit code `124`.

### Peer terminal without folder sync

Use this when you only want to run a command on another connected MyWorkStation PC.

```powershell
mws terminal peers
mws terminal peers --json
mws terminal exec --peer Laptop2 -- hostname
mws terminal exec --peer Laptop2 --cwd C:\Work\Repo -- git status
mws terminal shell --peer Laptop2 --cwd C:\Work\Repo
mws terminal stop --peer Laptop2 --session <sessionId>
```

Rules:

- Use `mws terminal peers` to discover available targets.
- If exactly one peer is connected, `--peer` can be omitted.
- Use `--cwd` to choose the remote working directory. If omitted or invalid, the receiver uses its user profile folder.
- This mode does not require a folder sync link.

### Compatibility commands

These legacy commands still work, but do not use them for new agent workflows:

```powershell
mws remote exec --sync-id 22 -- git status
mws remote shell --sync-id 22
mws remote stop --sync-id 22 --session <sessionId>
```

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

Git on another PC should normally go through MWS terminal:

```powershell
mws terminal exec --sync-id 22 -- git status
mws terminal exec --sync-id 22 -- git pull
mws terminal exec --sync-id 22 -- git add .
mws terminal exec --sync-id 22 -- git commit -m "Commit message"
mws terminal exec --sync-id 22 -- git push
mws terminal exec --sync-id 22 -- git checkout -b feature/demo
```

Existing Git shortcuts are still supported:

```powershell
mws git status --remote --sync-id 22
mws git pull --remote --sync-id 22
mws git add . --remote --sync-id 22
mws git commit -m "Commit message" --remote --sync-id 22
mws git push --remote --sync-id 22
```

## Clipboard commands

```powershell
mws clipboard send --text "texto para la otra PC"
mws clipboard send -- "texto libre con espacios"
mws clipboard send --current
```

`clipboard send` sends text through the existing clipboard sync channel. It requires the app runtime to be connected to the hub.

## File transfer commands

```powershell
mws files send C:\Temp\a.txt
mws files send C:\Temp\a.txt C:\Temp\b.txt
```

This uses the existing file transfer service and is independent from folder sync.

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

`workflow remote-build` runs `dotnet build` on the destination folder of the sync link. Future workflows should prefer `terminal` naming.

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
mws alias set cleanbuild terminal exec --sync-id {0} -- dotnet clean
mws alias remove cleanbuild
```

Default aliases:

```text
lsync    -> sync list
fsync    -> sync force --id {0}
slogs    -> sync logs --id {0} --tail 50
tpeers   -> terminal peers
texec    -> terminal exec --sync-id {0} -- {1}
tpeer    -> terminal exec --peer {0} -- {1}
tstatus  -> terminal exec --sync-id {0} -- git status
tbuild   -> terminal exec --sync-id {0} -- dotnet build
ttest    -> terminal exec --sync-id {0} -- dotnet test
tgit     -> terminal exec --sync-id {0} -- git {1}
cliptext -> clipboard send --text {0}
```

Legacy `rexec`, `rstatus`, `rbuild`, `rtest`, and `rgit` may exist for compatibility. Prefer the `t*` aliases.

Examples:

```powershell
mws lsync
mws fsync 22
mws tpeers
mws tstatus 22
mws tbuild 22
mws tgit 22 "status --short"
mws texec 22 "dotnet test"
mws tpeer Laptop2 "hostname"
mws cliptext "hola desde mws"
```

Alias placeholders use zero-based arguments: `{0}`, `{1}`, `{2}`.

## Agent recommendations

- First call `mws status --json`.
- If the app is not running, ask the user to open MyWorkStation or open it if the environment allows GUI apps.
- Use `mws sync list --json` to discover folder sync link ids.
- Use `mws terminal peers --json` to discover PC targets for peer terminal.
- Prefer `mws terminal exec --sync-id <id> -- <command>` for commands in the paired sync folder.
- Prefer `mws terminal exec --peer <peer> --cwd <path> -- <command>` for commands not tied to a sync folder.
- Prefer `--json` for machine decisions and plain output for user-facing summaries.
- Avoid destructive commands unless the user explicitly asked for them.
- For long-running commands, set `--timeout` and be ready to call `mws terminal stop` with the returned session id.

## Local simulation

The repo includes a smoke simulation for AI agents and developers:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Simulate-MwsCli.ps1
```

It builds against Debug outputs already present, copies `mws.exe` and `mws-engine-host.exe` beside `MyWorkStation.exe`, starts two local app instances with isolated pipes, creates dummy sender/receiver folders, pairs them, forces sync in both directions, validates `.gitignore`, executes terminal commands, interrupts a long-running command by timeout, verifies terminal execution still works after interruption, and confirms the spawned app/host processes exit cleanly.

For manual multi-instance testing:

```powershell
$env:MWS_ENGINE_PIPE = "Elysium.WorkStation.Engine.Test.Server"
$env:MWS_DEBUG_ROLE = "server"
$env:MWS_SERVER_URL = "http://localhost:5197"
```

Use a different `MWS_ENGINE_PIPE` for the client instance, then set the same variable before each `mws` command to target that instance.
