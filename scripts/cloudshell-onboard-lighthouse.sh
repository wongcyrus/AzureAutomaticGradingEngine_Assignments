#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 0 ]]; then
  echo "Usage: bash" >&2
  exit 2
fi

location="brazilsouth"
gist_base="https://gist.githubusercontent.com/wongcyrus/2550892ef2c43949eaf1ba99cbf5828c/raw"
gist_cache_buster="$(date +%s%N)"

curl -fsSL "$gist_base/cloudshell-onboard.sh?v=$gist_cache_buster" \
  | bash -s -- "$location" lighthouse
