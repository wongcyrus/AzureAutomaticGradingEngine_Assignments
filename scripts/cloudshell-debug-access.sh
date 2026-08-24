#!/usr/bin/env bash

set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: bash -s -- <grant|revoke> [location]" >&2
  exit 2
fi

action="${1,,}"
location="${2:-brazilsouth}"
instructor_principal_id="76407111-df2d-4199-b496-fd6b68c4bb91"
grading_tenant_id="8ff7db19-435d-4c3c-83d3-ca0a46234f51"
gist_base="https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw"
gist_cache_buster="$(date +%s%N)"

case "$action" in
  grant)
    export AZURE_ISEKAI_DEBUG_INSTRUCTOR_ID="$instructor_principal_id"
    curl -fsSL "$gist_base/cloudshell-onboard.sh?v=$gist_cache_buster" \
      | bash -s -- "$location"
    ;;
  revoke)
    subscription_id="$(az account show --query id --output tsv)"
    student_tenant_id="$(
      az account show \
        --subscription "$subscription_id" \
        --query tenantId \
        --output tsv
    )"
    if [[ "${student_tenant_id,,}" == "$grading_tenant_id" ]]; then
      subscription_scope="/subscriptions/$subscription_id"
      resource_group_scope="$subscription_scope/resourceGroups/projProd"
      az role assignment delete \
        --subscription "$subscription_id" \
        --assignee-object-id "$instructor_principal_id" \
        --role "Reader" \
        --scope "$subscription_scope" \
        --only-show-errors
      az role assignment delete \
        --subscription "$subscription_id" \
        --assignee-object-id "$instructor_principal_id" \
        --role "Website Contributor" \
        --scope "$resource_group_scope" \
        --only-show-errors
      echo "Instructor debug access revoked; grader access remains."
    else
      unset AZURE_ISEKAI_DEBUG_INSTRUCTOR_ID
      curl -fsSL "$gist_base/cloudshell-onboard.sh?v=$gist_cache_buster" \
        | bash -s -- "$location"
      echo "Instructor debug authorization removed from Lighthouse."
    fi
    ;;
  *)
    echo "Error: action must be grant or revoke." >&2
    exit 2
    ;;
esac
