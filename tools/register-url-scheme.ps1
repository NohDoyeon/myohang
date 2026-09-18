# Registers the harbor:// URL protocol for the CURRENT USER (no admin rights needed).
# The landing page's "start game" button opens harbor://<ticket>, which launches the
# Godot client with that ticket so it can skip the login screen.
#
# Run:   powershell -NoProfile -ExecutionPolicy Bypass -File tools\register-url-scheme.ps1
# Undo:  tools\unregister-url-scheme.ps1
#
# Messages are ASCII on purpose: a .bat/.ps1 with non-ASCII text can be misread on a
# CP949 console and break the script itself.

$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'client-godot'
$godot   = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'

# Fall back to searching the WinGet package folder if the pinned path moved (new Godot version).
if (-not (Test-Path $godot)) {
    $packages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path $packages) {
        $found = Get-ChildItem -Path $packages -Filter 'Godot_v*_mono_win64.exe' -Recurse -ErrorAction SilentlyContinue |
                 Where-Object { $_.Name -notlike '*console*' } |
                 Sort-Object Name -Descending | Select-Object -First 1
        if ($found) { $godot = $found.FullName }
    }
}

if (-not (Test-Path $godot)) {
    Write-Host "[!] Godot executable not found."
    Write-Host "    Open this script and set the `$godot variable to your Godot .NET (mono) exe."
    exit 1
}
if (-not (Test-Path $project)) {
    Write-Host "[!] client-godot folder not found at: $project"
    exit 1
}

# Windows hands the whole URL as %1, e.g.  harbor://ABC123/
# The client strips the scheme and any trailing slash itself.
$command = '"{0}" --path "{1}" -- --ticket="%1"' -f $godot, $project

New-Item -Path 'HKCU:\Software\Classes\harbor\shell\open\command' -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\Classes\harbor' -Name '(Default)'    -Value 'URL:Harbor Protocol'
Set-ItemProperty -Path 'HKCU:\Software\Classes\harbor' -Name 'URL Protocol' -Value ''
Set-ItemProperty -Path 'HKCU:\Software\Classes\harbor\shell\open\command' -Name '(Default)' -Value $command

Write-Host "OK. harbor:// is now registered for this user."
Write-Host ""
Write-Host "  Godot  : $godot"
Write-Host "  Project: $project"
Write-Host "  Command: $command"
Write-Host ""
Write-Host "Log in on the landing page and press the start button."
Write-Host "The browser will ask for permission the first time - allow it."
