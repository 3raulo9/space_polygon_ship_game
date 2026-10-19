"""CI smoke test for THE RIFT's Minecraft side.

Waits for a dedicated server (started with the mod) to finish loading, then plays
UNRENDERED's part over the bridge: hello, city, field, enter, tex, bye. It checks the
answers and scans the server log for mixin, datapack and handler failures.

    python3 tools/smoke.py run/server.log
"""
import json
import socket
import sys
import time
import zlib

LOG = sys.argv[1] if len(sys.argv) > 1 else "server.log"


def log_text():
    try:
        with open(LOG, encoding="utf-8", errors="replace") as f:
            return f.read()
    except FileNotFoundError:
        return ""


def wait_for(pattern, seconds):
    end = time.time() + seconds
    while time.time() < end:
        text = log_text()
        if pattern in text:
            return True
        if "Exception in server tick loop" in text or "Crash report saved" in text or "BUILD FAILED" in text:
            return False
        time.sleep(2)
    return False


class Link:
    def __init__(self, port):
        self.sock = socket.create_connection(("127.0.0.1", port), timeout=20)
        self.buf = b""

    def send(self, msg):
        self.sock.sendall((json.dumps(msg) + "\n").encode())

    def send_bin(self, kind, payload, **head):
        z = zlib.compressobj(6, zlib.DEFLATED, -15)
        data = z.compress(payload) + z.flush()
        head.update({"t": "bin", "k": kind, "n": len(data)})
        self.sock.sendall((json.dumps(head) + "\n").encode() + data)

    def line(self):
        while b"\n" not in self.buf:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise ConnectionError("bridge closed")
            self.buf += chunk
        line, self.buf = self.buf.split(b"\n", 1)
        return json.loads(line)

    def read_exact(self, n):
        while len(self.buf) < n:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise ConnectionError("bridge closed mid-frame")
            self.buf += chunk
        data, self.buf = self.buf[:n], self.buf[n:]
        return data

    def next(self, want=None, seconds=30):
        """The next message (skipping pings), or the next one of type `want`."""
        end = time.time() + seconds
        while time.time() < end:
            m = self.line()
            payload = None
            if m.get("t") == "bin":
                payload = zlib.decompress(self.read_exact(m["n"]), -15)
            if m.get("t") == "ping":
                continue
            if want is None or m.get("t") == want or (m.get("t") == "bin" and m.get("k") == want):
                return m, payload
        raise TimeoutError(f"no {want} within {seconds}s")


def main():
    failures = []
    if not wait_for("Done (", 600):
        print(log_text()[-6000:])
        print("FAIL: the server never finished starting")
        return 1
    print("server is up")
    if not wait_for("Rift listening for UNRENDERED on port", 60):
        failures.append("the bridge never started on the dedicated server")
        print(log_text()[-4000:])
        return 1

    port = 25570
    for line in log_text().splitlines():
        if "Rift listening for UNRENDERED on port" in line:
            port = int(line.rsplit(" ", 1)[1])
    link = Link(port)
    link.send({"t": "hello", "proto": 1, "name": "SMOKE", "chassis": "SOLDIER", "version": "ci"})
    m, _ = link.next("welcome")
    print("welcome:", m)
    if m.get("proto") != 1:
        failures.append(f"welcome carried proto {m.get('proto')}")
    m, _ = link.next("built", 20)
    print("built (full):", {k: (len(v) if isinstance(v, list) else v) for k, v in m.items()})

    # A small skyline: one tower and one arch.
    link.send({"t": "city", "hash": "ci-1", "size": 400, "structures": [
        {"i": 0, "h": 45.0, "legs": [[30.0, 40.0, 3.6]]},
        {"i": 1, "h": 7.0, "legs": [[-20.0, 10.0, 2.0], [-20.0, 46.0, 2.0]]},
    ]})
    link.send({"t": "field", "ents": [{"id": 7, "x": 5.0, "y": 0.0, "z": 9.0, "r": 4.16, "h": 6.9, "hp": 3, "max": 3}]})
    link.send({"t": "fell", "i": 1})

    # The pilot tries to come through with nobody standing in the world: the bridge has
    # to say so rather than fall over.
    link.send({"t": "enter", "name": "SMOKE", "chassis": "SOLDIER", "hp": 100, "max": 100})
    m, _ = link.next("chat", 20)
    print("chat:", m)

    # A texture request on a dedicated server has no client to read from: null, not a crash.
    link.send({"t": "tex", "ids": ["minecraft:textures/block/stone.png"]})
    m, _ = link.next("texdata", 20)
    print("texdata:", {k: v for k, v in m.items() if k != "png"})

    link.send({"t": "join"})
    time.sleep(4)   # let the city queue drain

    # One of UNRENDERED's players standing in its world: a stand-in, and rift entities around it.
    link.send({"t": "seats", "list": [{"seat": 0, "name": "SMOKE", "x": 10.0, "y": 0.0, "z": 10.0,
                                       "yaw": 0.0, "hp": 80, "max": 100, "r": 1.3, "h": 3.2}]})
    m, payload = link.next("rents", 20)
    print("rents:", len(payload or b""), "bytes")

    # A pickaxe went into the tower: its collision goes around the point.
    link.send({"t": "cut", "i": 0, "boxes": [[30.0, 2.0, 40.0, 2.5]]})

    # A hunter's body, then the hunter itself through the portal. A dedicated server with
    # nobody standing at a portal has nowhere to put it, so it must come straight back.
    import struct
    tri = struct.pack("<IBI", 1, 4, 3) + b"".join(struct.pack("<fffBBBB", *v) for v in
        [(0, 0, 0, 200, 30, 60, 255), (1, 0, 0, 200, 30, 60, 255), (0, 2, 0, 200, 30, 60, 255)])
    link.send_bin("model", tri, kind="hunter")
    link.send({"t": "cross", "kind": "hunter", "hp": 2.0, "max": 3.0})
    m, _ = link.next("return", 20)
    print("return:", m)
    if m.get("kind") != "hunter":
        failures.append(f"return came back as {m}")
    time.sleep(2)
    link.send({"t": "bye", "reason": "smoke test done"})
    time.sleep(2)

    text = log_text()
    for bad in ("Mixin apply failed", "MixinApplyError", "InvalidInjectionException", "Failed to load datapacks",
                "Rift: failed handling", "Exception in server tick loop", "Crash report saved",
                "Couldn't parse data file", "Failed to parse"):
        if bad in text:
            failures.append(f"server log contains: {bad}")
    if "raising UNRENDERED's skyline" not in text:
        failures.append("the city was never raised")
    if "a stand-in for UNRENDERED seat 0" not in text:
        failures.append("no stand-in was made for the seat")

    if failures:
        print(text[-8000:])
        for f in failures:
            print("FAIL:", f)
        return 1
    print("SMOKE OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
