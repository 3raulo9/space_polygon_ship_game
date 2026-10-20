#!/usr/bin/env python3
"""Prints the run's screenshots into the log as base64 JPEG contact sheets between markers,
so a CI log alone is enough to look at what both games showed.

  show.py <dir>            one sheet per group (Minecraft's own, UNRENDERED's), up to 12 a sheet
  =====PNG <name>===== ... =====END=====   (the data lines joined are one base64 JPEG)
"""
import base64, io, os, sys
from PIL import Image, ImageDraw

root = sys.argv[1]
groups = {}
for d, _, names in os.walk(root):
    for n in sorted(names):
        if not n.endswith(".png"):
            continue
        rel = os.path.relpath(os.path.join(d, n), root)
        key = "unrendered" if rel.startswith("unr") else "minecraft"
        groups.setdefault(key, []).append(os.path.join(d, n))


def pick(files, k):
    if len(files) <= k:
        return files
    return [files[round(i * (len(files) - 1) / (k - 1))] for i in range(k)]


def emit(name, img, quality=72):
    b = io.BytesIO()
    img.save(b, "JPEG", quality=quality)
    data = base64.b64encode(b.getvalue()).decode()
    print("=====PNG %s=====" % name)
    for i in range(0, len(data), 4000):
        print(data[i:i + 4000])
    print("=====END=====")


TW, TH, COLS = 400, 225, 3
for key, files in sorted(groups.items()):
    chosen = pick(files, 12)
    print("%s: %d screenshots, sheet of %d: %s" % (key, len(files), len(chosen),
                                                 ", ".join(os.path.basename(f) for f in chosen)))
    rows = (len(chosen) + COLS - 1) // COLS
    sheet = Image.new("RGB", (TW * COLS, TH * rows), (40, 0, 40))
    draw = ImageDraw.Draw(sheet)
    for i, f in enumerate(chosen):
        try:
            img = Image.open(f).convert("RGB")
            img.thumbnail((TW, TH))
            x, y = (i % COLS) * TW, (i // COLS) * TH
            sheet.paste(img, (x, y))
            draw.rectangle((x, y, x + 150, y + 12), fill=(0, 0, 0))
            draw.text((x + 2, y), os.path.basename(f)[:24], fill=(255, 255, 0))
        except Exception as e:
            print("could not show", f, e)
    emit("sheet-" + key, sheet)
    # The last frame of each, larger: the state things ended in.
    try:
        img = Image.open(files[-1]).convert("RGB")
        img.thumbnail((960, 540))
        emit("last-" + key, img, 80)
    except Exception as e:
        print("could not show", files[-1], e)


# The frames a check is about, each on its own and large enough to read.
FEATURED = ("inventory", "holding", "blast", "pack")
for key, files in sorted(groups.items()):
    for f in files:
        if any(w in os.path.basename(f) for w in FEATURED):
            try:
                img = Image.open(f).convert("RGB")
                img.thumbnail((960, 540))
                emit("big-" + os.path.basename(f)[:-4], img, 80)
            except Exception as e:
                print("could not show", f, e)
