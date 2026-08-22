#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)

read -r -p "Azure Isekai sign-in email: " STUDENT_EMAIL

"$SCRIPT_DIR/onboard-managed-identity.sh" \
  -s ed3efa51-d3c7-44a4-8223-0cd48c1cfa5f \
  -p 8feba365-a613-4d15-adfd-162e7feee3ec \
  -t 8ff7db19-435d-4c3c-83d3-ca0a46234f51 \
  -e "$STUDENT_EMAIL"
