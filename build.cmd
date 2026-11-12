@echo off
rem Builds HotasBridge.exe into .\out (needs the .NET 8 SDK). Full log goes to build.log.
cd /d "%~dp0"
echo Building HOTAS Bridge...
dotnet publish HotasBridge.csproj -c Release -o out > build.log 2>&1
if errorlevel 1 (
  echo.
  echo BUILD FAILED. Tell Claude, it can read build.log in this folder.
  pause
  exit /b 1
)
echo.
echo Done: %~dp0out\HotasBridge.exe
start "" "%~dp0out"
