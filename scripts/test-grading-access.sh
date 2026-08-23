#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMP_DIR=$(mktemp -d /tmp/grading-access-tests-XXXXXX)
trap 'rm -rf "$TEMP_DIR"' EXIT

SUBSCRIPTION_ID="11111111-1111-1111-1111-111111111111"
GRADING_PRINCIPAL_ID="22222222-2222-2222-2222-222222222222"
GRADING_TENANT_ID="33333333-3333-3333-3333-333333333333"
INSTRUCTOR_PRINCIPAL_ID="44444444-4444-4444-4444-444444444444"
STUDENT_EMAIL="student@example.com"
FAKE_BIN="$TEMP_DIR/bin"
LOG_FILE="$TEMP_DIR/az.log"
mkdir -p "$FAKE_BIN"

# shellcheck source=grading-access-common.sh
source "$SCRIPT_DIR/grading-access-common.sh"

cat >"$FAKE_BIN/az" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%q ' "$@" >>"$FAKE_AZ_LOG"
printf '\n' >>"$FAKE_AZ_LOG"

case "$*" in
  *"account show"*"--query tenantId"*)
    echo "$FAKE_STUDENT_TENANT"
    ;;
  *"role assignment list"*)
    if [[ "${FAKE_DIRECT_ASSIGNMENTS_EXIST:-false}" == "true" ]]; then
      echo "1"
    else
      echo "0"
    fi
    ;;
  *"group show"*"--query tags.GradingStudentEmail"*)
    echo "${FAKE_REGISTERED_EMAIL:-student@example.com}"
    ;;
  *"managedservices definition show"*|*"managedservices assignment show"*)
    if [[ "${FAKE_RESOURCES_EXIST:-false}" != "true" ]]; then
      exit 3
    fi
    ;;
esac
EOF
chmod +x "$FAKE_BIN/az"

run_onboard() {
  local tenant_id="$1"
  local include_instructor="${2:-true}"
  local -a instructor_args=()
  if [[ "$include_instructor" == "true" ]]; then
    instructor_args=(-i "$INSTRUCTOR_PRINCIPAL_ID")
  fi
  : >"$LOG_FILE"
  PATH="$FAKE_BIN:$PATH" \
  FAKE_AZ_LOG="$LOG_FILE" \
  FAKE_STUDENT_TENANT="$tenant_id" \
    "$SCRIPT_DIR/onboard-managed-identity.sh" \
      -s "$SUBSCRIPTION_ID" \
      -p "$GRADING_PRINCIPAL_ID" \
      -t "$GRADING_TENANT_ID" \
      -e "$STUDENT_EMAIL" \
      "${instructor_args[@]}"
}

run_offboard() {
  local tenant_id="$1"
  : >"$LOG_FILE"
  PATH="$FAKE_BIN:$PATH" \
  FAKE_AZ_LOG="$LOG_FILE" \
  FAKE_STUDENT_TENANT="$tenant_id" \
  FAKE_RESOURCES_EXIST=true \
  FAKE_DIRECT_ASSIGNMENTS_EXIST=true \
    "$SCRIPT_DIR/offboard-managed-identity.sh" \
      -s "$SUBSCRIPTION_ID" \
      -p "$GRADING_PRINCIPAL_ID" \
      -t "$GRADING_TENANT_ID" \
      -e "$STUDENT_EMAIL" \
      -i "$INSTRUCTOR_PRINCIPAL_ID"
}

assert_count() {
  local expected="$1"
  local pattern="$2"
  local actual
  actual=$(grep -c -- "$pattern" "$LOG_FILE" || true)
  if [[ "$actual" != "$expected" ]]; then
    echo "Expected $expected lines matching '$pattern', found $actual." >&2
    cat "$LOG_FILE" >&2
    exit 1
  fi
}

assert_occurrences() {
  local expected="$1"
  local pattern="$2"
  local actual
  actual=$(grep -o -- "$pattern" "$LOG_FILE" | wc -l)
  if [[ "$actual" != "$expected" ]]; then
    echo "Expected $expected occurrences of '$pattern', found $actual." >&2
    cat "$LOG_FILE" >&2
    exit 1
  fi
}

run_onboard "$GRADING_TENANT_ID"
assert_count 4 "role assignment create"
assert_count 0 "deployment sub create"

run_onboard "55555555-5555-5555-5555-555555555555"
assert_count 2 "deployment sub create"
assert_count 1 "lighthouse/subscription.json"
assert_count 1 "lighthouse/resource-group.json"
assert_occurrences 4 "$READER_ROLE_ID"
assert_occurrences 2 "$WEBSITE_CONTRIBUTOR_ROLE_ID"
assert_count 0 "role assignment create"

# The same offer IDs update authorizations when optional instructor access changes.
run_onboard "55555555-5555-5555-5555-555555555555" false
assert_count 2 "deployment sub create"
assert_occurrences 2 "$READER_ROLE_ID"
assert_occurrences 1 "$WEBSITE_CONTRIBUTOR_ROLE_ID"

run_offboard "$GRADING_TENANT_ID"
assert_count 4 "role assignment delete"
assert_count 0 "managedservices"

run_offboard "55555555-5555-5555-5555-555555555555"
assert_count 2 "managedservices assignment delete"
assert_count 2 "managedservices definition delete"
assert_count 0 "role assignment delete"

if PATH="$FAKE_BIN:$PATH" \
  FAKE_AZ_LOG="$LOG_FILE" \
  FAKE_STUDENT_TENANT="55555555-5555-5555-5555-555555555555" \
    "$SCRIPT_DIR/onboard-managed-identity.sh" \
      -s "$SUBSCRIPTION_ID" \
      -p "$GRADING_PRINCIPAL_ID" \
      -t "$GRADING_TENANT_ID" \
      -e "$STUDENT_EMAIL" \
      -m direct >/dev/null 2>&1; then
  echo "Forced direct mode unexpectedly accepted a cross-tenant subscription." >&2
  exit 1
fi

if PATH="$FAKE_BIN:$PATH" \
  FAKE_AZ_LOG="$LOG_FILE" \
  FAKE_STUDENT_TENANT="$GRADING_TENANT_ID" \
  FAKE_REGISTERED_EMAIL="other@example.com" \
    "$SCRIPT_DIR/offboard-managed-identity.sh" \
      -s "$SUBSCRIPTION_ID" \
      -p "$GRADING_PRINCIPAL_ID" \
      -t "$GRADING_TENANT_ID" \
      -e "$STUDENT_EMAIL" >/dev/null 2>&1; then
  echo "Offboarding unexpectedly accepted a mismatched ownership tag." >&2
  exit 1
fi

echo "Grading access script tests passed."
