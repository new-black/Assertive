#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

dotnet restore src/Assertive.sln
dotnet build src/Assertive.sln -c Release --no-restore
for proj in src/Assertive.Test*/; do
  dotnet test "$proj" -c Release --no-build
done