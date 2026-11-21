#!/usr/bin/env bash
# Prepares the Unity project: shared rule/protocol/content DLLs (and their NuGet dependencies) go to
# Assets/Plugins/Realmforge, YAML content is mirrored into Resources. Run after changing server/src/Shared.*
# or content/. Unity compiles Client.Core from source and gets Newtonsoft.Json from its own package;
# System.Memory/Buffers/Vectors/Unsafe already ship with Unity's .NET Standard 2.1 profile and would clash.
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
PLUGINS="$REPO/client/Assets/Plugins/Realmforge"
RESOURCES="$REPO/client/Assets/Game/Resources"
PUBLISH="$REPO/artifacts/unity-plugins"
EXCLUDED_ASSEMBLIES=" Realmforge.Client.Core.dll Newtonsoft.Json.dll System.Buffers.dll System.Memory.dll System.Numerics.Vectors.dll System.Runtime.CompilerServices.Unsafe.dll "

rm -rf "$PUBLISH"
dotnet publish "$REPO/client/Client.Core/Client.Core.csproj" -c Release -o "$PUBLISH" --nologo -v quiet

mkdir -p "$PLUGINS"
find "$PLUGINS" -name "*.dll" -delete
for dll in "$PUBLISH"/*.dll; do
  name="$(basename "$dll")"
  if [[ "$EXCLUDED_ASSEMBLIES" != *" $name "* ]]; then
    cp "$dll" "$PLUGINS/"
  fi
done

dotnet run --project "$REPO/tools/ContentTool" -c Release -- export-unity "$REPO/content" "$RESOURCES/Content"
echo "Unity plugins synced to $PLUGINS:"
ls "$PLUGINS"
