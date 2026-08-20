#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

frontend_directory="${1:-../azure-isekai}"
outputs_file="$(mktemp)"
trap 'rm -f "$outputs_file"' EXIT

for command in jq npx; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Error: $command is required." >&2
    exit 1
  fi
done

if [[ ! -f "$frontend_directory/index.html" ]]; then
  echo "Error: frontend not found at $frontend_directory. Initialize the azure-isekai submodule first." >&2
  exit 1
fi

npx cdktn output AzureAutomaticGradingEngineGrader \
  --skip-synth \
  --outputs-file "$outputs_file" \
  --outputs-file-include-sensitive-outputs

static_web_apps_token="$(jq -r '.AzureAutomaticGradingEngineGrader.static_web_app_api_key // empty' "$outputs_file")"

if [[ -z "$static_web_apps_token" ]]; then
  echo "Error: CDKTN did not return the Static Web Apps deployment token." >&2
  exit 1
fi

npx --yes @azure/static-web-apps-cli@2.0.10 deploy "$frontend_directory" \
  --api-location "$frontend_directory/api" \
  --swa-config-location "$frontend_directory/app_location" \
  --deployment-token "$static_web_apps_token" \
  --env production

echo "Azure Static Web App deployed from $frontend_directory."
