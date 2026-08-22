#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

frontend_directory="${1:-../azure-isekai}"
outputs_file="$(mktemp)"
config_directory="$(mktemp -d)"
trap 'rm -f "$outputs_file" "$config_directory/staticwebapp.config.json"; rmdir "$config_directory"' EXIT

for command in az jq npm npx; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Error: $command is required." >&2
    exit 1
  fi
done

if [[ ! -f "$frontend_directory/index.html" ]]; then
  echo "Error: frontend not found at $frontend_directory. Initialize the azure-isekai submodule first." >&2
  exit 1
fi

npm ci --prefix "$frontend_directory/api"

npx cdktn output AzureAutomaticGradingEngineGrader \
  --skip-synth \
  --outputs-file "$outputs_file" \
  --outputs-file-include-sensitive-outputs

static_web_apps_token="$(jq -r '.AzureAutomaticGradingEngineGrader.static_web_app_api_key // empty' "$outputs_file")"
client_id="$(jq -r '.AzureAutomaticGradingEngineGrader.AADB2C_PROVIDER_CLIENT_ID // empty' "$outputs_file")"
client_secret="$(jq -r '.AzureAutomaticGradingEngineGrader.AADB2C_PROVIDER_CLIENT_SECRET // empty' "$outputs_file")"
static_web_app_name="$(jq -r '.AzureAutomaticGradingEngineGrader.static_web_app_name // empty' "$outputs_file")"
resource_group_name="$(jq -r '.AzureAutomaticGradingEngineGrader.static_web_app_resource_group_name // empty' "$outputs_file")"
game_task_url="$(jq -r '.AzureAutomaticGradingEngineGrader.GradingEngineAssignmentGameTaskFunctionUrl // empty' "$outputs_file")"
grader_url="$(jq -r '.AzureAutomaticGradingEngineGrader.GradingEngineAssignmentGraderFunctionUrl // empty' "$outputs_file")"
pass_task_url="$(jq -r '.AzureAutomaticGradingEngineGrader.GradingEngineAssignmentPassTaskFunctionUrl // empty' "$outputs_file")"
registration_url="$(jq -r '.AzureAutomaticGradingEngineGrader.GradingEngineAssignmentStudentRegistrationFunctionUrl // empty' "$outputs_file")"
proxy_signing_key="$(jq -r '.AzureAutomaticGradingEngineGrader.grader_proxy_signing_key // empty' "$outputs_file")"
tenant_id="$(az account show --query tenantId --output tsv)"

if [[ -z "$static_web_apps_token" || -z "$client_id" || -z "$client_secret" ||
      -z "$static_web_app_name" || -z "$resource_group_name" ||
      -z "$game_task_url" || -z "$grader_url" || -z "$pass_task_url" ||
      -z "$registration_url" || -z "$proxy_signing_key" ||
      -z "$tenant_id" ]]; then
  echo "Error: missing required CDKTN output or Azure tenant ID." >&2
  exit 1
fi

az staticwebapp appsettings set \
  --name "$static_web_app_name" \
  --resource-group "$resource_group_name" \
  --setting-names \
    "AADB2C_PROVIDER_CLIENT_ID=$client_id" \
    "AADB2C_PROVIDER_CLIENT_SECRET=$client_secret" \
    "GameTaskFunctionUrl=$game_task_url" \
    "GraderFunctionUrl=$grader_url" \
    "PassTaskFunctionUrl=$pass_task_url" \
    "StudentRegistrationFunctionUrl=$registration_url" \
    "GRADER_PROXY_SIGNING_KEY=$proxy_signing_key" \
  --output none

jq --arg issuer "https://login.microsoftonline.com/$tenant_id/v2.0" \
  '.auth.identityProviders.azureActiveDirectory.registration.openIdIssuer = $issuer' \
  "$frontend_directory/app_location/staticwebapp.config.json" \
  > "$config_directory/staticwebapp.config.json"

npx --yes @azure/static-web-apps-cli@2.0.10 deploy "$frontend_directory" \
  --api-location "$frontend_directory/api" \
  --swa-config-location "$config_directory" \
  --deployment-token "$static_web_apps_token" \
  --env production

echo "Azure Static Web App deployed from $frontend_directory."
