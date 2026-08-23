#!/usr/bin/env bash

set -euo pipefail

resource_group="${1:-GradingEngineAssignmentResourceGroup}"
function_app_name="${2:-azureisekai2026}"
test_subscription_id="${3:-${AZURE_TEST_SUBSCRIPTION_ID:-}}"
test_email="${4:-${GRADER_TEST_EMAIL:-}}"
test_filter="${5:-${AZURE_TEST_FILTER:-}}"
expected_test_count="${6:-${AZURE_EXPECTED_TEST_COUNT:-35}}"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

function_key="${AZURE_FUNCTION_KEY:-}"
proxy_signing_key="${GRADER_PROXY_SIGNING_KEY:-}"
if [[ -z "$function_key" ]]; then
  function_key="$(
    az functionapp keys list \
      --resource-group "$resource_group" \
      --name "$function_app_name" \
      --query "functionKeys.default" \
      --output tsv
  )"
fi

if [[ -z "$proxy_signing_key" ]]; then
  proxy_signing_key="$(
    az functionapp config appsettings list \
      --resource-group "$resource_group" \
      --name "$function_app_name" \
      --query "[?name=='GRADER_PROXY_SIGNING_KEY'].value | [0]" \
      --output tsv
  )"
fi

if [[ -z "$function_key" ]]; then
  echo "No host-level Function key was found for $function_app_name." >&2
  exit 1
fi

if [[ -z "$proxy_signing_key" ]]; then
  echo "No grader proxy signing key was found for $function_app_name." >&2
  exit 1
fi

export FUNCTION_APP_BASE_URL="${FUNCTION_APP_BASE_URL:-https://${function_app_name}.azurewebsites.net/}"
export AZURE_FUNCTION_KEY="$function_key"
export GRADER_PROXY_SIGNING_KEY="$proxy_signing_key"
export AZURE_TEST_SUBSCRIPTION_ID="$test_subscription_id"
export GRADER_TEST_EMAIL="$test_email"
export AZURE_TEST_FILTER="$test_filter"
export AZURE_EXPECTED_TEST_COUNT="$expected_test_count"

dotnet test \
  "$repository_root/GraderFunctionApp.IntegrationTests/GraderFunctionApp.IntegrationTests.csproj" \
  --configuration Release
