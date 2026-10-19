#!/usr/bin/env python3
"""Prints every screenshot in a folder into the log, downscaled, as base64 between markers,
so a CI log alone is enough to look at what both games showed."""
import base64, io, os, sys
from PIL import Image

root = sys.argv[1]
files = []
for d, _, names in os.walk(root):
    for n in sorted(names):
        if n.endswith(".png"):
            files.append(os.path.join(d, n))
for f in files:
    try:
        img = Image.open(f).convert("RGB")
        w = 480
        if img.width > w:
            img = img.resize((w, int(img.height * w / img.width)))
        b = io.BytesIO()
        img.save(b, "JPEG", quality=70)
        print("=====PNG %s=====" % os.path.relpath(f, root))
        data = base64.b64encode(b.getvalue()).decode()
        for i in range(0, len(data), 4000):
            print(data[i:i + 4000])
        print("=====END=====")
    except Exception as e:
        print("could not show", f, e)
