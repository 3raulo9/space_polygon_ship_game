"""Builds the look of the Dimension Hopper mod: the two guns' 3D item models (cuboid elements)
after the reference — a dark-oak box chamber with iron brackets and bars over a glowing core,
three stacked iron barrels with glowing tips, an iron console with buttons and gauges, a
PORTAL FUEL canister cabled to the chamber, a wooden pistol grip — the parts they are crafted
from, the reinforced ender pearl's sprite, and the mod icon. Run from anywhere:

    python3 PortalMods/hopper/tools/make_hopper.py

The body texture is 128x128: a 4x4 grid of 32x32 patches (each 4x4 units of model UV space).
Faces sample their patch at two pixels per model unit, so every part has the same chunky
pixels; the PORTAL FUEL plate, the gauges, the buttons and the bolts are painted whole onto
their faces. The parts that move are animated textures of their own (Minecraft plays a
.png.mcmeta strip wherever the item is drawn — in a hand, in a slot, on the ground):

  hopper_fluid        the chamber's charge: a rolling surface, sparks rising, a swirl drifting
  hopper_fluid_idle   the same charge asleep — dark and slow, while the Hopper is not dialled
  hopper_fluid_surge  the same charge as it fires — bright, fast, boiling
  hopper_glow         the barrel tips, breathing
  hopper_flash        the barrel tips at the instant of the shot
  hopper_screen       the computer's readout, scrolling

Models: portal_gun (the "dumb" gun: always charged, no screen) and hopper (the same gun with a
computer's monitor bolted on, asleep until dialled), each with fire_settle / fire_kick variants
for the recoil — the gun kicks back and the barrel cluster turns a step about its own axis —
chosen by item model predicates the client sets from the stack and the gun's cooldown. And the
parts: portal_gun_handle, portal_chamber, monitor, dimensional_computer (3D, from the same
pieces), reinforced_ender_pearl (a sprite).
"""
import math
import json
import random
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent / "fabric/src/main/resources/assets/dimension_hopper"
random.seed(7)

PX = 8          # texture pixels per UV unit (128 px over 16 units)
DENSITY = 0.25  # UV units per model unit on ordinary faces: two pixels per model unit

# Patches, in UV units (x0, y0, x1, y1).
PATCH = {
    "wood": (0, 0, 4, 4),
    "iron": (4, 0, 8, 4),
    "dark_iron": (8, 0, 12, 4),
    "brass": (12, 0, 16, 4),
    "grip": (0, 4, 4, 8),
    "plate": (4, 4, 8, 6),
    "gauge": (4, 6, 6, 8),
    "button_green": (6, 6, 7, 7),
    "button_red": (7, 6, 8, 7),
    "bolt": (6, 7, 7, 8),
    "knob": (7, 7, 8, 8),
    "canister": (8, 4, 12, 8),
    "cable_red": (12, 4, 14, 8),
    "cable_green": (14, 4, 16, 8),
    "bars": (0, 8, 4, 12),
    "black": (4, 8, 8, 12),
    "console": (8, 8, 12, 12),
    "casing": (12, 8, 16, 12),
    "glass": (0, 12, 4, 16),
}
# Painted whole onto every face, whatever its size.
WHOLE = {"plate", "gauge", "button_green", "button_red", "bolt", "knob"}


# --- The body texture ---------------------------------------------------------------------

def noise(c, j, rnd=random):
    return tuple(max(0, min(255, v + rnd.randint(-j, j))) for v in c) + (255,)


