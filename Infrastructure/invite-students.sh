#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

emails_file="${1:-students.txt}"
outputs_file="$(mktemp)"
trap 'rm -f "$outputs_file"' EXIT

for command in az jq npx; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "Error: $command is required." >&2
    exit 1
  fi
done

if [[ ! -f "$emails_file" ]]; then
  echo "Error: email list not found: $emails_file" >&2
  exit 1
fi

if ! az account show >/dev/null 2>&1; then
  echo "Error: authenticate first with 'az login --use-device-code'." >&2
  exit 1
fi

npx cdktn output AzureAutomaticGradingEngineGrader \
  --skip-synth \
  --outputs-file "$outputs_file"

group_object_id="$(jq -r '.AzureAutomaticGradingEngineGrader.student_group_object_id // empty' "$outputs_file")"
invite_redirect_url="$(jq -r '.AzureAutomaticGradingEngineGrader.static_web_app_default_host_name // empty' "$outputs_file")"

if [[ -z "$group_object_id" || -z "$invite_redirect_url" ]]; then
  echo "Error: deploy the latest CDKTN stack before inviting students." >&2
  exit 1
fi

processed=0
while IFS= read -r line || [[ -n "$line" ]]; do
  email="$(printf '%s' "$line" | tr -d '\r' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
  if [[ -z "$email" || "$email" == \#* ]]; then
    continue
  fi
  if [[ ! "$email" =~ ^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$ ]]; then
    echo "Error: invalid email address: $email" >&2
    exit 1
  fi

  user_object_id="$(az ad user list \
    --filter "mail eq '$email'" \
    --query '[0].id' \
    --output tsv)"

  if [[ -z "$user_object_id" ]]; then
    invitation="$(az rest \
      --method post \
      --url 'https://graph.microsoft.com/v1.0/invitations' \
      --headers 'Content-Type=application/json' \
      --body "$(jq -n \
        --arg email "$email" \
        --arg redirect "$invite_redirect_url" \
        '{invitedUserEmailAddress: $email, inviteRedirectUrl: $redirect, sendInvitationMessage: true}')")"
    user_object_id="$(jq -r '.invitedUser.id // empty' <<<"$invitation")"
    if [[ -z "$user_object_id" ]]; then
      echo "Error: invitation did not return a user for $email." >&2
      exit 1
    fi
    echo "Invited $email."
  else
    echo "Found existing user $email."
  fi

  membership_complete=false
  for attempt in {1..12}; do
    is_member="$(az ad group member check \
      --group "$group_object_id" \
      --member-id "$user_object_id" \
      --query value \
      --output tsv \
      2>/dev/null || true)"
    if [[ "$is_member" == "true" ]]; then
      echo "$email is already in the student group."
      membership_complete=true
      break
    fi

    if az ad group member add \
      --group "$group_object_id" \
      --member-id "$user_object_id" \
      2>/dev/null; then
      echo "Added $email to the student group."
      membership_complete=true
      break
    fi

    if [[ "$attempt" -lt 12 ]]; then
      echo "Waiting for Microsoft Graph to make $email available..."
      sleep 5
    fi
  done

  if [[ "$membership_complete" != "true" ]]; then
    echo "Error: Microsoft Graph did not make $email available for group membership." >&2
    exit 1
  fi

  processed=$((processed + 1))
done < "$emails_file"

echo "Processed $processed student email(s)."
