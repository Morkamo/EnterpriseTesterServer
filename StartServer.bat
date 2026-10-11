@echo off
chcp 65001 >nul
title EnterpriseTester Dedicated Server
cd /d "%~dp0"
if exist "app\EnterpriseTesterServer.exe" (
  "app\EnterpriseTesterServer.exe" start --root "%~dp0." %*
) else if exist "artifacts\publish\win-x64\app\EnterpriseTesterServer.exe" (
  "artifacts\publish\win-x64\app\EnterpriseTesterServer.exe" start --root "%~dp0." %*
) else (
  where dotnet >nul 2>&1
  if errorlevel 1 (
    echo Нет готового Windows-пакета и .NET SDK. Используйте архив win-x64.
    pause
    exit /b 1
  )
  dotnet run --project "EnterpriseTester.ServerHost\EnterpriseTester.ServerHost.csproj" -c Release -- start --root "%~dp0." %*
)
echo.
echo Работа сервера завершена. Окно можно закрыть.
pause
