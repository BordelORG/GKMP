#!/usr/bin/env bash
# Builds the release packages from the current mod build.
# Usage: tools/package.sh <version>        e.g. tools/package.sh 0.1.0
# Needs: repo/bin/Release/net46/GraveyardKeeperCoop.dll (dotnet build -c Release),
#        BepInEx 5.4.23.3 extracted into deps/bepinex (Linux x64) and deps/bepinex-win (Windows x64).
# Output: dist/GKMP-<version>-Windows.zip, dist/GKMP-<version>-Linux.tar.gz
set -euo pipefail
VER="${1:?usage: tools/package.sh <version>}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DLL="$ROOT/repo/bin/Release/net46/GraveyardKeeperCoop.dll"
OUT="$ROOT/dist"
[ -f "$DLL" ] || { echo "Missing $DLL - build the mod first"; exit 1; }
for d in bepinex bepinex-win; do [ -d "$ROOT/deps/$d/BepInEx/core" ] || { echo "Missing deps/$d"; exit 1; }; done
SHA=$(sha256sum "$DLL" | cut -c1-16)
TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT
mkdir -p "$OUT"

# ---- Windows
W="$TMP/win"; mkdir -p "$W/BepInEx/plugins/GraveyardKeeperCoop"
cp -r "$ROOT/deps/bepinex-win/." "$W/"
cp "$DLL" "$W/BepInEx/plugins/GraveyardKeeperCoop/"
cp "$ROOT/repo/LICENSE" "$W/GKMP-LICENSE.txt"
cat > "$W/INSTALL.txt" <<TXT
GKMP $VER - online co-op for Graveyard Keeper (Windows)
Mod DLL sha256: $SHA...   Both players must use the same build.

1. Back up your saves: %USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper\
2. Steam -> Graveyard Keeper -> right click -> Manage -> Browse local files.
3. Copy everything from this archive into that folder, next to "Graveyard Keeper.exe".
4. Start the game from Steam. The main menu now has "Multiplayer".
   Game version (shown in the main menu) must be 1.407 for both players.

Problems: after closing the game, send BepInEx\LogOutput.log (it is overwritten on the next start).
Uninstall: delete BepInEx, winhttp.dll, doorstop_config.ini, changelog.txt, .doorstop_version.
Source and documentation: https://github.com/BordelORG/GKMP
TXT
rm -f "$OUT/GKMP-$VER-Windows.zip"
(cd "$W" && zip -qr "$OUT/GKMP-$VER-Windows.zip" .)

# ---- Linux (native build, Steam)
L="$TMP/linux"; mkdir -p "$L/BepInEx/plugins/GraveyardKeeperCoop"
cp -r "$ROOT/deps/bepinex/BepInEx/core" "$L/BepInEx/"
cp "$ROOT/deps/bepinex/libdoorstop.so" "$ROOT/deps/bepinex/changelog.txt" "$L/"
cp "$ROOT/tools/linux-launch/gkmp_launch.sh" "$ROOT/tools/linux-launch/gkmp_inner.sh" "$L/"
chmod +x "$L/gkmp_launch.sh" "$L/gkmp_inner.sh"
cp "$DLL" "$L/BepInEx/plugins/GraveyardKeeperCoop/"
cp "$ROOT/repo/LICENSE" "$L/GKMP-LICENSE.txt"
cat > "$L/INSTALL.txt" <<TXT
GKMP $VER - online co-op for Graveyard Keeper (Linux, native build)
Mod DLL sha256: $SHA...   Both players must use the same build.

1. Back up your saves: ~/.config/unity3d/Lazy Bear Games/Graveyard Keeper/
   (Flatpak Steam: ~/.var/app/com.valvesoftware.Steam/.config/unity3d/Lazy Bear Games/Graveyard Keeper/)
2. Steam -> Graveyard Keeper -> right click -> Manage -> Browse local files.
3. Extract this archive into that folder, next to "Graveyard Keeper.x86_64".
4. Steam -> Graveyard Keeper -> Properties -> Launch options:
     "<game folder>/gkmp_launch.sh" %command%
   (with the full path to the game folder, in quotes).
5. Start the game from Steam. The main menu now has "Multiplayer".
   Game version (shown in the main menu) must be 1.407 for both players.

Problems: send BepInEx/LogOutput.log from the game folder.
Uninstall: clear the launch option and delete BepInEx, libdoorstop.so, gkmp_launch.sh, gkmp_inner.sh, changelog.txt.
Source and documentation: https://github.com/BordelORG/GKMP
TXT
rm -f "$OUT/GKMP-$VER-Linux.tar.gz"
tar -C "$L" -czf "$OUT/GKMP-$VER-Linux.tar.gz" .

echo "Built (mod sha256 $SHA...):"
ls -la "$OUT/GKMP-$VER-Windows.zip" "$OUT/GKMP-$VER-Linux.tar.gz"
