#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

repository="${1:-wongcyrus/azure-isekai}"
outputs_file="$(mktemp)"
trap 'rm -f "$outputs_file"' EXIT

for command in gh jq npx; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Error: $command is required." >&2
    exit 1
  fi
done

if ! gh auth status >/dev/null 2>&1; then
  echo "Error: authenticate first with 'gh auth login'." >&2
  exit 1
fi

npx cdktn output AzureAutomaticGradingEngineGrader \
  --skip-synth \
  --outputs-file "$outputs_file" \
  --outputs-file-include-sensitive-outputs

client_id="$(jq -r '.AzureAutomaticGradingEngineGrader.AADB2C_PROVIDER_CLIENT_ID // empty' "$outputs_file")"
client_secret="$(jq -r '.AzureAutomaticGradingEngineGrader.AADB2C_PROVIDER_CLIENT_SECRET // empty' "$outputs_file")"
static_web_apps_token="$(jq -r '.AzureAutomaticGradingEngineGrader.static_web_app_api_key // empty' "$outputs_file")"

if [[ -z "$client_id" || -z "$client_secret" || -z "$static_web_apps_token" ]]; then
  echo "Error: CDKTN did not return all required deployment outputs." >&2
  exit 1
fi

printf '%s=%s\n' \
  "AADB2C_PROVIDER_CLIENT_ID" "$client_id" \
  "AADB2C_PROVIDER_CLIENT_SECRET" "$client_secret" \
  "AZURE_STATIC_WEB_APPS_API_TOKEN" "$static_web_apps_token" |
  gh secret set --repo "$repository" --app actions --env-file -

echo "GitHub Actions secrets synchronized to $repository."
