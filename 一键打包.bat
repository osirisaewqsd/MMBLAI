@echo off
chcp 65001 >nul 2>&1
title MMBLAI Build Script

setlocal enabledelayedexpansion

set "PROJECT_DIR=%~dp0"
set "PROJECT_FILE=%PROJECT_DIR%MMBLAI.csproj"
set "OUTPUT_DIR=%PROJECT_DIR%publish"

echo ============================================
echo   MMBLAI Build Script
echo ============================================
echo.

echo [1/4] Cleaning old output...
if exist "%OUTPUT_DIR%" (
    rmdir /s /q "%OUTPUT_DIR%" 2>nul
    echo       Removed publish directory
) else (
    echo       publish dir not found, skip
)
if exist "%PROJECT_DIR%bin" (
    rmdir /s /q "%PROJECT_DIR%bin" 2>nul
    echo       Cleaned bin directory
)
if exist "%PROJECT_DIR%obj" (
    rmdir /s /q "%PROJECT_DIR%obj" 2>nul
    echo       Cleaned obj directory
)
echo.

echo [2/4] Checking resources...
if not exist "%PROJECT_DIR%logo.ico" (
    echo       [ERROR] logo.ico not found!
    pause
    exit /b 1
) else (
    echo       logo.ico OK
)
echo.

echo [3/4] Publishing project (Release / SingleFile / SelfContained)...
echo.

dotnet publish "%PROJECT_FILE%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUTPUT_DIR%"

if %errorlevel% neq 0 (
    echo.
    echo       [ERROR] Publish failed!
    pause
    exit /b 1
)
echo.
echo       Publish succeeded!

echo.
echo [4/4] Build complete!
echo.

for %%A in ("%OUTPUT_DIR%\MMBLAI.exe") do (
    set /a "SIZE_MB=%%~zA/1048576"
    echo       Output: MMBLAI.exe
    echo       Size: !SIZE_MB! MB
    echo       Path: %OUTPUT_DIR%
)

echo.
echo ============================================
echo   Build succeeded! Window will close in 2s...
echo ============================================
echo.

:: Auto-close after 2 seconds on success
timeout /t 2 /nobreak >nul
exit 0
