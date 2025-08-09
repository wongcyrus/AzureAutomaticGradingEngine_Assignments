#!/usr/bin/env bash

set -euo pipefail

# create-sp-cloudshell.sh
# Purpose: Create or reuse a Service Principal and grant it minimal roles so tests can list Function App settings.
# Designed to run in Azure Cloud Shell (Bash). No external deps beyond Azure CLI.
#
# Roles assigned:
#  - Reader at subscription scope (as per original)
#  - Website Contributor (App Service) at the resource group scope (to allow listing app settings)
#
# Usage:
#   ./scripts/create-sp-cloudshell.sh -s <subscriptionId> -g <resourceGroupName> [-n <spName>] [-o <outputJsonPath>]
#
# Example:
#   ./scripts/create-sp-cloudshell.sh -s ef51af56-bac0-40cf-ab70-d582c23aa5f6 -g projProd \
#     -n grading-engine-sp -o ./testing/sp.json

SUB_ID=""
# Default resource group used by tests
RG_NAME="projProd"
# Default to a stable name so reruns reuse the same SP
SP_NAME="grading-engine-sp"
# Default credentials output file
OUT_JSON="sp.json"

while getopts ":s:g:n:o:" opt; do
  case ${opt} in
    s) SUB_ID="$OPTARG" ;;
    g) RG_NAME="$OPTARG" ;;
    n) SP_NAME="$OPTARG" ;;
    o) OUT_JSON="$OPTARG" ;;
    :) echo "Missing argument for -$OPTARG" >&2; exit 2 ;;
    \?) echo "Invalid option: -$OPTARG" >&2; exit 2 ;;
  esac
done

if [[ -z "$SUB_ID" || -z "$RG_NAME" ]]; then
  echo "Required: -s <subscriptionId> and -g <resourceGroupName>" >&2
  exit 2
fi

echo "Subscription: $SUB_ID"
_echo_rg() { echo "Resource Group: $RG_NAME"; }
_echo_rg

# Avoid printing secrets in Cloud Shell scrollback
echo "SP Name: $SP_NAME"
echo "Output JSON: $OUT_JSON"

az account set --subscription "$SUB_ID" >/dev/null

# Ensure RG exists
if ! az group show -n "$RG_NAME" >/dev/null 2>&1; then
  echo "Error: Resource group '$RG_NAME' not found in subscription '$SUB_ID'." >&2
  exit 3
fi

APP_ID=""
PASSWORD=""
TENANT=""
DISPLAY_NAME="$SP_NAME"

echo "Creating or reusing service principal..."

if az ad sp list --display-name "$SP_NAME" --query "[0].appId" -o tsv | grep -q "."; then
  # SP exists by display name
  APP_ID=$(az ad sp list --display-name "$SP_NAME" --query "[0].appId" -o tsv)
  if [[ -z "$APP_ID" ]]; then
    echo "Could not resolve existing SP appId for name '$SP_NAME'." >&2
    exit 4
  fi
  PASSWORD=$(az ad sp credential reset --id "$APP_ID" --query password -o tsv)
  TENANT=$(az account show --query tenantId -o tsv)
  DISPLAY_NAME=$(az ad sp show --id "$APP_ID" --query displayName -o tsv)
else
  # Create new SP without role assignments; capture values via --query to avoid parsing JSON manually
  if read -r APP_ID PASSWORD TENANT DISPLAY_NAME < <(
      az ad sp create-for-rbac \
        --name "$SP_NAME" \
        --skip-assignment \
        --only-show-errors \
        --query "[appId,password,tenant,displayName]" -o tsv
     ) && [[ -n "$APP_ID" && -n "$PASSWORD" && -n "$TENANT" ]]; then
    : # created via create-for-rbac
  else
    echo "create-for-rbac did not return credentials; falling back to explicit app + sp creation..."
    TENANT=$(az account show --query tenantId -o tsv)
    # Create application
    APP_ID=$(az ad app create --display-name "$SP_NAME" --query appId -o tsv)
    # Create SP and wait for propagation
    az ad sp create --id "$APP_ID" >/dev/null
    for attempt in {1..10}; do
      if az ad sp show --id "$APP_ID" >/dev/null 2>&1; then
        break
      fi
      echo "Waiting for SP propagation ($attempt/10)..."
      sleep 5
    done
    # Reset credentials to get a secret
    PASSWORD=$(az ad sp credential reset --id "$APP_ID" --query password -o tsv)
    DISPLAY_NAME=$(az ad sp show --id "$APP_ID" --query displayName -o tsv)
  fi
fi

if [[ -z "$APP_ID" || -z "$PASSWORD" || -z "$TENANT" ]]; then
  echo "Failed to resolve SP credentials (appId/password/tenant)." >&2
  exit 5
fi

OBJ_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)
SUB_SCOPE="/subscriptions/$SUB_ID"
RG_SCOPE=$(az group show -n "$RG_NAME" --query id -o tsv)

assign_role() {
  local principalObjId="$1" roleName="$2" scope="$3"
  if az role assignment list --assignee-object-id "$principalObjId" --role "$roleName" --scope "$scope" --query "[0]" -o tsv | grep -q ""; then
    echo "Role '$roleName' already assigned at scope '$scope'."
  else
    echo "Assigning role '$roleName' at scope '$scope'..."
    for attempt in {1..5}; do
      if az role assignment create \
          --assignee-object-id "$principalObjId" \
          --assignee-principal-type ServicePrincipal \
          --role "$roleName" \
          --scope "$scope" >/dev/null 2>&1; then
        echo "Assigned '$roleName' at '$scope'."
        return 0
      fi
      echo "Retry $attempt/5: waiting for directory propagation..."
      sleep 5
    done
    echo "Failed to assign role '$roleName' at '$scope'." >&2
    return 1
  fi
}

# Use roleDefinitionId GUID for Website Contributor to avoid name mismatches across clouds
# Website Contributor roleDefinitionId: de139f84-1756-47ae-9be6-808fbbe84772
assign_role "$OBJ_ID" "Reader" "$SUB_SCOPE"
assign_role "$OBJ_ID" "de139f84-1756-47ae-9be6-808fbbe84772" "$RG_SCOPE"

# Emit JSON in the format expected by the tests
mkdir -p "$(dirname "$OUT_JSON")"
cat >"$OUT_JSON" <<JSON
{
  "appId": "$APP_ID",
  "displayName": "$DISPLAY_NAME",
  "password": "$PASSWORD",
  "tenant": "$TENANT"
}
JSON

# Tighten permissions on the file (if supported)
chmod 600 "$OUT_JSON" 2>/dev/null || true

echo "Service principal written to: $OUT_JSON"
echo "Done."
