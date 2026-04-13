@echo off
setlocal

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
:: Verify a .NET 8 SDK is present
"%DOTNET%" --list-sdks 2>nul | findstr /R "^8\." >nul
if not errorlevel 1 goto :sdk_ok

echo .NET 8 SDK not found (other versions may be installed). Installing .NET 8...
echo.
call :install_dotnet
if errorlevel 1 goto :install_failed
set DOTNET=%LOCALAPPDATA%\dotnet\dotnet.exe

:sdk_ok
echo Building KubeTail...
"%DOTNET%" publish KubeTail\KubeTail.csproj -c Release -o publish
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
powershell -NoProfile -Command "Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile '%INSTALL_SCRIPT%'"
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
set PATH=%LOCALAPPDATA%\dotnet;%PATH%
echo .NET 8 SDK installed successfully.
echo.
exit /b 0
