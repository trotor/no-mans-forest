#!/usr/bin/env bash
# Builds and starts the No Man's Forest test skirmish.
# Usage: tools/run_game.sh [-- game args, e.g. -- --demo]
set -euo pipefail
cd "$(dirname "$0")/.."
GODOT="${GODOT:-godot-mono}"
dotnet build src/Nmf.Game/Nmf.Game.csproj -v q -nologo
if [ ! -d src/Nmf.Game/.godot ]; then
  "$GODOT" --headless --path src/Nmf.Game --import
fi
exec "$GODOT" --path src/Nmf.Game "$@"
