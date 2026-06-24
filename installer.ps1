#Requires -Version 5.1
<#
  ___ ___ ___ ___   _____        _ _ _
 / __| _ ) _ \ _ \ |_   _| _ __ _(_) | |__  ___ _ _ _ _  ___
 \__ \ _ \  _/   /   | || '_/ _` | | | '_ \/ _ \ '_| ' \/ -_)
 |___/___/_| |_|_\   |_||_| \__,_|_|_|_.__/\___/_| |_||_\___|

  Trailborne playtest bootstrapper (Windows / Steam Valheim)
  Repo:  https://github.com/PolyphonyRequiem/SBPRValheimMods   (MIT)

  ─────────────────────────────────────────────────────────────────────────
  WHAT THIS DOES — read it; it prints a summary at runtime and asks before
  doing anything that writes to disk:

    1. Locates your Steam Valheim install (vanilla).
    2. COPIES it (never moves/modifies) to a separate modded folder:
         %LOCALAPPDATA%\Trailborne\Valheim-Modded
       Your vanilla install stays 100% pristine — unmodded Valheim still
       launches normally from Steam.
    3. Downloads the Trailborne modpack (BepInEx + the SBPR.Trailborne mod
       + ServerDevcommands for admins) from the repo's GitHub Release,
       verifies its SHA256, and overlays it into the copy.
    4. Writes a launcher (Launch-Trailborne.cmd + a Desktop shortcut) that
       starts the MODDED copy with BepInEx, pointed at Steam so multiplayer
       + ownership work normally. The launcher adds -console (F5 dev console;
       admins also get full devcommands incl. spawn/god/fly via the bundled
       ServerDevcommands mod); pass -NoConsole to omit -console.
    5. Prints the server join info for tonight.

  WHAT IT DOES NOT DO:
    * Never edits, moves, or deletes your real Valheim install.
    * No admin rights needed. No registry surgery. Fully removable: just
      delete %LOCALAPPDATA%\Trailborne and the Desktop shortcut.

  RUN IT:
    iwr https://raw.githubusercontent.com/PolyphonyRequiem/SBPRValheimMods/main/installer.ps1 -UseBasicParsing | iex

  ADVANCED (pass options): download then invoke as a scriptblock, e.g.
    & ([scriptblock]::Create((iwr https://raw.githubusercontent.com/PolyphonyRequiem/SBPRValheimMods/main/installer.ps1 -UseBasicParsing))) -Force
  ─────────────────────────────────────────────────────────────────────────
#>
[CmdletBinding()]
param(
    # GitHub release asset (the assembled modpack). Pinned to a tag for stability.
    [string]$ModpackUrl    = 'https://github.com/PolyphonyRequiem/SBPRValheimMods/releases/download/v0.2.37-playtest/SBPR-Trailborne-Modpack-v0.2.37.zip',
    [string]$ExpectedSha256= '8544801a355603715421234a65956045229c067bd7229785d351e470844f6ace',
    # Live server status (join code drifts on every restart, so we FETCH it at
    # runtime instead of baking a stale code in). Falls back gracefully if down.
    [string]$StatusUrl     = 'https://gist.githubusercontent.com/PolyphonyRequiem/7b54a29aeefb3effee0393df79d0b03e/raw/niflheim-status.json',
    [string]$ModdedDirName = 'Valheim-Modded',
    [switch]$Force,          # skip the confirmation prompt
    [switch]$NoShortcut,     # don't drop a Desktop shortcut
    [switch]$NoConsole       # launch WITHOUT -console (omit the F5 dev console). Default: -console IS added.
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Version = '0.1.0 (2026-06-04)'

function Say  { param($m,$c='Gray')  Write-Host $m -ForegroundColor $c }
function Step { param($m) Write-Host "`n==> $m" -ForegroundColor Cyan }
function Ok   { param($m) Write-Host "  + $m" -ForegroundColor Green }
function Note { param($m) Write-Host "  - $m" -ForegroundColor DarkGray }
function Warn3{ param($m) Write-Host "  ! $m" -ForegroundColor Yellow }
function Die  { param($m) Write-Host "`nXX $m" -ForegroundColor Red; exit 1 }

Say ""
Say "Trailborne playtest bootstrapper  v$Version" White
Say "This copies Valheim to a modded folder (vanilla untouched), installs the mod, makes a launcher." DarkGray

# ── Live server status (fetched, not hardcoded — the join code drifts) ───
function Get-ServerStatus {
    param([string]$url)
    try {
        $j = Invoke-RestMethod -Uri $url -UseBasicParsing -TimeoutSec 10
        return [pscustomobject]@{
            Code   = "$($j.join_code)"
            Status = "$($j.status)"
            Stamp  = "$($j.updated_utc)"
        }
    } catch {
        return $null
    }
}
$srv = Get-ServerStatus -url $StatusUrl
function Format-JoinLine {
    if (-not $srv)            { return "join code: (couldn't reach status — ask Daniel for the current code)" }
    if ($srv.Status -ne 'online') { return "server is currently OFFLINE (status checked just now) — ask Daniel" }
    return "join code  $($srv.Code)   (live, as of $($srv.Stamp))"
}

# ── 1. Locate Steam + Valheim ────────────────────────────────────────────
Step "Locating Steam and Valheim"

function Get-SteamRoot {
    foreach ($p in @(
        (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -EA SilentlyContinue).SteamPath,
        (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -EA SilentlyContinue).InstallPath,
        'C:\Program Files (x86)\Steam'
    )) { if ($p -and (Test-Path $p)) { return (Resolve-Path $p).Path } }
    return $null
}

# Parse all Steam library folders (game may live on another drive).
function Get-SteamLibraries { param($steamRoot)
    $libs = @($steamRoot)
    $vdf = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        Select-String -Path $vdf -Pattern '"path"\s*"([^"]+)"' -AllMatches |
          ForEach-Object { $_.Matches } |
          ForEach-Object { $libs += ($_.Groups[1].Value -replace '\\\\','\') }
    }
    $libs | Select-Object -Unique
}

function Find-Valheim {
    $steam = Get-SteamRoot
    if ($steam) {
        Note "Steam: $steam"
        foreach ($lib in (Get-SteamLibraries $steam)) {
            $cand = Join-Path $lib 'steamapps\common\Valheim'
            if (Test-Path (Join-Path $cand 'valheim.exe')) { return (Resolve-Path $cand).Path }
        }
    }
    # Last-ditch common paths
    foreach ($p in @('C:\Program Files (x86)\Steam\steamapps\common\Valheim',
                     'D:\SteamLibrary\steamapps\common\Valheim',
                     'E:\SteamLibrary\steamapps\common\Valheim')) {
        if (Test-Path (Join-Path $p 'valheim.exe')) { return $p }
    }
    return $null
}

$vanilla = Find-Valheim
if (-not $vanilla) {
    Die "Couldn't find a Steam Valheim install (valheim.exe). Make sure Valheim is installed via Steam, then re-run. If it's on an unusual path, you can pass it: the script looks for steamapps\common\Valheim."
}
Ok "Vanilla Valheim: $vanilla"

# ── 2. Plan the modded copy ─────────────────────────────────────────────
$base      = Join-Path $env:LOCALAPPDATA 'Trailborne'
$modded    = Join-Path $base $ModdedDirName
$downloads = Join-Path $base 'downloads'

Step "Plan"
Note "Modded copy  -> $modded"
Note "Vanilla stays pristine at the path above (Steam still launches it normally)."
Note "Server status: $(Format-JoinLine)"

if (-not $Force) {
    $resp = Read-Host "`nProceed? This will copy Valheim (~1-2 GB) into the modded folder. [Y/n]"
    if ($resp -and $resp -notmatch '^(y|yes)$') { Say "Aborted — nothing was changed." Yellow; exit 0 }
}

New-Item -ItemType Directory -Force -Path $base,$downloads | Out-Null

# ── 3. Copy vanilla -> modded (robocopy mirror; vanilla never modified) ──
Step "Copying Valheim into the modded folder (vanilla is only READ)"
if (Test-Path (Join-Path $modded 'valheim.exe')) {
    Warn3 "Modded copy already exists — refreshing game files (your BepInEx config is preserved by the overlay step)."
}
New-Item -ItemType Directory -Force -Path $modded | Out-Null

# robocopy exit codes 0-7 are success; 8+ are real errors.
$rc = Start-Process robocopy -ArgumentList @(
        "`"$vanilla`"", "`"$modded`"", '/E', '/NFL','/NDL','/NJH','/NP',
        '/R:1','/W:1','/XD','BepInEx','/XF','winhttp.dll','doorstop_config.ini'
    ) -NoNewWindow -Wait -PassThru
if ($rc.ExitCode -ge 8) { Die "robocopy failed (code $($rc.ExitCode)) copying the game files." }
Ok "Game files copied (vanilla untouched)."

# steam_appid.txt lets the modded copy talk to Steam (multiplayer/ownership).
Set-Content -Path (Join-Path $modded 'steam_appid.txt') -Value '892970' -NoNewline -Encoding ascii
Note "Wrote steam_appid.txt (892970) so the modded copy authenticates with Steam."

# ── 4. Download + verify the modpack ────────────────────────────────────
Step "Downloading the Trailborne modpack"
$zip = Join-Path $downloads 'SBPR-Trailborne-Modpack.zip'
try {
    Invoke-WebRequest -Uri $ModpackUrl -OutFile $zip -UseBasicParsing
} catch {
    Die "Couldn't download the modpack from:`n    $ModpackUrl`n($($_.Exception.Message))`nIf the release isn't published yet, ping Daniel."
}
$got = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLower()
if ($ExpectedSha256 -and $got -ne $ExpectedSha256.ToLower()) {
    Die "Checksum mismatch!`n  expected $ExpectedSha256`n  got      $got`nRefusing to install a tampered/corrupt modpack."
}
Ok "Downloaded + SHA256 verified."

# ── 5. Overlay the modpack into the modded copy ─────────────────────────
Step "Installing BepInEx + Trailborne into the modded copy"
$extract = Join-Path $downloads 'extract'
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $extract -Force
# The zip contains a single top folder; find the one holding winhttp.dll.
$payload = Get-ChildItem $extract -Recurse -Filter winhttp.dll | Select-Object -First 1 | ForEach-Object { $_.Directory.FullName }
if (-not $payload) { Die "Modpack layout unexpected (no winhttp.dll inside)." }
Copy-Item -Path (Join-Path $payload '*') -Destination $modded -Recurse -Force
if (-not (Test-Path (Join-Path $modded 'BepInEx\plugins\SBPR.Trailborne\SBPR.Trailborne.dll'))) {
    Die "Overlay finished but the Trailborne DLL isn't where expected. Aborting so you don't launch a half-install."
}
# Clear BepInEx's typeloader cache so an UPGRADE always re-reads plugin metadata.
# BepInEx invalidates this cache by DLL mtime+length, but the modpack is packed
# DETERMINISTICALLY (every file pinned to a constant 2020-01-01 mtime), so two
# releases whose DLLs happen to share a byte-length look "unchanged" to BepInEx —
# it then reuses the OLD release's cached type metadata and prints the PREVIOUS
# version in its "Loading [SBPR Trailborne x.y.z]" banner even though the new code
# is what actually runs. The cache is regenerable; deleting it on every install is
# harmless and kills the stale-banner class outright. (robocopy /XD BepInEx
# preserves the tree on refresh, so without this the stale cache survives forever.)
$cacheDir = Join-Path $modded 'BepInEx\cache'
if (Test-Path $cacheDir) { Remove-Item $cacheDir -Recurse -Force }
Ok "BepInEx doorstop + Trailborne DLL + icons in place."

# ── 6. Launcher + shortcut ──────────────────────────────────────────────
# IMPORTANT: this is a SEPARATE copy, so we launch valheim.exe DIRECTLY.
# We do NOT use steam://run/892970 — that URI launches Steam's managed vanilla
# install, not our modded copy. Direct-exe works because (a) winhttp.dll in the
# folder auto-loads the BepInEx doorstop, and (b) steam_appid.txt (written above)
# lets the exe init Steamworks for multiplayer/ownership as long as Steam runs.
Step "Creating the launcher"
# -console enables Valheim's F5 developer console (required for admin commands /
# devcommands on the server). On by default; pass -NoConsole to omit it.
$consoleArg = if ($NoConsole) { '' } else { ' -console' }
$cmdExe = Join-Path $modded 'Launch-Trailborne.cmd'
@"
@echo off
rem  Launches the MODDED Valheim copy directly (BepInEx auto-loads via winhttp.dll).
rem  Steam must be running so Steamworks (multiplayer/ownership) initializes.
cd /d "%~dp0"
echo Starting modded Valheim (Trailborne)...
start "" "%~dp0valheim.exe"$consoleArg
"@ | Set-Content -Path $cmdExe -Encoding ascii
Ok "Wrote $cmdExe"
if (-not $NoConsole) { Note "Launcher includes -console (press F5 in-game for the dev console)." }

if (-not $NoShortcut) {
    try {
        $desktop = [Environment]::GetFolderPath('Desktop')
        $lnk = Join-Path $desktop 'Play Trailborne (Modded).lnk'
        $ws = New-Object -ComObject WScript.Shell
        $sc = $ws.CreateShortcut($lnk)
        $sc.TargetPath = $cmdExe
        $sc.WorkingDirectory = $modded
        $sc.IconLocation = (Join-Path $modded 'valheim.exe')
        $sc.Description = 'Launch modded Valheim (Trailborne playtest)'
        $sc.Save()
        Ok "Desktop shortcut: $lnk"
    } catch { Warn3 "Couldn't create Desktop shortcut ($($_.Exception.Message)); use $cmdExe directly." }
}

# ── 7. Done ─────────────────────────────────────────────────────────────
Say ""
Say "============================================================" Green
Say " Trailborne is installed. Vanilla Valheim is untouched." Green
Say "============================================================" Green
Say ""
Say " Play:    double-click 'Play Trailborne (Modded)' on your Desktop" White
Say "          (or run Launch-Trailborne.cmd in the modded folder)" DarkGray
Say " Folder:  $modded" White
Say " Server:  $(Format-JoinLine)" White
Say ""
Say " First launch shows a black BepInEx console window briefly — that's normal." DarkGray
if (-not $NoConsole) { Say " In-game: press F5 to open the dev console (admin commands work once you're an admin)." DarkGray }
Say " To uninstall: delete  $base  and the Desktop shortcut." DarkGray
Say ""
