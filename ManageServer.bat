@echo off
chcp 65001 >nul
cd /d "%~dp0"
if exist "app\EnterpriseTesterServer.exe" (
    "app\EnterpriseTesterServer.exe" --root "%~dp0." %*
) else if exist "artifacts\publish\win-x64\app\EnterpriseTesterServer.exe" (
    "artifacts\publish\win-x64\app\EnterpriseTesterServer.exe" --root "%~dp0." %*
) else (
    dotnet run --project "EnterpriseTester.ServerHost\EnterpriseTester.ServerHost.csproj" -c Release -- --root "%~dp0." %*
)
