#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test gld-kiln-csharp.sln -c Release --nologo
