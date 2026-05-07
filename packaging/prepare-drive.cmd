@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0prepare-drive.ps1" %*
