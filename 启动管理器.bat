@echo off
setlocal
cd /d "%~dp0"

if exist "%LOCALAPPDATA%\Microsoft\dotnet" (
    set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
    set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
)

set "EXE_RELEASE=%~dp0src\SekiroModManager.App\bin\Release\net8.0-windows\win-x64\publish\SekiroModManager.App.exe"
if exist "%EXE_RELEASE%" (
    start "" "%EXE_RELEASE%"
    exit /b 0
)

set "EXE_DEBUG=%~dp0src\SekiroModManager.App\bin\Debug\net8.0-windows\SekiroModManager.App.exe"
if exist "%EXE_DEBUG%" (
    start "" "%EXE_DEBUG%"
    exit /b 0
)

if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" (
    start "" "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" run --project "%~dp0src\SekiroModManager.App"
    exit /b 0
)

start "" dotnet run --project "%~dp0src\SekiroModManager.App"