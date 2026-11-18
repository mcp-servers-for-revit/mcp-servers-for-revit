@echo off
chcp 65001 >nul 2>&1
echo.
echo   ================================================================
echo     Revit MCP Plugin - Uninstaller
echo   ================================================================
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" -Uninstall
echo.
echo   Press any key to close.
pause >nul
