@echo off
setlocal

:: Ensure we're in the repo root (where build.cmd lives)
cd /d "%~dp0"

set DOTNET_NOLOGO=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1

taskkill /IM KubeTail.exe /F >nul 2>&1

:: Find dotnet SDK
set DOTNET=%LOCALAPPDATA%\dotnet\dotnet.exe
if exist "%DOTNET%" goto :found_dotnet

where dotnet >nul 2>&1
if errorlevel 1 goto :need_install
set DOTNET=dotnet
goto :found_dotnet

:need_install
echo .NET 8 SDK not found. Installing...
echo.
call :install_dotnet
if errorlevel 1 goto :install_failed
set DOTNET=%LOCALAPPDATA%\dotnet\dotnet.exe
goto :found_dotnet

:install_failed
echo INSTALL FAILED
pause
exit /b 1

:found_dotnet
:: Tell MSBuild where the SDK lives
for %%I in ("%DOTNET%") do set DOTNET_ROOT=%%~dpI
set PATH=%DOTNET_ROOT%;%PATH%

:: Verify a .NET 8 SDK is present
"%DOTNET%" --list-sdks 2>nul | findstr /R "^8\." >nul
if not errorlevel 1 goto :sdk_ok

echo .NET 8 SDK not found (other versions may be installed). Installing .NET 8...
echo.
call :install_dotnet
if errorlevel 1 goto :install_failed
set DOTNET=%LOCALAPPDATA%\dotnet\dotnet.exe
for %%I in ("%DOTNET%") do set DOTNET_ROOT=%%~dpI
set PATH=%DOTNET_ROOT%;%PATH%

:sdk_ok
echo Using: %DOTNET%
"%DOTNET%" --version
echo.

:: Restore packages first (separate step for clear error reporting)
echo Restoring NuGet packages...
"%DOTNET%" restore KubeTail\KubeTail.csproj
if errorlevel 1 (
    echo.
    echo RESTORE FAILED
    echo.
    echo Troubleshooting:
    echo   1. Check internet connection — can you reach https://api.nuget.org ?
    echo   2. If behind a proxy, set HTTP_PROXY / HTTPS_PROXY environment variables
    echo   3. Try running: "%DOTNET%" nuget list source
    pause
    exit /b 1
)
echo.

:: Build and publish
echo Publishing KubeTail...
"%DOTNET%" publish KubeTail\KubeTail.csproj -c Release -o publish --no-restore
if errorlevel 1 (
    echo BUILD FAILED
    pause
    exit /b 1
)

echo.
echo Build complete: publish\KubeTail.exe
pause
exit /b 0

:install_dotnet
set INSTALL_SCRIPT=%TEMP%\dotnet-install.ps1
echo Downloading dotnet-install.ps1...
powershell -NoProfile -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile '%INSTALL_SCRIPT%'"
if not exist "%INSTALL_SCRIPT%" (
    echo Failed to download install script.
    exit /b 1
)
echo Installing .NET 8 SDK to %LOCALAPPDATA%\dotnet ...
powershell -NoProfile -ExecutionPolicy Bypass -Command "& '%INSTALL_SCRIPT%' -Channel 8.0 -InstallDir '%LOCALAPPDATA%\dotnet'"
if errorlevel 1 (
    echo dotnet-install.ps1 failed.
    del "%INSTALL_SCRIPT%" >nul 2>&1
    exit /b 1
)
del "%INSTALL_SCRIPT%" >nul 2>&1
echo .NET 8 SDK installed successfully.
echo.
exit /b 0
