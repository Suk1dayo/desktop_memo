@echo off
setlocal
set "EXE=%~dp0DesktopMemoServer.exe"
if not exist "%EXE%" (
    echo [ERROR] DesktopMemoServer.exe not found. Please run build.bat first.
    exit /b 1
)
if "%~1"=="" (
    echo Starting DesktopMemoServer.exe using port from data\config.json ...
    "%EXE%"
) else (
    echo Starting DesktopMemoServer.exe on port %~1 ...
    "%EXE%" --port=%~1
)
exit /b %errorlevel%
