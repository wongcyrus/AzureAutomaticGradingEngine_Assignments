#!/usr/bin/env bash

set -euo pipefail

for command in az python3; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Error: $command is required." >&2
    exit 1
  fi
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
resource_group="${GRADING_RESOURCE_GROUP:-GradingEngineAssignmentResourceGroup}"
storage_account="${GRADING_STORAGE_ACCOUNT:-azureisekaigrading2026}"

export AZURE_STORAGE_ACCOUNT="$storage_account"
export AZURE_STORAGE_KEY="$(
  az storage account keys list \
    --resource-group "$resource_group" \
    --account-name "$storage_account" \
    --query '[0].value' \
    --output tsv
)"
trap 'unset AZURE_STORAGE_KEY' EXIT

python3 - "$script_dir/data/NPCCharacter.csv" "$script_dir/data/EasterEgg.csv" <<'PY'
import csv
import subprocess
import sys
from pathlib import Path

for table_name, csv_path in zip(("NPCCharacter", "EasterEgg"), sys.argv[1:]):
    with Path(csv_path).open(encoding="utf-8", newline="") as csv_file:
        rows = list(csv.DictReader(csv_file))

    for row in rows:
        entity = []
        for key, value in row.items():
            if not key.endswith("@type"):
                entity.append(f"{key}={value}")

        for key, value in row.items():
            if key.endswith("@type") and value and value != "String":
                entity.append(f"{key.removesuffix('@type')}@odata.type=Edm.{value}")

        subprocess.run(
            [
                "az", "storage", "entity", "insert",
                "--table-name", table_name,
                "--if-exists", "merge",
                "--entity", *entity,
                "--only-show-errors",
                "--output", "none",
            ],
            check=True,
        )

    print(f"Seeded {len(rows)} {table_name} entities.")
PY
