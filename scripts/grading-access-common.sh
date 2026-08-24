#!/usr/bin/env bash

READER_ROLE_ID="acdd72a7-3385-48ef-bd42-f606fba81ae7"
CONTRIBUTOR_ROLE_ID="b24988ac-6180-42a0-ab88-20f7382dd24c"
LEGACY_WEBSITE_CONTRIBUTOR_ROLE_ID="de139f84-1756-47ae-9be6-808fbbe84772"

require_guid() {
  local label="$1"
  local value="$2"

  if [[ ! "$value" =~ ^[[:xdigit:]]{8}-[[:xdigit:]]{4}-[[:xdigit:]]{4}-[[:xdigit:]]{4}-[[:xdigit:]]{12}$ ]]; then
    echo "$label must be a GUID." >&2
    return 1
  fi
}

deterministic_guid() {
  local digest
  digest=$(printf '%s' "$1" | sha256sum | cut -c1-32)
  printf '%s-%s-%s-%s-%s\n' \
    "${digest:0:8}" \
    "${digest:8:4}" \
    "${digest:12:4}" \
    "${digest:16:4}" \
    "${digest:20:12}"
}

resolve_access_mode() {
  local requested_mode="$1"
  local student_tenant_id="${2,,}"
  local grading_tenant_id="${3,,}"

  case "$requested_mode" in
    auto)
      if [[ "$student_tenant_id" == "$grading_tenant_id" ]]; then
        echo "direct"
      else
        echo "lighthouse"
      fi
      ;;
    direct)
      if [[ "$student_tenant_id" != "$grading_tenant_id" ]]; then
        echo "Direct RBAC cannot grant a managed identity from another tenant. Use auto or lighthouse mode." >&2
        return 1
      fi
      echo "direct"
      ;;
    lighthouse)
      if [[ "$student_tenant_id" == "$grading_tenant_id" ]]; then
        echo "Lighthouse mode is only used for cross-tenant subscriptions. Use auto or direct mode." >&2
        return 1
      fi
      echo "lighthouse"
      ;;
    *)
      echo "Access mode must be auto, direct, or lighthouse." >&2
      return 1
      ;;
  esac
}

lighthouse_definition_id() {
  deterministic_guid "azure-isekai|definition|$1|$2|$3|$4|$5"
}

lighthouse_assignment_id() {
  deterministic_guid "azure-isekai|assignment|$1|$2|$3|$4|$5"
}
