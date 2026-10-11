@echo off
setlocal EnableExtensions EnableDelayedExpansion

pushd "%~dp0" >nul 2>&1
if errorlevel 1 (
    echo ERROR: Cannot access the repository directory.
    exit /b 1
)

set "TOOL_ID=DimonSmart.LocalVectorSearchMcp"
set "PROJECT=src\DimonSmart.LocalVectorSearchMcp.Server\DimonSmart.LocalVectorSearchMcp.Server.csproj"
set "PACKAGE_DIR=%CD%\artifacts\local-tool"

rem Windows date/time formatting is intentionally based on the standard
rem Windows short-date layout used by this repository's development machines.
set "LOCAL_VERSION=0.1.2-local.%DATE:~6,4%%DATE:~3,2%%DATE:~0,2%%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%"
set "LOCAL_VERSION=!LOCAL_VERSION: =0!"

echo Build local package...
echo Version: !LOCAL_VERSION!

if exist "%PACKAGE_DIR%" rmdir /s /q "%PACKAGE_DIR%"
if errorlevel 1 (
    echo ERROR: Cannot remove the old local package directory.
    popd
    exit /b 1
)
mkdir "%PACKAGE_DIR%"
if errorlevel 1 (
    echo ERROR: Cannot create the local package directory.
    popd
    exit /b 1
)

echo.
echo Uninstall previous global tool...
dotnet tool uninstall --global "%TOOL_ID%" >nul 2>&1
if errorlevel 1 (
    dotnet tool list --global | findstr /I /B /C:"%TOOL_ID% " >nul
    if not errorlevel 1 (
        echo ERROR: Failed to uninstall the existing global tool.
        popd
        exit /b 1
    )
)

echo.
echo Build package...
dotnet pack "%PROJECT%" --configuration Release --output "%PACKAGE_DIR%" -p:Version=!LOCAL_VERSION!
if errorlevel 1 (
    echo ERROR: dotnet pack failed.
    popd
    exit /b 1
)

echo.
echo Install local global tool...
dotnet tool install --global "%TOOL_ID%" --version !LOCAL_VERSION! --add-source "%PACKAGE_DIR%" --ignore-failed-sources
if errorlevel 1 (
    echo ERROR: Local global tool installation failed.
    popd
    exit /b 1
)

echo.
echo Installed LocalVectorSearchMcp !LOCAL_VERSION!
dotnet tool list --global
if errorlevel 1 (
    echo ERROR: Could not verify the installed global tool.
    popd
    exit /b 1
)

popd
exit /b 0
