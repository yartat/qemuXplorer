"""Sanity-check authored .dc.html artboards before seeding the canvas.

Catches the mistakes that are invisible in a diff but break rendering:
malformed hex colours, stray non-ASCII, unbalanced tags, missing shell.
"""
import pathlib
import re
import sys
from collections import Counter

ALLOWED_NON_ASCII = {"·", "—", "●", "×", "…"}  # middle dot, em dash, disc, times, ellipsis
VOID = {"br", "img", "input", "hr", "meta", "link", "path", "rect", "circle",
        "ellipse", "line", "polyline", "polygon", "use", "stop", "source"}

problems = []
here = pathlib.Path(__file__).parent

for f in sorted(here.glob("*.dc.html")):
    src = f.read_text(encoding="utf-8")
    name = f.name

    # 1. hex colours must be exactly 3, 6 or 8 hex digits
    for m in re.finditer(r"#([0-9a-zA-Z]+)", src):
        tok = m.group(1)
        if not re.fullmatch(r"[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8}", tok):
            problems.append(f"{name}: malformed colour '#{tok}'")

    # 2. unexpected non-ASCII
    for ch in set(src):
        if ord(ch) > 127 and ch not in ALLOWED_NON_ASCII:
            problems.append(f"{name}: unexpected char U+{ord(ch):04X} {ch!r}")

    # 3. required Design Component shell
    for needed in ('<script src="./support.js"></script>', "<x-dc>", "</x-dc>", "<helmet>"):
        if needed not in src:
            problems.append(f"{name}: missing {needed}")

    # 4. balanced div/span nesting
    for tag in ("div", "span", "svg"):
        opens = len(re.findall(rf"<{tag}[\s>]", src))
        closes = len(re.findall(rf"</{tag}>", src))
        if opens != closes:
            problems.append(f"{name}: <{tag}> {opens} open vs {closes} close")

    # 5. style attributes should be double-quoted and non-empty
    for m in re.finditer(r'style="\s*"', src):
        problems.append(f"{name}: empty style attribute")

    # 6. root artboard should declare a fixed size and a background
    head = src[src.find("<x-dc>"):src.find("<x-dc>") + 1400]
    if "width: 1200px" not in head or "height: 780px" not in head:
        problems.append(f"{name}: root is not 1200x780")
    if "background:" not in head:
        problems.append(f"{name}: root sets no background")

files = sorted(p.name for p in here.glob("*.dc.html"))
print(f"checked {len(files)} artboards: {', '.join(files)}")
if problems:
    print(f"\n{len(problems)} problem(s):")
    for p in problems:
        print("  " + p)
    sys.exit(1)
print("all clean")