def body_texture():
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    px = img.load()
    d = ImageDraw.Draw(img)

    def box(name):
        x0, y0, x1, y1 = PATCH[name]
        return x0 * PX, y0 * PX, x1 * PX, y1 * PX

    def wood(name, base, dark, light):
        x0, y0, x1, y1 = box(name)
        rnd = random.Random(name)
        for y in range(y0, y1):
            # Grain runs along the plank; every few rows a darker seam, with knots here and there.
            row = rnd.random()
            for x in range(x0, x1):
                c = base
                if (y - y0) % 8 == 7:
                    c = dark
                elif math.sin((x - x0) * 0.35 + row * 6) > 0.85:
                    c = light
                px[x, y] = noise(c, 6, rnd)
        for _ in range(3):
            kx, ky = rnd.randrange(x0 + 2, x1 - 3), rnd.randrange(y0 + 2, y1 - 3)
            d.ellipse([kx, ky, kx + 2, ky + 1], fill=dark + (255,))

    def metal(name, base, edge_light, edge_dark, rivets=True, j=7):
        x0, y0, x1, y1 = box(name)
        rnd = random.Random(name)
        for y in range(y0, y1):
            for x in range(x0, x1):
                px[x, y] = noise(base, j, rnd)
        if rivets:
            for ty in range(y0, y1, 16):
                for tx in range(x0, x1, 16):
                    d.line([tx, ty, tx + 15, ty], fill=edge_light + (255,))
                    d.line([tx, ty, tx, ty + 15], fill=edge_light + (255,))
                    d.line([tx, ty + 15, tx + 15, ty + 15], fill=edge_dark + (255,))
                    d.line([tx + 15, ty, tx + 15, ty + 15], fill=edge_dark + (255,))
                    for rx, ry in ((2, 2), (12, 2), (2, 12), (12, 12)):
                        d.rectangle([tx + rx, ty + ry, tx + rx + 1, ty + ry + 1], fill=edge_dark + (255,))
                        px[tx + rx, ty + ry] = edge_light + (255,)

    wood("wood", (74, 48, 25), (44, 27, 12), (98, 66, 36))
    wood("grip", (60, 38, 20), (34, 20, 9), (84, 56, 30))
    metal("iron", (150, 152, 156), (196, 198, 202), (92, 94, 98))
    metal("dark_iron", (58, 60, 64), (92, 94, 98), (30, 31, 34), rivets=False, j=5)
    metal("brass", (176, 136, 58), (226, 190, 110), (120, 88, 32), rivets=False)
    metal("console", (100, 103, 108), (140, 143, 148), (64, 66, 70))
    metal("bars", (78, 80, 84), (120, 122, 126), (44, 45, 48), rivets=False, j=4)
    metal("black", (26, 26, 28), (50, 50, 54), (12, 12, 14), rivets=False, j=3)
    metal("casing", (196, 190, 172), (226, 222, 206), (140, 134, 118))
    metal("glass", (190, 240, 230), (230, 255, 250), (150, 200, 190), rivets=False, j=4)

    # Dark iron: faint lengthwise seams, so a barrel reads as rolled steel.
    x0, y0, x1, y1 = box("dark_iron")
    for x in range(x0, x1, 5):
        d.line([x, y0, x, y1 - 1], fill=(44, 46, 50, 255))

    # The canister: a drum, shaded across so it reads round.
    x0, y0, x1, y1 = box("canister")
    for x in range(x0, x1):
        k = 0.55 + 0.45 * math.sin(math.pi * (x - x0) / (x1 - x0))
        for y in range(y0, y1):
            px[x, y] = noise(tuple(round(v * k) for v in (150, 156, 162)), 5)
    for y in (y0 + 3, y1 - 4):
        d.line([x0, y, x1 - 1, y], fill=(80, 84, 88, 255))

    # Cables: braided, red and green.
    for name, a, b in (("cable_red", (150, 34, 30), (96, 18, 16)), ("cable_green", (44, 130, 52), (22, 80, 30))):
        x0, y0, x1, y1 = box(name)
        for y in range(y0, y1):
            for x in range(x0, x1):
                px[x, y] = noise(a if (x + y) % 4 < 2 else b, 6)

    # The plate: PORTAL / FUEL in black on beige, edged in brown, two screws.
    x0, y0, x1, y1 = box("plate")
    d.rectangle([x0, y0, x1 - 1, y1 - 1], fill=(206, 190, 140, 255))
    d.rectangle([x0, y0, x1 - 1, y1 - 1], outline=(118, 96, 56, 255))
    d.rectangle([x0 + 1, y0 + 1, x1 - 2, y1 - 2], outline=(176, 158, 108, 255))
    text(d, "PORTAL", x0 + 4, y0 + 2)
    text(d, "FUEL", x0 + 8, y0 + 9)

    # The gauge: a white face in a black ring, ticks, a red needle.
    x0, y0, x1, y1 = box("gauge")
    d.rectangle([x0, y0, x1 - 1, y1 - 1], fill=(70, 72, 76, 255))
    d.ellipse([x0 + 1, y0 + 1, x1 - 2, y1 - 2], fill=(20, 20, 22, 255))
    d.ellipse([x0 + 2, y0 + 2, x1 - 3, y1 - 3], fill=(236, 232, 220, 255))
    cx, cy = x0 + 7.5, y0 + 7.5
    for a in range(-120, 121, 40):
        r = math.radians(a - 90)
        d.point((round(cx + math.cos(r) * 5), round(cy + math.sin(r) * 5)), fill=(30, 30, 30, 255))
    r = math.radians(30 - 90)
    d.line([round(cx), round(cy), round(cx + math.cos(r) * 5), round(cy + math.sin(r) * 5)], fill=(200, 30, 30, 255))

    def button(name, c, hi):
        x0, y0, x1, y1 = box(name)
        d.rectangle([x0, y0, x1 - 1, y1 - 1], fill=(40, 40, 44, 255))
        d.rectangle([x0 + 1, y0 + 1, x1 - 2, y1 - 2], fill=c + (255,))
        d.rectangle([x0 + 2, y0 + 2, x0 + 3, y0 + 3], fill=hi + (255,))

    button("button_green", (40, 170, 60), (150, 255, 160))
    button("button_red", (196, 40, 40), (255, 160, 150))
    # A bolt: an iron head with a slot.
    x0, y0, x1, y1 = box("bolt")
    d.rectangle([x0, y0, x1 - 1, y1 - 1], fill=(110, 112, 116, 255))
    d.ellipse([x0 + 1, y0 + 1, x1 - 2, y1 - 2], fill=(160, 162, 166, 255))
    d.line([x0 + 2, y0 + 5, x1 - 3, y0 + 2], fill=(70, 72, 76, 255))
    # A brass knob.
    x0, y0, x1, y1 = box("knob")
    d.rectangle([x0, y0, x1 - 1, y1 - 1], fill=(150, 112, 44, 255))
    d.ellipse([x0 + 1, y0 + 1, x1 - 2, y1 - 2], fill=(210, 170, 90, 255))
    return img


