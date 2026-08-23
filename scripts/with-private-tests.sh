#!/usr/bin/env bash

set -euo pipefail

if [[ $# -eq 0 ]]; then
  echo "Usage: with-private-tests.sh <command> [args...]" >&2
  exit 2
fi

if [[ -z "${GITHUB_PACKAGES_TOKEN:-}" ]]; then
  echo "Error: GITHUB_PACKAGES_TOKEN with read:packages and repo access is required." >&2
  exit 1
fi

github_user="${GITHUB_PACKAGES_USER:-wongcyrus}"
config_file="$(mktemp)"
trap 'rm -f "$config_file"' EXIT
chmod 600 "$config_file"

cat >"$config_file" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github-private" value="https://nuget.pkg.github.com/wongcyrus/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-private>
      <add key="Username" value="$github_user" />
      <add key="ClearTextPassword" value="$GITHUB_PACKAGES_TOKEN" />
    </github-private>
  </packageSourceCredentials>
</configuration>
EOF

export UsePrivateTests=true
export RestoreConfigFile="$config_file"
"$@"
