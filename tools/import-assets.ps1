# Makes Godot import newly added images (art/atlas.png and friends).
#
# Why this is needed: dropping a PNG into the project folder is not enough. Godot can only
# load a texture through res:// once it has generated an .import file and a .ctex in
# .godot/imported/. Running the game with --path does NOT do that; only an editor pass does.
# Run this once after adding or replacing any image, then run the game normally.
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools\import-assets.ps1
#
# ASCII only on purpose - see register-url-scheme.ps1 for why.

$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'client-godot'
$godot   = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'

if (-not (Test-Path $godot)) {
    $packages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path $packages) {
        $found = Get-ChildItem -Path $packages -Filter 'Godot_v*_mono_win64_console.exe' -Recurse -ErrorAction SilentlyContinue |
                 Sort-Object Name -Descending | Select-Object -First 1
        if ($found) { $godot = $found.FullName }
    }
}
if (-not (Test-Path $godot)) { Write-Host "[!] Godot console executable not found."; exit 1 }
if (-not (Test-Path $project)) { Write-Host "[!] client-godot not found at: $project"; exit 1 }

Write-Host "Importing assets in $project ..."

# Godot 4 has --import for exactly this. Fall back to a headless editor pass if it is unavailable.
& $godot --path $project --headless --import 2>&1 | ForEach-Object { Write-Host "  $_" }

$stamp = Join-Path $project 'art\atlas.png.import'
if (-not (Test-Path $stamp)) {
    Write-Host "--import did not produce art\atlas.png.import; trying a headless editor pass..."
    & $godot --path $project --headless --editor --quit 2>&1 | ForEach-Object { Write-Host "  $_" }
}

Write-Host ""
if (Test-Path $stamp) {
    Write-Host "OK. atlas.png is imported."
    Get-ChildItem -Path (Join-Path $project 'art') | ForEach-Object { Write-Host ("  " + $_.Name) }
} else {
    Write-Host "[!] Still no art\atlas.png.import."
    Write-Host "    Open the editor once by hand and let it finish importing:"
    Write-Host "    `"$godot`" --path `"$project`" --editor"
    exit 1
}
