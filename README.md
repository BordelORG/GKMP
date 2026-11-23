# GKMP: online co-op for Graveyard Keeper

Two players play one Graveyard Keeper world over Steam. The host's save is used; quests,
technologies, chests, crafting, gathering, combat, time and sleep are synchronized, and
each player has their own inventory.

GKMP is a fork of [GYK Back From The Grave](https://github.com/ZlordHUN/GYK-Back-From-The-Grave)
(MIT) with fixes for two-player online play. The mod itself is in [`repo/`](repo/).

## Requirements

- Graveyard Keeper 1 on Steam, game version 1.407, base game (DLCs are not supported).
- Both players: Steam, BepInEx 5 and the **same build** of `GraveyardKeeperCoop.dll`.
- Cross-platform play works (Windows and native Linux).

## Installation

### Windows

1. Build the mod (see below).
2. Extract BepInEx 5 (x64) into the game folder (next to `Graveyard Keeper.exe`) and copy
   the DLL to `BepInEx/plugins/GraveyardKeeperCoop/GraveyardKeeperCoop.dll`.
3. Start the game from Steam as usual.

### Linux (native build)

1. Download [BepInEx 5.4.23.3 for Linux x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.3)
   and extract it into `deps/bepinex/`.
2. Build the mod (see below), then run:
   ```bash
   tools/install-linux.sh            # set GRAVEYARD_KEEPER_PATH if the game is elsewhere
   ```
3. In Steam, set the game's launch options to the line the script prints:
   ```
   "<game folder>/gkmp_launch.sh" %command%
   ```
   The stock `run_bepinex.sh` fails inside the Steam Linux Runtime when the game path
   contains spaces.

`tools/install-linux.sh --uninstall` removes everything the script added. Saves are not touched.

## Playing

1. The host opens **Multiplayer → Host**, invites a friend through Steam and picks a save.
2. The friend accepts the invite. When both are ready, the host starts the game.
3. The game runs on the host's save. The joiner's personal inventory is stored on the
   joiner's computer in `BepInEx/config/`.

**Sleep:** both players have to go to bed. The night lasts until both are rested, and either
player can wake everyone with the interaction key.

**Story items:** story and tutorial rewards exist once, with the player who earned them. To give
both players a copy, set `ShareStoryRewardsWithPartner = true` in
`BepInEx/config/com.zlord.graveyardkeeper.coop.cfg`. Both players need the same value.

## Building

Requires the .NET SDK and an installed copy of the game.

```bash
cd repo
GRAVEYARD_KEEPER_PATH="/path/to/Graveyard Keeper" \
  dotnet build -c Release -p:BepInExPath=../deps/bepinex/BepInEx
# output: repo/bin/Release/net46/GraveyardKeeperCoop.dll
```

## Tools

| Folder | Purpose |
|---|---|
| `tools/CoopQA` | Automated two-player test. Runs host and client as two local game copies with Steam mocked: `python3 tools/CoopQA/run_qa.py suite`, optionally `--lag MS --drop RATE`. |
| `tools/PatchCheck` | Checks that every Harmony patch target exists in the installed game version. |
| `tools/install-linux.sh`, `tools/linux-launch` | Linux installation and Steam launch wrapper. |

## License

MIT, same as the original mod. Graveyard Keeper belongs to Lazy Bear Games; this repository
contains no game files.