# A 3x5 pixel font, just the letters the plate needs.
FONT = {
    "P": ["111", "101", "111", "100", "100"],
    "O": ["111", "101", "101", "101", "111"],
    "R": ["110", "101", "110", "101", "101"],
    "T": ["111", "010", "010", "010", "010"],
    "A": ["010", "101", "111", "101", "101"],
    "L": ["100", "100", "100", "100", "111"],
    "F": ["111", "100", "110", "100", "100"],
    "U": ["101", "101", "101", "101", "111"],
    "E": ["111", "100", "110", "100", "111"],
}


def text(d, s, x, y):
    for i, ch in enumerate(s):
        for r, row in enumerate(FONT[ch]):
            for c, bit in enumerate(row):
                if bit == "1":
                    d.point((x + i * 4 + c, y + r), fill=(20, 16, 10, 255))


def pearl_sprite():
    """The reinforced ender pearl: an ender pearl caged in riveted iron bands."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    px = img.load()
    for y in range(16):
        for x in range(16):
            r = math.hypot(x - 7.5, y - 7.5)
            if r < 6.2:
                k = 1 - r / 7
                px[x, y] = (round(12 + 30 * k), round(70 + 110 * k), round(60 + 90 * k), 255)
            if r < 2.2:
                px[x, y] = (150, 240, 210, 255)
    d = ImageDraw.Draw(img)
    for c in ((7, 2, 8, 13), (2, 7, 13, 8)):
        d.rectangle(c, fill=(150, 152, 158, 255))
    d.rectangle((5, 5, 10, 10), outline=(110, 112, 118, 255))
    for p in ((7, 2), (2, 7), (13, 7), (7, 13), (7, 7)):
        px[p] = (220, 222, 228, 255)
    d.rectangle((1, 1, 14, 14), outline=None)
    return img


# --- Animated textures: each a vertical strip of 16x16 frames that loops seamlessly ---------

def fluid_frames(n, speed, bright, bubbles, still=False):
    """The vial's liquid, pixel by pixel. Everything that moves is periodic in n frames, so the
    strip loops without a seam: the surface's wave, each bubble's climb, the swirl's drift."""
    rnd = random.Random(11)
    seeds = [(rnd.randrange(16), rnd.randrange(16), rnd.choice((1, 2))) for _ in range(bubbles)]
    frames = []
    for f in range(n):
        t = f / n
        img = Image.new("RGBA", (16, 16))
        px = img.load()
        for x in range(16):
            amp = 0.6 if still else 1.3
            surface = 3 + round(amp * math.sin(2 * math.pi * (x / 16 * 2 + t * speed)))
            for y in range(16):
                if y < surface:
                    # Above the liquid: glowing vapour, a shade off the glass.
                    px[x, y] = shade((150, 255, 170), (0.32 + 0.05 * y) * bright)
                    continue
                depth = max(0, y - 2) / 13   # by height in the vial, not under the wave: no stripes
                base = mix((110, 255, 120), (14, 92, 40), depth)
                # A swirl: a bright diagonal band drifting through the liquid.
                band = (x + y * 2 - t * speed * 16 * 2) % 16
                if band < 1.2:
                    base = mix(base, (190, 255, 190), 0.55)
                elif band < 2.2:
                    base = mix(base, (150, 255, 160), 0.25)
                px[x, y] = shade(base, bright)
            # The surface line: a bright lip of foam.
            if 0 <= surface < 16:
                px[x, surface] = shade((210, 255, 215), bright)
        for bx, by, size in seeds:
            # Up one pixel at a time, round and round the strip: speed * 16 pixels per loop.
            y = int((by - t * 16 * max(1, round(speed)))) % 16
            x = (bx + (1 if (f // 3) % 2 == 0 and size == 2 else 0)) % 16
            for dx in range(size):
                for dy in range(size):
                    xx, yy = (x + dx) % 16, y + dy
                    if 0 <= yy < 16:
                        px[xx, yy] = shade((225, 255, 225), bright)
        frames.append(img)
    return frames


def glow_frames(n, flash=False):
    frames = []
    for f in range(n):
        k = 1.0 if flash else 0.62 + 0.38 * (0.5 + 0.5 * math.sin(2 * math.pi * f / n))
        img = Image.new("RGBA", (16, 16))
        px = img.load()
        for y in range(16):
            for x in range(16):
                r = math.hypot(x - 7.5, y - 7.5) / 10.6
                core = mix((255, 255, 255), (60, 220, 90), min(1, r * (0.7 if flash else 1.25)))
                px[x, y] = shade(core, k)
        frames.append(img)
    return frames


def screen_frames(n):
    frames = []
    rnd = random.Random(5)
    rows = [rnd.randrange(3, 14) for _ in range(16)]
    for f in range(n):
        img = Image.new("RGBA", (16, 16), (12, 40, 20, 255))
        d = ImageDraw.Draw(img)
        for i in range(5):
            row = (i * 3 + f) % 16
            w = rows[(i + f) % 16]
            d.line([(1, row), (1 + w, row)], fill=(90, 255, 120, 255))
        if f % 4 < 2:
            d.point((14, 13), fill=(255, 255, 255, 255))   # the blinking cursor
        frames.append(img)
    return frames


def strip(frames):
    img = Image.new("RGBA", (16, 16 * len(frames)))
    for i, f in enumerate(frames):
        img.paste(f, (0, 16 * i))
    return img


def mix(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def shade(c, k):
    return tuple(max(0, min(255, round(v * k))) for v in c[:3]) + (255,)


ANIMATED = {
    # name: (frames, ticks per frame)
    "hopper_fluid": (lambda: fluid_frames(32, 1, 1.0, 6), 2),
    "hopper_fluid_idle": (lambda: fluid_frames(32, 1, 0.55, 2, still=True), 4),
    "hopper_fluid_surge": (lambda: fluid_frames(16, 2, 1.25, 12), 1),
    "hopper_glow": (lambda: glow_frames(16), 2),
    "hopper_flash": (lambda: glow_frames(2, flash=True), 1),
    "hopper_screen": (lambda: screen_frames(16), 3),
}


# --- The model -----------------------------------------------------------------------------

SIDES = ("north", "south", "east", "west", "up", "down")
ANIMATED_PARTS = ("fluid", "glow", "screen")


def dims(frm, to):
    w, h, l = (to[i] - frm[i] for i in range(3))
    return {"north": (w, h), "south": (w, h), "east": (l, h), "west": (l, h), "up": (w, l), "down": (w, l)}


def face(patch, frm, to, salt=0):
    """Faces from a patch of the body texture: whole for the painted details, otherwise
    sampled at two pixels per model unit from a spot in the patch that varies element to
    element, so neighbouring planks do not repeat."""
    u0, v0, u1, v1 = PATCH[patch]
    faces = {}
    for i, (side, (a, b)) in enumerate(dims(frm, to).items()):
        if patch in WHOLE:
            faces[side] = {"uv": [u0, v0, u1, v1], "texture": "#body"}
            continue
        ua, vb = min(u1 - u0, a * DENSITY), min(v1 - v0, b * DENSITY)
        rnd = random.Random(salt * 7 + i)
        ou = round(rnd.uniform(0, (u1 - u0) - ua) * 4) / 4
        ov = round(rnd.uniform(0, (v1 - v0) - vb) * 4) / 4
        faces[side] = {"uv": [round(u0 + ou, 3), round(v0 + ov, 3), round(u0 + ou + ua, 3), round(v0 + ov + vb, 3)],
                       "texture": "#body"}
    return faces


def whole(tex, frm, to):
    """Faces painted from a 16x16 frame of an animated texture, cropped to each face's own
    proportions so the pixels stay square however long or squat the face is."""
    faces = {}
    for side, (a, b) in dims(frm, to).items():
        k = 16 / max(a, b, 1e-6)
        ua, vb = a * k, b * k
        u0, v0 = (16 - ua) / 2, (16 - vb) / 2
        faces[side] = {"uv": [round(u0, 3), round(v0, 3), round(u0 + ua, 3), round(v0 + vb, 3)], "texture": tex}
    return faces


_salt = [0]


def box(name, frm, to, patch, rotation=None):
    _salt[0] += 1
    e = {"name": name, "from": [round(v, 3) for v in frm], "to": [round(v, 3) for v in to]}
    e["faces"] = whole("#" + patch, frm, to) if patch in ANIMATED_PARTS else face(patch, frm, to, _salt[0])
    if rotation:
        e["rotation"] = rotation
    return e


def shifted(elements, dx, dy, dz):
    """The same parts moved, for a part shown on its own centred in its block."""
    out = []
    for e in elements:
        e = json.loads(json.dumps(e))
        e["from"] = [round(e["from"][0] + dx, 3), round(e["from"][1] + dy, 3), round(e["from"][2] + dz, 3)]
        e["to"] = [round(e["to"][0] + dx, 3), round(e["to"][1] + dy, 3), round(e["to"][2] + dz, 3)]
        if "rotation" in e:
            o = e["rotation"]["origin"]
            e["rotation"]["origin"] = [round(o[0] + dx, 3), round(o[1] + dy, 3), round(o[2] + dz, 3)]
        out.append(e)
    return out


# The gun lies along Z: barrels at the north end (-Z), the grip under the back. The near side in
# first person (and the face a slot shows) is -X: the buttons, the gauges, the plate and the
# computer's monitor are on it.

def chamber():
    """The wooden box the charge burns in: oak frame, iron bars over the glow, a bracket and a
    bolt at every corner."""
    els = [
        box("charge", [5, 7, 9], [11, 12, 17], "fluid"),
        box("front plate", [4, 6, 8], [12, 13, 9], "wood"),
        box("back plate", [4, 6, 17], [12, 13, 18], "wood"),
        box("floor", [4, 6, 9], [12, 7, 17], "wood"),
        box("rail top left", [4, 12, 9], [5, 13, 17], "wood"),
        box("rail top right", [11, 12, 9], [12, 13, 17], "wood"),
    ]
    for x0, x1 in ((4.3, 4.8), (11.2, 11.7)):
        side = "left" if x0 < 8 else "right"
        els += [
            box(f"bar {side} a", [x0, 7, 11.25], [x1, 12, 11.75], "bars"),
            box(f"bar {side} b", [x0, 7, 14.25], [x1, 12, 14.75], "bars"),
            box(f"strap {side}", [x0 - 0.1, 9.25, 9], [x1 + 0.1, 9.75, 17], "iron"),
        ]
    els += [
        box("bar top a", [5, 12.2, 11.25], [11, 12.7, 11.75], "bars"),
        box("bar top b", [5, 12.2, 14.25], [11, 12.7, 14.75], "bars"),
    ]
    for x in (3.7, 10.7):
        for y in (5.7, 11.7):
            for z in (7.7, 16.7):
                els.append(box("bracket", [x, y, z], [x + 1.6, y + 1.6, z + 1.6], "bolt"))
    return els


SPIN_ORIGIN = [8, 9.5, 0]


def barrels(spin=0.0):
    """Three stacked iron barrels through a clamp, glowing at the tips. When it fires the
    cluster turns a step about its own axis (spin is the element rotation, in degrees)."""
    rot = {"origin": SPIN_ORIGIN, "axis": "z", "angle": spin} if spin else None
    els = [
        box("hub", [5.5, 7, 6.5], [10.5, 12, 8], "dark_iron"),
        box("flange", [5, 6.5, 7.2], [11, 12.5, 7.8], "iron"),
    ]
    for name, y0 in (("top", 10.9), ("middle", 8.7), ("bottom", 6.5)):
        els.append(box(f"barrel {name}", [7.2, y0, -4], [8.8, y0 + 1.6, 6.5], "dark_iron", rot))
        els.append(box(f"tip {name}", [7.05, y0 - 0.15, -5], [8.95, y0 + 1.75, -4], "glow", rot))
    els.append(box("barrel band", [6.6, 6.0, 0], [9.4, 13.0, 1.5], "iron", rot))
    els.append(box("band bolt", [6.4, 9, 0.3], [6.6, 10, 1.2], "bolt", rot))
    return els


def console():
    return [
        box("console", [5.5, 6, 18], [10.5, 11, 21], "console"),
        box("console base", [5.5, 5, 18], [10.5, 6, 21.5], "wood"),
        box("button green", [5.1, 9, 18.5], [5.5, 10, 19.5], "button_green"),
        box("button red", [5.1, 9, 19.8], [5.5, 10, 20.8], "button_red"),
        box("side gauge", [5.25, 6.8, 18.5], [5.5, 8.6, 20.3], "gauge"),
        box("back gauge", [6.5, 7.2, 21], [9.5, 10.2, 21.3], "gauge"),
        box("knob", [9.8, 9.6, 21], [10.4, 10.2, 21.4], "knob"),
    ]


def canister():
    return [
        box("canister", [5.5, 13, 18], [10.5, 17, 22.5], "canister"),
        box("plate left", [5.3, 13.8, 18.3], [5.5, 15.75, 22.2], "plate"),
        box("plate right", [10.5, 13.8, 18.3], [10.7, 15.75, 22.2], "plate"),
        box("cap", [7, 17, 19.5], [9, 17.6, 21], "brass"),
        box("feed pipe", [7.5, 12.5, 17.4], [8.5, 13.5, 18.4], "brass"),
        box("cable red up", [4.6, 12.5, 15.3], [5.1, 15.5, 15.8], "cable_red"),
        box("cable red over", [4.6, 15.5, 15.3], [5.4, 16, 18.6], "cable_red"),
        box("cable green up", [10.9, 12.5, 15.3], [11.4, 15.5, 15.8], "cable_green"),
        box("cable green over", [10.6, 15.5, 15.3], [11.4, 16, 18.2], "cable_green"),
    ]


GRIP_TILT = {"origin": [8, 6, 20], "axis": "x", "angle": -22.5}


def handle():
    return [
        box("grip", [6.5, -1, 18.5], [9.5, 6, 21.5], "grip", GRIP_TILT),
        box("grip plate", [6.3, 0, 19], [6.5, 5, 21], "iron", GRIP_TILT),
        box("grip butt", [6.3, -1.6, 18.3], [9.7, -0.6, 21.7], "iron", GRIP_TILT),
        box("grip bolt", [6.1, 3.6, 19.6], [6.3, 4.6, 20.6], "bolt", GRIP_TILT),
        box("trigger", [7.6, 3.6, 16.8], [8.4, 6, 17.6], "black"),
        box("trigger guard", [7.6, 2.9, 16], [8.4, 3.4, 18.6], "iron"),
        box("guard front", [7.6, 3.4, 16], [8.4, 6, 16.5], "iron"),
    ]


def computer_monitor():
    """The computer's monitor, bolted to the near side of the chamber."""
    return [
        box("monitor", [2, 8, 9.5], [3.8, 12.5, 15.5], "casing"),
        box("monitor screen", [1.8, 8.5, 10], [2, 12, 15], "screen"),
        box("monitor arm", [3.6, 9.5, 11.5], [4.3, 11, 13.5], "iron"),
        box("monitor bolt", [1.9, 11.7, 9.6], [2, 12.4, 10.3], "bolt"),
    ]


def gun(computer, spin=0.0):
    _salt[0] = 0
    els = chamber() + barrels(spin) + console() + canister() + handle()
    if computer:
        els += computer_monitor()
    return els


FIRST_PERSON_R = {"rotation": [0, -38, 4], "translation": [2.5, 3.2, -2.5], "scale": [0.42, 0.42, 0.42]}
FIRST_PERSON_L = {"rotation": [0, 38, -4], "translation": [2.5, 3.2, -2.5], "scale": [0.42, 0.42, 0.42]}


def recoil(pose, kick, hand):
    r, t = pose["rotation"], pose["translation"]
    return {"rotation": [r[0], r[1], r[2] + hand * 16 * kick],
            "translation": [t[0], t[1] + 1.2 * kick, t[2] + 2.5 * kick],
            "scale": pose["scale"]}


def gun_model(computer, fluid="hopper_fluid", glow="hopper_glow", kick=0.0, spin=0.0):
    """kick 0..1: how far into the recoil this pose is — the gun pulled back toward the eye,
    lifted, and rolled up off the line of fire."""
    return {
        "credit": "Dimension Hopper, made by PortalMods/hopper/tools/make_hopper.py",
        "texture_size": [128, 128],
        "textures": textures(fluid, glow),
        "elements": gun(computer, spin),
        "display": {
            # Side on in a slot, barrel to the left, the near side with its plate and buttons.
            "gui": {"rotation": [20, 130, 0], "translation": [0, 0, 0], "scale": [0.52, 0.52, 0.52]},
            "ground": {"rotation": [0, 0, 0], "translation": [0, 3, 0], "scale": [0.3, 0.3, 0.3]},
            "fixed": {"rotation": [0, 90, 0], "translation": [0, 0, 0], "scale": [0.55, 0.55, 0.55]},
            "head": {"rotation": [0, 0, 0], "translation": [0, 13, 7], "scale": [0.6, 0.6, 0.6]},
            # Held like a pistol: barrels forward from the fist.
            "thirdperson_righthand": {"rotation": [90, 0, 0], "translation": [0, 3, -1], "scale": [0.38, 0.38, 0.38]},
            "thirdperson_lefthand": {"rotation": [90, 0, 0], "translation": [0, 3, -1], "scale": [0.38, 0.38, 0.38]},
            # Pointing into the screen, angled a touch in toward the crosshair.
            "firstperson_righthand": recoil(FIRST_PERSON_R, kick, 1),
            "firstperson_lefthand": recoil(FIRST_PERSON_L, kick, -1),
        },
    }


def textures(fluid="hopper_fluid", glow="hopper_glow"):
    return {"body": "dimension_hopper:item/hopper", "particle": "dimension_hopper:item/hopper",
            "fluid": "dimension_hopper:item/" + fluid, "glow": "dimension_hopper:item/" + glow,
            "screen": "dimension_hopper:item/hopper_screen"}


def part_model(elements, gui_scale, gui_rot=(20, 130, 0)):
    return {
        "credit": "Dimension Hopper, made by PortalMods/hopper/tools/make_hopper.py",
        "texture_size": [128, 128],
        "textures": textures(),
        "elements": elements,
        "display": {
            "gui": {"rotation": list(gui_rot), "translation": [0, 0, 0], "scale": [gui_scale] * 3},
            "ground": {"rotation": [0, 0, 0], "translation": [0, 3, 0], "scale": [0.4] * 3},
            "fixed": {"rotation": [0, 90, 0], "translation": [0, 0, 0], "scale": [0.7] * 3},
            "thirdperson_righthand": {"rotation": [75, 45, 0], "translation": [0, 2.5, 0], "scale": [0.45] * 3},
            "thirdperson_lefthand": {"rotation": [75, 45, 0], "translation": [0, 2.5, 0], "scale": [0.45] * 3},
            "firstperson_righthand": {"rotation": [0, 45, 0], "translation": [0, 2, 0], "scale": [0.5] * 3},
            "firstperson_lefthand": {"rotation": [0, 225, 0], "translation": [0, 2, 0], "scale": [0.5] * 3},
        },
    }


def computer_parts():
    """The computer: a beige box with a slot and two lights, its monitor on top."""
    return [
        box("case", [1, 0, 3], [15, 5, 13], "casing"),
        box("slot", [3, 2, 2.8], [8, 2.6, 3], "black"),
        box("light red", [11, 2, 2.8], [12, 3, 3], "button_red"),
        box("light green", [12.5, 2, 2.8], [13.5, 3, 3], "button_green"),
        box("neck", [6.5, 5, 6.5], [9.5, 6, 9.5], "iron"),
        box("screen case", [3, 6, 5], [13, 14, 10], "casing"),
        box("screen", [4, 7, 4.8], [12, 13, 5], "screen"),
    ]


def monitor_parts():
    return [
        box("stand", [6, 0, 7], [10, 2, 9], "iron"),
        box("neck", [7, 2, 7.5], [9, 3.5, 8.5], "iron"),
        box("case", [2, 3.5, 6], [14, 13, 10], "casing"),
        box("screen", [3, 4.5, 5.8], [13, 12, 6], "screen"),
        box("bolt left", [2.2, 12, 5.8], [3, 12.8, 6], "bolt"),
        box("bolt right", [13, 12, 5.8], [13.8, 12.8, 6], "bolt"),
    ]


PREDICATE_DIALLED = "dimension_hopper:dialled"
PREDICATE_FIRE = "dimension_hopper:fire"


def models():
    out = {}
    # The Hopper: asleep until dialled. Later overrides win: dialled wakes the charge; a shot's
    # cooldown plays the recoil, the first half with the flash and the surge, the second
    # settling back.
    hopper = gun_model(True, fluid="hopper_fluid_idle")
    hopper["overrides"] = [
        {"predicate": {PREDICATE_DIALLED: 1}, "model": "dimension_hopper:item/hopper_ready"},
        {"predicate": {PREDICATE_FIRE: 0.01}, "model": "dimension_hopper:item/hopper_fire_settle"},
        {"predicate": {PREDICATE_FIRE: 0.55}, "model": "dimension_hopper:item/hopper_fire_kick"},
    ]
    out["hopper"] = hopper
    out["hopper_ready"] = gun_model(True)
    out["hopper_fire_settle"] = gun_model(True, fluid="hopper_fluid_surge", kick=0.35, spin=22.5)
    out["hopper_fire_kick"] = gun_model(True, fluid="hopper_fluid_surge", glow="hopper_flash", kick=1.0, spin=45)
    # The dumb gun: always charged, no screen.
    dumb = gun_model(False)
    dumb["overrides"] = [
        {"predicate": {PREDICATE_FIRE: 0.01}, "model": "dimension_hopper:item/portal_gun_fire_settle"},
        {"predicate": {PREDICATE_FIRE: 0.55}, "model": "dimension_hopper:item/portal_gun_fire_kick"},
    ]
    out["portal_gun"] = dumb
    out["portal_gun_fire_settle"] = gun_model(False, fluid="hopper_fluid_surge", kick=0.35, spin=22.5)
    out["portal_gun_fire_kick"] = gun_model(False, fluid="hopper_fluid_surge", glow="hopper_flash", kick=1.0, spin=45)
    # The parts, each centred in its own block.
    _salt[0] = 100
    out["portal_gun_handle"] = part_model(shifted(handle(), 0, 4.5, -12), 0.95)
    _salt[0] = 200
    out["portal_chamber"] = part_model(shifted(chamber(), 0, -1.5, -5), 0.8)
    _salt[0] = 300
    out["monitor"] = part_model(monitor_parts(), 0.75, (20, 160, 0))
    _salt[0] = 400
    out["dimensional_computer"] = part_model(computer_parts(), 0.75, (20, 160, 0))
    out["reinforced_ender_pearl"] = {"parent": "minecraft:item/generated",
                                     "textures": {"layer0": "dimension_hopper:item/reinforced_ender_pearl"}}
    return out


def effect_icon():
    """Unstable teleportation's icon by the hotbar: a figure with an arm flying off it."""
    img = Image.new("RGBA", (18, 18), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    purple, dark, red = (150, 60, 230, 255), (60, 20, 100, 255), (190, 20, 20, 255)
    d.rectangle((7, 2, 10, 5), fill=purple)           # head
    d.rectangle((7, 6, 10, 11), fill=purple)          # body
    d.rectangle((11, 6, 12, 10), fill=purple)         # the arm still on
    d.rectangle((7, 12, 8, 16), fill=purple)          # legs
    d.rectangle((9, 12, 10, 16), fill=dark)
    d.rectangle((2, 3, 3, 7), fill=dark)              # the arm already gone
    for p in ((4, 5), (5, 6), (3, 9), (5, 9), (6, 7)):
        d.point(p, fill=red)
    for p in ((1, 2), (4, 1), (0, 6), (13, 14), (15, 12)):
        d.point(p, fill=(200, 150, 255, 255))
    return img


def icon():
    img = Image.new("RGBA", (128, 128), (10, 20, 12, 255))
    d = ImageDraw.Draw(img)
    for r, c in [(56, (40, 120, 50)), (46, (70, 200, 80)), (34, (120, 240, 120)), (20, (200, 255, 190))]:
        d.ellipse([(64 - r * 0.8, 64 - r), (64 + r * 0.8, 64 + r)], outline=c, width=6)
    return img


def main():
    (ROOT / "textures/item").mkdir(parents=True, exist_ok=True)
    (ROOT / "models/item").mkdir(parents=True, exist_ok=True)
    body_texture().save(ROOT / "textures/item/hopper.png")
    pearl_sprite().save(ROOT / "textures/item/reinforced_ender_pearl.png")
    for name, (make, ticks) in ANIMATED.items():
        strip(make()).save(ROOT / f"textures/item/{name}.png")
        (ROOT / f"textures/item/{name}.png.mcmeta").write_text(
            json.dumps({"animation": {"frametime": ticks, "interpolate": False}}, indent=2) + "\n")
    for name, m in models().items():
        (ROOT / f"models/item/{name}.json").write_text(json.dumps(m, indent=2) + "\n")
    icon().save(ROOT / "icon.png")
    (ROOT / "textures/mob_effect").mkdir(parents=True, exist_ok=True)
    effect_icon().save(ROOT / "textures/mob_effect/unstable_teleportation.png")
    print("wrote", ROOT)


if __name__ == "__main__":
    main()
