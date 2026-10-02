<#
.SYNOPSIS
    Deploys a VSIX to the Visual Studio experimental instance.
.DESCRIPTION
    Installs a VSIX into the selected Visual Studio installation's experimental
    hive, waits for the installer to finish, then clears caches and updates the
    configuration using that installation's devenv.exe.
.PARAMETER Path
    Path to the .vsix file to install.
.PARAMETER InstanceId
    Visual Studio installation instance ID, as reported by vswhere.exe.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path,

    [Parameter(Mandatory = $true, Position = 1)]
    [ValidateNotNullOrEmpty()]
    [string]$InstanceId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    Write-Error "VSIX not found: $Path"
    exit 1
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    Write-Error "vswhere.exe not found: $vswhere"
    exit 1
}

$instances = & $vswhere -all -prerelease -format json -utf8 | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) {
    Write-Error "vswhere.exe failed (exit code $LASTEXITCODE)."
    exit 1
}

$matchingInstances = @($instances | Where-Object { $_.instanceId -eq $InstanceId })
if ($matchingInstances.Count -ne 1) {
    Write-Error "Expected one Visual Studio installation with instance ID '$InstanceId', found $($matchingInstances.Count)."
    exit 1
}

$ideDirectory = Join-Path $matchingInstances[0].installationPath 'Common7\IDE'
$installer = Join-Path $ideDirectory 'VSIXInstaller.exe'
$devenv = Join-Path $ideDirectory 'devenv.exe'
foreach ($executable in @($installer, $devenv)) {
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        Write-Error "Executable not found for Visual Studio instance '$InstanceId': $executable"
        exit 1
    }
}

# --- 1. Install the VSIX ---------------------------------------------------
Write-Host "Installing VSIX: $Path into instance '$InstanceId' ($ideDirectory)" -ForegroundColor Cyan
& $installer "/instanceIds:$InstanceId" /rootSuffix:exp $Path /q
Write-Host "VSIXInstaller.exe launched (exit code $LASTEXITCODE)."
if ($LASTEXITCODE -ne 0) {
    Write-Error "VSIXInstaller.exe failed (exit code $LASTEXITCODE)."
    exit 1
}

# --- 2. Wait for the background installer to finish ------------------------
#   VSIXInstaller.exe returns immediately while a background process
#   completes the actual installation. Poll until it exits.
Write-Host "Waiting for VSIXInstaller.exe to finish..." -ForegroundColor Cyan
while (Get-Process -Name VSIXInstaller -ErrorAction SilentlyContinue) {
    Start-Sleep -Seconds 5
}
Write-Host "VSIX installation complete."

# --- 3. Clear caches and update configuration ------------------------------
Write-Host "Clearing VS experimental instance caches..." -ForegroundColor Cyan
& $devenv /rootsuffix exp /clearcache
if ($LASTEXITCODE -ne 0) {
    Write-Error "devenv.exe /clearcache failed (exit code $LASTEXITCODE)."
    exit 1
}
& $devenv /rootsuffix exp /updateconfiguration
if ($LASTEXITCODE -ne 0) {
    Write-Error "devenv.exe /updateconfiguration failed (exit code $LASTEXITCODE)."
    exit 1
}
Write-Host "Caches cleared and configuration updated." -ForegroundColor Green
