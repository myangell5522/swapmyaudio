"""Procedural pixel-art icon for Swap My Audio!

Everything is described in a 64x64 design space and rasterised per pixel,
so the same scene can be rendered crisp at 64 (workshop, upscaled x8),
80 (tModLoader icon.png) and 30 (icon_small.png).
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image

FRAMES = 64
FRAME_MS = 55
HALF = FRAMES // 2

OUT = (18, 14, 34)

SKY = [(14, 12, 40), (22, 18, 58), (32, 25, 78), (46, 34, 100), (64, 46, 122), (84, 58, 138)]
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0

GREEN = [(30, 104, 50), (62, 164, 62), (120, 212, 82), (198, 248, 140)]
BLUE = [(30, 66, 156), (50, 118, 220), (108, 174, 250), (192, 228, 255)]

STEEL = [(44, 46, 64), (78, 82, 104), (130, 136, 160), (188, 194, 214), (242, 246, 252)]
CRIMSON = [(110, 26, 46), (176, 44, 64), (228, 86, 92), (255, 160, 150)]

WAVE = [(104, 112, 196), (168, 190, 246), (250, 252, 255)]

STARS = [
    (5, 7, 0), (13, 4, 9), (21, 11, 20), (28, 3, 5), (38, 6, 27), (45, 15, 14),
    (58, 22, 3), (9, 17, 40), (34, 13, 50), (60, 4, 33), (17, 23, 58), (55, 30, 44),
    (3, 28, 22), (47, 3, 61),
]

NOTE = ["..##", "..#.", "..#.", "###.", "###."]


def smooth_ease(x):
    return x - math.sin(2 * math.pi * x) / (2 * math.pi)


def ring_angle(f):
    f = f
    half, local = divmod(f, HALF)
    return half * 180.0 + 180.0 * smooth_ease(local / HALF)


def flip_state(f):
    """Returns (sprite_index, x_scale, flashing). 0 = speaker, 1 = headphones."""
    half, local = divmod(f, HALF)
    before = half % 2          # half 0: speaker -> headphones, half 1: back
    after = 1 - before
    start, end = 12, 20
    if local < start:
        return before, 1.0, False
    if local >= end:
        return after, 1.0, False
    u = (local - start) / (end - start)
    s = abs(math.cos(math.pi * u))
    return (before if u < 0.5 else after), max(s, 0.0), 0.3 < u < 0.7


class Canvas:
    def __init__(self, size):
        self.n = size
        self.k = size / 64.0
        idx = np.arange(size) + 0.5
        self.X, self.Y = np.meshgrid(idx / self.k, idx / self.k)   # design-space centres
        self.px, self.py = np.meshgrid(np.arange(size), np.arange(size))
        self.rgb = np.zeros((size, size, 3), np.float64)
        self.alpha = np.ones((size, size), bool)

    def paint(self, mask, color):
        self.rgb[mask] = color

    def outlined(self, layer_mask, colors_fn):
        m = layer_mask
        d = np.zeros_like(m)
        d[1:, :] |= m[:-1, :]
        d[:-1, :] |= m[1:, :]
        d[:, 1:] |= m[:, :-1]
        d[:, :-1] |= m[:, 1:]
        self.paint(d & ~m, OUT)
        colors_fn()


def hash01(x, y, seed=0):
    v = np.sin(x * 12.9898 + y * 78.233 + seed * 37.719) * 43758.5453
    return v - np.floor(v)


def ground_y(x):
    return 52.6 + 1.3 * np.sin(0.13 * x + 1.3) + 0.8 * np.sin(0.33 * x)


def hills_y(x):
    return 45.5 + 2.6 * np.sin(0.18 * x + 0.5) + 1.6 * np.sin(0.41 * x + 2.0)


def draw_background(c, simple=False):
    X, Y = c.X, c.Y
    t = np.clip(Y / 50.0, 0, 1) * (len(SKY) - 1)
    lo = np.floor(t).astype(int)
    frac = t - lo
    thr = BAYER[c.py % 4, c.px % 4]
    idx = np.clip(lo + (frac > thr), 0, len(SKY) - 1)
    c.rgb[:] = np.array(SKY)[idx]

    if simple:
        return
    # moon + soft halo
    mx, my, mr = 52.0, 11.5, 4.6
    d = np.hypot(X - mx, Y - my)
    c.paint((d > mr + 0.6) & (d < mr + 3.6), (40, 32, 92))
    c.paint((d > mr + 0.6) & (d < mr + 1.8), (58, 46, 120))
    moon = d <= mr
    c.paint(moon, (246, 238, 200))
    c.paint(moon & ((X - mx) + (Y - my) > 2.4), (222, 210, 168))
    crater = (np.hypot(X - (mx - 1.5), Y - (my + 0.5)) < 1.1) | (np.hypot(X - (mx + 1.4), Y - (my - 1.6)) < 0.8)
    c.paint(moon & crater, (210, 198, 156))

    # far hills
    hy = hills_y(X)
    c.paint(Y >= hy, (36, 30, 82))
    c.paint((Y >= hy) & (Y < hy + 1.0 / c.k + 0.01), (56, 46, 112))

    # tree (behind the ground line)
    trunk = (X >= 6.6) & (X < 9.0) & (Y > 37) & (Y < 54)
    c.paint(trunk, (106, 70, 42))
    c.paint(trunk & (X >= 8.0), (78, 50, 30))
    blobs = [(7.8, 35.0, 4.6), (4.6, 37.8, 3.0), (11.0, 37.6, 3.0), (7.8, 31.6, 3.0)]
    canopy = np.zeros_like(trunk)
    for bx, by, br in blobs:
        canopy |= np.hypot(X - bx, Y - by) <= br
    leaf_lite = np.zeros_like(trunk)
    for bx, by, br in blobs:
        leaf_lite |= np.hypot(X - bx + 0.9, Y - by + 0.9) <= br - 1.2
    ring_out = np.zeros_like(canopy)
    ring_out[1:, :] |= canopy[:-1, :]
    ring_out[:-1, :] |= canopy[1:, :]
    ring_out[:, 1:] |= canopy[:, :-1]
    ring_out[:, :-1] |= canopy[:, 1:]
    c.paint(ring_out & ~canopy & ~trunk, (20, 40, 34))
    c.paint(canopy, (36, 118, 56))
    c.paint(canopy & leaf_lite, (70, 168, 70))
    speck = canopy & (hash01(c.px, c.py, 3) > 0.86)
    c.paint(speck, (122, 210, 90))

    # ground
    gy = ground_y(X)
    ground = Y >= gy
    c.paint(ground, (110, 72, 46))
    c.paint(ground & (hash01(c.px, c.py, 1) > 0.78), (86, 54, 34))
    c.paint(ground & (hash01(c.px, c.py, 2) > 0.95), (104, 104, 118))
    px1 = 1.0 / c.k
    c.paint(ground & (Y < gy + 2 * px1), (70, 154, 52))
    c.paint(ground & (Y < gy + px1), (128, 208, 76))
    hang = ground & (Y >= gy + 2 * px1) & (Y < gy + 3 * px1) & (hash01(np.floor(X), 0, 4) > 0.6)
    c.paint(hang, (70, 154, 52))
    tuft = (Y < gy) & (Y >= gy - px1) & (hash01(np.floor(X * c.k), 0, 5) > 0.72)
    c.paint(tuft, (128, 208, 76))
    for fx, col in ((17.5, (255, 214, 80)), (44.5, (255, 130, 190)), (58.5, (170, 220, 255))):
        fxm = (np.abs(X - fx) < 0.5 * px1 + 0.01)
        gyf = ground_y(np.array(fx))
        c.paint(fxm & (Y < gyf) & (Y >= gyf - 2 * px1), (70, 154, 52))
        c.paint(fxm & (Y < gyf - 2 * px1) & (Y >= gyf - 3 * px1), col)


def draw_stars(c, f):
    for sx, sy, ph in STARS:
        x = int(sx * c.k)
        y = int(sy * c.k)
        b = 0.5 + 0.5 * math.cos(2 * math.pi * (f / 32.0 + ph / 64.0))
        if b > 0.85:
            c.rgb[y, x] = (255, 255, 240)
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if 0 <= x + dx < c.n and 0 <= y + dy < c.n:
                    c.rgb[y + dy, x + dx] = (150, 140, 210)
        elif b > 0.4:
            c.rgb[y, x] = (220, 214, 250)
        else:
            c.rgb[y, x] = (120, 108, 180)


def draw_shooting_star(c, f):
    start, length = 36, 9
    if not (start <= f < start + length):
        return
    u = (f - start) / (length - 1)
    hx, hy = 4 + 26 * u, 2 + 9 * u
    for i, col in enumerate([(255, 255, 255), (220, 220, 255), (160, 150, 230), (110, 96, 190)]):
        x = int((hx - 1.6 * i) * c.k)
        y = int((hy - 0.55 * i) * c.k)
        if 0 <= x < c.n and 0 <= y < c.n:
            c.rgb[y, x] = col


def draw_fireflies(c, f):
    flies = [(14.0, 49.0, 0.0), (49.0, 47.0, 0.4), (27.0, 51.0, 0.7)]
    for bx, by, ph in flies:
        a = 2 * math.pi * (f / FRAMES + ph)
        x = int((bx + 2.5 * math.sin(a)) * c.k)
        y = int((by + 1.6 * math.sin(2 * a)) * c.k)
        on = math.sin(2 * a * 2 + ph * 9) > -0.2
        if 0 <= x < c.n and 0 <= y < c.n and on:
            c.rgb[y, x] = (232, 255, 130)


def ring_layers(c, angle, cx=32.0, cy=30.5, rc=19.0, hw=2.3):
    dx, dy = c.X - cx, c.Y - cy
    r = np.hypot(dx, dy)
    th = np.degrees(np.arctan2(dy, dx))
    out = []
    for i, pal in enumerate((GREEN, BLUE)):
        start = angle + i * 180.0
        t = (th - start) % 360.0
        body = (t <= 118) & (np.abs(r - rc) <= hw)
        tip = 154.0
        head_w = (hw + 3.4) * (tip - t) / (tip - 116.0)
        head = (t >= 116) & (t < tip) & (np.abs(r - rc) <= head_w)
        shape = body | head
        rr = (r - rc) / hw
        tone = np.ones_like(r, dtype=int)
        tone[rr > 0.45] = 2
        tone[rr < -0.45] = 0
        sin = np.sin(np.radians(th))
        tone[(sin < -0.35) & (tone == 1)] = 2
        tone[(sin > 0.45)] -= 1
        tone[(sin < -0.55) & (rr > 0.45)] = 3
        
        tone = np.clip(tone, 0, 3)
        out.append((shape, tone, pal))
    return out


def draw_ring(c, angle, **kw):
    layers = ring_layers(c, angle, **kw)
    union = layers[0][0] | layers[1][0]

    def colors():
        for shape, tone, pal in layers:
            for k in range(4):
                c.paint(shape & (tone == k), pal[k])
    c.outlined(union, colors)


def speaker_shape(X, Y, cy=30.5):
    ay = np.abs(Y - cy)
    body = (X >= 19.5) & (X < 24.5) & (ay <= 3.2)
    cone_h = 3.2 + (X - 24.5) * (5.0 / 7.0)
    cone = (X >= 24.5) & (X < 31.5) & (ay <= cone_h + 0.3)
    rim = (X >= 31.5) & (X < 33.0) & (ay <= 8.6)
    return body, cone, rim


def headphone_shape(X, Y):
    d = np.hypot(X - 32.0, Y - 30.0)
    band = (d >= 7.0) & (d <= 9.2) & (Y <= 30.5)
    band_hi = band & (d >= 8.3) & (Y < 26)
    lcup = (np.abs(X - 23.2) <= 2.3) & (Y >= 27.0) & (Y <= 37.5)
    rcup = (np.abs(X - 40.8) <= 2.3) & (Y >= 27.0) & (Y <= 37.5)
    cut = lambda cx: ((np.abs(X - cx) > 1.3) & ((Y < 28.0) | (Y > 36.5)))
    lcup &= ~cut(23.2)
    rcup &= ~cut(40.8)
    lcush = lcup & (X > 24.4)
    rcush = rcup & (X < 39.6)
    return band, band_hi, lcup, rcup, lcush, rcush


def draw_center(c, f):
    which, s, flash = flip_state(f)
    cx = 32.0
    X = cx + (c.X - cx) / max(s, 1e-3)
    Y = c.Y
    if s < 0.08:
        mask = np.zeros_like(c.X, bool)
        mid = (np.abs(c.X - cx) < 0.5 / c.k + 0.01) & (np.abs(Y - 30.5) < 8)
        c.outlined(mid, lambda: c.paint(mid, STEEL[4]))
    elif which == 0:
        body, cone, rim = speaker_shape(X, Y)
        mask = body | cone | rim
        rel = (Y - 30.5)

        def colors():
            c.paint(body, STEEL[1])
            c.paint(body & (rel < -1.8), STEEL[2])
            c.paint(body & (rel > 1.8), STEEL[0])
            c.paint(cone, STEEL[3])
            ch = 3.2 + (X - 24.5) * (5.0 / 7.0)
            c.paint(cone & (rel < -ch * 0.45), STEEL[4])
            c.paint(cone & (rel > ch * 0.35), STEEL[2])
            c.paint(cone & (rel > ch * 0.75), STEEL[1])
            c.paint(rim, STEEL[2])
            c.paint(rim & (rel < 0), STEEL[4])
            c.paint(rim & (rel > 5.5), STEEL[1])
        c.outlined(mask, colors)
    else:
        band, band_hi, lcup, rcup, lcush, rcush = headphone_shape(X, Y)
        mask = band | lcup | rcup

        def colors():
            c.paint(band, STEEL[1])
            c.paint(band_hi, STEEL[3])
            for cup in (lcup, rcup):
                c.paint(cup, CRIMSON[1])
                c.paint(cup & (Y < 29.5), CRIMSON[2])
                c.paint(cup & (Y < 28.2), CRIMSON[3])
                c.paint(cup & (Y > 35.5), CRIMSON[0])
            c.paint(lcush | rcush, STEEL[0])
            c.paint((lcush | rcush) & (Y < 29.5), STEEL[1])
        c.outlined(mask, colors)

    if flash:
        x0, y0 = int(32 * c.k), int(30.5 * c.k)
        L = max(2, int(5 * c.k * (1 - abs(s))))
        for i in range(-L, L + 1):
            col = (255, 255, 255) if abs(i) < L - 1 else (200, 220, 255)
            for (x, y) in ((x0 + i, y0), (x0, y0 + i)):
                if 0 <= x < c.n and 0 <= y < c.n:
                    c.rgb[y, x] = col
    return which, s


def draw_waves(c, f):
    local = f % 16
    dx, dy = c.X - 33.0, c.Y - 30.5
    r = np.hypot(dx, dy)
    th = np.degrees(np.arctan2(dy, dx))
    half_px = 0.55 / c.k
    for j, R in enumerate((4.2, 7.4, 10.6)):
        arc = (np.abs(r - R) <= max(half_px, 0.5)) & (np.abs(th) <= 50)
        front = local / 4.0
        d = front - j
        if 0 <= d < 1:
            col = WAVE[2]
        elif 1 <= d < 2:
            col = WAVE[1]
        else:
            col = WAVE[0]
        c.paint(arc, col)


def draw_notes(c, f):
    for k, (bx, side) in enumerate(((44.5, 1), (17.0, -1))):
        local = (f + k * 8) % 16
        u = local / 15.0
        x = int((bx + side * 2.0 * u + math.sin(u * 6) * 0.8) * c.k)
        y = int((33.0 - 13.0 * u) * c.k)
        col = (255, 236, 150) if u < 0.6 else ((200, 170, 120) if u < 0.85 else None)
        if col is None:
            continue
        for ry, row in enumerate(NOTE):
            for rx, ch in enumerate(row):
                if ch == "#":
                    xx, yy = x + rx, y + ry - 4
                    if 0 <= xx < c.n and 0 <= yy < c.n:
                        c.rgb[yy, xx] = col


def draw_border(c):
    n = c.n
    rad = max(2.0, 5.0 * c.k)
    x = c.px + 0.5
    y = c.py + 0.5
    qx = np.abs(x - n / 2) - (n / 2 - rad)
    qy = np.abs(y - n / 2) - (n / 2 - rad)
    sdf = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - rad
    inside = -sdf
    c.alpha = inside > 0
    edge0 = (inside > 0) & (inside <= 1)
    edge1 = (inside > 1) & (inside <= 2)
    edge2 = (inside > 2) & (inside <= 3)
    c.paint(edge0, OUT)
    lit = (c.px + c.py) < n
    c.paint(edge1 & lit, (128, 150, 226))
    c.paint(edge1 & ~lit, (54, 66, 140))
    if n >= 48:
        c.paint(edge2, (24, 22, 56))


def render(size, f, simple=False):
    c = Canvas(size)
    draw_background(c, simple)
    if not simple:
        draw_stars(c, f)
        draw_shooting_star(c, f)
        draw_fireflies(c, f)
    draw_ring(c, ring_angle(f))
    which, s = draw_center(c, f)
    if not simple:
        if which == 0 and s > 0.99:
            draw_waves(c, f)
        elif which == 1 and s > 0.99:
            draw_notes(c, f)
    elif which == 0:
        draw_waves(c, f)
    draw_border(c)
    rgba = np.zeros((size, size, 4), np.uint8)
    rgba[..., :3] = np.clip(c.rgb, 0, 255).astype(np.uint8)
    rgba[..., 3] = np.where(c.alpha, 255, 0)
    return Image.fromarray(rgba, "RGBA")


def to_gif_frames(frames, scale):
    out = []
    for im in frames:
        big = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
        out.append(big)
    return out


def save_gif(frames, path):
    # Shared palette from all frames; index 0 is transparent.
    colors = set()
    for im in frames:
        a = np.asarray(im)
        rgb = a[..., :3][a[..., 3] > 0].reshape(-1, 3)
        colors.update(tuple(int(v) for v in row) for row in np.unique(rgb, axis=0))
    colors = sorted(colors)
    assert len(colors) <= 255, len(colors)
    lut = {col: i + 1 for i, col in enumerate(colors)}
    flat = [0, 0, 0]
    for col in colors:
        flat += list(col)
    flat += [0, 0, 0] * (256 - len(colors) - 1)
    pal_frames = []
    for im in frames:
        a = np.asarray(im)
        key = (a[..., 0].astype(np.int64) << 16) | (a[..., 1].astype(np.int64) << 8) | a[..., 2]
        idx = np.zeros(key.shape, np.uint8)
        for col, i in lut.items():
            idx[key == ((col[0] << 16) | (col[1] << 8) | col[2])] = i
        idx[a[..., 3] == 0] = 0
        p = Image.fromarray(idx, "P")
        p.putpalette(flat)
        p.info["transparency"] = 0
        pal_frames.append(p)
    pal_frames[0].save(
        path, save_all=True, append_images=pal_frames[1:], duration=FRAME_MS,
        loop=0, transparency=0, disposal=1, optimize=False,
    )
    return len(colors)


if __name__ == "__main__":
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    frames64 = [render(64, f) for f in range(FRAMES)]
    n = save_gif(to_gif_frames(frames64, 8), out / "icon_workshop.gif")
    print("colors", n)
    to_gif_frames(frames64[:1], 8)[0].save(out / "icon_workshop.png", optimize=True)
    frames80 = [render(80, f) for f in range(FRAMES)]
    frames80[0].save(out / "icon.png", optimize=True)
    save_gif(to_gif_frames(frames80, 4), out / "preview_80_x4.gif")
    render(30, 0, simple=True).save(out / "icon_small.png", optimize=True)
    # contact sheet for review
    sheet = Image.new("RGBA", (64 * 8 * 2, 64 * 2 * 4), (40, 40, 40, 255))
    for i, f in enumerate(range(0, 64, 4)):
        im = frames64[f].resize((128, 128), Image.NEAREST)
        sheet.paste(im, ((i % 8) * 128, (i // 8) * 128), im)
    sheet.save(out / "sheet.png")


def save_sheet(out_path, size=80, cols=8):
    """Spritesheet the mod itself uses to animate its icon in the in-game Mods list."""
    rows = (FRAMES + cols - 1) // cols
    sheet = Image.new("RGBA", (size * cols, size * rows), (0, 0, 0, 0))
    for f in range(FRAMES):
        sheet.paste(render(size, f), ((f % cols) * size, (f // cols) * size))
    sheet.save(out_path, optimize=True)
