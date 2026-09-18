@echo off
rem Removes the harbor:// registration for the current user. ASCII only - see register-url-scheme.bat.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Remove-Item -Path 'HKCU:\Software\Classes\harbor' -Recurse -Force -ErrorAction SilentlyContinue; Write-Host 'OK. harbor:// removed.'; Read-Host 'Press Enter to close'"
