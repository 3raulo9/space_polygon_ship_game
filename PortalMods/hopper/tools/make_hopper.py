"""Builds the Dimension Hopper's look: its 3D item model (cuboid elements) and the texture
the model is painted with, plus the mod icon. Run from anywhere:

    python3 PortalMods/hopper/tools/make_hopper.py

The texture is 32x32, laid out in eight 8x8-unit patches of model UV space (each unit is two
pixels): brushed metal, dark grip, green fluid, glass, the emitter's glow, brass, a little
screen, and black. Every element of the model takes its faces from one patch.
"""
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
    fill("glass", (190, 240, 235), 8, alpha=110)
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


def face(patch, sides=("north", "south", "east", "west", "up", "down"), tint=None):
    u0, v0, u1, v1 = PATCH[patch]
    return {s: {"uv": [u0, v0, u1, v1], "texture": "#body"} for s in sides}


def box(name, frm, to, patch, rotation=None):
    e = {"name": name, "from": frm, "to": to, "faces": face(patch)}
    if rotation:
        e["rotation"] = rotation
    return e


def model():
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
        box("vial glass", [6.5, 11.5, 5.5], [9.5, 14.5, 11], "glass"),
        box("vial fluid", [7, 11.75, 6], [9, 14, 10.5], "fluid"),
        box("vial cap front", [6.25, 11.25, 5], [9.75, 14.75, 5.5], "brass"),
        box("vial cap back", [6.25, 11.25, 11], [9.75, 14.75, 11.5], "brass"),
        box("dial", [10.5, 8, 8], [11.5, 10, 10], "brass"),
        box("dial ring", [10.5, 7.5, 7.5], [11, 10.5, 10.5], "dark"),
        box("screen", [6.25, 8, 13], [9.75, 10.5, 13.5], "screen"),
        box("fluid line", [5.25, 9.25, 4.5], [5.5, 10, 12.5], "fluid"),
    ]
    return {
        "credit": "Dimension Hopper, made by PortalMods/hopper/tools/make_hopper.py",
        "textures": {"body": "dimension_hopper:item/hopper", "particle": "dimension_hopper:item/hopper"},
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
            # Pointing into the screen, angled in toward the crosshair.
            "firstperson_righthand": {"rotation": [0, 8, 0], "translation": [2, 1, -2], "scale": [0.75, 0.75, 0.75]},
            "firstperson_lefthand": {"rotation": [0, -8, 0], "translation": [2, 1, -2], "scale": [0.75, 0.75, 0.75]},
        },
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
    (ROOT / "models/item/hopper.json").write_text(json.dumps(model(), indent=2) + "\n")
    icon().save(ROOT / "icon.png")
    print("wrote", ROOT)


if __name__ == "__main__":
    main()
