"""Builds the Dimension Hopper's look: its 3D item model (cuboid elements) and the texture
the model is painted with, plus the mod icon. Run from anywhere:

    python3 PortalMods/hopper/tools/make_hopper.py

The body texture is 32x32, laid out in 8x8-unit patches of model UV space (each unit is two
pixels): brushed metal, dark grip, glass, brass. The parts that move are animated textures of
their own (Minecraft plays a .png.mcmeta strip wherever the item is drawn — in a hand, in a
slot, on the ground):

  hopper_fluid        the vial: a rolling surface, bubbles rising, a swirl drifting through
  hopper_fluid_idle   the same liquid asleep — dark and slow, while the gun is not dialled
  hopper_fluid_surge  the same liquid as it fires — bright, fast, boiling
  hopper_glow         the emitter, breathing
  hopper_flash        the emitter at the instant of the shot
  hopper_screen       the readout on the back, scrolling

and four models: hopper (not dialled), hopper_ready (dialled), hopper_fire_kick and
hopper_fire_settle (the two halves of the recoil), chosen by item model predicates the client
sets from the stack and the gun's cooldown.
"""
import math
import json
import random
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent / "fabric/src/main/resources/assets/dimension_hopper"
random.seed(7)

# UV patches, in model units (0..16 across the 32px texture).
PATCH = {
    "metal": (0, 0, 8, 8),
    "dark": (8, 0, 16, 8),
    "fluid": (0, 8, 8, 16),
    "glass": (8, 8, 12, 12),
    "glow": (12, 8, 16, 12),
    "brass": (8, 12, 12, 16),
    "screen": (12, 12, 16, 16),
}


def texture():
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = img.load()

    def fill(name, base, jitter, alpha=255):
        x0, y0, x1, y1 = (v * 2 for v in PATCH[name])
        for y in range(y0, y1):
            for x in range(x0, x1):
                j = random.randint(-jitter, jitter)
                px[x, y] = tuple(max(0, min(255, c + j)) for c in base) + (alpha,)

    fill("metal", (176, 184, 190), 10)
    fill("dark", (44, 48, 54), 6)
    fill("fluid", (70, 235, 90), 18)
    fill("glass", (200, 245, 240), 6, alpha=55)
    fill("glow", (190, 255, 170), 10)
    fill("brass", (196, 150, 64), 12)
    fill("screen", (18, 60, 30), 6)

    d = ImageDraw.Draw(img)
    # Metal: a panel seam and a bright edge, so the body reads as machined.
    d.line([(0, 5), (15, 5)], fill=(120, 128, 136, 255))
    d.line([(0, 0), (15, 0)], fill=(222, 228, 232, 255))
    d.line([(10, 6), (10, 15)], fill=(132, 140, 148, 255))
    # Grip: ridges.
    for y in range(1, 16, 3):
        d.line([(16, y), (31, y)], fill=(30, 32, 36, 255))
    # Fluid: bright swirls.
    for _ in range(14):
        x, y = random.randint(0, 15), random.randint(16, 31)
        d.point((x, y), fill=(220, 255, 210, 255))
    d.arc([(2, 18), (13, 29)], 20, 300, fill=(160, 255, 150, 255))
    # Glow: a white-hot centre.
    d.rectangle([(27, 19), (28, 20)], fill=(255, 255, 255, 255))
    # Screen: a lit readout.
    d.line([(26, 26), (29, 26)], fill=(90, 255, 120, 255))
    d.line([(26, 28), (28, 28)], fill=(90, 255, 120, 255))
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


def face(patch, sides=SIDES):
    u0, v0, u1, v1 = PATCH[patch]
    return {s: {"uv": [u0, v0, u1, v1], "texture": "#body"} for s in sides}


def whole(tex, frm, to):
    """Faces painted from a 16x16 frame of an animated texture, cropped to each face's own
    proportions so the pixels stay square however long or squat the face is."""
    w, h, l = (to[i] - frm[i] for i in range(3))
    dims = {"north": (w, h), "south": (w, h), "east": (l, h), "west": (l, h), "up": (w, l), "down": (w, l)}
    faces = {}
    for side, (a, b) in dims.items():
        k = 16 / max(a, b)
        ua, vb = a * k, b * k
        u0, v0 = (16 - ua) / 2, (16 - vb) / 2
        faces[side] = {"uv": [round(u0, 3), round(v0, 3), round(u0 + ua, 3), round(v0 + vb, 3)], "texture": tex}
    return faces


def box(name, frm, to, patch, rotation=None):
    e = {"name": name, "from": frm, "to": to}
    e["faces"] = whole("#" + patch, frm, to) if patch in ("fluid", "glow", "screen") else face(patch)
    if rotation:
        e["rotation"] = rotation
    return e


FIRST_PERSON_R = {"rotation": [0, -82, 4], "translation": [1.5, 2, -1], "scale": [0.6, 0.6, 0.6]}
FIRST_PERSON_L = {"rotation": [0, 82, -4], "translation": [1.5, 2, -1], "scale": [0.6, 0.6, 0.6]}


