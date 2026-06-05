<#
.SYNOPSIS
    SBPR.Trailborne developer setup (PowerShell).

.DESCRIPTION
    Helps a fresh developer get to a green build on Windows (or pwsh anywhere):
      1. Verifies prerequisites (.NET SDK).
      2. Locates the Valheim "Managed" assembly folder (or accepts a hint).
      3. Ensures the BepInEx core folder exists (points at fetch-sdk.ps1).
      4. Writes / updates a gitignored repo-root .env with the resolved paths.

    Safe to re-run (idempotent). Existing valid .env values are kept unless you
    override them via -ValheimManaged / -ValheimInstall / -BepInExCore or env vars.

.EXAMPLE
    pwsh scripts/setup.ps1

.EXAMPLE
    pwsh scripts/setup.ps1 -ValheimInstall "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
#>
[CmdletBinding()]
param(
    [string]$ValheimManaged = $env:VALHEIM_MANAGED,
    [string]$ValheimInstall = $env:VALHEIM_INSTALL,
    [string]$BepInExCore    = $env:BEPINEX_CORE
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = (Resolve-Path (Join-Path $ScriptDir '..')).Path
$EnvFile   = Join-Path $RepoRoot '.env'

function Write-Info($m) { Write-Host "  $m" }
function Write-Ok($m)   { Write-Host "  [ok] $m" }
function Write-Warn2($m){ Write-Warning $m }
function Write-Head($m) { Write-Host "`n== $m ==" }

function Read-EnvValue([string]$Key) {
    if (-not (Test-Path $EnvFile)) { return '' }
    foreach ($line in Get-Content -LiteralPath $EnvFile) {
        if ($line -match "^\s*$([regex]::Escape($Key))\s*=\s*(.*?)\s*$") { return $Matches[1] }
    }
    return ''
}

function Write-EnvValue([string]$Key, [string]$Value) {
    $lines = @()
    if (Test-Path $EnvFile) { $lines = @(Get-Content -LiteralPath $EnvFile) }
    $found = $false
    $out = foreach ($line in $lines) {
        if ($line -match "^\s*$([regex]::Escape($Key))\s*=") { $found = $true; "$Key=$Value" }
        else { $line }
    }
    if (-not $found) { $out = @($out) + "$Key=$Value" }
    Set-Content -LiteralPath $EnvFile -Value $out -Encoding utf8
}

# ----------------------------------------------------------------------------
Write-Head 'Prerequisites'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) { Write-Ok "dotnet $(dotnet --version)" }
else { throw "the .NET SDK ('dotnet') is not on PATH. Install .NET SDK 8.x: https://dotnet.microsoft.com/download" }

# ----------------------------------------------------------------------------
Write-Head 'Valheim managed assemblies'
$vm = if ($ValheimManaged) { $ValheimManaged } else { Read-EnvValue 'VALHEIM_MANAGED' }

if (-not $vm) {
    $vi = if ($ValheimInstall) { $ValheimInstall } else { Read-EnvValue 'VALHEIM_INSTALL' }
    $candidates = @()
    if ($vi) { $candidates += @("$vi\valheim_server_Data\Managed", "$vi\valheim_Data\Managed") }
    $candidates += @(
        'C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed',
        'C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\valheim_server_Data\Managed',
        "$env:HOME\.steam\steam\steamapps\common\Valheim\valheim_Data\Managed"
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c 'assembly_valheim.dll'))) { $vm = $c; break }
    }
}

if (-not $vm) {
    Write-Warn2 'Could not auto-detect the Valheim Managed folder.'
    Write-Info  'Set it explicitly and re-run, e.g.:'
    Write-Info  '  pwsh scripts/setup.ps1 -ValheimManaged "C:\...\valheim_Data\Managed"'
    Write-Info  '  (or -ValheimInstall "C:\...\common\Valheim")'
}
elseif (-not (Test-Path (Join-Path $vm 'assembly_valheim.dll'))) {
    Write-Warn2 "VALHEIM_MANAGED='$vm' does not contain assembly_valheim.dll — double-check the path."
    Write-EnvValue 'VALHEIM_MANAGED' $vm
}
else {
    Write-Ok "Valheim Managed: $vm"
    Write-EnvValue 'VALHEIM_MANAGED' $vm
}

# ----------------------------------------------------------------------------
Write-Head 'BepInEx core assemblies'
$bc = if ($BepInExCore) { $BepInExCore } else { Read-EnvValue 'BEPINEX_CORE' }
$defaultSdkCore = Join-Path $RepoRoot '.sdk\BepInExPack_Valheim\BepInEx\core'
if (-not $bc -and (Test-Path (Join-Path $defaultSdkCore 'BepInEx.dll'))) { $bc = $defaultSdkCore }

if (-not $bc -or -not (Test-Path (Join-Path $bc 'BepInEx.dll'))) {
    Write-Warn2 'BepInEx core not found.'
    Write-Info  'Fetch it locally (downloads BepInExPack_Valheim into .sdk\):'
    Write-Info  '  pwsh scripts/fetch-sdk.ps1'
    Write-Info  '...then re-run scripts/setup.ps1, or set -BepInExCore yourself.'
}
else {
    Write-Ok "BepInEx core: $bc"
    Write-EnvValue 'BEPINEX_CORE' $bc
}

# ----------------------------------------------------------------------------
Write-Head 'Result'
if (Test-Path $EnvFile) {
    Write-Ok ".env written at: $EnvFile"
    Write-Info 'Current contents:'
    Get-Content -LiteralPath $EnvFile | ForEach-Object { Write-Host "    $_" }
}

$ready = $vm -and (Test-Path (Join-Path $vm 'assembly_valheim.dll')) -and `
         $bc -and (Test-Path (Join-Path $bc 'BepInEx.dll'))
if ($ready) {
    Write-Host "`n  You're ready to build:"
    Write-Host '    dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release'
}
else {
    Write-Host "`n  Not fully configured yet. Resolve the warnings above, then build:"
    Write-Host '    dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release'
}
