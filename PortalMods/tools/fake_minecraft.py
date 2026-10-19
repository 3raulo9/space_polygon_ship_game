#!/usr/bin/env python3
"""
A stand-in for the Minecraft side of THE RIFT, for developing and testing the UNRENDERED mod
without Minecraft running. It speaks PROTOCOL.md: beacons on the LAN, welcomes UNRENDERED,
serves a small hilly slice of "Minecraft" (grass, dirt, stone, water, a tree and a glowstone
portal), walks a zombie about, drops a sword, and sends a pretend Minecraft player through
into UNRENDERED.

Its textures are drawn here procedurally. They are placeholders, not Minecraft's art. The
real Fabric mod sends the textures the player's own Minecraft is using.

Every `scene` UNRENDERED streams back is decoded and painted to scene.png, so you can see
exactly what a Minecraft visitor would be shown.

    python3 fake_minecraft.py [--port 25570] [--visitor] [--out DIR]
"""
import argparse, base64, io, json, math, os, socket, struct, sys, threading, time, zlib

from PIL import Image, ImageDraw

ap = argparse.ArgumentParser()
ap.add_argument("--port", type=int, default=25570)
ap.add_argument("--visitor", action="store_true", help="send a Minecraft player into UNRENDERED")
ap.add_argument("--out", default=".")
ap.add_argument("--quit-after", type=float, default=0)
args = ap.parse_args()
os.makedirs(args.out, exist_ok=True)
LOG = open(os.path.join(args.out, "fake_minecraft.log"), "w")


def log(*a):
    s = " ".join(str(x) for x in a)
    print(s, flush=True)
    LOG.write(s + "\n"); LOG.flush()


# --- placeholder textures --------------------------------------------------------------

def png(img):
    b = io.BytesIO(); img.save(b, "PNG"); return b.getvalue()


def noise_tile(base, spread, seed, size=16):
    import random
    r = random.Random(seed)
    img = Image.new("RGBA", (size, size))
    for y in range(size):
        for x in range(size):
            k = r.randint(-spread, spread)
            img.putpixel((x, y), tuple(max(0, min(255, c + k)) for c in base) + (255,))
    return img


