#!/usr/bin/env bash

set -euo pipefail

resource_group="GradingEngineAssignmentResourceGroup"
storage_account=""
assume_yes=false
student_email=""
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

usage() {
  cat <<'EOF'
Usage: scripts/release-student-subscription.sh [options] <student-email>

Atomically removes a consistent pair of indexes from the grading storage
account's SubscriptionRegistrations table. This administrator-only command
does not alter Azure access, tags, game progress, reports, or test results.

Options:
  --resource-group <name>    Grading resource group
  --storage-account <name>   Grading storage account (auto-detected by default)
  --yes                      Skip the confirmation prompt
  -h, --help                 Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --resource-group)
      [[ $# -ge 2 ]] || {
        echo "Error: --resource-group requires a value." >&2
        exit 2
      }
      resource_group="$2"
      shift 2
      ;;
    --storage-account)
      [[ $# -ge 2 ]] || {
        echo "Error: --storage-account requires a value." >&2
        exit 2
      }
      storage_account="$2"
      shift 2
      ;;
    --yes)
      assume_yes=true
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

for command in az dotnet; do
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

connection_string="$(
  az storage account show-connection-string \
    --resource-group "$resource_group" \
    --name "$storage_account" \
    --query connectionString \
    --output tsv
)"
if [[ -z "$connection_string" ]]; then
  echo "Error: grading storage credentials are unavailable." >&2
  exit 1
fi

admin_args=("$student_email")
if [[ "$assume_yes" == true ]]; then
  admin_args+=(--yes)
fi

GRADING_STORAGE_CONNECTION_STRING="$connection_string" \
  dotnet run \
    --project "$script_dir/StudentSubscriptionAdmin/StudentSubscriptionAdmin.csproj" \
    --configuration Release \
    --no-launch-profile \
    -- "${admin_args[@]}"
