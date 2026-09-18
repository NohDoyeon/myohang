@echo off
rem Thin wrapper. See unregister-url-scheme.ps1. Keep this file ASCII-only.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0unregister-url-scheme.ps1"
