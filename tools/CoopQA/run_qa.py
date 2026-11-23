#!/usr/bin/env python3
"""Runs a CoopQA test: broker + host (P1) + client (P2) as two game copies on this PC.

Usage: run_qa.py <test> [--lag MS] [--drop RATE] [--timeout S] [--stagger S] [--keep]
Results: qa/runs/<timestamp>-<test>/ (result_P*.txt, LogOutput_P*.log, player_P*.log, broker.log)
Only the processes this script started are stopped. The real game install and saves are only read.
"""
import argparse
import os
import re
import shutil
import signal
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
QA = ROOT / "qa"
GAME = Path(os.environ.get("GRAVEYARD_KEEPER_PATH", "/mnt/FedoraData/SteamLibrary/steamapps/common/Graveyard Keeper"))
REAL_SAVES = Path.home().parent.parent / ".var/app/com.valvesoftware.Steam/.config/unity3d/Lazy Bear Games/Graveyard Keeper"
MOD_DLL = ROOT / "repo/bin/Release/net46/GraveyardKeeperCoop.dll"
QA_DLL = ROOT / "tools/CoopQA/bin/Release/net46/CoopQA.dll"
SAVE_SUBDIR = Path("xdg/unity3d/Lazy Bear Games/Graveyard Keeper")


def real_saves_dir():
    flatpak = Path.home() / ".var/app/com.valvesoftware.Steam/.config/unity3d/Lazy Bear Games/Graveyard Keeper"
    native = Path.home() / ".config/unity3d/Lazy Bear Games/Graveyard Keeper"
    for p in (flatpak, native, REAL_SAVES):
        if (p / "1.dat").exists():
            return p
    return None


def prepare(player, fixture_src, fixture_name, with_mod=True, keep=False):
    d = QA / f"P{player}"
    if not (d / "Graveyard Keeper.x86_64").exists():
        subprocess.run([str(ROOT / "tools/qa-make-copies.sh"), f"P{player}"], check=True)
    plugins = d / "BepInEx/plugins"
    if plugins.exists():
        shutil.rmtree(plugins)
    plugins.mkdir(parents=True)
    shutil.copy2(QA_DLL, plugins)
    if with_mod:
        shutil.copy2(MOD_DLL, plugins)
    saves = d / SAVE_SUBDIR
    if keep:
        # The next evening: keep the saves and the joiner profiles from the last run.
        for f in ("BepInEx/LogOutput.log", "player.log"):
            (d / f).unlink(missing_ok=True)
        real_cfg = GAME / "BepInEx/config/com.zlord.graveyardkeeper.coop.cfg"
        if real_cfg.exists():
            shutil.copy2(real_cfg, d / "BepInEx/config" / real_cfg.name)
        return d
    if saves.exists():
        for f in saves.iterdir():
            if f.is_file() and f.name not in ("prefs",):
                f.unlink()
    saves.mkdir(parents=True, exist_ok=True)
    if player == 1 and fixture_src:
        for ext in (".dat", ".info"):
            shutil.copy2(fixture_src.with_suffix(ext), saves / (fixture_name + ext))
    # Same mod settings as the real install (base game only: DLC sync off).
    real_cfg = GAME / "BepInEx/config/com.zlord.graveyardkeeper.coop.cfg"
    if real_cfg.exists():
        (d / "BepInEx/config").mkdir(parents=True, exist_ok=True)
        shutil.copy2(real_cfg, d / "BepInEx/config" / real_cfg.name)
    # Fresh joiner every run (as a friend joining for the first time): drop saved joiner profiles.
    for f in (d / "BepInEx/config").glob("gkcoop_joiner_inventory_*"):
        f.unlink()
    for f in ("BepInEx/LogOutput.log", "player.log"):
        (d / f).unlink(missing_ok=True)
    return d


