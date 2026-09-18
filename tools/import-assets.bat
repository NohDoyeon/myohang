@echo off
rem Thin wrapper. See import-assets.ps1. Keep this file ASCII-only.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0import-assets.ps1"
