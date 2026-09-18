@echo off
rem Thin wrapper. The real work is in register-url-scheme.ps1.
rem Keep this file ASCII-only: cmd reads .bat in the OEM codepage (CP949 here),
rem so non-ASCII text corrupts the script and splits commands in half.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0register-url-scheme.ps1"
