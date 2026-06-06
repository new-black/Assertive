#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

dotnet restore src/Assertive.slnx
dotnet build src/Assertive.slnx -c Release --no-restore
for proj in src/Assertive.Test*/; do
  name=$(basename "$proj")
  # TUnit is Microsoft.Testing.Platform-native. .NET 10 SDK + MTP 2.x removed VSTest-mode
  # support, so it must be invoked as an executable rather than through `dotnet test`.
  if [[ "$name" == "Assertive.Test.TUnit" ]]; then
    dotnet run --project "$proj" -c Release --no-build
  else
    dotnet test "$proj" -c Release --no-build
  fi
done
