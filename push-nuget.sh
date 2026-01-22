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
XUNIT_PKG="src/Assertive.xUnit/bin/Release/Assertive.xUnit.${VERSION}.nupkg"

echo "Pushing Assertive packages version ${VERSION} to NuGet..."

# Check that packages exist
for pkg in "$ASSERTIVE_PKG" "$XUNIT_PKG"; do
    if [ ! -f "$pkg" ]; then
        echo "Error: Package not found: $pkg"
        echo "Make sure to build in Release mode first: dotnet pack -c Release"
        exit 1
    fi
done

# Note: dotnet nuget push automatically uploads .snupkg symbols if present in the same directory
echo "Pushing Assertive.${VERSION}.nupkg..."
dotnet nuget push "$ASSERTIVE_PKG" --api-key "$API_KEY" --source "$NUGET_SOURCE"

echo "Pushing Assertive.xUnit.${VERSION}.nupkg..."
dotnet nuget push "$XUNIT_PKG" --api-key "$API_KEY" --source "$NUGET_SOURCE"

echo "Done! All packages pushed successfully."