def tex_for(tid):
    seed = sum(map(ord, tid))
    if "glowstone" in tid:
        img = noise_tile((200, 160, 80), 40, seed)
        d = ImageDraw.Draw(img)
        for i in range(6):
            x, y = (i * 5) % 14, (i * 7) % 14
            d.rectangle([x, y, x + 2, y + 2], fill=(255, 236, 160, 255))
        return img, 1
    if "water" in tid or "portal" in tid:
        frames = 8
        img = Image.new("RGBA", (16, 16 * frames))
        for f in range(frames):
            t = noise_tile((150, 150, 150), 25, seed + f)
            img.paste(t, (0, 16 * f))
        return img, 2
    if "grass_block_top" in tid: return noise_tile((140, 140, 140), 30, seed), 1   # grey, tinted green
    if "grass_block_side" in tid:
        img = noise_tile((134, 96, 67), 20, seed)
        ImageDraw.Draw(img).rectangle([0, 0, 15, 3], fill=(95, 159, 53, 255))
        return img, 1
    if "dirt" in tid: return noise_tile((134, 96, 67), 20, seed), 1
    if "stone" in tid: return noise_tile((125, 125, 125), 18, seed), 1
    if "oak_log" in tid: return noise_tile((102, 81, 50), 20, seed), 1
    if "leaves" in tid:
        img = noise_tile((120, 120, 120), 40, seed)
        for i in range(0, 256, 7): img.putpixel((i % 16, i // 16), (0, 0, 0, 0))
        return img, 1
    if "sword" in tid:
        img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        d.line([(2, 13), (13, 2)], fill=(100, 230, 230, 255), width=2)
        d.line([(1, 11), (5, 15)], fill=(70, 50, 30, 255), width=2)
        return img, 1
    if "zombie" in tid or "steve" in tid or "skin" in tid:
        img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        skin = (90, 150, 80, 255) if "zombie" in tid else (200, 150, 110, 255)
        shirt = (40, 160, 160, 255)
        pants = (60, 60, 150, 255)
        d.rectangle([0, 0, 31, 15], fill=skin)
        d.rectangle([8, 8, 15, 15], fill=skin); d.rectangle([9, 11, 10, 12], fill=(20, 20, 20, 255)); d.rectangle([13, 11, 14, 12], fill=(20, 20, 20, 255))
        d.rectangle([16, 16, 39, 31], fill=shirt)
        d.rectangle([40, 16, 55, 31], fill=skin)
        d.rectangle([0, 16, 15, 31], fill=pants)
        d.rectangle([16, 48, 47, 63], fill=pants)
        return img, 1
    if "sun" in tid:
        img = Image.new("RGBA", (32, 32), (0, 0, 0, 0)); ImageDraw.Draw(img).rectangle([8, 8, 23, 23], fill=(255, 250, 200, 255)); return img, 1
    if "moon" in tid:
        img = Image.new("RGBA", (128, 64), (0, 0, 0, 0)); ImageDraw.Draw(img).rectangle([10, 10, 22, 22], fill=(220, 220, 230, 255)); return img, 1
    return None, 1


# --- the slice of world ------------------------------------------------------------------

SPAWN = (100.5, 0.0, 200.5)
OX, OY, OZ = 68, 46, 168
SX, SY, SZ = 64, 64, 64

T = lambda n: "minecraft:textures/block/%s.png" % n
DIRS = [(0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1), (-1, 0, 0), (1, 0, 0)]


def face(d, tex, tint=None, shade=True, cull=True, inset=0.0):
    """One cube face, wound so its normal points outward (CCW seen from outside)."""
    dx, dy, dz = DIRS[d]
    if dy:
        y = 1 - inset if dy > 0 else inset
        pts = [(0, y, 0), (0, y, 1), (1, y, 1), (1, y, 0)]
    elif dz:
        z = 1 - inset if dz > 0 else inset
        pts = [(0, 0, z), (1, 0, z), (1, 1, z), (0, 1, z)]
    else:
        x = 1 - inset if dx > 0 else inset
        pts = [(x, 0, 0), (x, 0, 1), (x, 1, 1), (x, 1, 0)]
    a, b, c = pts[0], pts[1], pts[2]
    u = (b[0] - a[0], b[1] - a[1], b[2] - a[2]); v = (c[0] - a[0], c[1] - a[1], c[2] - a[2])
    n = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
    uvs = [(0, 1), (1, 1), (1, 0), (0, 0)]
    if n[0] * dx + n[1] * dy + n[2] * dz < 0: pts, uvs = pts[::-1], uvs[::-1]
    vv = []
    for (x, y, z), (uu, vvv) in zip(pts, uvs): vv += [x, y, z, uu, vvv]
    return {"tex": tex, "cull": d if cull else -1, "tint": tint, "shade": shade, "v": vv}


def cube(i, bid, top, side=None, bottom=None, tint_top=None, tint_all=None, **kw):
    side = side or top; bottom = bottom or side
    quads = []
    for d in range(6):
        t = top if d == 1 else bottom if d == 0 else side
        tint = tint_all or (tint_top if d == 1 else None)
        quads.append(face(d, T(t), tint))
    e = {"i": i, "id": bid, "solid": True, "collide": True, "top": 1.0, "quads": quads, "liquid": None, "emit": 0}
    e.update(kw)
    return e


PALETTE = [
    {"i": 0, "id": "minecraft:air", "solid": False, "collide": False, "top": 0, "quads": []},
    cube(1, "minecraft:grass_block", "grass_block_top", "grass_block_side", "dirt", tint_top=[124, 189, 107]),
    cube(2, "minecraft:dirt", "dirt"),
    cube(3, "minecraft:stone", "stone"),
    {"i": 4, "id": "minecraft:water[level=0]", "solid": False, "collide": False, "top": 0, "liquid": "water",
     "quads": [face(1, T("water_still"), [63, 118, 228], inset=0.125)] + [face(d, T("water_still"), [63, 118, 228]) for d in (0, 2, 3, 4, 5)]},
    cube(5, "minecraft:oak_log", "oak_log_top", "oak_log"),
    dict(cube(6, "minecraft:oak_leaves", "oak_leaves", tint_all=[72, 181, 24]), solid=False),
    dict(cube(7, "minecraft:glowstone", "glowstone"), emit=15),
    {"i": 8, "id": "unrendered_portal:portal", "solid": False, "collide": False, "top": 0, "portal": True, "liquid": None,
     "quads": [face(2, T("water_still"), [63, 118, 228], cull=False, inset=0.45), face(3, T("water_still"), [63, 118, 228], cull=False, inset=0.45)]},
]

blocks = bytearray(SX * SY * SZ * 2)
light = bytearray([0xF0]) * (SX * SY * SZ)


def idx(x, y, z): return ((y - OY) * SZ + (z - OZ)) * SX + (x - OX)


def setb(x, y, z, p):
    if OX <= x < OX + SX and OY <= y < OY + SY and OZ <= z < OZ + SZ:
        struct.pack_into("<H", blocks, idx(x, y, z) * 2, p)


def getb(x, y, z):
    if OX <= x < OX + SX and OY <= y < OY + SY and OZ <= z < OZ + SZ:
        return struct.unpack_from("<H", blocks, idx(x, y, z) * 2)[0]
    return 0


def height(x, z):
    return int(66 + 3 * math.sin(x / 7.0) + 2 * math.cos(z / 5.0) + 1.5 * math.sin((x + z) / 11.0))


for x in range(OX, OX + SX):
    for z in range(OZ, OZ + SZ):
        h = height(x, z)
        if abs(x - 100) <= 4 and abs(z - 200) <= 6: h = 66      # flat ground at the portal
        for y in range(OY, h - 3): setb(x, y, z, 3)
        for y in range(h - 3, h): setb(x, y, z, 2)
        setb(x, h, z, 1)
        if h < 65:
            for y in range(h + 1, 66): setb(x, y, z, 4)
# a tree
for y in range(67, 72): setb(108, y, 194, 5)
for dx in range(-2, 3):
    for dz in range(-2, 3):
        for y in range(70, 73):
            if abs(dx) + abs(dz) < 4 and not (dx == 0 and dz == 0 and y < 72): setb(108 + dx, y, 194 + dz, 6)
# the portal: glowstone frame 4 wide x 5 tall in the X-Y plane at z=198, water inside
for x in range(99, 103):
    for y in range(67, 72):
        edge = x in (99, 102) or y in (67, 71)
        setb(x, y, 198, 7 if edge else 8)
SPAWN = (100.5, 67.0, 200.5)

TEXTURES = {}


def texdata(tid):
    if tid not in TEXTURES:
        img, ft = tex_for(tid)
        TEXTURES[tid] = (base64.b64encode(png(img)).decode() if img else None, ft)
    return TEXTURES[tid]


# --- a zombie, a sword on the ground, and a visitor's body ------------------------------

def box_quads(x0, y0, z0, x1, y1, z1, uv0, uvsz):
    """Six quads of a box, all mapped onto one rectangle of a 64x64 skin — a stand-in for
    what Minecraft's model renderer would emit."""
    u0, v0 = uv0[0] / 64.0, uv0[1] / 64.0
    du, dv = uvsz[0] / 64.0, uvsz[1] / 64.0
    out = []
    for d in range(6):
        f = face(d, "", None)
        v = f["v"]
        q = []
        for k in range(4):
            px, py, pz, uu, vv = v[k * 5:k * 5 + 5]
            q.append((x0 + px * (x1 - x0), y0 + py * (y1 - y0), z0 + pz * (z1 - z0), u0 + uu * du, v0 + vv * dv))
        out.append(q)
    return out


def biped(walk, yaw):
    s = math.sin(walk) * 0.5
    parts = [
        ((-0.25, 1.5, -0.25, 0.25, 2.0, 0.25), (8, 8), (8, 8), 0),
        ((-0.25, 0.75, -0.125, 0.25, 1.5, 0.125), (20, 20), (8, 12), 0),
        ((-0.5, 0.75, -0.125, -0.25, 1.5, 0.125), (44, 20), (4, 12), s),
        ((0.25, 0.75, -0.125, 0.5, 1.5, 0.125), (44, 20), (4, 12), -s),
        ((-0.25, 0.0, -0.125, 0.0, 0.75, 0.125), (4, 20), (4, 12), -s),
        ((0.0, 0.0, -0.125, 0.25, 0.75, 0.125), (4, 20), (4, 12), s),
    ]
    cy, sy = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
    quads = []
    for (b, uv, sz, swing) in parts:
        for q in box_quads(*b, uv, sz):
            qq = []
            for (x, y, z, u, v) in q:
                pivot = b[4]
                if swing:
                    dy, dz = y - pivot, z
                    y, z = pivot + dy * math.cos(swing) - dz * math.sin(swing), dy * math.sin(swing) + dz * math.cos(swing)
                x, z = x * cy - z * sy, x * sy + z * cy
                qq.append((x, y, z, u, v))
            quads.append(qq)
    return quads


def put_str(b, s):
    e = s.encode(); b += struct.pack("<H", len(e)); b += e


def entity_record(eid, pos, w, h, hp, mx, flags, name, layers):
    b = bytearray()
    b += struct.pack("<ifffffffB", eid, pos[0], pos[1], pos[2], w, h, hp, mx, flags)
    put_str(b, name)
    b += struct.pack("<H", len(layers))
    for tex, quads, rgba in layers:
        put_str(b, tex)
        b += struct.pack("<I", len(quads))
        for q in quads:
            for (x, y, z, u, v) in q:
                b += struct.pack("<fffffBBBB", x, y, z, u, v, *rgba)
    return bytes(b)


# --- the wire ---------------------------------------------------------------------------

class Conn:
    def __init__(self, sock):
        self.s = sock
        self.lock = threading.Lock()
        self.alive = True

    def send(self, o):
        with self.lock:
            self.s.sendall((json.dumps(o) + "\n").encode())

    def send_bin(self, kind, payload, **head):
        z = zlib.compressobj(6, zlib.DEFLATED, -15)
        data = z.compress(payload) + z.flush()
        head.update({"t": "bin", "k": kind, "n": len(data)})
        with self.lock:
            self.s.sendall((json.dumps(head) + "\n").encode() + data)


def read_msgs(sock):
    f = sock.makefile("rb")
    while True:
        line = f.readline()
        if not line: return
        o = json.loads(line)
        if o.get("t") == "bin":
            raw = f.read(o["n"])
            yield o.get("k"), o, zlib.decompress(raw, -15)
        else:
            yield o.get("t"), o, None


def beacon():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    s.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_TTL, 2)
    msg = ("UNRENDERED-RIFT/1 " + json.dumps({"port": args.port, "world": "Test World", "host": "Steve",
                                              "motd": "Steve - Test World", "mc": "1.21.1"})).encode()
    while True:
        for dst in (("224.0.2.61", 4446), ("127.0.0.1", 4446)):
            try: s.sendto(msg, dst)
            except OSError: pass
        time.sleep(1.5)


def paint_scene(payload, path):
    """Decodes a `scene` and paints it from the visitor's eye — what a Minecraft player in
    UNRENDERED would see, give or take Minecraft's own projection."""
    r = io.BytesIO(payload)
    def rd(fmt): return struct.unpack(fmt, r.read(struct.calcsize(fmt)))
    top = rd("<4B"); mid = rd("<4B"); hor = rd("<4B"); fog = rd("<4B")
    murk, lx, ly, lz, sa, saz, ma, maz, night = rd("<9f")
    ex, ey, ez = rd("<3f")
    draws, = rd("<I")
    W, H = 480, 360
    img = Image.new("RGB", (W, H), hor[:3])
    d = ImageDraw.Draw(img)
    for y in range(H // 2):
        t = (y / (H / 2)) ** 3
        d.line([(0, y), (W, y)], fill=tuple(int(top[i] + (hor[i] - top[i]) * t) for i in range(3)))
    tris = []
    total = 0
    yaw = VISITOR_YAW
    fx, fz = -math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
    rx, rz = fz, -fx
    def proj(p):
        dx, dy, dz = p[0] - ex, p[1] - ey, p[2] - ez
        depth = dx * fx + dz * fz
        if depth < 0.1: return None
        sx = (dx * rx + dz * rz) / depth; sy = dy / depth
        f = 1.0 / math.tan(math.radians(31))
        return (W / 2 - sx * f * H / 2, H / 2 - sy * f * H / 2, depth)
    for _ in range(draws):
        mode, n = rd("<BI")
        verts = [rd("<fffBBBB") for _ in range(n)]
        total += n
        if mode == 4:
            groups = [verts[i:i + 3] for i in range(0, n - 2, 3)]
        elif mode == 7:
            groups = []
            for i in range(0, n - 3, 4):
                q = verts[i:i + 4]; groups += [q[:3], [q[0], q[2], q[3]]]
        else:
            continue
        for g in groups:
            ps = [proj(v) for v in g]
            if None in ps: continue
            tris.append((sum(p[2] for p in ps) / 3, [(p[0], p[1]) for p in ps], g[0][3:6]))
    tris.sort(key=lambda t: -t[0])
    for _, pts, col in tris: d.polygon(pts, fill=col)
    img.save(path)
    return total, draws


VISITOR_YAW = 0.0


def serve(sock, addr):
    global VISITOR_YAW
    c = Conn(sock)
    log("UNRENDERED connected from", addr)
    state = {"pilot": None, "hp": 20.0, "sword": True, "scene": 0, "visitor_sent": False}
    portal = {"x": 100, "y": 68, "z": 198, "axis": "x", "dim": "minecraft:overworld"}

    def ticker():
        t0 = time.time()
        while c.alive:
            t = time.time() - t0
            try:
                if state["pilot"]:
                    zx, zz = 108 + 5 * math.cos(t * 0.4), 206 + 5 * math.sin(t * 0.4)
                    zy = height(int(zx), int(zz)) + 1
                    yaw = math.degrees(t * 0.4) + 180
                    recs = []
                    if state["hp"] > 0:
                        recs.append(entity_record(7, (zx, zy, zz), 0.6, 1.95, state["hp"], 20, 1 | 0, "Zombie",
                                                  [("minecraft:textures/entity/zombie/zombie.png", biped(t * 6, yaw), (255, 255, 255, 255))]))
                    if state["sword"]:
                        sq = [[(-0.25, 0.25, 0, 0, 1), (0.25, 0.25, 0, 1, 1), (0.25, 0.75, 0, 1, 0), (-0.25, 0.75, 0, 0, 0)]]
                        recs.append(entity_record(9, (102.5, 67.0, 203.5), 0.25, 0.25, 0, 0, 4 | 8, "Diamond Sword",
                                                  [("minecraft:textures/item/diamond_sword.png", sq, (255, 255, 255, 255))]))
                    payload = struct.pack("<H", len(recs)) + b"".join(recs)
                    c.send_bin("ents", payload, time=int(1000 + t * 20) % 24000)
                if state["visitor_sent"]:
                    VISITOR_YAW = (t * 15) % 360
                    vx, vz = state["vx"] + 2 * math.sin(t * 0.5), state["vz"] + 2 * math.cos(t * 0.5)
                    c.send({"t": "visitor", "uuid": "0000-steve", "x": vx, "y": 0.0, "z": vz, "yaw": VISITOR_YAW,
                            "pitch": 0, "hp": 20, "max": 20, "swing": 0})
                    if int(t * 10) % 2 == 0:
                        c.send_bin("avatar", entity_record(1, (vx, 64.0, vz), 0.6, 1.8, 20, 20, 2, "Steve",
                                                           [("minecraft:skins/steve_test", biped(t * 6, VISITOR_YAW), (255, 255, 255, 255))]),
                                   uuid="0000-steve")
                if args.visitor and not state["visitor_sent"] and t > 3:
                    state["visitor_sent"] = True
                    c.send({"t": "arrive", "uuid": "0000-steve", "name": "Steve"})
                    log("sent a visitor through")
                time.sleep(0.05)
            except OSError:
                return

    threading.Thread(target=ticker, daemon=True).start()
    try:
        for t, o, b in read_msgs(sock):
            if t == "hello":
                log("hello:", o)
                c.send({"t": "welcome", "proto": 1, "world": "Test World", "host": "Steve", "mc": "1.21.1", "portal": portal})
            elif t == "join":
                log("join pressed; portal placed")
                c.send({"t": "chat", "text": "Steve joined worlds — a glowstone portal stands in front of them"})
                c.send(dict(portal, t="portal"))
            elif t == "enter":
                log("pilot entering:", o)
                state["pilot"] = o
                c.send({"t": "palette", "entries": PALETTE})
                c.send_bin("region", bytes(blocks) + bytes(light), ox=OX, oy=OY, oz=OZ, sx=SX, sy=SY, sz=SZ)
                c.send({"t": "spawn", "x": SPAWN[0], "y": SPAWN[1], "z": SPAWN[2], "yaw": 0.0})
            elif t == "tex":
                for tid in o["ids"]:
                    data, ft = texdata(tid)
                    c.send({"t": "texdata", "id": tid, "png": data, "frametime": ft})
            elif t == "me":
                state["me"] = o
                if int(time.time() * 2) % 10 == 0: log("pilot at %.1f %.1f %.1f hp %.0f" % (o["x"], o["y"], o["z"], o["hp"]))
            elif t == "hit":
                state["hp"] -= o["dmg"]
                log("pilot hit entity", o["id"], "for", o["dmg"], "-> zombie hp", state["hp"])
                if state["hp"] <= 0: c.send({"t": "chat", "text": "Zombie was slain by PILOT"})
            elif t == "break":
                x, y, z = o["x"], o["y"], o["z"]
                log("pilot shot block", x, y, z, "id", getb(x, y, z))
                if getb(x, y, z) not in (0, 7, 8):
                    setb(x, y, z, 0)
                    c.send({"t": "blocks", "set": [[x, y, z, 0, 0xF0]]})
            elif t == "pickup":
                log("pickup", o)
                if o["id"] == 9 and state["sword"]:
                    state["sword"] = False
                    c.send({"t": "got", "stack": {"id": "minecraft:diamond_sword", "count": 1}, "item": "minecraft:diamond_sword",
                            "name": "Diamond Sword", "count": 1, "tex": "minecraft:textures/item/diamond_sword.png",
                            "attack": 7, "food": 0, "speed": 1.6})
            elif t == "drop":
                log("pilot dropped", o["stack"], "at", o["x"], o["y"], o["z"])
                state["sword"] = True
            elif t == "leave":
                log("pilot went home")
                state["pilot"] = None
            elif t == "craft":
                state["craft"] = state.get("craft", 0) + 1
                if state["craft"] % 20 == 1: log("craft frame: %d bytes" % len(b))
            elif t == "city":
                log("city: %d structures" % len(o["structures"]))
            elif t == "place":
                log("place:", o)
                state["vx"], state["vz"] = o["x"], o["z"]
            elif t == "field":
                state["field"] = o
                # The visitor swings a diamond sword at whatever is nearest, once a second.
                if o["ents"] and time.time() - state.get("swung", 0) > 1.0:
                    state["swung"] = time.time()
                    e = o["ents"][0]
                    c.send({"t": "strike", "id": e["id"], "dmg": 7.0, "uuid": "0000-steve"})
                    log("visitor struck", e["id"], "hp", e["hp"])
            elif t == "harm":
                log("visitor harmed:", o)
            elif t == "give":
                log("visitor given:", o)
            elif t == "scene":
                state["scene"] += 1
                if state["scene"] % 10 == 1:
                    verts, draws = paint_scene(b, os.path.join(args.out, "scene.png"))
                    log("scene #%d: %d bytes raw, %d verts in %d draws" % (state["scene"], len(b), verts, draws))
            elif t not in ("ping",):
                log("?", t, o)
    except (OSError, ValueError) as e:
        log("link error:", e)
    c.alive = False
    log("UNRENDERED went away")


threading.Thread(target=beacon, daemon=True).start()
srv = socket.socket(); srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
srv.bind(("0.0.0.0", args.port)); srv.listen(2)
log("fake Minecraft listening on", args.port)
if args.quit_after:
    threading.Timer(args.quit_after, lambda: os._exit(0)).start()
while True:
    s, a = srv.accept()
    threading.Thread(target=serve, args=(s, a), daemon=True).start()
