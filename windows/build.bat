@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo No se encontro "dotnet". Instala el SDK de .NET 8: https://dotnet.microsoft.com/download/dotnet/8.0
  exit /b 1
)

set MODE=%1
if "%MODE%"=="" set MODE=small
if /i "%MODE%"=="full" goto full

:small
echo Compilando version ligera (requiere .NET 8 Desktop Runtime)...
dotnet publish TransferDood.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
if errorlevel 1 exit /b 1
echo.
echo Listo: dist\TransferDood.exe
exit /b 0

:full
echo Compilando version autonoma (no requiere instalar nada, pesa mas)...
dotnet publish TransferDood.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist-full
if errorlevel 1 exit /b 1
echo.
echo Listo: dist-full\TransferDood.exe
exit /b 0
