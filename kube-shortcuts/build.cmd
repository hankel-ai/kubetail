@echo off
setlocal

cd /d "%~dp0"

set "GO_VERSION=1.26.3"
set "GO_PORTABLE=%LOCALAPPDATA%\kubetail-go"
set "GO_PORTABLE_BIN=%GO_PORTABLE%\go\bin\go.exe"
set "OUTDIR_FILE=%~dp0.outdir"

if not "%OUT_DIR%"=="" goto :validate_outdir

if exist "%OUTDIR_FILE%" (
    for /f "usebackq delims=" %%L in ("%OUTDIR_FILE%") do set "OUT_DIR=%%L"
)
if "%OUT_DIR%"=="" goto :prompt_outdir
if exist "%OUT_DIR%" goto :have_outdir
echo.
echo Saved output directory does not exist: %OUT_DIR%
echo ^(.outdir may be stale from another machine via OneDrive sync.^)
echo.
set "OUT_DIR="

:prompt_outdir
set "OUT_DIR=%USERPROFILE%\OneDrive\Programs"
set /p "OUT_DIR=Output directory for l.exe and e.exe [%OUT_DIR%]: "
if "%OUT_DIR%"=="" (
    echo No directory entered. Aborting.
    exit /b 1
)
> "%OUTDIR_FILE%" echo %OUT_DIR%
echo Saved to .outdir ^(gitignored^).
echo.

:validate_outdir
if exist "%OUT_DIR%" goto :have_outdir
echo Output directory does not exist: %OUT_DIR%
choice /C YN /M "Create it now?"
if errorlevel 2 (
    echo Aborted.
    exit /b 1
)
mkdir "%OUT_DIR%" 2>nul
if errorlevel 1 (
    echo Failed to create %OUT_DIR%.
    exit /b 1
)

:have_outdir

set "GO="
if exist "%GO_PORTABLE_BIN%" (
    set "GO=%GO_PORTABLE_BIN%"
    set "PATH=%GO_PORTABLE%\go\bin;%PATH%"
    goto :found_go
)
where go >nul 2>&1
if not errorlevel 1 (
    set "GO=go"
    goto :found_go
)

echo Go SDK not found on PATH and no portable install at %GO_PORTABLE%.
echo.
choice /C YN /M "Install portable Go %GO_VERSION% (no admin required)?"
if errorlevel 2 (
    echo.
    echo Aborted. Install Go yourself ^(https://go.dev/dl/^) and re-run build.cmd.
    exit /b 1
)

call :install_portable_go
if errorlevel 1 (
    echo Portable install failed.
    pause
    exit /b 1
)
set "GO=%GO_PORTABLE_BIN%"
set "PATH=%GO_PORTABLE%\go\bin;%PATH%"

:found_go
"%GO%" version
echo.

echo Building l.exe and e.exe to %OUT_DIR% ...
"%GO%" build -o "%OUT_DIR%\l.exe" .\cmd\l
if errorlevel 1 (
    echo BUILD FAILED
    pause
    exit /b 1
)
"%GO%" build -o "%OUT_DIR%\e.exe" .\cmd\e
if errorlevel 1 (
    echo BUILD FAILED
    pause
    exit /b 1
)

echo.
echo Built:
echo   %OUT_DIR%\l.exe
echo   %OUT_DIR%\e.exe
exit /b 0

:install_portable_go
set "GO_ZIP=go%GO_VERSION%.windows-amd64.zip"
set "GO_DL=%TEMP%\%GO_ZIP%"
echo Downloading https://go.dev/dl/%GO_ZIP% ...
powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://go.dev/dl/%GO_ZIP%' -OutFile '%GO_DL%'"
if errorlevel 1 exit /b 1
if not exist "%GO_DL%" exit /b 1

if exist "%GO_PORTABLE%" rmdir /s /q "%GO_PORTABLE%"
mkdir "%GO_PORTABLE%"
echo Extracting to %GO_PORTABLE% ...
powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; Expand-Archive -Path '%GO_DL%' -DestinationPath '%GO_PORTABLE%' -Force"
if errorlevel 1 exit /b 1
del "%GO_DL%" >nul 2>&1

if not exist "%GO_PORTABLE_BIN%" (
    echo Expected %GO_PORTABLE_BIN% after extraction but not found.
    exit /b 1
)
echo Go %GO_VERSION% installed to %GO_PORTABLE%.
echo.
exit /b 0
