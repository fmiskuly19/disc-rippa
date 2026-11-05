@echo off
setlocal
cd /d "%~dp0"
echo.
echo  DiscRippa - building the single-file app...
echo.
where dotnet >nul 2>nul
if errorlevel 1 (
  echo  The .NET SDK isn't installed. Get it from https://dotnet.microsoft.com/download ^(".NET 10 SDK" or newer^), then run this again.
  pause
  exit /b 1
)
dotnet publish "src\DiscRippa\DiscRippa.csproj" -c Release -o "publish" %*
if errorlevel 1 (
  echo.
  echo  Build failed - see the messages above.
  pause
  exit /b 1
)
copy /y "publish\DiscRippa.exe" "DiscRippa.exe" >nul
echo.
echo  Done:  %~dp0DiscRippa.exe
echo.
pause