def launch(player, d, test, out, port, save, label="probe"):
    env = dict(os.environ)
    env.update({
        "XDG_CONFIG_HOME": str(d / "xdg"),
        "QA_PLAYER": str(player), "QA_TEST": test, "QA_OUT": str(out),
        "QA_BROKER_PORT": str(port), "QA_SAVE": save, "QA_LABEL": label,
        "PULSE_SERVER": "unix:/nonexistent",  # mute
        # no vsync: a window the compositor considers hidden must not block on present
        "vblank_mode": "0", "__GL_SYNC_TO_VBLANK": "0",
    })
    args = [str(d / "gkmp_inner.sh"), str(d / "Graveyard Keeper.x86_64"), "-coopqa",
            "-logFile", str(d / "player.log"), "-screen-fullscreen", "0", "-window-mode", "windowed",
            "-screen-width", "960", "-screen-height", "540"]
    return subprocess.Popen(args, cwd=d, env=env, stdout=open(out / f"stdout_P{player}.txt", "w"),
                            stderr=subprocess.STDOUT, start_new_session=True)


def place_windows(pids, workspace, timeout=240):
    """Hyprland 0.57+ (Lua dispatchers): move each QA window to `workspace` without following it,
    floating, side by side. Unity names the window class after the product, so windows are matched
    by pid. Runs in a thread; no-op without Hyprland."""
    if not shutil.which("hyprctl") or not os.environ.get("HYPRLAND_INSTANCE_SIGNATURE"):
        return
    import json
    slots = {pid: (20 + i * 950, 60) for i, pid in enumerate(pids)}
    placed = {}
    t0 = time.time()
    while time.time() - t0 < timeout and len(placed) < len(pids):
        try:
            clients = json.loads(subprocess.run(["hyprctl", "clients", "-j"], capture_output=True, text=True).stdout or "[]")
        except ValueError:
            clients = []
        for c in clients:
            pid = c.get("pid")
            if pid not in slots:
                continue
            x, y = slots[pid]
            sel = f"pid:{pid}"
            ok = (c["workspace"]["name"] == str(workspace) and c.get("floating") and not c.get("fullscreen")
                  and c["at"] == [x, y] and c["size"] == [940, 530])
            if ok:
                placed[pid] = placed.get(pid, 0) + 1   # re-check: Unity may resize after its splash
                continue
            placed.pop(pid, None)
            lua = (f"hl.dispatch(hl.dsp.window.move({{ workspace = '{workspace}', follow = false, window = '{sel}' }})) "
                   + ("" if c.get("floating") else f"hl.dispatch(hl.dsp.window.float({{ action = 'enable', window = '{sel}' }})) ")
                   + f"hl.dispatch(hl.dsp.window.resize({{ x = 940, y = 530, window = '{sel}' }})) "
                   f"hl.dispatch(hl.dsp.window.move({{ x = {x}, y = {y}, window = '{sel}' }}))")
            subprocess.run(["hyprctl", "eval", lua], capture_output=True)
        if len(placed) == len(pids) and all(v >= 20 for v in placed.values()):
            break
        time.sleep(0.25)


def result_of(out, player):
    f = out / f"result_P{player}.txt"
    if not f.exists():
        return None
    m = re.findall(r"RESULT: (PASS|FAIL.*)", f.read_text(errors="replace"))
    return m[-1] if m else None


