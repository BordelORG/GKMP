#!/usr/bin/env python3
"""CoopQA broker: a fake Steam backend (lobbies + P2P relay) for game copies on one PC.

Each game copy (CoopQA plugin) connects over TCP and speaks JSON lines.
The broker owns lobby state and pushes a full lobby snapshot to members after every change,
plus an event naming what changed so the plugin can raise the matching Steam callback.

Steam rules copied (see coop-anything PR #87 notes):
  - create: LobbyCreated_t then LobbyEnter_t
  - join: joiner gets LobbyDataUpdate_t then LobbyEnter_t; members get LobbyChatUpdate_t(Entered)
  - data change: LobbyDataUpdate_t on every member
  - owner leaves: next member becomes owner
  - P2P: sizes are checked in the plugin; the broker relays, with optional lag and drop

Usage: broker.py [--port 27800] [--lag MS] [--drop RATE] [--log FILE]
"""
import argparse
import asyncio
import json
import random
import sys
import time

STEAM_INDIVIDUAL_BASE = 76561197960265728
LOBBY_BASE = (1 << 56) | (8 << 52) | (0x20000 << 32)  # public universe, chat type, MMS lobby flag

clients = {}   # steam_id -> Client
lobbies = {}   # lobby_id -> dict
rich_presence = {}  # steam_id -> {k: v}
next_lobby = 1
args = None
logf = None


def log(*a):
    line = time.strftime("%H:%M:%S ") + " ".join(str(x) for x in a)
    print(line, flush=True)
    if logf:
        logf.write(line + "\n")
        logf.flush()


class Client:
    def __init__(self, reader, writer):
        self.reader = reader
        self.writer = writer
        self.steam_id = None
        self.name = None
        self.player = None

    async def send(self, msg):
        try:
            self.writer.write((json.dumps(msg, separators=(",", ":")) + "\n").encode())
            await self.writer.drain()
        except (ConnectionError, RuntimeError):
            pass


def lobby_state(lid):
    lb = lobbies[lid]
    return {"op": "lobby_state", "lobby": lid, "owner": lb["owner"], "members": lb["members"],
            "data": lb["data"], "mdata": {str(k): v for k, v in lb["mdata"].items()},
            "limit": lb["limit"], "type": lb["type"]}


async def push_state(lid, event=None, targets=None):
    if lid not in lobbies:
        return
    st = lobby_state(lid)
    for sid in (targets or lobbies[lid]["members"]):
        c = clients.get(sid)
        if c:
            await c.send(st)
            if event:
                await c.send(event)


async def broadcast_peers():
    peers = [{"id": c.steam_id, "name": c.name, "player": c.player} for c in clients.values()]
    for c in clients.values():
        await c.send({"op": "peers", "peers": peers})


async def leave(sid, lid):
    lb = lobbies.get(lid)
    if not lb or sid not in lb["members"]:
        return
    lb["members"].remove(sid)
    lb["mdata"].pop(sid, None)
    if not lb["members"]:
        del lobbies[lid]
        log("lobby", lid, "closed")
        return
    if lb["owner"] == sid:
        lb["owner"] = lb["members"][0]
        log("lobby", lid, "owner ->", lb["owner"])
    await push_state(lid, {"op": "chat_update", "lobby": lid, "user": sid, "by": sid, "change": 2})


blackout_until = 0.0   # loop time: until then the link is down (see op "blackout")
last_deadline = {}     # target -> last scheduled delivery, keeps each stream in order


async def deliver_p2p(target, msg, deadline):
    delay = deadline - asyncio.get_event_loop().time()
    if delay > 0:
        await asyncio.sleep(delay)
    c = clients.get(target)
    if c:
        await c.send(msg)


