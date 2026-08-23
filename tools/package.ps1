<#
.SYNOPSIS
    Builds Release and packs the mod into a Nexus-ready archive.

.DESCRIPTION
    Produces Schedule-I-Cookbook-<version>.zip with the game-folder layout inside it:

        Mods/
          RecipePlanner.dll
          RecipePlanner.Core.dll
          RecipePlanner.Game.dll
          RecipePlanner.UI.dll
          RecipePlanner.PhoneApp.dll
        README.txt
        LICENSE

    The Mods/ prefix matters: mod managers extract relative to the game folder, so a flat archive
    of loose DLLs installs to the wrong place.

    Version is read from the MelonInfo attribute so the archive name, the assembly and the Nexus
    page cannot drift apart.

.PARAMETER OutDir
    Where to write the archive. Defaults to release/ in the repo root.

.PARAMETER GameDir
    Passed through to the build when Steam is not in the default location.

.EXAMPLE
    pwsh tools/package.ps1
    pwsh tools/package.ps1 -GameDir "D:\Steam\steamapps\common\Schedule I"
#>
[CmdletBinding()]
param(
    [string] $OutDir,
    [string] $GameDir
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repo 'release' }

# --- version, from the one place that is authoritative -----------------------------------------
$modSource = Join-Path $repo 'src\RecipePlanner.Mod\RecipePlannerMod.cs'
$melonInfo = Select-String -Path $modSource -Pattern 'MelonInfo\(.*?,\s*"(?<name>[^"]+)"\s*,\s*"(?<version>[^"]+)"' |
             Select-Object -First 1

if (-not $melonInfo) { throw "Could not read MelonInfo from $modSource — has the attribute changed shape?" }

$modName = $melonInfo.Matches[0].Groups['name'].Value
$version = $melonInfo.Matches[0].Groups['version'].Value
Write-Host "Packaging $modName v$version" -ForegroundColor Cyan

# --- build -------------------------------------------------------------------------------------
$buildArgs = @('build', '-c', 'Release', (Join-Path $repo 'Schedule1RecipePlanner.slnx'))
if ($GameDir) { $buildArgs += "-p:GameDir=$GameDir" }

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

# --- collect -----------------------------------------------------------------------------------
$dist = Join-Path $repo 'dist'

# PhoneApp is Mono-branch only. Its absence is legitimate (an IL2CPP-only build cannot produce it),
# but shipping without it silently would hand players an archive missing the entire UI, so say so.
$required = @(
    'RecipePlanner.dll'
    'RecipePlanner.Core.dll'
    'RecipePlanner.Game.dll'
    'RecipePlanner.UI.dll'
)
$optional = @('RecipePlanner.PhoneApp.dll')

$missing = $required | Where-Object { -not (Test-Path (Join-Path $dist $_)) }
if ($missing) { throw "dist\ is missing required files: $($missing -join ', '). Did the build stage?" }

foreach ($f in $optional) {
    if (-not (Test-Path (Join-Path $dist $f))) {
        Write-Warning "$f is not in dist\ — this archive will have NO in-game UI. Build on the Mono ('alternate') branch to include it."
    }
}

# --- stage -------------------------------------------------------------------------------------
$staging = Join-Path ([System.IO.Path]::GetTempPath()) "s1cookbook-$version-$PID"
$modsDir = Join-Path $staging 'Mods'
New-Item -ItemType Directory -Path $modsDir -Force | Out-Null

foreach ($f in ($required + $optional)) {
    $src = Join-Path $dist $f
    if (Test-Path $src) { Copy-Item $src -Destination $modsDir }
}

Copy-Item (Join-Path $repo 'LICENSE') -Destination $staging

@"
$modName v$version
==================

INSTALL
  1. Install MelonLoader v0.7.3 into your Schedule I folder.
  2. Launch the game ONCE and let it reach the main menu. First run generates files and can
     look frozen for up to a minute. Do not skip this.
  3. Copy the contents of Mods\ into  Schedule I\Mods\  -- all of the DLL files.

CHECK IT WORKED
  Look in  Schedule I\MelonLoader\Latest.log  for:
      [Schedule_I_Cookbook] Symbol check PASSED (13/13 hooks resolved)
      [Schedule_I_Cookbook] Production tracking ENABLED

BRANCHES
  Tracking, history and statistics work on BOTH Steam branches.
  The in-game Cookbook phone app needs the 'alternate' (Mono) branch.
  The mod tells you which mode it is in at startup.

YOUR SAVES ARE NOT TOUCHED
  This mod never writes to game save data. Its own records live in
      %APPDATA%\Schedule1RecipePlanner\

UNINSTALL
  Delete the DLL files from Schedule I\Mods\.
  Delete %APPDATA%\Schedule1RecipePlanner\ to remove its data too.

Full guide, troubleshooting and source: see the mod page.
Licensed MIT -- see LICENSE.
"@ | Set-Content -Path (Join-Path $staging 'README.txt') -Encoding UTF8

# --- zip ---------------------------------------------------------------------------------------
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$archive = Join-Path $OutDir "Schedule-I-Cookbook-$version.zip"
if (Test-Path $archive) { Remove-Item $archive -Force }

Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive
Remove-Item $staging -Recurse -Force

$sizeKb = [math]::Round((Get-Item $archive).Length / 1KB, 1)
Write-Host ""
Write-Host "Wrote $archive ($sizeKb KB)" -ForegroundColor Green
Write-Host "Contents:" -ForegroundColor Green
Expand-Archive -Path $archive -DestinationPath (Join-Path ([System.IO.Path]::GetTempPath()) "verify-$PID") -Force
Get-ChildItem (Join-Path ([System.IO.Path]::GetTempPath()) "verify-$PID") -Recurse -File |
    ForEach-Object { "  " + $_.FullName.Substring((Join-Path ([System.IO.Path]::GetTempPath()) "verify-$PID").Length + 1) }
Remove-Item (Join-Path ([System.IO.Path]::GetTempPath()) "verify-$PID") -Recurse -Force
