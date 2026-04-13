@echo off
setlocal

taskkill /IM KubeTail.exe /F >nul 2>&1

set DOTNET=%LOCALAPPDATA%\dotnet\dotnet.exe
if not exist "%DOTNET%" (
    set DOTNET=dotnet
)

echo Building KubeTail...
"%DOTNET%" publish KubeTail\KubeTail.csproj -c Release -o publish
if %errorlevel% neq 0 (
    echo BUILD FAILED
    pause
    exit /b 1
)

echo.
echo Build complete: publish\KubeTail.exe
pause
