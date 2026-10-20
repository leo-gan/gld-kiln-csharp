#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p examples/generated
dotnet run --project src/Gld.Kilngen/Gld.Kilngen.csproj -c Release -- testdata/schema/point.json -o examples/generated/Point.cs
dotnet run --project src/Gld.Kilngen/Gld.Kilngen.csproj -c Release -- testdata/schema/message.json -o examples/generated/Message.cs
