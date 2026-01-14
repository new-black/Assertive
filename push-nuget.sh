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

ASSERTIVE_PKG="src/Assertive/bin/Release/Assertive.${VERSION}.nupkg"
ASSERTIVE_SYMBOLS="src/Assertive/bin/Release/Assertive.${VERSION}.snupkg"
XUNIT_PKG="src/Assertive.xUnit/bin/Release/Assertive.xUnit.${VERSION}.nupkg"
XUNIT_SYMBOLS="src/Assertive.xUnit/bin/Release/Assertive.xUnit.${VERSION}.snupkg"

echo "Pushing Assertive packages version ${VERSION} to NuGet..."

# Check that all packages exist
for pkg in "$ASSERTIVE_PKG" "$ASSERTIVE_SYMBOLS" "$XUNIT_PKG" "$XUNIT_SYMBOLS"; do
    if [ ! -f "$pkg" ]; then
        echo "Error: Package not found: $pkg"
        echo "Make sure to build in Release mode first: dotnet pack -c Release"
        exit 1
    fi
done

echo "Pushing Assertive.${VERSION}.nupkg..."
dotnet nuget push "$ASSERTIVE_PKG" --api-key "$API_KEY" --source "$NUGET_SOURCE"

echo "Pushing Assertive.${VERSION}.snupkg..."
dotnet nuget push "$ASSERTIVE_SYMBOLS" --api-key "$API_KEY" --source "$NUGET_SOURCE"

echo "Pushing Assertive.xUnit.${VERSION}.nupkg..."
dotnet nuget push "$XUNIT_PKG" --api-key "$API_KEY" --source "$NUGET_SOURCE"

echo "Pushing Assertive.xUnit.${VERSION}.snupkg..."
dotnet nuget push "$XUNIT_SYMBOLS" --api-key "$API_KEY" --source "$NUGET_SOURCE"

echo "Done! All packages pushed successfully."
