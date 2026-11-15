"""Draws src/DareuMouse/mouse.ico: the pixel-art mouse of the tray icon, full battery. Pure Python, no Pillow.

    python tools/make_icon.py src/DareuMouse/mouse.ico

The sprite and colors are the ones in TrayIcon (src/DareuMouse/Theme.cs); keep both in sync.
"""
import struct
import sys
import zlib

# K dark edge, O outline, W scroll wheel, b body (battery fill).
SPRITE = [
    ".....KKKKKK.....",
    "....KOOOOOOK....",
    "...KObbOObbOK...",
    "..KObbbWWbbbOK..",
    "..KObbbWWbbbOK..",
    "..KObbbOObbbOK..",
    "..KObbbbbbbbOK..",
    "..KObbbbbbbbOK..",
    "..KObbbbbbbbOK..",
    "..KObbbbbbbbOK..",
    "..KObbbbbbbbOK..",
    "..KObbbbbbbbOK..",
    "..KObbbbbbbbOK..",
    "...KObbbbbbOK...",
    "....KOOOOOOK....",
    ".....KKKKKK.....",
]
EDGE, OUTLINE, WHEEL, GOOD = (0x16, 0x17, 0x19), (0xFF, 0xFF, 0xFF), (0x9A, 0xA0, 0xA6), (0x4C, 0xC3, 0x8A)


def mix(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def color(x, y):
    row = SPRITE[y]
    ch = row[x]
    if ch == "K":
        return EDGE
    if ch == "O":
        return OUTLINE
    if ch == "W":
        return WHEEL
    if ch == "b":
        if x == row.find("b"):
            return mix(GOOD, (255, 255, 255), 0.35)
        if x == row.rfind("b"):
            return mix(GOOD, (0, 0, 0), 0.25)
        return GOOD
    return None


def render(size):
    """PNG of the sprite scaled to `size` px with nearest neighbour (sharp pixels at every size)."""
    rows = []
    for py in range(size):
        row = bytearray()
        for px in range(size):
            c = color(px * 16 // size, py * 16 // size)
            row += bytes(c + (255,)) if c else b"\0\0\0\0"
        rows.append(bytes(row))
    raw = b"".join(b"\0" + r for r in rows)

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


assert all(len(r) == 16 for r in SPRITE) and len(SPRITE) == 16
sizes = [16, 24, 32, 48, 256]
pngs = [render(s) for s in sizes]
out = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
for s, p in zip(sizes, pngs):
    out += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(p), offset)
    offset += len(p)
out += b"".join(pngs)
open(sys.argv[1], "wb").write(out)
