@echo off
setlocal enabledelayedexpansion

REM Fast Azure Function deployment script for Windows
REM This script builds and deploys the Azure Function without going through CDKTF

echo 🚀 Starting fast Azure Function deployment...

REM Configuration
set RESOURCE_GROUP=GradingEngineAssignmentResourceGroup
set FUNCTION_APP_NAME=GraderFunctionApp
set PROJECT_PATH=%~dp0..\GraderFunctionApp

echo ℹ️  Configuration:
echo    Resource Group: %RESOURCE_GROUP%
echo    Function App: %FUNCTION_APP_NAME%
echo    Project Path: %PROJECT_PATH%
echo.

REM Step 1: Clean previous builds
echo ℹ️  Cleaning previous builds...
cd /d "%PROJECT_PATH%"
dotnet clean --configuration Release
if errorlevel 1 (
    echo ❌ Clean failed
    exit /b 1
)
echo ✅ Clean completed

REM Step 2: Build the project
echo ℹ️  Building the project...
dotnet build --configuration Release
if errorlevel 1 (
    echo ❌ Build failed
    exit /b 1
)
echo ✅ Build completed

REM Step 3: Publish the project
echo ℹ️  Publishing the project...
dotnet publish --configuration Release --output bin\Release\publish
if errorlevel 1 (
    echo ❌ Publish failed
    exit /b 1
)
echo ✅ Publish completed

REM Step 4: Create deployment package
echo ℹ️  Creating deployment package...
set DEPLOYMENT_ZIP=%PROJECT_PATH%\deployment.zip
cd bin\Release\publish
if exist "%DEPLOYMENT_ZIP%" del "%DEPLOYMENT_ZIP%"
powershell -command "Compress-Archive -Path '.\*' -DestinationPath '%DEPLOYMENT_ZIP%' -Force"
cd ..\..\..
echo ✅ Deployment package created at: %DEPLOYMENT_ZIP%

REM Step 5: Check if Function App exists
echo ℹ️  Checking if Function App exists...
az functionapp show --name "%FUNCTION_APP_NAME%" --resource-group "%RESOURCE_GROUP%" >nul 2>&1
if errorlevel 1 (
    echo ❌ Function App '%FUNCTION_APP_NAME%' not found in resource group '%RESOURCE_GROUP%'
    echo ℹ️  Please deploy the infrastructure first using: cd Infrastructure ^&^& npx cdktn deploy
    exit /b 1
)
echo ✅ Function App found

REM Step 6: Stop the Function App
echo ℹ️  Stopping Function App for faster deployment...
az functionapp stop --name "%FUNCTION_APP_NAME%" --resource-group "%RESOURCE_GROUP%" >nul 2>&1
echo ✅ Function App stopped

REM Step 7: Deploy to Azure
echo ℹ️  Deploying to Azure Function App...
az functionapp deployment source config-zip --resource-group "%RESOURCE_GROUP%" --name "%FUNCTION_APP_NAME%" --src "%DEPLOYMENT_ZIP%" --timeout 600
if errorlevel 1 (
    echo ❌ Deployment failed
    exit /b 1
)
echo ✅ Deployment completed

REM Step 8: Start the Function App
echo ℹ️  Starting Function App...
az functionapp start --name "%FUNCTION_APP_NAME%" --resource-group "%RESOURCE_GROUP%" >nul 2>&1
echo ✅ Function App started

echo.
echo 🎉 Deployment completed successfully!
echo.
echo 📍 Function App Details:
echo    Resource Group: %RESOURCE_GROUP%
echo    Function App: %FUNCTION_APP_NAME%
echo.

REM Show Function App URL
for /f "tokens=*" %%i in ('az functionapp show --name "%FUNCTION_APP_NAME%" --resource-group "%RESOURCE_GROUP%" --query "defaultHostName" --output tsv 2^>nul') do set FUNCTION_HOST=%%i
if defined FUNCTION_HOST (
    echo 🔗 Function App URL: https://!FUNCTION_HOST!
    echo.
)

echo ✅ Fast deployment script completed! 🚀

REM Cleanup
echo ℹ️  Cleaning up deployment package...
if exist "%DEPLOYMENT_ZIP%" (
    del "%DEPLOYMENT_ZIP%"
    echo ✅ Deployment package cleaned up
)

pause
