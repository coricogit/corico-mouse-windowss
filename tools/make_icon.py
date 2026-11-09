"""Draws src/DareuMouse/mouse.ico: a mouse outline with a battery-green body. Pure Python, no Pillow."""
import struct, zlib, sys

def render(size, ss=4):
    n = size * ss
    px = []
    cx, w = n / 2, n * 0.56
    top, bottom, r = n * 0.06, n * 0.94, w / 2
    stroke = max(1.5 * ss, n / 12)
    def inside(x, y, shrink):
        rr = r - shrink
        if y < top + r:
            return (x - cx) ** 2 + (y - (top + r)) ** 2 <= rr ** 2
        if y > bottom - r:
            return (x - cx) ** 2 + (y - (bottom - r)) ** 2 <= rr ** 2
        return abs(x - cx) <= rr
    rows = []
    for py in range(size):
        row = bytearray()
        for pxl in range(size):
            acc = [0, 0, 0, 0]
            for sy in range(ss):
                for sx in range(ss):
                    x, y = pxl * ss + sx + 0.5, py * ss + sy + 0.5
                    if inside(x, y, 0):
                        if not inside(x, y, stroke) or (abs(x - cx) < stroke / 2 and top + stroke < y < top + (bottom - top) * 0.3):
                            c = (255, 255, 255)
                        elif y > top + (bottom - top) * 0.45:
                            c = (0x4C, 0xC3, 0x8A)
                        else:
                            c = (0x2B, 0x2D, 0x31)
                        acc[0] += c[0]; acc[1] += c[1]; acc[2] += c[2]; acc[3] += 255
            k = ss * ss
            a = acc[3] // k
            if a:
                row += bytes([acc[0] * 255 // acc[3], acc[1] * 255 // acc[3], acc[2] * 255 // acc[3], a])
            else:
                row += b"\0\0\0\0"
        rows.append(bytes(row))
    raw = b"".join(b"\0" + r for r in rows)
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")

sizes = [16, 24, 32, 48, 256]
pngs = [render(s) for s in sizes]
out = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
for s, p in zip(sizes, pngs):
    out += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(p), offset)
    offset += len(p)
out += b"".join(pngs)
open(sys.argv[1], "wb").write(out)
