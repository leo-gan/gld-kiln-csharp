#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir"' EXIT
dotnet run --project src/Gld.Kilngen/Gld.Kilngen.csproj -c Release -- testdata/schema/point.json -o "$tmpdir/point.cs"
dotnet run --project src/Gld.Kilngen/Gld.Kilngen.csproj -c Release -- testdata/schema/point.json -o "$tmpdir/point2.cs"
dotnet run --project src/Gld.Kilngen/Gld.Kilngen.csproj -c Release -- testdata/schema/message.json -o "$tmpdir/message.cs"
dotnet run --project src/Gld.Kilngen/Gld.Kilngen.csproj -c Release -- testdata/schema/message.json -o "$tmpdir/message2.cs"
cmp "$tmpdir/point.cs" "$tmpdir/point2.cs"
cmp "$tmpdir/message.cs" "$tmpdir/message2.cs"
