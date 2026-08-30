"""生成多尺寸 app.ico（星盘罗盘风格，无 PIL 依赖，纯手写 PNG/BMP 编码）。"""
import struct, math, os, zlib

def make_png(size, pixels):
    raw = b""
    for y in range(size):
        raw += b"\x00"
        for x in range(size):
            r, g, b, a = pixels.get((x, y), (0, 0, 0, 0))
            raw += bytes((r, g, b, a))

    def chunk(typ, data):
        return (struct.pack(">I", len(data)) + typ + data
                + struct.pack(">I", zlib.crc32(typ + data) & 0xFFFFFFFF))

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))

def draw_icon(size):
    cx = cy = (size - 1) / 2
    R = size * 0.46
    px = {}
    for y in range(size):
        for x in range(size):
            dx, dy = x - cx, y - cy
            d = math.hypot(dx, dy)
            if d > R:
                continue
            t = d / R
            base = (18 + int(24 * t), 24 + int(40 * t), 58 + int(64 * t), 255)
            ring1 = abs(d - R * 0.88) < size * 0.03
            ring2 = abs(d - R * 0.62) < size * 0.015
            on_axis = abs(dx) < size * 0.02 or abs(dy) < size * 0.02
            star = on_axis and d < R * 0.6
            center = d < size * 0.07
            diag = abs(abs(dx) - abs(dy)) < size * 0.015 and d < R * 0.62
            if center:
                c = (255, 224, 130, 255)
            elif star or ring1 or ring2 or diag:
                c = (250, 200, 90, 255)
            else:
                c = base
            px[(x, y)] = c
    return px

def bmp_for(size, pixels):
    row = size * 4
    xor = b""
    for y in range(size - 1, -1, -1):
        for x in range(size):
            r, g, b, a = pixels.get((x, y), (0, 0, 0, 0))
            xor += bytes((b, g, r, a))
    mask_row = ((size + 31) // 32) * 4
    andmask = b"\x00" * (mask_row * size)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0,
                         len(xor) + len(andmask), 0, 0, 0, 0)
    return header + xor + andmask

sizes = [16, 24, 32, 48, 64, 128, 256]
images = []
for s in sizes:
    px = draw_icon(s)
    if s >= 64:
        images.append((s, make_png(s, px)))
    else:
        images.append((s, bmp_for(s, px)))

ico_path = r"F:\_Workspace\The-Celestial-Diviner\src\TheCelestialDiviner\Resources\app.ico"
os.makedirs(os.path.dirname(ico_path), exist_ok=True)
count = len(images)
ico = struct.pack("<HHH", 0, 1, count)
offset = 6 + 16 * count
for s, data in images:
    w = s if s < 256 else 0
    ico += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
for s, data in images:
    ico += data
with open(ico_path, "wb") as f:
    f.write(ico)
print(f"ico written: {os.path.getsize(ico_path)} bytes, {count} sizes")