def model(fluid="hopper_fluid", glow="hopper_glow", kick=0.0):
    """kick 0..1: how far into the recoil this pose is — the gun pulled back toward the eye,
    lifted, and rolled up off the line of fire."""
    # The barrel points north (-Z), the grip hangs below the back, the vial rides on top.
    elements = [
        box("body", [5.5, 7, 4], [10.5, 11, 13], "metal"),
        box("spine", [6.5, 11, 3.5], [9.5, 11.5, 12.5], "dark"),
        box("grip", [6.25, 1.5, 9.5], [9.75, 7, 12.75], "dark",
            {"origin": [8, 7, 11], "axis": "x", "angle": -22.5}),
        box("trigger guard", [7.5, 4.5, 7.5], [8.5, 7, 8], "dark"),
        box("trigger", [7.5, 5.5, 8.5], [8.5, 7, 9.25], "brass"),
        box("barrel", [6.5, 7.75, 1.5], [9.5, 10.25, 4], "metal"),
        box("emitter", [7, 8.25, 0.25], [9, 9.75, 1.5], "glow"),
        box("prong left", [5.75, 8.5, -0.5], [6.5, 9.5, 2.5], "dark"),
        box("prong right", [9.5, 8.5, -0.5], [10.25, 9.5, 2.5], "dark"),
        box("prong top", [7.5, 10.25, -0.5], [8.5, 11, 2.5], "dark"),
        box("vial glass", [6.25, 11.5, 5.5], [9.75, 15, 11], "glass"),
        box("vial fluid", [6.75, 11.75, 6], [9.25, 14.75, 10.5], "fluid"),
        box("vial cap front", [6, 11.25, 5], [10, 15.25, 5.5], "brass"),
        box("vial cap back", [6, 11.25, 11], [10, 15.25, 11.5], "brass"),
        box("dial", [10.5, 8, 8], [11.5, 10, 10], "brass"),
        box("dial ring", [10.5, 7.5, 7.5], [11, 10.5, 10.5], "dark"),
        box("screen", [6.25, 8, 13], [9.75, 10.5, 13.5], "screen"),
        box("fluid line", [5.25, 9.25, 4.5], [5.5, 10, 12.5], "fluid"),
    ]
    return {
        "credit": "Dimension Hopper, made by PortalMods/hopper/tools/make_hopper.py",
        "textures": {"body": "dimension_hopper:item/hopper", "particle": "dimension_hopper:item/hopper",
                     "fluid": "dimension_hopper:item/" + fluid, "glow": "dimension_hopper:item/" + glow,
                     "screen": "dimension_hopper:item/hopper_screen"},
        "elements": elements,
        "display": {
            # Side on in a slot, barrel to the right, tipped to show the vial.
            "gui": {"rotation": [15, -90, 0], "translation": [0, 0, 0], "scale": [0.85, 0.85, 0.85]},
            "ground": {"rotation": [0, 0, 0], "translation": [0, 2, 0], "scale": [0.5, 0.5, 0.5]},
            "fixed": {"rotation": [0, -90, 0], "translation": [0, 0, 0], "scale": [0.9, 0.9, 0.9]},
            "head": {"rotation": [0, 0, 0], "translation": [0, 13, 7], "scale": [1, 1, 1]},
            # Held like a pistol: barrel forward from the fist, vial up.
            "thirdperson_righthand": {"rotation": [90, 0, 0], "translation": [0, 1, -3], "scale": [0.6, 0.6, 0.6]},
            "thirdperson_lefthand": {"rotation": [90, 0, 0], "translation": [0, 1, -3], "scale": [0.6, 0.6, 0.6]},
            # Pointing into the screen, angled in toward the crosshair. (First person turns
            # an item a quarter round before these apply, hence the -82.)
            "firstperson_righthand": recoil(FIRST_PERSON_R, kick, 1),
            "firstperson_lefthand": recoil(FIRST_PERSON_L, kick, -1),
        },
    }


def recoil(pose, kick, hand):
    r, t = pose["rotation"], pose["translation"]
    return {"rotation": [r[0], r[1], r[2] + hand * 16 * kick],
            "translation": [t[0], t[1] + 1.2 * kick, t[2] + 2.5 * kick],
            "scale": pose["scale"]}


PREDICATE_DIALLED = "dimension_hopper:dialled"
PREDICATE_FIRE = "dimension_hopper:fire"


def models():
    base = model(fluid="hopper_fluid_idle")
    # Later overrides win: dialled wakes the liquid; a shot's cooldown plays the recoil, the
    # first half with the flash and the surge, the second settling back.
    base["overrides"] = [
        {"predicate": {PREDICATE_DIALLED: 1}, "model": "dimension_hopper:item/hopper_ready"},
        {"predicate": {PREDICATE_FIRE: 0.01}, "model": "dimension_hopper:item/hopper_fire_settle"},
        {"predicate": {PREDICATE_FIRE: 0.55}, "model": "dimension_hopper:item/hopper_fire_kick"},
    ]
    return {
        "hopper": base,
        "hopper_ready": model(),
        "hopper_fire_settle": model(fluid="hopper_fluid_surge", kick=0.35),
        "hopper_fire_kick": model(fluid="hopper_fluid_surge", glow="hopper_flash", kick=1.0),
    }


def icon():
    img = Image.new("RGBA", (128, 128), (10, 20, 12, 255))
    d = ImageDraw.Draw(img)
    for r, c in [(56, (40, 120, 50)), (46, (70, 200, 80)), (34, (120, 240, 120)), (20, (200, 255, 190))]:
        d.ellipse([(64 - r * 0.8, 64 - r), (64 + r * 0.8, 64 + r)], outline=c, width=6)
    return img


def main():
    (ROOT / "textures/item").mkdir(parents=True, exist_ok=True)
    (ROOT / "models/item").mkdir(parents=True, exist_ok=True)
    texture().save(ROOT / "textures/item/hopper.png")
    for name, (make, ticks) in ANIMATED.items():
        strip(make()).save(ROOT / f"textures/item/{name}.png")
        (ROOT / f"textures/item/{name}.png.mcmeta").write_text(
            json.dumps({"animation": {"frametime": ticks, "interpolate": False}}, indent=2) + "\n")
    for name, m in models().items():
        (ROOT / f"models/item/{name}.json").write_text(json.dumps(m, indent=2) + "\n")
    icon().save(ROOT / "icon.png")
    print("wrote", ROOT)


if __name__ == "__main__":
    main()
