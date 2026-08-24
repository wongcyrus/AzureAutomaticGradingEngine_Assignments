#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
SUBSCRIPTION_ID="ed3efa51-d3c7-44a4-8223-0cd48c1cfa5f"

read -r -p "Azure Isekai sign-in email: " STUDENT_EMAIL

az group create \
  --subscription "$SUBSCRIPTION_ID" \
  --name projProd \
  --location brazilsouth \
  --only-show-errors \
  >/dev/null
echo "Resource group 'projProd' is ready."

"$SCRIPT_DIR/onboard-managed-identity.sh" \
  -s "$SUBSCRIPTION_ID" \
  -p 078c7abf-66ed-409c-9e40-e8fdb6a93221 \
  -t 8ff7db19-435d-4c3c-83d3-ca0a46234f51 \
  -e "$STUDENT_EMAIL" \
  -i 76407111-df2d-4199-b496-fd6b68c4bb91
