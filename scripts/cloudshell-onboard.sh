#!/usr/bin/env bash

set -euo pipefail

if [[ $# -gt 2 ]]; then
  echo "Usage: bash -s -- [location] [auto|direct|lighthouse]" >&2
  exit 2
fi

location="${1:-brazilsouth}"
access_mode="${2:-auto}"
required_location="brazilsouth"
if [[ "${location,,}" != "$required_location" ]]; then
  echo "Error: this assignment requires projProd in '$required_location'." >&2
  exit 2
fi
grading_principal_id="078c7abf-66ed-409c-9e40-e8fdb6a93221"
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

case "${access_mode,,}" in
  auto)
    if [[ "${student_tenant_id,,}" == "$grading_tenant_id" ]]; then
      resolved_access_mode="direct"
    else
      resolved_access_mode="lighthouse"
    fi
    ;;
  direct)
    if [[ "${student_tenant_id,,}" != "$grading_tenant_id" ]]; then
      echo "Error: direct onboarding requires the subscription and grader to be in the same tenant." >&2
      exit 2
    fi
    resolved_access_mode="direct"
    ;;
  lighthouse)
    if [[ "${student_tenant_id,,}" == "$grading_tenant_id" ]]; then
      echo "Error: Lighthouse onboarding requires the subscription and grader to be in different tenants." >&2
      exit 2
    fi
    resolved_access_mode="lighthouse"
    ;;
  *)
    echo "Error: access mode must be auto, direct, or lighthouse." >&2
    exit 2
    ;;
esac

echo "Using current Cloud Shell subscription: $subscription_name ($subscription_id)"
echo "Using current Cloud Shell identity: $student_email"
if [[ "$resolved_access_mode" == "direct" ]]; then
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

resource_group_location="$(
  az group show \
    --subscription "$subscription_id" \
    --name projProd \
    --query location \
    --output tsv
)"
if [[ "${resource_group_location,,}" != "${location,,}" ]]; then
  echo "Error: projProd is in '$resource_group_location', but this assignment requires '$location'." >&2
  echo "Delete and recreate projProd in '$location', then rerun onboarding." >&2
  exit 5
fi

onboarding_args=(
  -s "$subscription_id"
  -p "$grading_principal_id"
  -t "$grading_tenant_id"
  -e "$student_email"
  -m "$resolved_access_mode"
)
if [[ -n "$instructor_principal_id" ]]; then
  onboarding_args+=(-i "$instructor_principal_id")
  echo "Instructor debug access: enabled."
else
  echo "Instructor debug access: not granted."
fi

"$work_dir/onboard-managed-identity.sh" "${onboarding_args[@]}"