async def handle(msg, c):
    global next_lobby, blackout_until
    op = msg.get("op")
    if op == "hello":
        c.player = int(msg["player"])
        c.steam_id = STEAM_INDIVIDUAL_BASE + 900000000 + c.player
        c.name = msg.get("name") or f"QA-P{c.player}"
        old = clients.get(c.steam_id)
        if old and old is not c:
            log("replacing stale connection for", c.name)
        clients[c.steam_id] = c
        await c.send({"op": "welcome", "id": c.steam_id, "name": c.name})
        await broadcast_peers()
        log("hello", c.name, c.steam_id)
    elif op == "create_lobby":
        lid = LOBBY_BASE + next_lobby
        next_lobby += 1
        lobbies[lid] = {"owner": c.steam_id, "members": [c.steam_id], "data": {}, "mdata": {},
                        "limit": int(msg.get("max", 4)), "type": int(msg.get("type", 1))}
        log("create_lobby", lid, "by", c.name)
        # Real Steam: the lobby exists (and accepts SetLobbyData) when LobbyCreated_t fires.
        await c.send(lobby_state(lid))
        await c.send({"op": "lobby_created", "lobby": lid, "call": msg.get("call"), "result": 1})
        await c.send({"op": "entered", "lobby": lid, "call": msg.get("call"), "response": 1})
    elif op == "join":
        lid = int(msg["lobby"])
        lb = lobbies.get(lid)
        if not lb:
            await c.send({"op": "entered", "lobby": lid, "call": msg.get("call"), "response": 2})  # DoesntExist
            return
        if len(lb["members"]) >= lb["limit"]:
            await c.send({"op": "entered", "lobby": lid, "call": msg.get("call"), "response": 4})  # Full
            return
        if c.steam_id not in lb["members"]:
            lb["members"].append(c.steam_id)
        log("join", lid, c.name)
        await c.send(lobby_state(lid))
        await c.send({"op": "data_update", "lobby": lid, "member": lid, "success": 1})
        await c.send({"op": "entered", "lobby": lid, "call": msg.get("call"), "response": 1})
        others = [m for m in lb["members"] if m != c.steam_id]
        await push_state(lid, {"op": "chat_update", "lobby": lid, "user": c.steam_id, "by": c.steam_id, "change": 1},
                         targets=others)
    elif op == "leave":
        log("leave", msg["lobby"], c.name)
        await leave(c.steam_id, int(msg["lobby"]))
    elif op == "set_data":
        lid = int(msg["lobby"])
        lb = lobbies.get(lid)
        if lb and lb["owner"] == c.steam_id:
            lb["data"][msg["k"]] = msg["v"]
            await push_state(lid, {"op": "data_update", "lobby": lid, "member": lid, "success": 1})
    elif op == "set_mdata":
        lid = int(msg["lobby"])
        lb = lobbies.get(lid)
        if lb and c.steam_id in lb["members"]:
            lb["mdata"].setdefault(c.steam_id, {})[msg["k"]] = msg["v"]
            await push_state(lid, {"op": "data_update", "lobby": lid, "member": c.steam_id, "success": 1})
    elif op == "request_data":
        lid = int(msg["lobby"])
        if lid in lobbies:
            await c.send(lobby_state(lid))
            await c.send({"op": "data_update", "lobby": lid, "member": lid, "success": 1})
        else:
            await c.send({"op": "data_update", "lobby": lid, "member": lid, "success": 0})
    elif op == "invite":
        t = clients.get(int(msg["to"]))
        log("invite", c.name, "->", t.name if t else msg["to"])
        if t:
            await t.send({"op": "invited", "lobby": int(msg["lobby"]), "from": c.steam_id})
    elif op == "rp_set":
        rp = rich_presence.setdefault(c.steam_id, {})
        if msg.get("clear"):
            rp.clear()
        else:
            rp[msg["k"]] = msg["v"]
        for o in clients.values():
            await o.send({"op": "rp", "user": c.steam_id, "values": rp})
    elif op == "list_lobbies":
        out = [lobby_state(l) for l in lobbies]
        await c.send({"op": "lobby_list", "call": msg.get("call"), "lobbies": out})
    elif op == "p2p":
        target = int(msg["to"])
        out = {"op": "p2p", "from": c.steam_id, "ch": msg["ch"], "data": msg["data"], "rel": msg["rel"]}
        now = asyncio.get_event_loop().time()
        down = now < blackout_until
        if not msg["rel"] and (down or (args.drop and random.random() < args.drop)):
            return
        if args.lag or down or last_deadline.get(target, 0.0) > now:
            # Reliable traffic sent while the link is down arrives once it is back (Steam resends it).
            deadline = max(now, blackout_until if down else now) + args.lag / 1000.0
            deadline = max(deadline, last_deadline.get(target, 0.0))
            last_deadline[target] = deadline
            asyncio.ensure_future(deliver_p2p(target, out, deadline))
        else:
            t = clients.get(target)
            if t:
                await t.send(out)
    elif op == "blackout":
        # A test cuts the P2P link for a while (both directions): an internet hiccup.
        blackout_until = asyncio.get_event_loop().time() + float(msg.get("ms", 3000)) / 1000.0
        log("blackout", msg.get("ms"), "ms by", c.name)
    elif op == "qa":
        log("qa", c.name, msg.get("k"), "=", msg.get("v"))
        for o in clients.values():
            if o is not c:
                await o.send({"op": "qa", "from": c.player, "k": msg.get("k"), "v": msg.get("v")})
    else:
        log("unknown op", op)


async def serve(reader, writer):
    c = Client(reader, writer)
    try:
        while True:
            line = await reader.readline()
            if not line:
                break
            try:
                msg = json.loads(line)
            except ValueError:
                log("bad json from", c.name)
                continue
            await handle(msg, c)
    except (ConnectionError, asyncio.IncompleteReadError):
        pass
    finally:
        if c.steam_id and clients.get(c.steam_id) is c:
            del clients[c.steam_id]
            for lid in [l for l, lb in lobbies.items() if c.steam_id in lb["members"]]:
                await leave(c.steam_id, lid)
            await broadcast_peers()
            log("disconnected", c.name)
        writer.close()


async def main():
    global args, logf
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=27800)
    ap.add_argument("--lag", type=int, default=0, help="extra one-way P2P delay in ms")
    ap.add_argument("--drop", type=float, default=0.0, help="loss rate for unreliable P2P")
    ap.add_argument("--log")
    args = ap.parse_args()
    if args.log:
        logf = open(args.log, "a")
    server = await asyncio.start_server(serve, "127.0.0.1", args.port, limit=64 * 1024 * 1024)
    log(f"broker listening on 127.0.0.1:{args.port} lag={args.lag}ms drop={args.drop}")
    async with server:
        await server.serve_forever()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        sys.exit(0)
