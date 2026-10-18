@echo off
setlocal
cd /d "%~dp0"
title Catego - Build

echo ============================================
echo   Catego - Build
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 goto :nosdk
dotnet --list-sdks | findstr /r "^[89]\. ^1[0-9]\." >nul
if errorlevel 1 goto :nosdk
goto :build

:nosdk
echo .NET 8 SDK was not found.
echo Trying to install it with winget (Windows Package Manager)...
echo.
winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
if errorlevel 1 (
  echo.
  echo Automatic install failed. Please install the .NET 8 SDK manually from:
  echo   https://dotnet.microsoft.com/download/dotnet/8.0
  echo Then run build.bat again.
  pause
  exit /b 1
)
echo.
echo .NET SDK installed. Please CLOSE this window and run build.bat again
echo (a new window is needed so Windows picks up the new install).
pause
exit /b 0

:build
echo [1/2] Running engine tests...
dotnet run --project tests\AutoCategory.Tests -c Release
if errorlevel 1 (
  echo.
  echo Tests FAILED - see the output above.
  pause
  exit /b 1
)

echo.
echo Closing the running Catego (if open)...
taskkill /im Catego.exe /f >nul 2>nul
taskkill /im BengoAutoCategory.exe /f >nul 2>nul
timeout /t 1 /nobreak >nul

echo.
echo [2/2] Building Catego.exe ...
if exist publish rmdir /s /q publish
dotnet publish src\AutoCategory.App\AutoCategory.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
  -o publish
if errorlevel 1 (
  echo.
  echo Build FAILED - copy the red errors above and send them to Claude.
  pause
  exit /b 1
)

echo.
echo ============================================
echo   Done!  Starting the new version...
echo ============================================
start "" "%~dp0publish\Catego.exe"
timeout /t 3 /nobreak >nul
