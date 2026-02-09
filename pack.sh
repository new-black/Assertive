#!/bin/bash
set -e

OUTPUT_DIR="$(dirname "$0")/artifacts"
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

dotnet pack src/Assertive/Assertive.csproj -c Release -o "$OUTPUT_DIR"
dotnet pack src/Assertive.xUnit/Assertive.xUnit.csproj -c Release -o "$OUTPUT_DIR"

echo ""
echo "Packages:"
ls -1 "$OUTPUT_DIR"
