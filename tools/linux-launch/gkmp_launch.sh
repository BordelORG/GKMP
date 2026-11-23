#!/bin/sh
# GKMP: Steam launch option wrapper for the NATIVE Linux build of Graveyard Keeper.
# Steam option:  "<game dir>/gkmp_launch.sh" %command%
# Steam runs native games through the Steam Linux Runtime container, so BepInEx (doorstop)
# must be set up INSIDE it. We only insert gkmp_inner.sh right before the game executable
# in Steam's command line; every argument stays quoted (paths contain spaces).
DIR=$(cd "$(dirname "$0")" && pwd)
INNER="$DIR/gkmp_inner.sh"
n=$#
inserted=0
while [ "$n" -gt 0 ]; do
    arg=$1; shift; n=$((n-1))
    case "$arg" in
        */"Graveyard Keeper.x86_64"|"Graveyard Keeper.x86_64")
            if [ "$inserted" = 0 ]; then set -- "$@" "$INNER"; inserted=1; fi ;;
    esac
    set -- "$@" "$arg"
done
[ "$inserted" = 1 ] || echo "[gkmp_launch] game executable not found in command line; starting without BepInEx" >&2
exec "$@"
