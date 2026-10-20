#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -z "${NUGET_API_KEY:-}" ]]; then
  echo "NuGet publish skipped: NUGET_API_KEY is unset"
  exit 0
fi
dotnet pack src/Kiln/Kiln.csproj -c Release -o dist
dotnet nuget push dist/*.nupkg --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate
