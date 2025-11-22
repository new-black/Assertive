#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

dotnet restore Assertive.sln
dotnet build Assertive.sln -c Release --no-restore
dotnet test Assertive.sln -c Release --no-build
