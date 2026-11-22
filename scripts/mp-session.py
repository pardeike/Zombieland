#!/usr/bin/env python3
"""Zombieland Multiplayer host+client session harness (see scripts/README.md).

Run with `python3 -I scripts/mp-session.py MODE ...`. Modes:
  run      host loads a save (or a debug game), hosts, client joins, time runs through
           multiplayer/set_time_speed only, then both peers are compared at a paused boundary.
  fixture  build the busy single-player fixture save in the host profile.
  hold     launch peers and keep them up for manual bridge calls (bounded by --minutes).
  call     call one bridge tool on a peer of the newest `hold` session.
Full output goes to artifacts/logs/mp/<stamp>-<mode>/. Stdout is a compact summary.
"""
import argparse
import collections
import fcntl
import hashlib
import json
import os
import re
import signal
import socket
import subprocess
import sys
import time
import uuid
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
ART = REPO / "artifacts"
GAME_DIR = ART / "mp17" / "Game"
GAME_BIN = GAME_DIR / "RimWorld.app" / "Contents" / "MacOS" / "RimWorld"
ZL_DLL = GAME_DIR / "Mods" / "ZombieLand" / "1.7" / "Assemblies" / "ZombieLand.dll"
MARKER = ART / "mp17" / "GAME_IN_USE"
BUILD_LOCK = ART / ".build.lock"
LOG_ROOT = ART / "logs" / "mp"
SUMMARIZER = REPO / "scripts" / "summarize-rimworld-log.sh"
MODS = "brrainz.harmony;brrainz.rimbridgeserver;rwmt.multiplayer;brrainz.zombieland"
PROFILES = {"host": ART / "mp17-host-profile", "client": ART / "mp17-client-profile"}
USERNAMES = {"host": "ZLHost", "client": "ZLClient"}
FIXTURE = "ZL_MP_Fixture"
FIXTURES = {"base": FIXTURE, "features": "ZL_MP_Features", "natural": "ZL_MP_Natural"}
# -NoWindow UI-scale reset, and the Multiplayer 1.7 port's Map.get_Biome failure while hosting (its attention
# sample is cut before the stack, so the message prefix is matched as well).
EXPECTED_ATTENTION = ("Resolution too small", "get_Biome",
                      "Could not execute post-long-event action. Exception: System.ArgumentOutOfRangeException")
MP_EVENT_RE = re.compile(r"Processed disconnect packet|Net client disconnected|Exception ticking the server")
KEEP_RUNS = 20
RUN_DIR_RE = re.compile(r"^\d{8}-\d{6}-[a-z]+$")
ANSI_RE = re.compile(r"\x1b\[[0-9;]*m")
STAMP_RE = re.compile(r"^\d\d:\d\d:\d\d ")
LEVEL_RE = re.compile(r"^\s*(TRACE|DEBUG|INFO|WARN|ERROR|FATAL)\s(.*)$")


class HarnessError(Exception):
    pass


class ToolError(HarnessError):
    pass


class Interrupted(BaseException):
    pass


# ---------------------------------------------------------------- GABP
# Framing and handshake follow RimBridgeServer/artifacts/mp17/gabp.py. That helper runs at import
# time, so its logic is reproduced here with event-frame skipping and timeouts.

def _frame(obj):
    body = json.dumps(obj).encode()
    return f"Content-Length: {len(body)}\r\n\r\n".encode() + body


def _read_frame(sock, buf):
    while b"\r\n\r\n" not in buf[0]:
        chunk = sock.recv(65536)
        if not chunk:
            raise ConnectionError("GABP connection closed")
        buf[0] += chunk
    head, rest = buf[0].split(b"\r\n\r\n", 1)
    length = int(re.search(rb"Content-Length:\s*(\d+)", head, re.I).group(1))
    while len(rest) < length:
        chunk = sock.recv(65536)
        if not chunk:
            raise ConnectionError("GABP connection closed")
        rest += chunk
    buf[0] = rest[length:]
    return json.loads(rest[:length])


class Peer:
    def __init__(self, role, port, token, session=None):
        self.role, self.port, self.token, self.session = role, port, token, session

    def request(self, method, params, timeout=120):
        with socket.create_connection(("127.0.0.1", self.port), timeout=min(timeout, 10)) as sock:
            sock.settimeout(timeout)
            buf = [b""]
            for m, p in (("session/hello", {"token": self.token, "bridgeVersion": "1.0.0", "platform": "macos",
                                            "launchId": str(uuid.uuid4())}), (method, params)):
                rid = str(uuid.uuid4())
                sock.sendall(_frame({"v": "gabp/1", "id": rid, "type": "request", "method": m, "params": p}))
                while True:
                    reply = _read_frame(sock, buf)
                    if reply.get("id") == rid and reply.get("type") != "event":
                        break
                if reply.get("error"):
                    raise ToolError(f"{self.role} {m}: {short(reply['error'], 300)}")
            return reply.get("result", reply)

    def call(self, tool, args=None, timeout=120, check=True, log=True):
        started = time.time()
        ok, result = False, None
        try:
            result = unwrap(self.request("tools/call", {"name": tool, "arguments": args or {}}, timeout))
            ok = not (isinstance(result, dict) and result.get("success") is False)
            if not ok and check:
                raise ToolError(f"{self.role} {tool}: {short(result.get('error') or result, 300)}")
            return result
        finally:
            if log and self.session:
                self.session.log_call(self.role, tool, args, time.time() - started, ok, result)

    def status(self):
        return self.call("multiplayer/status", timeout=20, log=False)


def unwrap(raw):
    if isinstance(raw, dict):
        if raw.get("isError"):
            raise ToolError(short(raw, 400))
        if raw.get("structuredContent") is not None:
            return raw["structuredContent"]
        content = raw.get("content")
        if isinstance(content, list) and content and isinstance(content[0], dict) and "text" in content[0]:
            try:
                return json.loads(content[0]["text"])
            except ValueError:
                return {"text": content[0]["text"]}
    return raw


def short(obj, n=200):
    text = obj if isinstance(obj, str) else json.dumps(obj, default=str)
    return text if len(text) <= n else text[: n - 3] + "..."


def free_port(kind):
    with socket.socket(socket.AF_INET, kind) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


# ---------------------------------------------------------------- session / process lifecycle

