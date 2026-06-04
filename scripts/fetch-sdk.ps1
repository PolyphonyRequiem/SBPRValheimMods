<#
.SYNOPSIS
    SBPR.Trailborne — fetch build SDK (BepInEx) (PowerShell).

.DESCRIPTION
    Idempotently obtains the managed reference assemblies needed to COMPILE the
    mod against BepInEx / HarmonyX. Downloads the Thunderstore
    "BepInExPack_Valheim" and unpacks it into a gitignored .sdk\ directory,
    then prints the BEPINEX_CORE path to put in your .env.

        Placement:  <repo>\.sdk\BepInExPack_Valheim\BepInEx\core   (gitignored)

    Valheim's OWN assemblies (assembly_valheim.dll, UnityEngine*.dll) are
    copyrighted game binaries and are NOT downloaded here — they come from your
    local Valheim install. Point VALHEIM_MANAGED / VALHEIM_INSTALL at it.
    Nothing copyrighted is ever committed to this repo.

.EXAMPLE
    pwsh scripts/fetch-sdk.ps1

.EXAMPLE
    pwsh scripts/fetch-sdk.ps1 -Force
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [string]$BepInExVersion = $(if ($env:BEPINEX_VERSION) { $env:BEPINEX_VERSION } else { '5.4.2333' }),
    [string]$Namespace      = $(if ($env:BEPINEX_NAMESPACE) { $env:BEPINEX_NAMESPACE } else { 'denikson' })
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = (Resolve-Path (Join-Path $ScriptDir '..')).Path
$SdkDir    = Join-Path $RepoRoot '.sdk'

$PackageName = 'BepInExPack_Valheim'
$DownloadUrl = "https://thunderstore.io/package/download/$Namespace/$PackageName/$BepInExVersion/"
$CoreDir     = Join-Path $SdkDir "$PackageName\BepInEx\core"

function Write-Info($m) { Write-Host "  $m" }
function Write-Ok($m)   { Write-Host "  [ok] $m" }

if (-not $Force -and (Test-Path (Join-Path $CoreDir 'BepInEx.dll')) -and (Test-Path (Join-Path $CoreDir '0Harmony.dll'))) {
    Write-Ok 'BepInEx core already present (use -Force to re-fetch):'
    Write-Info $CoreDir
    Write-Host "`n  Add this to your .env:`n    BEPINEX_CORE=$CoreDir"
    return
}

New-Item -ItemType Directory -Force -Path $SdkDir | Out-Null
$tmpZip = Join-Path ([System.IO.Path]::GetTempPath()) ("bepinex_" + [System.IO.Path]::GetRandomFileName() + ".zip")
$stage  = Join-Path ([System.IO.Path]::GetTempPath()) ("bepinex_stage_" + [System.IO.Path]::GetRandomFileName())

try {
    Write-Info "Fetching $PackageName v$BepInExVersion from Thunderstore..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $tmpZip -MaximumRedirection 5

    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Expand-Archive -LiteralPath $tmpZip -DestinationPath $stage -Force

    $srcPack = Get-ChildItem -Path $stage -Recurse -Directory -Filter $PackageName |
               Select-Object -First 1
    if (-not $srcPack) { throw "could not find '$PackageName' inside the downloaded package." }

    $destPack = Join-Path $SdkDir $PackageName
    if (Test-Path $destPack) { Remove-Item -Recurse -Force $destPack }
    Copy-Item -Recurse -Force -Path $srcPack.FullName -Destination $SdkDir

    if (-not (Test-Path (Join-Path $CoreDir 'BepInEx.dll'))) {
        throw "post-extract sanity check failed: $CoreDir\BepInEx.dll missing."
    }

    Write-Ok 'BepInEx core installed at:'
    Write-Info $CoreDir
    Write-Host "`n  Add this to your .env:`n    BEPINEX_CORE=$CoreDir"
    Write-Host "`n  (scripts/setup.ps1 will pick this up automatically on its next run.)"
}
finally {
    if (Test-Path $tmpZip) { Remove-Item -Force $tmpZip -ErrorAction SilentlyContinue }
    if (Test-Path $stage)  { Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue }
}