def stop(p):
    if p and p.poll() is None:
        try:
            os.killpg(p.pid, signal.SIGTERM)
            p.wait(10)
        except Exception:
            try:
                os.killpg(p.pid, signal.SIGKILL)
            except Exception:
                pass


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("test")
    ap.add_argument("--lag", type=int, default=0)
    ap.add_argument("--drop", type=float, default=0.0)
    ap.add_argument("--timeout", type=int, default=900)
    ap.add_argument("--stagger", type=float, default=3.0)
    ap.add_argument("--port", type=int, default=27800)
    ap.add_argument("--save", default="coop_1")
    ap.add_argument("--keep", action="store_true", help="keep saves and joiner profiles from the last run (next evening)")
    ap.add_argument("--workspace", default="2", help="Hyprland workspace for the test windows")
    a = ap.parse_args()

    for f in (MOD_DLL, QA_DLL):
        if not f.exists():
            sys.exit(f"missing {f}; build first")
    src_dir = real_saves_dir()
    fixture = src_dir / "1.dat" if src_dir else None
    if not fixture:
        sys.exit("no fixture save found")

    out = QA / "runs" / (time.strftime("%Y%m%d-%H%M%S") + "-" + a.test.split(":")[0])
    if a.test == "hud_probe":
        # Single player, one instance: compare the HUD with and without the mod.
        out.mkdir(parents=True)
        import threading
        for label, with_mod in (("vanilla", False), ("with_mod", True)):
            d1 = prepare(1, fixture, "1", with_mod=with_mod)
            p = launch(1, d1, "hud_probe", out, a.port, "1", label=label)
            threading.Thread(target=place_windows, args=([p.pid], a.workspace), daemon=True).start()
            t0 = time.time()
            res = out / f"hud_{label}.txt"
            while time.time() - t0 < 600 and p.poll() is None and not res.exists():
                time.sleep(1)
            time.sleep(1)
            stop(p)
            for src, dst in (("BepInEx/LogOutput.log", f"LogOutput_{label}.log"), ("player.log", f"player_{label}.log")):
                if (d1 / src).exists():
                    shutil.copy2(d1 / src, out / dst)
            print(f"== {label}: {res.read_text() if res.exists() else 'no result'}")
        print("->", out)
        return
    out.mkdir(parents=True)
    d1 = prepare(1, fixture, a.save, keep=a.keep)
    d2 = prepare(2, None, a.save, keep=a.keep)

    procs = []
    broker = subprocess.Popen([sys.executable, "-I", str(ROOT / "tools/CoopQA/broker.py"), "--port", str(a.port),
                               "--lag", str(a.lag), "--drop", str(a.drop), "--log", str(out / "broker.log")],
                              stdout=subprocess.DEVNULL, stderr=open(out / "broker.err", "w"), start_new_session=True)
    procs.append(broker)
    time.sleep(0.7)
    t0 = time.time()
    status = "TIMEOUT"
    try:
        p1 = launch(1, d1, a.test, out, a.port, a.save); procs.append(p1)
        time.sleep(a.stagger)
        p2 = launch(2, d2, a.test, out, a.port, a.save); procs.append(p2)
        import threading
        threading.Thread(target=place_windows, args=([p1.pid, p2.pid], a.workspace), daemon=True).start()
        while time.time() - t0 < a.timeout:
            r1, r2 = result_of(out, 1), result_of(out, 2)
            if (r1 and r2) or (r1 and r1.startswith("FAIL")) or (r2 and r2.startswith("FAIL")):
                time.sleep(3)
                status = "DONE"
                break
            for n, p in ((1, p1), (2, p2)):
                if p.poll() is not None:
                    status = f"P{n} exited with code {p.returncode}"
                    break
            if status.startswith("P"):
                break
            time.sleep(1)
    finally:
        for p in reversed(procs):
            stop(p)
        for n, d in ((1, d1), (2, d2)):
            for src, dst in (("BepInEx/LogOutput.log", f"LogOutput_P{n}.log"), ("player.log", f"player_P{n}.log")):
                if (d / src).exists():
                    shutil.copy2(d / src, out / dst)

    print(f"== {a.test}  ({time.time() - t0:.0f}s, {status})  -> {out}")
    ok = True
    for n in (1, 2):
        r = result_of(out, n)
        print(f"P{n}: {r or 'no result'}")
        ok = ok and r == "PASS"
        log = out / f"LogOutput_P{n}.log"
        if log.exists():
            errs = [l for l in log.read_text(errors="replace").splitlines()
                    if (l.startswith("[Error") or l.startswith("[Fatal") or "Exception" in l) and "PROFILER" not in l]
            uniq = list(dict.fromkeys(e[:200] for e in errs))
            print(f"   {len(errs)} error lines, {len(uniq)} unique")
            for e in uniq[:12]:
                print("     " + e)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
