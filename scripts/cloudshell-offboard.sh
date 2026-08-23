#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 0 ]]; then
  echo "Usage: bash"
  exit 2
fi

grading_principal_id="8feba365-a613-4d15-adfd-162e7feee3ec"
grading_tenant_id="8ff7db19-435d-4c3c-83d3-ca0a46234f51"
instructor_principal_id="76407111-df2d-4199-b496-fd6b68c4bb91"
gist_base="https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw"
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

for command in az curl; do
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
echo "Using current Cloud Shell subscription: $subscription_name ($subscription_id)"
echo "Using current Cloud Shell identity: $student_email"

curl -fsSLo "$work_dir/offboard-managed-identity.sh" \
  "$gist_base/offboard-managed-identity.sh"
curl -fsSLo "$work_dir/grading-access-common.sh" \
  "$gist_base/grading-access-common.sh"
chmod +x "$work_dir/offboard-managed-identity.sh"

"$work_dir/offboard-managed-identity.sh" \
  -s "$subscription_id" \
  -p "$grading_principal_id" \
  -t "$grading_tenant_id" \
  -e "$student_email" \
  -i "$instructor_principal_id" \
  -m auto

echo "Azure Isekai access removed. The projProd resource group was kept."
