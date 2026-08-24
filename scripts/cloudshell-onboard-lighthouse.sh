#!/usr/bin/env bash

set -euo pipefail

if [[ $# -gt 1 ]]; then
  echo "Usage: bash -s -- [location]" >&2
  exit 2
fi

location="${1:-eastasia}"
gist_base="https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw"
gist_cache_buster="$(date +%s%N)"

curl -fsSL "$gist_base/cloudshell-onboard.sh?v=$gist_cache_buster" \
  | bash -s -- "$location" lighthouse
