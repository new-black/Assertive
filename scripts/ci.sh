#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

dotnet restore src/Assertive.slnx
dotnet build src/Assertive.slnx -c Release --no-restore
for proj in src/Assertive.Test*/; do
  name=$(basename "$proj")
  case "$name" in
    Assertive.Test.Aot|Assertive.Test.TUnit.Aot)
      # Native AOT smoke projects are published to native executables and run directly
      # (see below), not through dotnet test.
      continue
      ;;
    Assertive.Test.TUnit)
      # TUnit is Microsoft.Testing.Platform-native. .NET 10 SDK + MTP 2.x removed VSTest-mode
      # support, so it must be invoked as an executable rather than through `dotnet test`.
      dotnet run --project "$proj" -c Release --no-build
      ;;
    *)
      dotnet test "$proj" -c Release --no-build
      ;;
  esac
done

# Native AOT smoke tests: publish each project to a native binary and run it. This is the only
# way to exercise the generated reporting once it has actually been trimmed/AOT-compiled (the
# trim/AOT analyzers run during publish, and the binaries assert behaviour at runtime). Each
# binary exits non-zero on failure, so `set -e` fails the build.
RID="${AOT_RID:-linux-x64}"

aot_bin() { echo "$1/bin/Release/net8.0/$RID/publish/$(basename "$1")"; }

# Console harness — rooted (full decomposition) then fully-trimmed (graceful source-text degradation).
dotnet publish src/Assertive.Test.Aot -c Release -r "$RID"
"$(aot_bin src/Assertive.Test.Aot)"
dotnet publish src/Assertive.Test.Aot -c Release -r "$RID" -p:RootApp=false
"$(aot_bin src/Assertive.Test.Aot)" --expect-degraded

# Realistic Assertive + TUnit, AOT-published and run through TUnit's native test host.
dotnet publish src/Assertive.Test.TUnit.Aot -c Release -r "$RID"
"$(aot_bin src/Assertive.Test.TUnit.Aot)"
