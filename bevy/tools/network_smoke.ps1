param(
    [Parameter(Mandatory = $true)][string]$Driver,
    [Parameter(Mandatory = $true)][string]$DatabaseDirectory,
    [string]$Address = "127.0.0.1:43127",
    [string]$Secret = "smoke-secret",
    [string]$Actor = "smoke-actor",
    [string]$Site = "smoke-site",
    [int]$TimeoutSeconds = 30,
    [switch]$DriverHelp
)

$ErrorActionPreference = "Stop"
if ($DriverHelp) {
    Write-Output "Driver contract: --smoke-host --database PATH --address HOST:PORT --secret SECRET --actor ACTOR --site SITE"
    Write-Output "               --smoke-client --database PATH --address HOST:PORT --secret SECRET --site SITE"
    Write-Output "Host prints HOST_READY only after listening; client prints CLIENT_BASELINE after receiving its authorized baseline."
    exit 0
}

$driverPath = (Resolve-Path $Driver).Path
New-Item -ItemType Directory -Force -Path $DatabaseDirectory | Out-Null
$dbRoot = (Resolve-Path $DatabaseDirectory).Path
$hostDb = Join-Path $dbRoot "host.sqlite"
$clientDb = Join-Path $dbRoot "client.sqlite"
$hostLog = Join-Path $dbRoot "host.log"
$clientLog = Join-Path $dbRoot "client.log"
Remove-Item $hostDb, $clientDb, $hostLog, $clientLog -Force -ErrorAction SilentlyContinue

$hostArgs = @("--smoke-host", "--database", $hostDb, "--address", $Address, "--secret", $Secret, "--actor", $Actor, "--site", $Site)
$clientArgs = @("--smoke-client", "--database", $clientDb, "--address", $Address, "--secret", $Secret, "--site", $Site)
$hostProcess = Start-Process -FilePath $driverPath -ArgumentList $hostArgs -PassThru -NoNewWindow -RedirectStandardOutput $hostLog -RedirectStandardError "$hostLog.err"
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($hostProcess.HasExited) { throw "Host exited early (code $($hostProcess.ExitCode)); see $hostLog" }
        if ((Test-Path $hostLog) -and (Select-String -Path $hostLog -Pattern '^HOST_READY$' -Quiet)) { break }
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path $hostLog) -or -not (Select-String -Path $hostLog -Pattern '^HOST_READY$' -Quiet)) { throw "Timed out waiting for HOST_READY; see $hostLog" }

    $clientProcess = Start-Process -FilePath $driverPath -ArgumentList $clientArgs -PassThru -NoNewWindow -Wait -RedirectStandardOutput $clientLog -RedirectStandardError "$clientLog.err"
    if ($clientProcess.ExitCode -ne 0) { throw "Client failed (code $($clientProcess.ExitCode)); see $clientLog" }
    if (-not (Select-String -Path $clientLog -Pattern '^CLIENT_BASELINE$' -Quiet)) { throw "Client did not confirm baseline; see $clientLog" }
    Write-Output "PASS: separate host/client processes received an authorized site baseline. Logs: $dbRoot"
}
finally {
    if (-not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id -Force }
    Wait-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue
}
