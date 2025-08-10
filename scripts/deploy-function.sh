#!/bin/bash

# Fast Azure Function deployment script
# This script builds and deploys the Azure Function without going through CDKTF

set -e  # Exit on any error

echo "🚀 Starting fast Azure Function deployment..."

# Configuration
RESOURCE_GROUP="GradingEngineAssignmentResourceGroup"
FUNCTION_APP_NAME="GraderFunctionApp"
PROJECT_PATH="/workspaces/AzureAutomaticGradingEngine_Assignments/GraderFunctionApp"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

log_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

log_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

log_error() {
    echo -e "${RED}❌ $1${NC}"
}

# Step 1: Clean previous builds
log_info "Cleaning previous builds..."
cd "$PROJECT_PATH"
dotnet clean --configuration Release
log_success "Clean completed"

# Step 2: Build the project
log_info "Building the project..."
dotnet build --configuration Release
if [ $? -ne 0 ]; then
    log_error "Build failed"
    exit 1
fi
log_success "Build completed"

# Step 3: Publish the project
log_info "Publishing the project..."
dotnet publish --configuration Release --output bin/Release/publish
if [ $? -ne 0 ]; then
    log_error "Publish failed"
    exit 1
fi
log_success "Publish completed"

# Step 4: Create deployment package
log_info "Creating deployment package..."
DEPLOYMENT_ZIP="$PROJECT_PATH/deployment.zip"
if [ -f "$DEPLOYMENT_ZIP" ]; then
    rm "$DEPLOYMENT_ZIP"
fi
cd bin/Release/publish
zip -r "$DEPLOYMENT_ZIP" . > /dev/null 2>&1
cd ../../..
log_success "Deployment package created at: $DEPLOYMENT_ZIP"

# Step 5: Check if Function App exists
log_info "Checking if Function App exists..."
if ! az functionapp show --name "$FUNCTION_APP_NAME" --resource-group "$RESOURCE_GROUP" > /dev/null 2>&1; then
    log_error "Function App '$FUNCTION_APP_NAME' not found in resource group '$RESOURCE_GROUP'"
    log_info "Please deploy the infrastructure first using: cd Infrastructure && npx cdktf deploy"
    exit 1
fi
log_success "Function App found"

# Step 6: Stop the Function App (optional, for faster deployment)
log_info "Stopping Function App for faster deployment..."
az functionapp stop --name "$FUNCTION_APP_NAME" --resource-group "$RESOURCE_GROUP" > /dev/null 2>&1
log_success "Function App stopped"

# Step 7: Deploy to Azure
log_info "Deploying to Azure Function App..."
az functionapp deployment source config-zip \
    --resource-group "$RESOURCE_GROUP" \
    --name "$FUNCTION_APP_NAME" \
    --src "$DEPLOYMENT_ZIP" \
    --timeout 600
if [ $? -ne 0 ]; then
    log_error "Deployment failed"
    exit 1
fi
log_success "Deployment completed"

# Step 8: Start the Function App
log_info "Starting Function App..."
az functionapp start --name "$FUNCTION_APP_NAME" --resource-group "$RESOURCE_GROUP" > /dev/null 2>&1
log_success "Function App started"

# Step 9: Get Function URLs
log_info "Getting Function URLs..."
GRADER_FUNCTION_URL=$(az functionapp function show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$FUNCTION_APP_NAME" \
    --function-name "AzureGraderFunction" \
    --query "invokeUrlTemplate" \
    --output tsv 2>/dev/null)

GAME_TASK_FUNCTION_URL=$(az functionapp function show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$FUNCTION_APP_NAME" \
    --function-name "GameTaskFunction" \
    --query "invokeUrlTemplate" \
    --output tsv 2>/dev/null)

echo ""
echo "🎉 Deployment completed successfully!"
echo ""
echo "📍 Function App Details:"
echo "   Resource Group: $RESOURCE_GROUP"
echo "   Function App: $FUNCTION_APP_NAME"
echo ""
echo "🔗 Function URLs:"
if [ ! -z "$GRADER_FUNCTION_URL" ]; then
    echo "   AzureGraderFunction: $GRADER_FUNCTION_URL"
fi
if [ ! -z "$GAME_TASK_FUNCTION_URL" ]; then
    echo "   GameTaskFunction: $GAME_TASK_FUNCTION_URL"
fi
echo ""

# Step 10: Show recent logs
log_info "Showing recent deployment logs..."
echo "Recent logs (last 50 lines):"
az functionapp logs tail --name "$FUNCTION_APP_NAME" --resource-group "$RESOURCE_GROUP" --max-lines 50 || log_warning "Could not retrieve logs"

# Step 11: Cleanup
log_info "Cleaning up deployment package..."
if [ -f "$DEPLOYMENT_ZIP" ]; then
    rm "$DEPLOYMENT_ZIP"
    log_success "Deployment package cleaned up"
fi

echo ""
log_success "Fast deployment script completed! 🚀"
