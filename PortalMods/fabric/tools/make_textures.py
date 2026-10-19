"""Draws THE RIFT's item sprites: UNRENDERED's salvage as 16x16 Minecraft items.

Each is a few flat-shaded facets in the colour UNRENDERED gives that salvage on the grid
(World.SalvageColour / Palette.cs), with a dark rim, so they read as the same objects
seen through Minecraft's eyes. Run from anywhere:

    python3 PortalMods/fabric/tools/make_textures.py
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent / "src/main/resources/assets/unrendered_portal"
OUT = ROOT / "textures/item"

PALETTE = {
    "scrap_metal": (122, 133, 140),
    "copper_wire": (160, 113, 63),
    "space_gunpowder": (110, 92, 134),
    "dense_alloy": (82, 92, 110),
    "lead": (78, 83, 88),
    "zinc": (147, 168, 180),
    "lithium": (176, 140, 168),
    "battery": (90, 230, 200),
    "bullet": (201, 178, 58),
    "crab_fragment": (255, 40, 40),
    "crab_core": (230, 60, 200),
    "repair_kit": (138, 160, 168),
    "moonstone": (176, 190, 198),
    "sun_fragment": (226, 196, 150),
    "moon_fragment": (220, 230, 236),
}


def shade(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c) + (255,)


def facets(d, polys, base):
    # UNRENDERED's three light bands: 0.45, 0.63, 0.82 of the base, and a lit top facet.
    bands = [1.0, 0.82, 0.63, 0.45]
    for i, p in enumerate(polys):
        d.polygon(p, fill=shade(base, bands[min(i, 3)]))


def outline(img):
    """A one-pixel dark rim around everything drawn, the way Minecraft items sit on a slot."""
    src = img.copy()
    px, out = src.load(), img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            if px[x, y][3]:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and px[nx, ny][3] and px[nx, ny][:3] != (12, 10, 18):
                    out[x, y] = (12, 10, 18, 255)
                    break


def draw(name):
    c = PALETTE[name]
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if name == "scrap_metal":
        facets(d, [[(3, 6), (9, 3), (13, 6), (8, 8)], [(3, 6), (8, 8), (7, 13), (2, 10)],
                   [(8, 8), (13, 6), (12, 11), (7, 13)]], c)
        d.point([(10, 9), (5, 10)], fill=shade(c, 0.3))
    elif name == "copper_wire":
        for i, y in enumerate(range(3, 13, 2)):
            d.ellipse([3, y, 12, y + 3], outline=shade(c, 1.0 if i % 2 else 0.75))
        d.line([(12, 4), (14, 2)], fill=shade(c, 1.1))
    elif name == "space_gunpowder":
        facets(d, [[(8, 4), (13, 12), (8, 13)], [(8, 4), (8, 13), (3, 12)]], c)
        for p in [(6, 9), (9, 7), (10, 11), (7, 12), (11, 9)]:
            d.point(p, fill=(220, 200, 255, 255))
    elif name == "dense_alloy":
        facets(d, [[(3, 7), (10, 5), (14, 7), (7, 9)], [(3, 7), (7, 9), (7, 12), (3, 10)],
                   [(7, 9), (14, 7), (14, 10), (7, 12)]], c)
    elif name in ("lead", "zinc", "lithium"):
        facets(d, [[(5, 5), (10, 4), (12, 8), (7, 8)], [(5, 5), (7, 8), (6, 12), (3, 9)],
                   [(7, 8), (12, 8), (10, 12), (6, 12)]], c)
    elif name == "battery":
        body = (40, 120, 110)
        facets(d, [[(5, 3), (11, 3), (11, 13), (5, 13)]], body)
        d.rectangle([6, 2, 10, 2], fill=shade((138, 160, 168), 1.0))
        d.rectangle([7, 5, 9, 11], fill=c + (255,))
        d.point([(8, 6), (8, 8), (8, 10)], fill=(220, 255, 245, 255))
    elif name == "bullet":
        for i, x in enumerate((3, 7, 11)):
            d.rectangle([x, 7, x + 2, 13], fill=shade(c, 0.85 - 0.1 * i))
            d.polygon([(x, 7), (x + 1, 4), (x + 2, 7)], fill=shade((138, 160, 168), 1.1))
    elif name == "crab_fragment":
        facets(d, [[(7, 2), (11, 6), (8, 8)], [(7, 2), (8, 8), (4, 12)], [(8, 8), (11, 6), (9, 14), (4, 12)]], c)
    elif name == "crab_core":
        facets(d, [[(8, 2), (13, 8), (8, 8)], [(8, 2), (8, 8), (3, 8)], [(3, 8), (8, 8), (8, 14)],
                   [(8, 8), (13, 8), (8, 14)]], c)
        d.point([(8, 7), (7, 8), (9, 8), (8, 9)], fill=(255, 220, 250, 255))
    elif name == "repair_kit":
        facets(d, [[(3, 5), (13, 5), (13, 13), (3, 13)]], c)
        d.rectangle([6, 3, 10, 4], fill=shade(c, 0.6))
        d.rectangle([7, 7, 9, 11], fill=(255, 40, 40, 255))
        d.rectangle([6, 8, 10, 10], fill=(255, 40, 40, 255))
    elif name == "moonstone":
        facets(d, [[(5, 4), (11, 4), (13, 8), (8, 9)], [(5, 4), (8, 9), (8, 13), (3, 8)],
                   [(8, 9), (13, 8), (11, 12), (8, 13)]], c)
        d.point([(7, 6)], fill=(255, 255, 255, 255))
    elif name in ("sun_fragment", "moon_fragment"):
        facets(d, [[(8, 1), (12, 7), (8, 9)], [(8, 1), (8, 9), (4, 7)], [(4, 7), (8, 9), (8, 15)],
                   [(8, 9), (12, 7), (8, 15)]], c)
        glow = (255, 240, 200, 255) if name == "sun_fragment" else (255, 255, 255, 255)
        d.point([(8, 5), (7, 8), (9, 8)], fill=glow)
    outline(img)
    img.save(OUT / f"{name}.png")


def icon():
    """The mod's icon: a glowstone frame with water in it, on UNRENDERED's void."""
    img = Image.new("RGBA", (128, 128), (5, 7, 10, 255))
    d = ImageDraw.Draw(img)
    for y in range(0, 128, 16):
        for x in range(0, 128, 16):
            if ((x // 16) + (y // 16)) % 2 == 0:
                d.rectangle([x, y, x + 15, y + 15], fill=(16, 28, 40, 255))
    glow = [(255, 214, 120), (232, 170, 70), (255, 236, 170), (196, 140, 60)]
    cell = 18
    ox, oy = 28, 19
    for j in range(5):
        for i in range(4):
            x0, y0 = ox + i * cell, oy + j * cell
            frame = i in (0, 3) or j in (0, 4)
            if frame:
                for k in range(0, cell, 3):
                    for m in range(0, cell, 3):
                        col = glow[(i * 7 + j * 3 + k + m // 3) % 4]
                        d.rectangle([x0 + k, y0 + m, x0 + k + 2, y0 + m + 2], fill=col + (255,))
            else:
                d.rectangle([x0, y0, x0 + cell - 1, y0 + cell - 1], fill=(63, 118, 228, 230))
                d.line([(x0 + 3, y0 + 5), (x0 + 12, y0 + 5)], fill=(150, 190, 255, 255))
                d.line([(x0 + 6, y0 + 12), (x0 + 15, y0 + 12)], fill=(120, 170, 250, 255))
    img.save(ROOT / "icon.png")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for n in PALETTE:
        draw(n)
    icon()
    print(f"wrote {len(PALETTE)} item sprites and the icon to {ROOT}")
