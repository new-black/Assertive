#!/bin/bash

set -e

if [ $# -ne 2 ]; then
    echo "Usage: $0 <version> <api-key>"
    echo "Example: $0 0.18.0 your-api-key-here"
    exit 1
fi

VERSION=$1
API_KEY=$2
NUGET_SOURCE="https://api.nuget.org/v3/index.json"

ASSERTIVE_PKG="artifacts/Assertive.${VERSION}.nupkg"
XUNIT_PKG="artifacts/Assertive.xUnit.${VERSION}.nupkg"
XUNIT_V3_PKG="artifacts/Assertive.xUnit.v3.${VERSION}.nupkg"
# Assertive.Mocking is versioned independently of the core package, so discover whatever version
# pack.sh produced rather than assuming it matches $VERSION.
MOCKING_PKG="$(ls artifacts/Assertive.Mocking.*.nupkg 2>/dev/null | head -n1 || true)"

echo "Pushing Assertive packages version ${VERSION} to NuGet..."

# Check that packages exist
for pkg in "$ASSERTIVE_PKG" "$XUNIT_PKG" "$XUNIT_V3_PKG"; do
    if [ ! -f "$pkg" ]; then
        echo "Error: Package not found: $pkg"
        echo "Make sure to run ./pack.sh first"
        exit 1
    fi
done

if [ -z "$MOCKING_PKG" ] || [ ! -f "$MOCKING_PKG" ]; then
    echo "Error: Assertive.Mocking package not found in artifacts/"
    echo "Make sure to run ./pack.sh first"
    exit 1
fi

# Note: dotnet nuget push automatically uploads .snupkg symbols if present in the same directory
echo "Pushing Assertive.${VERSION}.nupkg..."
dotnet nuget push "$ASSERTIVE_PKG" --api-key "$API_KEY" --source "$NUGET_SOURCE" --skip-duplicate

echo "Pushing Assertive.xUnit.${VERSION}.nupkg..."
dotnet nuget push "$XUNIT_PKG" --api-key "$API_KEY" --source "$NUGET_SOURCE" --skip-duplicate

echo "Pushing Assertive.xUnit.v3.${VERSION}.nupkg..."
dotnet nuget push "$XUNIT_V3_PKG" --api-key "$API_KEY" --source "$NUGET_SOURCE" --skip-duplicate

echo "Pushing $(basename "$MOCKING_PKG")..."
dotnet nuget push "$MOCKING_PKG" --api-key "$API_KEY" --source "$NUGET_SOURCE" --skip-duplicate

echo "Done! All packages pushed successfully."
