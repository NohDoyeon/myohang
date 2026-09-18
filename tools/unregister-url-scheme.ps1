# Removes the harbor:// URL protocol registration made by register-url-scheme.ps1.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools\unregister-url-scheme.ps1

$ErrorActionPreference = 'SilentlyContinue'
Remove-Item -Path 'HKCU:\Software\Classes\harbor' -Recurse -Force
Write-Host "harbor:// registration removed."
