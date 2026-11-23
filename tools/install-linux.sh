#!/usr/bin/env bash
# Installs BepInEx (Linux x64, native game build) and the GKMP build of the mod into the game.
# Usage: tools/install-linux.sh [--uninstall]
# Reversible: --uninstall removes everything this script added (saves are not touched).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME="${GRAVEYARD_KEEPER_PATH:-/mnt/FedoraData/SteamLibrary/steamapps/common/Graveyard Keeper}"
BEPINEX_SRC="$ROOT/deps/bepinex"
DLL="$ROOT/repo/bin/Release/net46/GraveyardKeeperCoop.dll"

[ -x "$GAME/Graveyard Keeper.x86_64" ] || { echo "Game not found in: $GAME"; exit 1; }

if [ "${1:-}" = "--uninstall" ]; then
  rm -rf "$GAME/BepInEx" "$GAME/doorstop_libs" "$GAME/libdoorstop.so" "$GAME/run_bepinex.sh" "$GAME/changelog.txt" "$GAME/.doorstop_version" "$GAME/gkmp_launch.sh" "$GAME/gkmp_inner.sh"
  echo "Uninstalled. Remember to clear the launch options in Steam."
  exit 0
fi

[ -f "$DLL" ] || { echo "Missing $DLL - build first: cd repo && dotnet build -c Release"; exit 1; }

cp -rn "$BEPINEX_SRC/." "$GAME/"
sed -i 's|^executable_name=""|executable_name="Graveyard Keeper.x86_64"|' "$GAME/run_bepinex.sh"
chmod +x "$GAME/run_bepinex.sh"
# run_bepinex.sh breaks under the Steam Linux Runtime (splits paths with spaces); use our wrappers.
cp "$ROOT/tools/linux-launch/gkmp_launch.sh" "$ROOT/tools/linux-launch/gkmp_inner.sh" "$GAME/"
chmod +x "$GAME/gkmp_launch.sh" "$GAME/gkmp_inner.sh"

mkdir -p "$GAME/BepInEx/plugins/GraveyardKeeperCoop"
cp "$DLL" "$GAME/BepInEx/plugins/GraveyardKeeperCoop/"

echo "Done. In Steam: Graveyard Keeper -> Properties -> Launch options:"
echo "  \"$GAME/gkmp_launch.sh\" %command%"
echo "Log after launch: $GAME/BepInEx/LogOutput.log"
