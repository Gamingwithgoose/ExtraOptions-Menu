@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0" || exit /b 1
title Monsterpatch Extra Options Builder
set "PROJECT=%CD%\Monsterpatch_ExtraOptions_Patcher\src\Monsterpatch_ExtraOptions_Patcher.csproj"
set "OUTPUT=%CD%\release"
set "DLL=Monsterpatch_ExtraOptions_Patcher.dll"
set "LOG=%CD%\build.log"
set "STAGE=%TEMP%\ExtraOptions-build-%RANDOM%-%RANDOM%"
set "RC=1"

echo ===========================================================
echo Monsterpatch Extra Options - Release Builder
echo ===========================================================
echo.
> "%LOG%" echo Monsterpatch Extra Options build
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK not found. Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
    goto failed
)
dotnet --list-sdks >> "%LOG%" 2>&1
dotnet --version >> "%LOG%" 2>&1
if errorlevel 1 (
    echo [ERROR] No usable .NET SDK found. See build.log.
    goto failed
)
if not exist "%PROJECT%" (
    echo [ERROR] Project file missing.
    goto failed
)
mkdir "%STAGE%" || goto failed
echo [BUILD] Restoring dependencies and compiling Release...
dotnet build "%PROJECT%" --configuration Release --output "%STAGE%" --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto failed
if not exist "%STAGE%\%DLL%" (
    echo [ERROR] Build completed without the expected DLL.
    goto failed
)
if not exist "%OUTPUT%" mkdir "%OUTPUT%"
if not exist "%OUTPUT%" goto failed
copy /y "%STAGE%\%DLL%" "%OUTPUT%\%DLL%" >nul
if errorlevel 1 goto failed
set "RC=0"
echo.
echo [SUCCESS] Build complete:
echo "%OUTPUT%\%DLL%"
echo Copy this DLL into the game's BepInEx\patchers folder.
goto finish

:failed
echo.
echo [FAILED] See the build output below and "%LOG%".
echo Any previously released DLL has not been intentionally removed.
if exist "%LOG%" type "%LOG%"

:finish
if exist "%STAGE%" rmdir /s /q "%STAGE%"
echo.
if /i not "%~1"=="--no-pause" pause
exit /b %RC%
