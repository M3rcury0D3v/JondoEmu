@echo off
setlocal
cd /d "%~dp0"

set "DOFUS_DIR=E:\Juegos\Dofus - Propio"
set "CLIENT_DIR=E:\Juegos\Dofus - Propio\Cliente 3.6.10.11"
set "MODS_DIR=%CLIENT_DIR%\Mods"

echo ========================================================
echo   JondoEmu - Build and Deploy to Game Folder
echo ========================================================

echo.
echo [1/4] Stopping running server instances if any...
taskkill /f /im "Jondo Server.exe" 2>nul

echo.
echo [2/4] Building Jondo Server...
dotnet publish "Jondo.Unity.Server\Jondo.Unity.Server.csproj" -c Debug -r win-x64 --self-contained false -p:SkipTests=true --nologo -v q
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Server build failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/4] Building and deploying mods (JondoFix and Heroes)...
dotnet build "JondoFix\JondoFix.csproj" -c Debug -p:DofusClientDir="%CLIENT_DIR%" --nologo -v q
if %ERRORLEVEL% neq 0 (
    echo [ERROR] JondoFix build failed!
    pause
    exit /b %ERRORLEVEL%
)

dotnet build "Heroes\Heroes.csproj" -c Debug -p:DofusClientDir="%CLIENT_DIR%" --nologo -v q
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Heroes mod build failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [4/4] Deploying Jondo Server binaries and dependencies...
copy /Y "Jondo.Unity.Server\bin\Debug\net10.0\win-x64\Jondo*.*" "%DOFUS_DIR%\" >nul

echo.
echo ========================================================
echo   DEPLOY COMPLETE!
echo   Server updated: %DOFUS_DIR%\Jondo Server.exe
echo   Mods updated:   %MODS_DIR%
echo ========================================================
echo.
