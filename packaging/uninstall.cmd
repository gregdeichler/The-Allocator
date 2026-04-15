@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ProgramFiles%\Vassar College\The Allocator\uninstall.ps1" -Quiet
exit /b %errorlevel%
