#!/usr/bin/env bash
# Playwright fails with an opaque error when its browsers are absent, so say so plainly.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

pw_cache="${HOME}/Library/Caches/ms-playwright"
[ -d "$pw_cache" ] || pw_cache="${HOME}/.cache/ms-playwright"

if ! compgen -G "${pw_cache}/chromium-*" > /dev/null; then
  echo "No Playwright chromium under ${pw_cache}." >&2
  echo "Install it: pwsh WelcomeMat.E2E/bin/Debug/net10.0/playwright.ps1 install chromium" >&2
  exit 1
fi

dotnet test WelcomeMat.E2E --configuration Debug --no-build --nologo
