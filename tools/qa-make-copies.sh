#!/usr/bin/env bash
# Creates isolated test copies qa/P1, qa/P2 of the game (mostly symlinks; ~35 MB each).
# The real install is only read. Each copy has its own BepInEx, config and save folder.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME="${GRAVEYARD_KEEPER_PATH:-/mnt/FedoraData/SteamLibrary/steamapps/common/Graveyard Keeper}"
for P in ${@:-P1 P2}; do
  D="$ROOT/qa/$P"
  mkdir -p "$D/Graveyard Keeper_Data" "$D/BepInEx/plugins" "$D/xdg"
  cp -f "$GAME/Graveyard Keeper.x86_64" "$GAME/UnityPlayer.so" "$D/"
  for e in "$GAME/Graveyard Keeper_Data/"*; do
    n=$(basename "$e")
    [ "$n" = "boot.config" ] && continue
    ln -sfn "$e" "$D/Graveyard Keeper_Data/$n"
  done
  grep -v '^single-instance' "$GAME/Graveyard Keeper_Data/boot.config" > "$D/Graveyard Keeper_Data/boot.config"
  cp -rf "$ROOT/deps/bepinex/BepInEx/core" "$D/BepInEx/"
  cp -f "$ROOT/deps/bepinex/libdoorstop.so" "$ROOT/tools/linux-launch/gkmp_inner.sh" "$D/"
  echo 599140 > "$D/steam_appid.txt"
  echo "ready: $D"
done
