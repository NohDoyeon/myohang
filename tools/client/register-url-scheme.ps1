# Registers harbor:// for the CURRENT USER so the website's "start game" button
# can launch this client directly. No admin rights needed.
#
# This is the TESTER version: it points at myohang.exe sitting next to this script.
# (tools/register-url-scheme.ps1 is the developer version - it runs the Godot editor instead.)
#
# Run:  double-click register-url-scheme.bat
# Undo: delete HKCU:\Software\Classes\harbor  (or run unregister-url-scheme.bat)
#
# Messages are ASCII on purpose: a .ps1 with non-ASCII text can be misread on a
# CP949 console and break the script itself.

$ErrorActionPreference = 'Stop'

$exe = Join-Path $PSScriptRoot 'myohang.exe'

if (-not (Test-Path $exe)) {
    Write-Host "[!] myohang.exe not found next to this script."
    Write-Host "    Unzip the whole archive first, then run this from the unzipped folder."
    Write-Host "    Looked in: $PSScriptRoot"
    Read-Host "Press Enter to close"
    exit 1
}

# Windows hands the whole URL as %1, e.g.  harbor://myhost:30000/abc123/
# The client finds it by scanning its arguments for one starting with harbor://
$command = '"{0}" "%1"' -f $exe

New-Item -Path 'HKCU:\Software\Classes\harbor\shell\open\command' -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\Classes\harbor' -Name '(Default)'    -Value 'URL:Harbor Protocol'
Set-ItemProperty -Path 'HKCU:\Software\Classes\harbor' -Name 'URL Protocol' -Value ''
Set-ItemProperty -Path 'HKCU:\Software\Classes\harbor\shell\open\command' -Name '(Default)' -Value $command

Write-Host "OK. harbor:// is registered for this user."
Write-Host ""
Write-Host "  Client : $exe"
Write-Host ""
Write-Host "Now log in on the website and press the start button."
Write-Host "The browser asks for permission the first time - allow it."
Write-Host ""
Write-Host "NOTE: if you move or re-unzip this folder, run this script again."
Read-Host "Press Enter to close"
