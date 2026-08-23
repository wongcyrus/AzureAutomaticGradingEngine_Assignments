#!/usr/bin/env bash

set -euo pipefail

if [[ $# -gt 1 ]]; then
  echo "Usage: bash -s -- [location]" >&2
  exit 2
fi

location="${1:-eastasia}"
grading_principal_id="8feba365-a613-4d15-adfd-162e7feee3ec"
grading_tenant_id="8ff7db19-435d-4c3c-83d3-ca0a46234f51"
instructor_principal_id="${AZURE_ISEKAI_DEBUG_INSTRUCTOR_ID:-}"
gist_base="https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw"
gist_cache_buster="$(date +%s%N)"
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

for command in az curl jq; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Error: $command is required." >&2
    exit 1
  fi
done

subscription_id="$(az account show --query id --output tsv)"
subscription_name="$(az account show --query name --output tsv)"
student_email="$(az account show --query user.name --output tsv)"
student_email="${student_email,,}"
if [[ "$student_email" =~ [[:space:]] ]] || [[ "$student_email" != *@* ]]; then
  echo "Error: could not detect a student email from the active Azure CLI identity." >&2
  exit 2
fi
student_tenant_id="$(
  az account show \
    --subscription "$subscription_id" \
    --query tenantId \
    --output tsv
)"
echo "Using current Cloud Shell subscription: $subscription_name ($subscription_id)"
echo "Using current Cloud Shell identity: $student_email"
if [[ "${student_tenant_id,,}" == "$grading_tenant_id" ]]; then
  echo "Access mode: same-tenant direct RBAC"
else
  echo "Access mode: cross-tenant Azure Lighthouse"
fi

mkdir -p "$work_dir/lighthouse"
curl -fsSLo "$work_dir/onboard-managed-identity.sh" \
  "$gist_base/onboard-managed-identity.sh?v=$gist_cache_buster"
curl -fsSLo "$work_dir/grading-access-common.sh" \
  "$gist_base/grading-access-common.sh?v=$gist_cache_buster"
curl -fsSLo "$work_dir/lighthouse/subscription.json" \
  "$gist_base/subscription.json?v=$gist_cache_buster"
curl -fsSLo "$work_dir/lighthouse/resource-group.json" \
  "$gist_base/resource-group.json?v=$gist_cache_buster"
chmod +x "$work_dir/onboard-managed-identity.sh"

az group create \
  --subscription "$subscription_id" \
  --name projProd \
  --location "$location" \
  --only-show-errors \
  --output none

onboarding_args=(
  -s "$subscription_id"
  -p "$grading_principal_id"
  -t "$grading_tenant_id"
  -e "$student_email"
  -m auto
)
if [[ -n "$instructor_principal_id" ]]; then
  onboarding_args+=(-i "$instructor_principal_id")
  echo "Instructor debug access: enabled."
else
  echo "Instructor debug access: not granted."
fi

"$work_dir/onboard-managed-identity.sh" "${onboarding_args[@]}"
