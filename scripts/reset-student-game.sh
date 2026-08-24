#!/usr/bin/env bash

set -euo pipefail

resource_group="GradingEngineAssignmentResourceGroup"
storage_account=""
assume_yes=false
purge_failures=false
purge_results=false
student_email=""

usage() {
  cat <<'EOF'
Usage: scripts/reset-student-game.sh [options] <student-email>

Deletes the student's GameStates and PassTests. Failed attempts, test-result
blobs, and the Subscription registration are preserved by default.

Options:
  --resource-group <name>    Grading resource group
  --storage-account <name>   Grading storage account (auto-detected by default)
  --purge-failures           Also delete FailTests history
  --purge-results            Also delete test-result blobs
  --yes                      Skip the confirmation prompt
  -h, --help                 Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --resource-group)
      [[ $# -ge 2 ]] || { echo "Error: --resource-group requires a value." >&2; exit 2; }
      resource_group="$2"
      shift 2
      ;;
    --storage-account)
      [[ $# -ge 2 ]] || { echo "Error: --storage-account requires a value." >&2; exit 2; }
      storage_account="$2"
      shift 2
      ;;
    --yes)
      assume_yes=true
      shift
      ;;
    --purge-failures)
      purge_failures=true
      shift
      ;;
    --purge-results)
      purge_results=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    -*)
      echo "Error: unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
    *)
      if [[ -n "$student_email" ]]; then
        echo "Error: only one student email may be specified." >&2
        exit 2
      fi
      student_email="${1,,}"
      shift
      ;;
  esac
done

if [[ -z "$student_email" ]]; then
  usage >&2
  exit 2
fi

if [[ ! "$student_email" =~ ^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$ ]]; then
  echo "Error: invalid student email: $student_email" >&2
  exit 2
fi

for command in az jq; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Error: $command is required." >&2
    exit 1
  fi
done

az account show --output none

if [[ -z "$storage_account" ]]; then
  mapfile -t storage_accounts < <(
    az storage account list \
      --resource-group "$resource_group" \
      --query '[].name' \
      --output tsv
  )
  if [[ ${#storage_accounts[@]} -ne 1 ]]; then
    echo "Error: expected one storage account in $resource_group, found ${#storage_accounts[@]}." >&2
    echo "Specify the grading account with --storage-account." >&2
    exit 1
  fi
  storage_account="${storage_accounts[0]}"
fi

query_partition() {
  local table_name="$1"
  az storage entity query \
    --account-name "$storage_account" \
    --auth-mode key \
    --table-name "$table_name" \
    --filter "PartitionKey eq '$student_email'" \
    --output json \
    2>/dev/null
}

declare -A table_rows
reset_tables=(GameStates PassTests)
if [[ "$purge_failures" == true ]]; then
  reset_tables+=(FailTests)
fi

deletable_rows=0
for table_name in GameStates PassTests FailTests; do
  table_rows["$table_name"]="$(query_partition "$table_name")"
  count="$(jq '.items | length' <<<"${table_rows[$table_name]}")"
  printf '%-12s %s row(s)\n' "$table_name" "$count"
  if [[ "$table_name" != "FailTests" || "$purge_failures" == true ]]; then
    deletable_rows=$((deletable_rows + count))
  fi
done

blobs="$(
  az storage blob list \
    --account-name "$storage_account" \
    --auth-mode key \
    --container-name test-results \
    --prefix "${student_email}_" \
    --output json \
    2>/dev/null
)"
blob_count="$(jq 'length' <<<"$blobs")"
printf '%-12s %s blob(s)\n' "TestResults" "$blob_count"
echo "Subscription registration will be preserved."
if [[ "$purge_failures" != true ]]; then
  echo "Failed-attempt history will be preserved."
fi
if [[ "$purge_results" != true ]]; then
  echo "Test-result blobs will be preserved."
fi

deletable_blobs=0
if [[ "$purge_results" == true ]]; then
  deletable_blobs=$blob_count
fi
if [[ $deletable_rows -eq 0 && $deletable_blobs -eq 0 ]]; then
  echo "Game state is already reset for $student_email."
  exit 0
fi

if [[ "$assume_yes" != true ]]; then
  if [[ ! -t 0 ]]; then
    echo "Error: confirmation requires an interactive terminal; use --yes." >&2
    exit 1
  fi
  read -r -p "Reset selected game data for $student_email? [y/N] " confirmation
  if [[ ! "$confirmation" =~ ^[Yy]$ ]]; then
    echo "Reset cancelled."
    exit 0
  fi
fi

for attempt in {1..5}; do
  for table_name in "${reset_tables[@]}"; do
    while IFS= read -r row_key; do
      az storage entity delete \
        --account-name "$storage_account" \
        --auth-mode key \
        --table-name "$table_name" \
        --partition-key "$student_email" \
        --row-key "$row_key" \
        --if-match '*' \
        --output none \
        2>/dev/null
    done < <(jq -r '.items[].RowKey' <<<"$(query_partition "$table_name")")
  done

  if [[ "$purge_results" == true ]]; then
    while IFS= read -r blob_name; do
      az storage blob delete \
        --account-name "$storage_account" \
        --auth-mode key \
        --container-name test-results \
        --name "$blob_name" \
        --output none \
        2>/dev/null
    done < <(
      az storage blob list \
        --account-name "$storage_account" \
        --auth-mode key \
        --container-name test-results \
        --prefix "${student_email}_" \
        --query '[].name' \
        --output tsv \
        2>/dev/null
    )
  fi

  sleep 1
  remaining_rows=0
  for table_name in "${reset_tables[@]}"; do
    remaining="$(jq '.items | length' <<<"$(query_partition "$table_name")")"
    remaining_rows=$((remaining_rows + remaining))
  done
  remaining_blobs=0
  if [[ "$purge_results" == true ]]; then
    remaining_blobs="$(
      az storage blob list \
        --account-name "$storage_account" \
        --auth-mode key \
        --container-name test-results \
        --prefix "${student_email}_" \
        --query 'length(@)' \
        --output tsv \
        2>/dev/null
    )"
  fi
  if [[ $remaining_rows -eq 0 && $remaining_blobs -eq 0 ]]; then
    echo "Reset complete for $student_email."
    exit 0
  fi
done

echo "Error: game data is still being created for $student_email." >&2
echo "Close the active game client, then rerun this command." >&2
exit 1
