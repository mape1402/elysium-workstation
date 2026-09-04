param(
    [string]$ServerUrl = "http://localhost:5197"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$appOut = Join-Path $root "src\Elysium.WorkStation\bin\Debug\net9.0-windows10.0.19041.0\win10-x64"
$appExe = Join-Path $appOut "MyWorkStation.exe"
$cliExe = Join-Path $appOut "mws.exe"
$cliOut = Join-Path $root "src\Elysium.WorkStation.Cli\bin\Debug\net9.0"
$hostOut = Join-Path $root "src\Elysium.WorkStation.Engine.Host\bin\Debug\net9.0"

if (-not (Test-Path $appExe)) {
    throw "MyWorkStation.exe not found. Build Debug first."
}

Copy-Item -Path (Join-Path $cliOut "*") -Destination $appOut -Recurse -Force
Copy-Item -Path (Join-Path $hostOut "*") -Destination $appOut -Recurse -Force

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$simRoot = Join-Path $root ("artifacts\cli-roadmap-sim\" + $stamp)
$sender = Join-Path $simRoot "sender"
$receiver = Join-Path $simRoot "receiver"
New-Item -ItemType Directory -Force -Path $sender | Out-Null
New-Item -ItemType Directory -Force -Path $receiver | Out-Null

$serverPipe = "Elysium.WorkStation.Engine.Sim.Server.$stamp"
$clientPipe = "Elysium.WorkStation.Engine.Sim.Client.$stamp"
$startedPids = New-Object System.Collections.Generic.List[int]

function Start-MwsInstance([string]$role, [string]$pipe) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $appExe
    $psi.WorkingDirectory = $appOut
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables["MWS_DEBUG_ROLE"] = $role
    $psi.EnvironmentVariables["MWS_SERVER_URL"] = $ServerUrl
    $psi.EnvironmentVariables["MWS_ENGINE_PIPE"] = $pipe
    $process = [System.Diagnostics.Process]::Start($psi)
    $script:startedPids.Add($process.Id)
    return $process
}

function Invoke-Mws([string]$pipe, [string[]]$arguments, [switch]$Json) {
    $oldPipe = $env:MWS_ENGINE_PIPE
    $env:MWS_ENGINE_PIPE = $pipe
    try {
        $allArgs = @($arguments)
        if ($Json) {
            $allArgs += "--json"
        }

        $output = & $cliExe @allArgs 2>&1
        $exit = $LASTEXITCODE
        $raw = $output -join "`n"
        return [pscustomobject]@{
            ExitCode = $exit
            Raw = $raw
            Json = if ($Json -and -not [string]::IsNullOrWhiteSpace($raw)) { $raw | ConvertFrom-Json } else { $null }
        }
    }
    finally {
        $env:MWS_ENGINE_PIPE = $oldPipe
    }
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

function Wait-Until([scriptblock]$predicate, [string]$label, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $last = $null
    while ((Get-Date) -lt $deadline) {
        $last = & $predicate
        if ($last) {
            return $last
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Timeout waiting for $label. Last value: $last"
}

function Wait-MwsStatus([string]$pipe, [string]$label) {
    Wait-Until {
        $result = Invoke-Mws $pipe @("status") -Json
        if ($result.ExitCode -ne 0 -or $null -eq $result.Json) {
            return $null
        }

        if ($result.Json.success -eq $true -and
            $result.Json.data.role -ne "Undetermined" -and
            $result.Json.data.engine.externalHostRunning -eq $true -and
            $result.Json.data.services.folderSyncConnected -eq $true) {
            return $result.Json
        }

        return $null
    } $label 40
}

function Close-Mws([string]$pipe) {
    try {
        [void](Invoke-Mws $pipe @("app", "exit") -Json)
    }
    catch {
    }
}

$server = $null
$client = $null
$serverHostPid = $null
$clientHostPid = $null

try {
    $server = Start-MwsInstance "server" $serverPipe
    Start-Sleep -Seconds 2
    $client = Start-MwsInstance "client" $clientPipe

    $serverStatus = Wait-MwsStatus $serverPipe "server status"
    $clientStatus = Wait-MwsStatus $clientPipe "client status"
    $serverHostPid = $serverStatus.data.engine.externalHostProcessId
    $clientHostPid = $clientStatus.data.engine.externalHostProcessId

    $create = Invoke-Mws $serverPipe @("sync", "create", "--name", "Dummy-$stamp", "--path", $sender) -Json
    Assert-True ($create.ExitCode -eq 0 -and $create.Json.success -eq $true) "sync create failed: $($create.Raw)"
    $linkId = [string]$create.Json.data.id
    $syncId = [string]$create.Json.data.syncId

    $invite = Invoke-Mws $serverPipe @("sync", "invite", $linkId) -Json
    Assert-True ($invite.ExitCode -eq 0 -and $invite.Json.success -eq $true) "sync invite failed: $($invite.Raw)"

    $pending = Wait-Until {
        $invites = Invoke-Mws $clientPipe @("sync", "invites") -Json
        if ($invites.ExitCode -eq 0 -and $invites.Json.success -eq $true) {
            $match = @($invites.Json.data | Where-Object { $_.syncId -eq $syncId })
            if ($match.Count -gt 0) {
                return $match[0]
            }
        }

        return $null
    } "client invite" 30

    $accept = Invoke-Mws $clientPipe @("sync", "accept", "--sync-id", $syncId, "--path", $receiver) -Json
    Assert-True ($accept.ExitCode -eq 0 -and $accept.Json.success -eq $true) "sync accept failed: $($accept.Raw)"

    [void](Wait-Until {
        $status = Invoke-Mws $serverPipe @("sync", "status", $linkId) -Json
        if ($status.ExitCode -eq 0 -and $status.Json.data.isAccepted -eq $true) {
            return $status.Json
        }

        return $null
    } "server link accepted" 30)

    $start = Invoke-Mws $serverPipe @("sync", "start", $linkId) -Json
    Assert-True ($start.ExitCode -eq 0 -and $start.Json.success -eq $true) "sync start failed: $($start.Raw)"

    [void](Wait-Until {
        $status = Invoke-Mws $clientPipe @("sync", "status", $syncId) -Json
        if ($status.ExitCode -eq 0 -and $status.Json.data.continuousSyncEnabled -eq $true) {
            return $status.Json
        }

        return $null
    } "client continuous sync enabled" 30)

    Set-Content -Path (Join-Path $sender ".gitignore") -Value @("ignored/", "*.tmp")
    New-Item -ItemType Directory -Force -Path (Join-Path $sender "ignored") | Out-Null
    Set-Content -Path (Join-Path $sender "hello.txt") -Value "hello from sender $stamp"
    Set-Content -Path (Join-Path $sender "skip.tmp") -Value "must stay ignored"
    Set-Content -Path (Join-Path $sender "ignored\hidden.txt") -Value "must stay ignored"

    $force = Invoke-Mws $serverPipe @("sync", "force", $linkId) -Json
    Assert-True ($force.ExitCode -eq 0 -and $force.Json.success -eq $true) "sync force failed: $($force.Raw)"

    [void](Wait-Until {
        if (Test-Path (Join-Path $receiver "hello.txt")) {
            return $true
        }

        return $null
    } "receiver hello.txt" 30)

    Assert-True (-not (Test-Path (Join-Path $receiver "skip.tmp"))) ".gitignore failed: skip.tmp was copied"
    Assert-True (-not (Test-Path (Join-Path $receiver "ignored\hidden.txt"))) ".gitignore failed: ignored/hidden.txt was copied"

    $remote = Invoke-Mws $serverPipe @("remote", "exec", $linkId, "--timeout", "20", "--", "Write-Output `"remote-ok`"; Get-Location | Select-Object -ExpandProperty Path") -Json
    Assert-True ($remote.ExitCode -eq 0 -and $remote.Json.success -eq $true) "remote exec failed: $($remote.Raw)"
    Assert-True ($remote.Json.standardOutput -like "*remote-ok*") "remote exec did not return remote-ok: $($remote.Raw)"
    Assert-True ($remote.Json.standardOutput -like "*$receiver*") "remote exec did not run in receiver folder: $($remote.Raw)"

    $timeout = Invoke-Mws $serverPipe @("remote", "exec", $linkId, "--timeout", "5", "--", "ping 127.0.0.1 -t") -Json
    Assert-True ($timeout.ExitCode -eq 124 -and $timeout.Json.timedOut -eq $true) "remote timeout/interrupt failed: $($timeout.Raw)"

    $afterTimeout = Invoke-Mws $serverPipe @("remote", "exec", $linkId, "--timeout", "20", "--", "Write-Output `"after-timeout-ok`"") -Json
    Assert-True ($afterTimeout.ExitCode -eq 0 -and $afterTimeout.Json.standardOutput -like "*after-timeout-ok*") "remote exec after timeout failed: $($afterTimeout.Raw)"

    $switch = Invoke-Mws $serverPipe @("sync", "switch-role", $linkId) -Json
    Assert-True ($switch.ExitCode -eq 0 -and $switch.Json.success -eq $true) "sync switch-role failed: $($switch.Raw)"

    [void](Wait-Until {
        $status = Invoke-Mws $clientPipe @("sync", "status", $syncId) -Json
        if ($status.ExitCode -eq 0 -and $status.Json.data.isEmitter -eq $true) {
            return $status.Json
        }

        return $null
    } "client emitter after switch" 30)

    Set-Content -Path (Join-Path $receiver "back.txt") -Value "hello from receiver $stamp"
    $clientForce = Invoke-Mws $clientPipe @("sync", "force", $syncId) -Json
    Assert-True ($clientForce.ExitCode -eq 0 -and $clientForce.Json.success -eq $true) "client sync force failed: $($clientForce.Raw)"

    [void](Wait-Until {
        if (Test-Path (Join-Path $sender "back.txt")) {
            return $true
        }

        return $null
    } "sender back.txt" 30)

    [pscustomobject]@{
        success = $true
        simRoot = $simRoot
        sender = $sender
        receiver = $receiver
        serverPid = $server.Id
        clientPid = $client.Id
        serverHostPid = $serverHostPid
        clientHostPid = $clientHostPid
        linkId = $linkId
        syncId = $syncId
        checks = @(
            "server/client roles and SignalR connected",
            "external engine hosts running",
            "sync create/invite/accept via CLI",
            "force sync sender to receiver",
            ".gitignore respected",
            "remote exec streams output and working directory",
            "long command timeout sends interrupt",
            "remote exec works after timeout",
            "role switch and reverse force sync"
        )
    } | ConvertTo-Json -Depth 6
}
finally {
    if ($serverPipe) { Close-Mws $serverPipe }
    if ($clientPipe) { Close-Mws $clientPipe }
    Start-Sleep -Seconds 4

    $ids = @($server?.Id, $client?.Id, $serverHostPid, $clientHostPid) | Where-Object { $_ }
    $remaining = @()
    foreach ($id in $ids) {
        $process = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($process) {
            $remaining += $process | Select-Object ProcessName, Id, Path
        }
    }

    if ($remaining.Count -gt 0) {
        Write-Error ("Processes still running after app exit: " + (($remaining | ConvertTo-Json -Depth 4) -replace "`r?`n", " "))
    }
}
