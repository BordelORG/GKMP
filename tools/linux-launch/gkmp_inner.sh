#!/bin/sh
# GKMP: runs inside the Steam Linux Runtime; enables Unity Doorstop (BepInEx 5) and starts the game.
# Usage (done by gkmp_launch.sh): gkmp_inner.sh "<game dir>/Graveyard Keeper.x86_64" [args]
DIR=$(cd "$(dirname "$0")" && pwd)
export DOORSTOP_ENABLED=1
export DOORSTOP_TARGET_ASSEMBLY="$DIR/BepInEx/core/BepInEx.Preloader.dll"
export DOORSTOP_IGNORE_DISABLED_ENV=0
export DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE=""
export DOORSTOP_MONO_DEBUG_ENABLED=0
export DOORSTOP_MONO_DEBUG_ADDRESS="127.0.0.1:10000"
export DOORSTOP_MONO_DEBUG_SUSPEND=0
# LD_PRELOAD splits on spaces, so preload by bare name and resolve it via LD_LIBRARY_PATH.
export LD_LIBRARY_PATH="$DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export LD_PRELOAD="libdoorstop.so${LD_PRELOAD:+:$LD_PRELOAD}"
echo "[gkmp_inner] doorstop -> $DOORSTOP_TARGET_ASSEMBLY" >&2
exec "$@"