class Session:
    def __init__(self, mode, max_minutes):
        self.mode, self.max_seconds = mode, int(max_minutes * 60)
        self.run_dir = LOG_ROOT / f"{time.strftime('%Y%m%d-%H%M%S')}-{mode}"
        self.procs, self.peers, self.logs, self.log_marks = {}, {}, {}, {}
        self.attention, self.leftover = [], []
        self.started = time.time()
        self.marker_text = None
        self.lock_fd = None
        self.build = None
        self.desync_baseline = {r: set(desync_files(r)) for r in PROFILES}

    def __enter__(self):
        LOG_ROOT.mkdir(parents=True, exist_ok=True)
        prune_runs()
        self.run_dir.mkdir(parents=True)
        self.take_build_lock()
        running = clone_pids()
        if running:
            raise HarnessError(f"RimWorld from the mp17 clone is already running (pids {running})")
        text = (f"harness agent: scripts/mp-session.py {self.mode} pid={os.getpid()} "
                f"started={time.strftime('%Y-%m-%dT%H:%M:%S')} run={self.run_dir}\n")
        try:
            fd = os.open(MARKER, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o644)
        except FileExistsError:
            raise HarnessError(f"{MARKER} exists: {MARKER.read_text().strip()[:200]}")
        with os.fdopen(fd, "w") as f:
            f.write(text)
        self.marker_text = text
        for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            signal.signal(sig, self._on_signal)
        signal.signal(signal.SIGALRM, self._on_alarm)
        signal.alarm(self.max_seconds)
        self.build = build_identity()
        return self

    def take_build_lock(self, timeout=900):
        """Wait for a running build/deploy; hold the lock until the peers have loaded the DLLs."""
        self.lock_fd = os.open(BUILD_LOCK, os.O_RDWR | os.O_CREAT, 0o644)
        deadline = time.time() + timeout
        while True:
            try:
                fcntl.flock(self.lock_fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
                return
            except BlockingIOError:
                if time.time() > deadline:
                    raise HarnessError(f"{BUILD_LOCK} still held after {timeout} s")
                time.sleep(2)

    def release_build_lock(self):
        if self.lock_fd is not None:
            fcntl.flock(self.lock_fd, fcntl.LOCK_UN)
            os.close(self.lock_fd)
            self.lock_fd = None

    def __exit__(self, *exc):
        signal.alarm(0)
        for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            signal.signal(sig, signal.SIG_IGN)
        try:
            self.stop()
        finally:
            self.release_marker()
            self.release_build_lock()
            for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
                signal.signal(sig, signal.SIG_DFL)
        return False

    def _on_signal(self, signum, _frame):
        raise Interrupted(f"signal {signum}")

    def _on_alarm(self, *_):
        raise HarnessError(f"watchdog: session exceeded {self.max_seconds} s")

    def release_marker(self):
        try:
            if self.marker_text and MARKER.read_text() == self.marker_text:
                MARKER.unlink()
        except OSError:
            pass
        self.marker_text = None

    def launch(self, role):
        port, token = free_port(socket.SOCK_STREAM), uuid.uuid4().hex
        PROFILES[role].mkdir(parents=True, exist_ok=True)
        log = open(self.run_dir / f"{role}.stdout.log", "wb")
        env = dict(os.environ, GABS_GAME_ID=f"zl-mp-{role}", GABP_SERVER_PORT=str(port), GABP_TOKEN=token)
        cmd = [str(GAME_BIN), "-NoWindow", "-WindowSize=1920,1080", f"-savedatafolder={PROFILES[role]}",
               f"-mod={MODS}"]
        proc = subprocess.Popen(cmd, cwd=self.run_dir, env=env, stdin=subprocess.DEVNULL, stdout=log,
                                stderr=subprocess.STDOUT, start_new_session=True)
        self.procs[role], self.logs[role] = proc, log
        self.peers[role] = Peer(role, port, token, self)
        self.write_json("session.json", {r: {"pid": self.procs[r].pid, "port": p.port, "token": p.token}
                                         for r, p in self.peers.items()})
        return self.peers[role]

    def stop(self):
        """Terminate the client first, then the host; SIGKILL after 20 s."""
        order = [r for r in ("client", "host") if r in self.procs]
        for role in order:
            if self.procs[role].poll() is None:
                try:
                    os.killpg(self.procs[role].pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
        deadline = time.time() + 20
        for role in order:
            proc = self.procs[role]
            try:
                proc.wait(timeout=max(1, deadline - time.time()))
            except subprocess.TimeoutExpired:
                try:
                    os.killpg(proc.pid, signal.SIGKILL)
                    proc.wait(timeout=10)
                except (ProcessLookupError, subprocess.TimeoutExpired):
                    pass
        for log in self.logs.values():
            log.close()
        self.logs = {}
        ours = {p.pid for p in self.procs.values()}
        self.leftover = [pid for pid in clone_pids() if pid in ours]

    def alive(self, role):
        return self.procs[role].poll() is None

    def mark_logs(self):
        """Split each peer's log into startup (before) and session (after) sections."""
        for role in self.peers:
            self.log_marks[role] = (self.run_dir / f"{role}.stdout.log").stat().st_size

    def write_json(self, name, obj):
        (self.run_dir / name).write_text(json.dumps(obj, indent=1, default=str))

    def log_call(self, role, tool, args, seconds, ok, result):
        with open(self.run_dir / "calls.jsonl", "a") as f:
            f.write(json.dumps({"t": round(time.time() - self.started, 1), "role": role, "tool": tool, "args": args,
                                "s": round(seconds, 2), "ok": ok, "result": short(result, 600)}, default=str) + "\n")

    def note(self, text):
        with open(self.run_dir / "notes.txt", "a") as f:
            f.write(f"[{time.time() - self.started:7.1f}] {text}\n")

    def wait_menu(self, roles, timeout=300):
        """Wait until each bridge answers and Multiplayer reports the main menu."""
        pending = set(roles)
        deadline = time.time() + timeout
        while pending:
            for role in sorted(pending):
                if not self.alive(role):
                    raise HarnessError(f"{role} exited with {self.procs[role].returncode} during startup")
                try:
                    st = self.peers[role].status()
                except (OSError, ConnectionError, ToolError, ValueError):
                    continue
                if st.get("available") is not True:
                    raise HarnessError(f"{role}: Multiplayer not loaded ({short(st)})")
                if st.get("programState") == "Entry":
                    pending.discard(role)
                    self.note(f"{role} at main menu")
            if pending and time.time() > deadline:
                raise HarnessError(f"bridge/main menu not ready on {sorted(pending)} after {timeout} s")
            if pending:
                time.sleep(2)
        self.release_build_lock()
        for role in roles:
            self.drain_attention(role)
        self.mark_logs()

    def drain_attention(self, role):
        """Record and acknowledge open bridge attention items; known port/NoWindow noise is expected."""
        peer = self.peers[role]
        for _ in range(6):
            try:
                att = (peer.request("attention/current", {}, 15) or {}).get("attention")
                if not att:
                    return
                expected = any(s in json.dumps(att) for s in EXPECTED_ATTENTION)
                self.attention.append({"role": role, "expected": expected,
                                       "title": att.get("summary") or att.get("title") or short(att, 160)})
                with open(self.run_dir / "attention.jsonl", "a") as f:
                    f.write(json.dumps({"role": role, "expected": expected, "attention": att}) + "\n")
                peer.request("attention/ack", {"attentionId": att.get("attentionId")}, 15)
            except (ToolError, OSError, ConnectionError):
                return

    def wait_for(self, desc, fn, timeout, interval=1.0):
        deadline = time.time() + timeout
        while True:
            for role in self.procs:
                if not self.alive(role):
                    raise HarnessError(f"{role} exited ({self.procs[role].returncode}) while waiting for {desc}")
            ok, last = fn()
            if ok:
                return last
            if time.time() > deadline:
                raise HarnessError(f"timeout after {timeout} s waiting for {desc}: {short(last, 300)}")
            time.sleep(interval)


def clone_pids():
    """RimWorld processes started from the clone (shell lines that merely mention the path do not count)."""
    def pids(*args):
        return {int(x) for x in subprocess.run(["pgrep", *args], capture_output=True, text=True).stdout.split()}
    return sorted(pids("-f", str(GAME_BIN)) & pids("-x", "RimWorld"))


def prune_runs():
    runs = sorted(p for p in LOG_ROOT.iterdir() if p.is_dir() and RUN_DIR_RE.match(p.name))
    for old in runs[: max(0, len(runs) - (KEEP_RUNS - 1))]:
        for sub in sorted(old.rglob("*"), reverse=True):
            sub.rmdir() if sub.is_dir() else sub.unlink()
        old.rmdir()


def desync_files(role):
    d = PROFILES[role] / "MpDesyncs"
    return sorted(str(p) for p in d.glob("Desync-*.zip")) if d.is_dir() else []


def build_identity():
    if not ZL_DLL.exists():
        return "ZombieLand.dll missing in clone"
    stamp = time.strftime("%Y-%m-%d %H:%M", time.localtime(ZL_DLL.stat().st_mtime))
    return f"ZombieLand.dll {stamp} sha256:{hashlib.sha256(ZL_DLL.read_bytes()).hexdigest()[:12]}"


def rel(path):
    try:
        return str(Path(path).relative_to(REPO))
    except ValueError:
        return str(path)


# ---------------------------------------------------------------- state helpers

def players_playing(st):
    players = st.get("players") or []
    return len(players) >= 2 and all(p.get("status") == "Playing" for p in players)


def is_desynced(st):
    return (st.get("desynced") is True or any(p.get("status") == "Desynced" for p in st.get("players") or [])
            or any("Desync" in (w or "") for w in st.get("windows") or []))


def clocks(st):
    return {"ticksGame": st.get("ticksGame"), "worldTicks": st.get("worldTicks"),
            "mapClocks": [[c.get("mapId"), c.get("ticks")] for c in st.get("mapClocks") or []],
            "speed": st.get("desiredTimeSpeed")}


SPECIAL_FLAGS = ("isSuicideBomber", "isToxicSplasher", "isTanky", "isMiner", "isElectrifier", "isAlbino",
                 "isDarkSlimer", "isHealer")


def zombie_digest(listing):
    """Type counts plus a hash over (id, cell, state) for comparing peers at the same tick."""
    kinds, rows = collections.Counter(), []
    for z in listing.get("zombies") or []:
        flags = [k[2:] for k in SPECIAL_FLAGS if z.get(k)] + (["Raging"] if (z.get("raging") or 0) > 0 else [])
        kind = z.get("kind")
        kinds["+".join(flags) if flags else (kind if isinstance(kind, str) else z.get("kindDef") or "?")] += 1
        pos = z.get("position") or {}
        rows.append([z.get("thingId"), pos.get("x"), pos.get("z"), z.get("state"), z.get("dead"), z.get("downed")])
    rows.sort(key=lambda r: str(r[0]))
    return {"count": len(rows), "kinds": dict(kinds.most_common()),
            "hash": hashlib.sha256(json.dumps(rows).encode()).hexdigest()[:12], "rows": rows}


# ---------------------------------------------------------------- log analysis

def split_log(session, role):
    """Write colour/timestamp-free startup and session logs; return (level, text) entries per section."""
    raw = (session.run_dir / f"{role}.stdout.log").read_bytes()
    mark = session.log_marks.get(role, len(raw))
    sections = {}
    for name, data in (("startup", raw[:mark]), ("session", raw[mark:])):
        entries, lines = [], []
        for line in data.decode(errors="replace").splitlines():
            line = STAMP_RE.sub("", ANSI_RE.sub("", line))
            m = LEVEL_RE.match(line)
            if m:
                entries.append([m.group(1), m.group(2)])
                line = m.group(2)
            elif entries:
                entries[-1][1] += "\n" + line
            lines.append(line)
        path = session.run_dir / f"{role}.{name}.log"
        path.write_text("\n".join(lines) + "\n")
        sections[name] = (path, entries)
    return sections


def summarize_log(path):
    out = subprocess.run(["bash", str(SUMMARIZER), str(path)], capture_output=True, text=True, timeout=180).stdout
    parts = re.split(r"^---- (\d+)x ----\n", out, flags=re.M)
    return [(int(parts[i]), parts[i + 1].strip()) for i in range(1, len(parts) - 1, 2)]


def zl_related(text):
    return re.search(r"zombie", text.replace(str(REPO), "<repo>"), re.I) is not None


def normalize(text):
    first = text.strip().splitlines()[0] if text.strip() else ""
    return re.sub(r"\d+", "#", first.replace(str(REPO), "<repo>"))[:220]


def log_findings(path, entries):
    """De-duplicated Zombieland-related warning/error lines: summarizer blocks plus WARN/ERROR entries."""
    found = collections.OrderedDict()
    for count, block in summarize_log(path):
        if zl_related(block):
            key = normalize(block)
            found[key] = found.get(key, 0) + count
    for level, text in entries:
        if level in ("WARN", "ERROR", "FATAL") and zl_related(text):
            key = normalize(text)
            tagged = f"[{level}] {key}"
            if key in found:
                continue
            found[tagged] = found.get(tagged, 0) + 1
    return found


def analyse_logs(session):
    """Return ZL findings per peer/section plus Multiplayer desync and connection event lines."""
    logs, desyncs, events = {}, [], []
    for role in session.peers:
        if not (session.run_dir / f"{role}.stdout.log").exists():
            continue
        sections = split_log(session, role)
        logs[role] = {name: log_findings(*sections[name]) for name in sections}
        desyncs += [f"{role}: {short(text.splitlines()[0], 200)}" for _, text in sections["session"][1]
                    if "Desynced after last valid tick" in text]
        events += [f"{role}: {short(text.splitlines()[0], 200)}" for _, text in sections["session"][1]
                   if MP_EVENT_RE.search(text)]
    return logs, desyncs, events


def describe_desync_zip(path):
    try:
        with zipfile.ZipFile(path) as z:
            names = z.namelist()
            info = z.read("desync_info").decode(errors="replace") if "desync_info" in names else ""
            tick = re.search(r"Last Valid Tick - Local\|\|\|(\S+)", info)
            return f"{rel(path)} (last valid tick {tick.group(1) if tick else '?'}; {', '.join(names)})"
    except (OSError, zipfile.BadZipFile, KeyError) as e:
        return f"{rel(path)} (unreadable: {e})"


def log_lines(logs, limit):
    lines = []
    for role, sections in logs.items():
        session_found, startup_found = sections["session"], sections["startup"]
        lines.append(f"ZL logs   {role}: {len(session_found)} unique in session, {len(startup_found)} at startup "
                     f"({role}.session.log / {role}.startup.log)")
        for key, count in list(session_found.items())[:limit]:
            lines.append(f"          {count:4d}x {short(key, 150)}")
    return lines


# ---------------------------------------------------------------- run mode

def cmd_run(a):
    result = {"source": "debug game" if a.debug_game else a.save, "joined": False, "desync": False, "errors": []}
    session = Session("run", a.max_minutes)
    constants = PROFILES["client"] / "Config" / "ZombielandAdvancedSettings.json"
    original = constants.read_text() if a.client_constant else None
    try:
        if a.client_constant:
            # The client plays with a different local advanced setting; the session must use the host's.
            name, value = a.client_constant.split("=", 1)
            data = json.loads(original)
            data[name]["value"] = json.loads(value)
            constants.write_text(json.dumps(data, indent="\t"))
        with session:
            run_session(session, a, result)
    except Interrupted as e:
        result["errors"].append(f"interrupted ({e})")
    except HarnessError as e:
        result["errors"].append(str(e))
    finally:
        if original is not None:
            kept = json.loads(constants.read_text())[name]["value"] == json.loads(value)
            result["client_constant"] = {"setting": a.client_constant, "local_file_kept": kept}
            if not kept:
                result["errors"].append("the client's local advanced settings file was overwritten")
            constants.write_text(original)
    return report_run(session, result, a)


def run_session(session, a, result):
    host, client = session.launch("host"), session.launch("client")
    session.wait_menu(["host", "client"])
    load = {"pauseIfNeeded": True, "timeoutMs": 240000, "readiness": "playable"}
    if a.debug_game:
        host.call("rimworld/start_debug_game_ready", load, timeout=260)
    else:
        host.call("rimworld/load_game_ready", dict(load, saveName=a.save), timeout=260)
    session.note("host game loaded")
    mp_port = free_port(socket.SOCK_DGRAM)
    host.call("multiplayer/host_local", {"username": USERNAMES["host"], "port": mp_port,
                                         "desyncTraces": a.desync_traces, "asyncTime": a.async_time})
    # hostReady precedes the server's FullyStarted flag; a join before that is rejected as ServerStarting.
    # The host's own player entry appears once the server runs.
    def host_started():
        st = host.status()
        return st.get("hostReady") is True and any(p.get("status") == "Playing" for p in st.get("players") or []), st

    session.wait_for("server start", host_started, 180)
    session.drain_attention("host")
    joined_at = time.time()
    joins = [time.time()]
    client.call("multiplayer/join_local", {"username": USERNAMES["client"], "port": mp_port})

    def both_playing():
        sh, sc = host.status(), client.status()
        ok = players_playing(sh) and players_playing(sc) and sc.get("programState") == "Playing" \
            and (sc.get("mapCount") or 0) > 0
        dropped = sc.get("sessionActive") is False and sc.get("programState") == "Entry"
        if dropped and len(joins) < 3 and time.time() - joins[-1] > 5:
            session.note(f"client dropped to menu ({sc.get('windows')}); join retry {len(joins)}")
            joins.append(time.time())
            client.call("multiplayer/join_local", {"username": USERNAMES["client"], "port": mp_port})
        return ok or is_desynced(sh) or is_desynced(sc), (sh, sc)

    sh, sc = session.wait_for("both players Playing", both_playing, a.join_timeout, 2)
    for role in ("host", "client"):
        session.drain_attention(role)
    result["joined"] = players_playing(sh) and players_playing(sc)
    result["join_seconds"] = round(time.time() - joined_at)
    result["join_attempts"] = len(joins)
    session.write_json("status-joined.json", {"host": sh, "client": sc})
    session.note(f"joined={result['joined']}")
    start = {"host": sh.get("ticksGame"), "client": sc.get("ticksGame")}
    samples, run_started = [], time.time()
    if result["joined"] and not (is_desynced(sh) or is_desynced(sc)):
        result["zombies_at_join"] = (host.call("zombieland/get_status", timeout=60).get("tickManager") or {}) \
            .get("liveZombieCount")
        if a.ui:
            result["ui"] = run_ui_actions(host, client)
        if a.incident:
            # Zombieland routes this debug action through a synchronized method, so both peers spawn.
            res = host.call("rimworld/execute_debug_action", {"path": f"Actions\\T: Spawn: Incident ({a.incident})",
                                                              "x": a.incident_x, "z": a.incident_z})
            result["incident"] = bool(res.get("success"))
        def set_speed(speed):
            host.call("multiplayer/set_time_speed", {"speed": speed})
            if a.async_time:  # async time gives each map its own speed; the fixture has map 0
                host.call("multiplayer/set_time_speed", {"speed": speed, "mapId": 0})

        set_speed(a.speed)
        deadline = time.time() + a.run_timeout
        while True:
            sh, sc = host.status(), client.status()
            if a.rejoin_after and "rejoin" not in result and (sh.get("ticksGame") or 0) - (start["host"] or 0) >= a.rejoin_after:
                # A new join point: the host saves and every peer reloads while the incident runs.
                result["rejoin"] = {"left_at": sh.get("ticksGame")}
                client.call("multiplayer/leave")
                session.wait_for("client at menu", lambda: (lambda st: (st.get("sessionActive") is False and st.get("programState") == "Entry", st))(client.status()), 120, 1)
                joins.append(time.time())
                client.call("multiplayer/join_local", {"username": USERNAMES["client"], "port": mp_port})
                sh, sc = session.wait_for("both players Playing again", both_playing, a.join_timeout, 2)
                for role in ("host", "client"):
                    session.drain_attention(role)
                result["rejoin"].update(joined=players_playing(sh) and players_playing(sc), at=sc.get("ticksGame"))
                if not result["rejoin"]["joined"]:
                    result["errors"].append("client did not rejoin")
                set_speed(a.speed)
                deadline = time.time() + a.run_timeout
            samples.append({"t": round(time.time() - run_started, 1), "host": clocks(sh), "client": clocks(sc),
                            "desync": [is_desynced(sh), is_desynced(sc)]})
            if is_desynced(sh) or is_desynced(sc) or (sh.get("ticksGame") or 0) - (start["host"] or 0) >= a.ticks:
                break
            if time.time() > deadline:
                result["errors"].append(f"tick target not reached in {a.run_timeout} s")
                break
            if not (session.alive("host") and session.alive("client")):
                raise HarnessError("a peer exited during simulation")
            time.sleep(1)
        if not (is_desynced(sh) or is_desynced(sc)):
            set_speed("Paused")
            prev = [None]

            def paused_equal():
                h, c = host.status(), client.status()
                now = (h.get("ticksGame"), c.get("ticksGame"))
                stable = prev[0] == now and now[0] == now[1]
                prev[0] = now
                return stable or is_desynced(h) or is_desynced(c), (h, c)

            try:
                sh, sc = session.wait_for("paused boundary", paused_equal, 90, 1.5)
            except HarnessError as e:
                result["errors"].append(str(e))
                sh, sc = host.status(), client.status()
    result["run_seconds"] = round(time.time() - run_started)
    session.write_json("samples.json", samples)
    session.write_json("status-final.json", {"host": sh, "client": sc})
    result["final"] = {"host": clocks(sh), "client": clocks(sc)}
    result["ticks"] = {r: (result["final"][r]["ticksGame"] or 0) - (start[r] or 0) for r in start}
    result["desync"] = is_desynced(sh) or is_desynced(sc) or any(any(s["desync"]) for s in samples)
    if result["desync"]:
        # Multiplayer writes MpDesyncs/Desync-NN.zip from the desync window after host info or 5 s.
        deadline = time.time() + 25
        while time.time() < deadline and not any(set(desync_files(r)) - session.desync_baseline[r]
                                                 for r in PROFILES):
            time.sleep(1)
        time.sleep(2)
    collect_zombies(session, result)


GIZMO_TEXT = {"avoid": ("avoid zombies", "ignore zombies"), "extract": ("zombie extract",),
              "doubletap": ("double tap",), "chainsaw": ("turn chainsaw",), "switch": ("turns the device on or off",)}


def press_gizmo(peer, key):
    gizmos = peer.call("rimworld/list_selected_gizmos", log=False).get("gizmos") or []
    match = [g for g in gizmos if any(t in (g.get("description") or "").lower() for t in GIZMO_TEXT[key])]
    if len(match) != 1:
        raise HarnessError(f"gizmo {key}: {len(match)} matches in {[short(g.get('description'), 30) for g in gizmos]}")
    if match[0].get("disabled"):
        raise HarnessError(f"gizmo {key} disabled: {match[0].get('disabledReason')}")
    return peer.call("rimworld/execute_gizmo", {"gizmoId": match[0]["id"]})


def run_ui_actions(host, client):
    """Zombieland UI input on both peers through the real UI code paths, then each effect on both peers."""
    steps, peers = [], {"host": host, "client": client}

    def step(role, what, fn):
        try:
            res = fn()
            steps.append({"role": role, "what": what, "ok": res.get("success", True) is not False,
                          "context": res.get("context"), "skipped": res.get("skipped")})
            if res.get("success") is False:
                steps[-1]["error"] = short(res.get("error") or res.get("message"), 200)
        except HarnessError as e:
            steps.append({"role": role, "what": what, "ok": False, "error": short(str(e), 200)})

    def ui(role, **args):
        return lambda: peers[role].call("zombieland/mp_ui_action", args, check=False)

    def gizmo(role, target, key):
        step(role, f"select {target}", ui(role, action="select", name=target))
        step(role, f"gizmo {key}", lambda: press_gizmo(peers[role], key))

    def state(peer):
        return peer.call("zombieland/mp_sync_state", timeout=60, log=False)["sections"]

    def wait_row(peer, section, text, timeout=20):
        # Toggles send absolute values, so the next peer must see the previous command first.
        end = time.time() + timeout
        while time.time() < end and not any(text in row for row in state(peer)[section]):
            time.sleep(0.5)

    sec = state(client)
    sawyers = {r.split("|")[1] for r in sec["chainsaws"]}
    candidates = [r.split("|") for r in sec["colonistFlags"] if r.split("|")[0] not in sawyers]
    flip = {"True": "False", "False": "True"}
    expect, doctor = {}, None
    for i, key in enumerate(("avoid", "extract", "doubletap")):
        # Extract harvesting needs a colonist who can doctor; the gizmo is disabled otherwise.
        for pid, *flags in candidates:
            gizmo("client", pid, key)
            if steps[-1]["ok"]:
                row = expect.get(pid, [pid, *flags])
                row[1 + i] = flip[row[1 + i]]
                expect[pid] = row
                doctor = pid if key == "extract" else doctor
            if steps[-1]["ok"] or "disabled" not in (steps[-1].get("error") or ""):
                break
            steps[-2:] = []
        else:
            steps.append({"role": "client", "what": f"gizmo {key}", "ok": False, "error": "disabled for every colonist"})
    expect = {"colonistFlags": ["|".join(row) for row in expect.values()]}
    corpses = [r.split("|") for r in sec["things"] if r.split("|")[1].startswith("Corpse_Zombie")]
    if corpses and doctor:
        # A forced order through the map context menu: Multiplayer syncs the ordered job.
        dx, dz = (int(v) for v in next(r for r in sec["things"] if r.startswith(f"{doctor}|")).split("|")[2].split(","))
        x, z = min(((int(a), int(b)) for a, b in (c[2].split(",") for c in corpses)),
                   key=lambda c: (c[0] - dx) ** 2 + (c[1] - dz) ** 2)
        step("client", f"select {doctor}", ui("client", action="select", name=doctor))
        step("client", "context menu on zombie corpse", lambda: client.call("rimworld/open_context_menu", {"x": x, "z": z}, check=False))

        def order():
            options = client.call("rimworld/get_context_menu_options", check=False).get("options") or []
            match = [o for o in options if "harvest" in (o.get("label") or "").lower()]
            if not match:
                # A reserved or unreachable corpse offers only "Go here", which RimWorld runs without a menu.
                return {"skipped": "no harvest option for this corpse"}
            return client.call("rimworld/execute_context_menu_option", {"optionIndex": match[0]["index"]}, check=False)

        step("client", "order extract harvesting", order)
        if steps[-1]["ok"] and not steps[-1].get("skipped"):
            expect["pawnHealth"] = (f"{doctor}|", "|ExtractZombieSerum")
    step("client", "area Home=ColonistInside", ui("client", action="area", name="Home=ColonistInside"))
    expect["areas"] = "|Home|ColonistInside"
    step("host", "settings colonyMultiplier=1.5", ui("host", action="settings", name="colonyMultiplier", value="1.5"))
    expect["settings"] = "colonyMultiplier=1.5"
    if sec["chainsaws"]:
        gizmo("host", "chainsaw", "chainsaw")
        expect["chainsaws"] = "|True|"
    if sec["thumpers"]:
        thumper = sec["thumpers"][0].split("|")[0]
        gizmo("client", "thumper", "switch")
        wait_row(host, "switches", f"{thumper}|False")
        gizmo("host", "thumper", "switch")
        expect["switches"] = f"{thumper}|True"
        step("host", "thumper dialog 0.5/625", ui("host", action="thumper", value="0.5", value2="625"))
        expect["thumpers"] = f"{thumper}|0.5|625"
    if sec["storage"]:
        step("client", "storage range 0.2~0.8", ui("client", action="storage", value="0.2", value2="0.8"))
        expect["storage"] = ("|0.2", "~0.8")
    if any("Symbiant" in r.split("|")[1] for r in sec["things"]):
        step("client", "select symbiant", ui("client", action="select", name="symbiant"))
    for role in peers:
        peers[role].call("rimworld/clear_selection", log=False)

    def met():
        states = {r: p.call("zombieland/mp_sync_state", timeout=60, log=False)["sections"] for r, p in peers.items()}
        wanted = [(k, v if k == "colonistFlags" else [v]) for k, v in expect.items()]
        unmet = [f"{r} {k}: no row with {w!r}" for r, st in states.items() for k, ws in wanted for w in ws
                 if not any(all(t in row for t in (w if isinstance(w, tuple) else (w,))) for row in st[k])]
        return unmet

    deadline, unmet = time.time() + 20, []
    while time.time() < deadline:
        unmet = met()
        if not unmet:
            break
        time.sleep(1)
    return {"steps": steps, "checked": sorted(expect), "unmet": unmet}


def collect_zombies(session, result):
    result["zombies"] = {}
    for role, peer in session.peers.items():
        try:
            listing = peer.call("zombieland/list_zombies", {"limit": 500}, timeout=60)
            status = peer.call("zombieland/get_status", timeout=60)
            session.write_json(f"{role}-zombies.json", listing)
            session.write_json(f"{role}-zl-status.json", status)
            digest = zombie_digest(listing)
            digest["liveZombieCount"] = (status.get("tickManager") or {}).get("liveZombieCount")
            result["zombies"][role] = digest
        except (HarnessError, OSError, ConnectionError) as e:
            result["errors"].append(f"{role} zombie evidence: {short(str(e), 200)}")
    states = {}
    for role, peer in session.peers.items():
        try:
            states[role] = peer.call("zombieland/mp_sync_state", timeout=60)
            session.write_json(f"{role}-sync-state.json", states[role])
        except (HarnessError, OSError, ConnectionError) as e:
            result["errors"].append(f"{role} sync state: {short(str(e), 200)}")
    if len(states) == 2:
        hs, cs = states["host"]["sections"], states["client"]["sections"]
        differ = sorted(k for k in hs if hs[k] != cs.get(k))
        result["sync_state"] = {"sections": len(hs), "differ": differ, "counts": states["host"]["counts"]}
        defs = {}
        for row in hs["things"]:
            defs[row.split("|")[1]] = defs.get(row.split("|")[1], 0) + 1
        result["sync_state"]["activity"] = ", ".join(f"{k} {v}" for k, v in (
            ("granite", defs.get("Granite")), ("extract", defs.get("ZombieExtract")),
            ("zombie corpses", sum(v for k, v in defs.items() if k.startswith("Corpse_Zombie"))),
            ("chainsaw", ";".join(hs["chainsaws"]) or None), ("contaminated things", len(hs["contamination"]) - 1),
            ("conditions", ";".join(hs["conditions"]) or None)) if v)
        if differ:
            session.write_json("sync-diff.json", {k: {"host_only": sorted(set(hs[k]) - set(cs.get(k) or [])),
                                                       "client_only": sorted(set(cs.get(k) or []) - set(hs[k]))}
                                                   for k in differ})
    z = result["zombies"]
    if "host" in z and "client" in z and z["host"]["hash"] != z["client"]["hash"]:
        hr, cr = ({r[0]: r for r in z[k]["rows"]} for k in ("host", "client"))
        diff = [{"thingId": k, "host": hr.get(k), "client": cr.get(k)} for k in sorted(set(hr) | set(cr), key=str)
                if hr.get(k) != cr.get(k)]
        session.write_json("zombie-diff.json", diff)
        z["diff_count"] = len(diff)
    for digest in z.values():
        if isinstance(digest, dict):
            digest.pop("rows", None)


def report_run(session, result, a):
    result["build"] = session.build or build_identity()
    result["attention"] = session.attention
    result["leftover_pids"] = session.leftover
    result["desync_files"] = {r: sorted(set(desync_files(r)) - session.desync_baseline[r]) for r in PROFILES}
    if session.run_dir.exists():
        result["logs"], result["desync_messages"], result["mp_events"] = analyse_logs(session)
    z, final, ticks = result.get("zombies") or {}, result.get("final") or {}, result.get("ticks") or {}
    clocks_equal = bool(final) and all(final["host"][k] == final["client"][k]
                                       for k in ("ticksGame", "worldTicks", "mapClocks"))
    zombies_equal = "host" in z and "client" in z and z["host"]["hash"] == z["client"]["hash"]
    enough = len(ticks) == 2 and all(v >= a.ticks for v in ticks.values())
    sync = result.get("sync_state") or {}
    ui_failed = [s for s in (result.get("ui") or {}).get("steps", []) if not s["ok"]] \
        + (result.get("ui") or {}).get("unmet", [])
    failed = bool(result["errors"]) or not result["joined"] or result["desync"] or not clocks_equal \
        or not zombies_equal or not enough or bool(result["leftover_pids"]) or not sync or bool(sync.get("differ")) \
        or bool(ui_failed)
    result["verdict"] = "FAIL" if failed else "PASS"
    lines = [f"mp-session {result['verdict']}  run={rel(session.run_dir)}",
             f"build     {result['build']}",
             f"source    {result['source']}",
             f"joined    {'yes' if result['joined'] else 'no'}"
             + (f" ({result.get('join_seconds')} s, {result.get('join_attempts')} join attempt(s))"
                if result["joined"] else "")]
    if ticks:
        lines.append(f"ticks     host {ticks.get('host')} client {ticks.get('client')} (target {a.ticks}, {a.speed}, "
                     f"{result.get('run_seconds')} s)")
    if final:
        h, c = final["host"], final["client"]
        lines.append(f"clocks    paused boundary {'equal' if clocks_equal else 'DIFFERENT'}: ticksGame "
                     f"{h['ticksGame']}/{c['ticksGame']}, worldTicks {h['worldTicks']}/{c['worldTicks']}, "
                     f"maps {h['mapClocks']}/{c['mapClocks']} (host/client)")
    if not result["joined"] or result["errors"]:
        lines += [f"mp event  {e}" for e in (result.get("mp_events") or [])[:4]]
    lines.append(f"desync    {'YES' if result['desync'] else 'no'}")
    for msg in (result.get("desync_messages") or [])[:4]:
        lines.append(f"          {msg}")
    for role, files in result["desync_files"].items():
        for f in files:
            lines.append(f"          {role} report {describe_desync_zip(f)}")
    if z:
        hz, cz = z.get("host") or {}, z.get("client") or {}
        lines.append(f"zombies   host {hz.get('count')} client {cz.get('count')}, (id,cell,state) hash "
                     f"{'equal' if zombies_equal else 'DIFFERENT'}"
                     + (f", {z['diff_count']} differ (zombie-diff.json)" if z.get("diff_count") else ""))
        lines.append(f"          host kinds {short(hz.get('kinds'), 160)}")
        if hz.get("kinds") != cz.get("kinds"):
            lines.append(f"          client kinds {short(cz.get('kinds'), 160)}")
    if sync:
        lines.append(f"state     {sync['sections'] - len(sync['differ'])}/{sync['sections']} sections equal"
                     + (f", DIFFERENT: {', '.join(sync['differ'])} (sync-diff.json)" if sync["differ"] else "")
                     + f"; zombies at join {result.get('zombies_at_join')}, at end {(z.get('host') or {}).get('count')}")
    if result.get("ui"):
        ui = result["ui"]
        lines.append(f"ui        {sum(s['ok'] for s in ui['steps'])}/{len(ui['steps'])} actions ok, "
                     f"{len(ui['checked']) - len(ui['unmet'])}/{len(ui['checked'])} effects on both peers")
        lines += [f"          FAILED {short(x, 200)}" for x in ui_failed[:6]]
    if sync.get("activity"):
        lines.append(f"activity  {sync['activity']}")
    if result.get("rejoin"):
        r = result["rejoin"]
        lines.append(f"rejoin    client left at tick {r.get('left_at')}, rejoined={r.get('joined')} at {r.get('at')}")
    if result.get("client_constant"):
        c = result["client_constant"]
        lines.append(f"constants client local {c['setting']}; session state equal above; local file kept={c['local_file_kept']}")
    unexpected = [x for x in result["attention"] if not x["expected"]]
    lines.append(f"attention {len(result['attention']) - len(unexpected)} expected acked, {len(unexpected)} unexpected")
    lines += [f"          {x['role']}: {short(x['title'], 150)}" for x in unexpected[:3]]
    lines += log_lines(result.get("logs") or {}, a.log_lines)
    lines += [f"error     {short(err, 300)}" for err in result["errors"]]
    if result["leftover_pids"]:
        lines.append(f"error     leftover RimWorld pids {result['leftover_pids']}")
    finish_output(session, result, lines)
    return 1 if failed else 0


def finish_output(session, result, lines):
    text = "\n".join(lines)
    if session.run_dir.exists():
        session.write_json("result.json", result)
        (session.run_dir / "summary.txt").write_text(text + "\n")
    print(text)


# ---------------------------------------------------------------- fixture mode

def cmd_fixture(a):
    result = {"errors": []}
    session = Session("fixture", a.max_minutes)
    try:
        with session:
            host = session.launch("host")
            session.wait_menu(["host"])
            build_fixture(host, a.name or FIXTURES[a.variant], result, session.note, a.variant)
    except Interrupted as e:
        result["errors"].append(f"interrupted ({e})")
    except HarnessError as e:
        result["errors"].append(str(e))
    result["build"] = session.build or build_identity()
    result["attention"] = session.attention
    result["leftover_pids"] = session.leftover
    if session.run_dir.exists():
        result["logs"] = analyse_logs(session)[0]
    ok = not result["errors"] and result.get("saved") and not result["leftover_pids"]
    lines = [f"mp-fixture {'PASS' if ok else 'FAIL'}  run={rel(session.run_dir)}", f"build     {result['build']}"]
    if result.get("saved"):
        name = a.name or FIXTURES[a.variant]
        lines.append(f"saved     {name} -> {rel(PROFILES['host'] / 'Saves' / (name + '.rws'))}")
    if result.get("contents"):
        lines.append(f"contents  {short(result['contents'], 600)}")
    if result.get("features"):
        lines.append(f"features  {short('; '.join(result['features']), 600)}")
    lines += log_lines(result.get("logs") or {}, a.log_lines)
    lines += [f"error     {short(err, 300)}" for err in result["errors"]]
    finish_output(session, result, lines)
    return 0 if ok else 1


def debug(host, path, **target):
    return host.call("rimworld/execute_debug_action", dict(target, path=path))


def local_hour(host):
    return ((host.call("zombieland/get_status", log=False).get("ambientSound") or {}).get("localHour") or 0.0)


def build_fixture(host, name, result, note, variant="base"):
    """Quick test colony at 22:00 on day 4: walled base with doors, armed colonists, ~90 zombies of all types.
    features adds the Multiplayer feature clusters, natural has no zombies (natural population spawning)."""
    host.call("rimworld/start_debug_game_ready",
              {"pauseIfNeeded": True, "timeoutMs": 240000, "readiness": "playable"}, timeout=260)
    # Zombieland's default grace period is 3 days; jump past it without simulating.
    while (host.status().get("ticksGame") or 0) < 3 * 60000:
        debug(host, "Actions\\Increment time...\\1 day")
    if variant == "natural":
        # Population spawning needs a dynamic threat level above zero; zero threat even kills zombies.
        for _ in range(10):
            if (host.call("zombieland/incident_threat_state", log=False).get("state") or {}).get("threat", {}) \
                    .get("currentThreat", 0) >= 0.5:
                break
            debug(host, "Actions\\Increment time...\\1 day")
    for _ in range(24):
        if 21.5 <= local_hour(host) < 23:
            break
        debug(host, "Actions\\Increment time...\\1 hour")
    note(f"time advanced, hour {local_hour(host):.1f}")

    colonists = host.call("rimworld/list_colonists", {"currentMapOnly": True})["colonists"]
    if not colonists:
        raise HarnessError("quick test colony has no colonists")
    cx = round(sum(c["position"]["x"] for c in colonists) / len(colonists))
    cz = round(sum(c["position"]["z"] for c in colonists) / len(colonists))
    for i in range(2):
        host.call("zombieland/spawn_colonist", {"x": cx + 2 * i - 1, "z": cz - 2, "namePrefix": "ZL_MP",
                                                "disableWork": False})
    colonists = host.call("rimworld/list_colonists", {"currentMapOnly": True})["colonists"]
    weapons = ["Gun_AssaultRifle", "Gun_BoltActionRifle", "Gun_ChainShotgun", "Gun_Autopistol", "MeleeWeapon_LongSword"]
    for i, col in enumerate(colonists):
        host.call("rimworld/select_pawn", {"pawnId": col["pawnId"]})
        debug(host, "Actions\\Equip primary (selected)...\\" + weapons[i % len(weapons)])
    host.call("rimworld/clear_selection")
    host.call("rimworld/set_draft", {"pawnId": colonists[0]["pawnId"], "drafted": True})

    half = 9
    x0, x1, z0, z1 = cx - half, cx + half, cz - half, cz + half
    skipped = 0
    for x, z, length, direction in ((x0, z0, 2 * half + 1, "east"), (x0, z1, 2 * half + 1, "east"),
                                    (x0, z0 + 1, 2 * half - 1, "north"), (x1, z0 + 1, 2 * half - 1, "north")):
        res = host.call("zombieland/place_wall_line", {"x": x, "z": z, "length": length, "direction": direction},
                        check=False)
        skipped += len(res.get("skipped") or [])
    for x, z in ((cx, z0), (cx, z1), (x0, cz), (x1, cz)):
        host.call("zombieland/place_thing", {"defName": "Door", "x": x, "z": z, "stuffDefName": "WoodLog"})

    groups = [(cx + 32, cz, 40, "Normal", True, 12), (cx - 32, cz, 20, "Random", True, 12),
              (cx, cz - 30, 8, "Normal", False, 8)]
    specials = ["SuicideBomber", "ToxicSplasher", "TankyOperator", "Miner", "Electrifier", "Albino", "DarkSlimer",
                "Healer"]
    for i, kind in enumerate(specials):
        groups.append((cx - 28 + 8 * i, cz + 30, 3, kind, True, 4))
    if variant == "natural":
        groups = []
    elif variant == "features":
        groups = groups[3:]  # specials only, so colonists live long enough to harvest and saw
    for x, z, count, kind, standing, radius in groups:
        host.call("zombieland/spawn_zombie_group", {"x": x, "z": z, "count": count, "type": kind,
                                                    "appearDirectly": standing, "radius": radius})
    if variant != "natural":
        debug(host, "Actions\\T: Spawn: Zombie Spitter", x=cx + 30, z=cz - 30)
    if variant == "features":
        result["features"] = host.call("zombieland/mp_fixture_features", {"cx": cx, "cz": cz}).get("notes")
        host.call("zombieland/spawn_symbiant", {"x": cx + 14, "z": cz - 14})
    listing = host.call("zombieland/list_zombies", {"limit": 500})
    normals = [z for z in listing.get("zombies") or [] if not any(z.get(k) for k in SPECIAL_FLAGS)
               and z.get("kind") != "Spitter" and (z.get("position") or {}).get("x", 0) > cx + 20][:6]
    for z in normals:
        debug(host, "Actions\\T: Apply: Zombie raging", thingId=z["pawnId"])
    host.call("rimworld/clear_selection")
    digest = zombie_digest(host.call("zombieland/list_zombies", {"limit": 500}))
    threat = (host.call("zombieland/incident_threat_state", log=False).get("state") or {}).get("threat", {}).get("currentThreat")
    result["contents"] = (f"day {(host.status().get('ticksGame') or 0) // 60000 + 1} {local_hour(host):.0f}:00, "
                          f"threat {threat}, {len(colonists)} armed colonists (1 drafted), "
                          f"{19 * 4 - 2 * 2} wall cells around ({cx},{cz}) with 4 doors ({skipped} skipped), "
                          f"{digest['count']} zombies {digest['kinds']}")
    host.call("rimworld/save_game", {"saveName": name}, timeout=180)
    result["saved"] = (PROFILES["host"] / "Saves" / f"{name}.rws").exists()
    if not result["saved"]:
        raise HarnessError(f"save {name}.rws not found after rimworld/save_game")


# ---------------------------------------------------------------- hold / call modes

def cmd_hold(a):
    roles = ["host", "client"] if a.role == "both" else [a.role]
    session = Session("hold", a.minutes + 1)
    try:
        with session:
            for role in roles:
                session.launch(role)
            session.wait_menu(roles)
            print(f"hold ready: {rel(session.run_dir)}/session.json (SIGTERM or {a.minutes} min stops it)", flush=True)
            end = time.time() + a.minutes * 60
            while time.time() < end and all(session.alive(r) for r in roles):
                time.sleep(2)
    except (Interrupted, HarnessError) as e:
        print(f"hold ended: {e}")
    print(f"hold stopped; leftover pids {session.leftover}")
    return 0


def cmd_call(a):
    runs = sorted(p for p in LOG_ROOT.glob("*-hold") if (p / "session.json").exists())
    if not runs or not MARKER.exists():
        raise HarnessError("no running hold session")
    info = json.loads((runs[-1] / "session.json").read_text())[a.role]
    peer = Peer(a.role, info["port"], info["token"])
    args = json.loads(a.args) if a.args else {}
    if a.tool.startswith("attention/"):
        res = peer.request(a.tool, args, a.timeout)
    else:
        res = peer.call(a.tool, args, timeout=a.timeout, check=False)
    text = json.dumps(res, indent=1, default=str)
    if a.out:
        Path(a.out).write_text(text)
    print(text[: a.limit])
    return 0


# ---------------------------------------------------------------- main

def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="mode", required=True)
    r = sub.add_parser("run", help="host+client session with a compact verdict")
    src = r.add_mutually_exclusive_group()
    src.add_argument("--save", default=FIXTURE, help=f"host save name (default {FIXTURE})")
    src.add_argument("--debug-game", action="store_true", help="start RimWorld's quick test colony instead")
    r.add_argument("--ticks", type=int, default=2500, help="game ticks to simulate (default 2500)")
    r.add_argument("--incident", type=int, choices=[4, 25, 100, 200], help="trigger a synchronized Zombieland incident of this size after joining")
    r.add_argument("--incident-x", type=int, default=140)
    r.add_argument("--incident-z", type=int, default=140)
    r.add_argument("--speed", default="Fast", choices=["Normal", "Fast", "Superfast", "Ultrafast"])
    r.add_argument("--join-timeout", type=int, default=300)
    r.add_argument("--run-timeout", type=int, default=300, help="real seconds allowed for the tick target")
    r.add_argument("--desync-traces", action="store_true", help="Multiplayer desync stack traces (unstable on Arm64)")
    r.add_argument("--async-time", action="store_true", help="host with Multiplayer async time (per-map clocks)")
    r.add_argument("--ui", action="store_true", help="Zombieland UI input on both peers after joining, effects checked on both")
    r.add_argument("--rejoin-after", type=int, default=0, help="client leaves and rejoins after this many ticks (new join point)")
    r.add_argument("--client-constant", help="client plays with this local advanced setting, e.g. ZOMBIE_HIT_CHANCE_IDLE=0.9")
    r.add_argument("--log-lines", type=int, default=6, help="ZL log lines printed per peer")
    r.add_argument("--max-minutes", type=float, default=20, help="watchdog for the whole session")
    f = sub.add_parser("fixture", help=f"build the single-player fixture save (default {FIXTURE})")
    f.add_argument("--variant", default="base", choices=sorted(FIXTURES))
    f.add_argument("--name", help="save name (default per variant: " + ", ".join(f"{k} {v}" for k, v in FIXTURES.items()) + ")")
    f.add_argument("--log-lines", type=int, default=6)
    f.add_argument("--max-minutes", type=float, default=15)
    h = sub.add_parser("hold", help="launch peers for manual calls")
    h.add_argument("role", choices=["host", "client", "both"])
    h.add_argument("--minutes", type=float, default=15)
    c = sub.add_parser("call", help="call a tool on the newest hold session")
    c.add_argument("role", choices=["host", "client"])
    c.add_argument("tool")
    c.add_argument("args", nargs="?", default="")
    c.add_argument("--out")
    c.add_argument("--timeout", type=int, default=120)
    c.add_argument("--limit", type=int, default=3000)
    a = p.parse_args()
    try:
        return {"run": cmd_run, "fixture": cmd_fixture, "hold": cmd_hold, "call": cmd_call}[a.mode](a)
    except HarnessError as e:
        print(f"mp-session error: {e}")
        return 2


if __name__ == "__main__":
    sys.exit(main())
