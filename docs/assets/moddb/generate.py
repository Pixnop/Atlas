#!/usr/bin/env python3
"""Procedural generator for the Atlas Mod DB page assets (old-map theme).

Everything here is drawn from scratch with numpy/Pillow, except the titan,
which is rasterised from the repository's own docs/assets/logo.svg.
No downloads, no stock imagery.

    python3 docs/assets/moddb/generate.py

Outputs into the directory holding this script.
"""

from __future__ import annotations

import math
import pathlib
import subprocess
import sys

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

OUT = pathlib.Path(__file__).resolve().parent
LOGO = OUT.parent / "logo.svg"
SERIF = "/usr/share/fonts/gsfonts/P052-Roman.otf"

# --- palette -----------------------------------------------------------------
PARCHMENT = (243, 230, 201)  # #F3E6C9 base parchment
PARCH_DEEP = (214, 190, 146)  # #D6BE92 mottling / shade
PARCH_LIGHT = (250, 241, 220)  # #FAF1DC highlight
INK = (59, 42, 26)  # #3B2A1A dark sepia ink
INK_SOFT = (110, 84, 52)  # #6E5434 diluted ink, hatching
RED = (156, 59, 46)  # #9C3B2E faded red-brown, north point
TEAL = (31, 78, 90)  # #1F4E5A deep blue-teal, water lines
GOLD = (176, 134, 43)  # #B0862B gold leaf, used sparingly

SEED = 20260904


def rng(offset: int = 0) -> np.random.Generator:
    return np.random.default_rng(SEED + offset)


# --- helpers -----------------------------------------------------------------
def periodic_noise(size: int, beta: float, gen: np.random.Generator) -> np.ndarray:
    """Seamless 1/f^beta noise: filtering white noise in the Fourier domain
    keeps the result periodic on both axes, so the tile wraps exactly."""
    white = gen.standard_normal((size, size))
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    radius = np.sqrt(fx**2 + fy**2)
    radius[0, 0] = 1.0
    spectrum = np.fft.fft2(white) * radius**-beta
    spectrum[0, 0] = 0.0  # kill the DC term, keep the field centred on zero
    field = np.fft.ifft2(spectrum).real
    span = np.abs(field).max()
    return field / span if span else field


def periodic_blur(a: np.ndarray, sigma: float) -> np.ndarray:
    """Gaussian blur that wraps at the edges, so tiles stay seamless."""
    if sigma <= 0:
        return a
    n = a.shape[0]
    fy = np.fft.fftfreq(n)[:, None]
    fx = np.fft.fftfreq(n)[None, :]
    kernel = np.exp(-2 * (math.pi * sigma) ** 2 * (fx**2 + fy**2))
    return np.fft.ifft2(np.fft.fft2(a) * kernel).real


def norm01(a: np.ndarray) -> np.ndarray:
    lo, hi = a.min(), a.max()
    return (a - lo) / (hi - lo) if hi > lo else np.zeros_like(a)


def lerp_rgb(a, b, t: np.ndarray) -> np.ndarray:
    t = t[..., None]
    return np.array(a)[None, None, :] * (1 - t) + np.array(b)[None, None, :] * t


def accent_palette(img: Image.Image, colors: int) -> Image.Image:
    """Median cut allocates palette entries by area, so a few hundred pixels of
    red needle or gold pivot get merged into the parchment. Painting swatches
    on a throwaway copy reserves entries for them."""
    swatches = (INK, INK_SOFT, RED, GOLD, TEAL, (163, 120, 55),
                (198, 152, 126), (126, 96, 74))
    src = img.convert("RGB").copy()
    d = ImageDraw.Draw(src)
    band = src.height / len(swatches)
    for i, col in enumerate(swatches):
        d.rectangle([0, i * band, src.width / 6, (i + 1) * band], fill=col)
    return src.quantize(colors=colors, method=Image.Quantize.MEDIANCUT)


def save(img: Image.Image, name: str, colors: int | None = None,
         accents: bool = False) -> None:
    if colors and accents:
        img = img.convert("RGB").quantize(palette=accent_palette(img, colors),
                                          dither=Image.Dither.NONE)
    elif colors:
        # FASTOCTREE is the only alpha-aware quantiser, but it is not
        # translation invariant, which would put a seam in the tile.
        method = (Image.Quantize.FASTOCTREE if img.mode == "RGBA"
                  else Image.Quantize.MEDIANCUT)
        img = img.quantize(colors=colors, method=method, dither=Image.Dither.NONE)
    path = OUT / name
    img.save(path, optimize=True)
    print(f"{name:24s} {img.size[0]}x{img.size[1]}  {path.stat().st_size / 1024:6.1f} KB")


def ink_layer(size: int) -> tuple[Image.Image, ImageDraw.ImageDraw]:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def polar(cx: float, cy: float, angle_deg: float, r: float) -> tuple[float, float]:
    """Angle 0 points north (up), growing clockwise, like a bearing."""
    a = math.radians(angle_deg - 90.0)
    return cx + r * math.cos(a), cy + r * math.sin(a)


def hatch_facet(target: Image.Image, poly, angle_deg: float, spacing: int,
                width: int, color) -> None:
    """Fill a polygon with fine parallel lines: the engraver's light facet."""
    size = target.size[0]
    mask = Image.new("L", target.size, 0)
    ImageDraw.Draw(mask).polygon(poly, fill=255)
    lines = Image.new("L", target.size, 0)
    ld = ImageDraw.Draw(lines)
    a = math.radians(angle_deg)
    dx, dy = math.cos(a), math.sin(a)
    reach = size * 1.5
    for i in range(-int(reach / spacing), int(reach / spacing)):
        ox, oy = -dy * i * spacing + size / 2, dx * i * spacing + size / 2
        ld.line([(ox - dx * reach, oy - dy * reach), (ox + dx * reach, oy + dy * reach)],
                fill=255, width=width)
    target.paste(color, mask=ImageChops.multiply(mask, lines))


# --- 1. parchment ------------------------------------------------------------
def fibers(size: int, gen: np.random.Generator, count: int, length: int) -> np.ndarray:
    """Short wrapping strokes: linen fibres pressed into the sheet.
    Signed, so some catch the light and some sit in shadow."""
    acc = np.zeros((size, size), np.float32)
    for _ in range(count):
        x, y = gen.uniform(0, size, 2)
        a = gen.uniform(0, 2 * math.pi)
        curl = gen.normal(0, 0.06)
        amp = gen.uniform(0.35, 1.0) * gen.choice([-1.0, 1.0], p=[0.42, 0.58])
        for _step in range(int(gen.integers(length // 2, length))):
            a += curl
            x += math.cos(a)
            y += math.sin(a)
            acc[math.floor(y) % size, math.floor(x) % size] += amp
    return acc


def make_parchment(size: int = 512) -> Image.Image:
    gen = rng(1)
    # a tile repeats, so keep the energy in mid frequencies: a strong low
    # frequency blob would read as a landmark every 512 px
    broad = periodic_noise(size, 1.9, gen)  # cloudy sheet formation
    mid = periodic_noise(size, 1.4, gen)  # laid texture
    grain = periodic_noise(size, 0.55, gen)  # tooth of the paper

    foxing = norm01(periodic_noise(size, 2.1, gen))
    foxing = np.clip((foxing - 0.74) / 0.26, 0, 1) ** 1.7  # small, faint brown blooms

    fib = periodic_blur(fibers(size, gen, count=2200, length=30), 0.5)
    fib = np.clip(fib / (np.abs(fib).max() or 1) * 2.6, -1, 1)

    field = 0.52 + 0.20 * broad + 0.24 * mid + 0.095 * grain + 0.17 * fib
    field = np.clip(field, 0, 1)

    rgb = lerp_rgb(PARCH_DEEP, PARCH_LIGHT, field)
    # foxing pulls towards diluted ink rather than plain darkening
    rgb = rgb * (1 - 0.11 * foxing[..., None]) + np.array(INK_SOFT)[None, None, :] * 0.11 * foxing[..., None]
    # hold the whole sheet near the nominal parchment: dark ink must stay AA readable
    rgb = 0.72 * rgb + 0.28 * np.array(PARCHMENT)[None, None, :]
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB")


def check_tileable(name: str) -> None:
    """Wrapping edges must be no more contrasted than any other pixel column,
    otherwise the repeated tile shows a grid of seams."""
    im = np.asarray(Image.open(OUT / name).convert("RGB"), np.int16)
    seam_x = np.abs(im[:, 0] - im[:, -1]).mean()
    seam_y = np.abs(im[0] - im[-1]).mean()
    typ_x = np.abs(np.diff(im, axis=1)).mean()
    typ_y = np.abs(np.diff(im, axis=0)).mean()
    assert seam_x < typ_x * 1.35 and seam_y < typ_y * 1.35, (
        f"visible seam in {name}: {seam_x:.2f}/{seam_y:.2f} "
        f"vs typical {typ_x:.2f}/{typ_y:.2f}")
    print(f"  tileable ok (seam {seam_x:.2f}/{seam_y:.2f}, "
          f"typical {typ_x:.2f}/{typ_y:.2f})")


# --- 2. compass rose ---------------------------------------------------------
def draw_rose(size: int, letters: bool = True, north=RED) -> Image.Image:
    """Engraved eight point rose. Drawn at 4x and downsampled.
    `north` is dropped to plain ink when a red magnetic needle sits on top."""
    S = size * 4
    c = S / 2
    img, d = ink_layer(S)
    unit = S / 1920.0

    def w(px: float) -> int:
        return max(1, round(px * unit))

    def ring(r: float, lw: float, col, alpha: int = 255) -> None:
        d.ellipse([c - r * unit, c - r * unit, c + r * unit, c + r * unit],
                  outline=col + (alpha,), width=w(lw))

    # engraved rules, with a lettering band between the outer pair
    ring(934, 3, INK)
    ring(916, 9, INK)
    ring(816, 3, INK)
    ring(734, 4, GOLD)

    # bearing ring, every three degrees
    for deg in range(0, 360, 3):
        major = deg % 15 == 0
        cardinal = deg % 45 == 0
        r1 = 810 - (20 if not major else (38 if not cardinal else 52))
        d.line([polar(c, c, deg, 810 * unit), polar(c, c, deg, r1 * unit)],
               fill=(INK if major else INK_SOFT) + (255,),
               width=w(6 if major else 4))

    # the eight secondary rays: hairline outlines, the shortest of the three ranks
    for i in range(16):
        deg = i * 22.5
        if deg % 45:
            d.polygon([polar(c, c, deg, 716 * unit), polar(c, c, deg - 90, 15 * unit),
                       (c, c), polar(c, c, deg + 90, 15 * unit)],
                      outline=INK_SOFT + (255,), width=w(3))

    # eight point star: solid facet clockwise, hatched facet anticlockwise,
    # as if lit from the north west
    def star_point(deg: float, reach: float, half: float, solid, hatch_gap: float):
        tip = polar(c, c, deg, reach * unit)
        left = polar(c, c, deg - 90, half * unit)
        right = polar(c, c, deg + 90, half * unit)
        d.polygon([tip, right, (c, c)], fill=solid + (255,))
        # ruled across the point, not along it, or the lines merge into a solid
        hatch_facet(img, [tip, left, (c, c)], deg, w(hatch_gap), w(4),
                    INK_SOFT + (255,))
        d.line([tip, left, (c, c), right, tip], fill=INK + (255,), width=w(4),
               joint="curve")

    for deg in (45, 135, 225, 315):
        star_point(deg, 452, 42, INK, 34)
    for deg in (90, 180, 270):
        star_point(deg, 700, 58, INK, 34)
    star_point(0, 700, 58, north, 34)

    # hub
    ring(120, 6, INK)
    d.ellipse([c - 100 * unit, c - 100 * unit, c + 100 * unit, c + 100 * unit],
              fill=(0, 0, 0, 0))
    ring(86, 7, GOLD)
    d.ellipse([c - 26 * unit, c - 26 * unit, c + 26 * unit, c + 26 * unit],
              fill=INK + (255,))

    if letters and pathlib.Path(SERIF).exists():
        font = ImageFont.truetype(SERIF, int(88 * unit))
        for deg, ch in ((0, "N"), (90, "E"), (180, "S"), (270, "W")):
            d.text(polar(c, c, deg, 868 * unit), ch, font=font,
                   fill=((north if ch == "N" else INK) + (255,)), anchor="mm")

    # ink bleed and paper bite
    img = img.filter(ImageFilter.GaussianBlur(1.3 * unit))
    alpha = np.asarray(img.getchannel("A"), np.float32) / 255.0
    bite = norm01(periodic_noise(S, 1.1, rng(2)))
    alpha *= 0.86 + 0.14 * bite
    img.putalpha(Image.fromarray((np.clip(alpha, 0, 1) * 255).astype(np.uint8)))
    return img.resize((size, size), Image.Resampling.LANCZOS)


# --- 3. divider --------------------------------------------------------------
def make_divider(width: int = 800, height: int = 40) -> Image.Image:
    S = 4
    W, H = width * S, height * S
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx, cy = W / 2, H / 2

    def dot(x, y, r, col):
        d.ellipse([x - r * S, y - r * S, x + r * S, y + r * S], fill=col + (255,))

    def curl(x, y, rx, ry, start, end, col, lw):
        d.arc([x - rx * S, y - ry * S, x + rx * S, y + ry * S],
              start=start, end=end, fill=col + (255,), width=lw)

    # double rule either side of the centre motif, drawn as tapering quads:
    # a stepped line width would read as an accident
    for side in (-1, 1):
        inner = cx + side * 40 * S
        outer = cx + side * (width / 2 - 22) * S
        for dy in (-4 * S, 4 * S):
            t0, t1 = 1.3 * S, 0.5 * S
            d.polygon([(inner, cy + dy - t0), (outer, cy + dy - t1),
                       (outer, cy + dy + t1), (inner, cy + dy + t0)],
                      fill=INK + (255,))
        # hairline down the middle of the pair, held back from both ends
        d.line([(inner + side * 20 * S, cy), (inner + (outer - inner) * 0.72, cy)],
               fill=INK_SOFT + (255,), width=S)
        # terminal: a small hook turning back on itself, then a pip
        curl(outer, cy - 4 * S, 7, 7, 180 if side > 0 else 270, 270 if side > 0 else 360,
             INK, 2 * S)
        curl(outer, cy + 4 * S, 7, 7, 90 if side > 0 else 0, 180 if side > 0 else 90,
             INK, 2 * S)
        dot(outer + side * 7 * S, cy, 2.5, INK)
        # a pip flanking the lozenge, in place of the rule's inner terminal
        dot(inner - side * 6 * S, cy, 2.5, INK)

    # central lozenge with a gold pip
    for half_w, half_h, fill in ((22, 13, None), (12, 7, INK)):
        d.polygon([(cx, cy - half_h * S), (cx + half_w * S, cy),
                   (cx, cy + half_h * S), (cx - half_w * S, cy)],
                  fill=(fill + (255,)) if fill else None,
                  outline=INK + (255,), width=2 * S)
    dot(cx, cy, 3, GOLD)

    img = img.filter(ImageFilter.GaussianBlur(1.6))
    return img.resize((width, height), Image.Resampling.LANCZOS)


# --- 4/6. titan --------------------------------------------------------------
def titan(height: int) -> Image.Image:
    raw = OUT / f".titan-{height}.png"
    subprocess.run(["rsvg-convert", "-h", str(height), "-o", str(raw), str(LOGO)], check=True)
    img = Image.open(raw).convert("RGBA")
    raw.unlink()

    a = np.asarray(img, np.float32) / 255.0
    lum = a[..., :3] @ np.array([0.35, 0.5, 0.15])  # the green reads lighter than the slate
    solid = a[..., 3] > 0.9
    lo, hi = np.percentile(lum[solid], [2, 98])
    # stretch across the logo's own two tones so the slate body and the turf on
    # the cube stay legibly apart once both are sepia
    t = np.clip((lum - lo) / max(hi - lo, 1e-6), 0, 1) ** 0.9
    duo = lerp_rgb(INK, (163, 120, 55), t)  # dark ink to warm ochre
    out = np.dstack([duo, a[..., 3:] * 255])
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")


def make_titan_sepia(size: int = 400) -> Image.Image:
    t = titan(int(size * 0.94))
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.alpha_composite(t, ((size - t.width) // 2, (size - t.height) // 2))
    return canvas


# --- 5. banner ---------------------------------------------------------------
def banner_ground(width: int, height: int) -> Image.Image:
    tile = Image.open(OUT / "parchment-tile.png").convert("RGB")
    ground = Image.new("RGB", (width, height))
    for y in range(0, height, tile.height):
        for x in range(0, width, tile.width):
            ground.paste(tile, (x, y))

    # a graticule: parallels sag towards the edges, meridians bulge away from
    # the central one, the way a globe unrolls onto a sheet
    grid = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    g = ImageDraw.Draw(grid)
    cx, cy = width / 2, height / 2
    for i in range(-4, 5):
        y = cy + i * height / 8.5
        pts = [(x, y + ((x - cx) / cx) ** 2 * i * 9.0) for x in range(0, width + 12, 12)]
        g.line(pts, fill=TEAL + (30,), width=3 if i == 0 else 2)
    for i in range(-7, 8):
        x = cx + i * width / 15.0
        bow = -(i / 7.0) * 26.0
        pts = [(x + bow * (1 - ((y - cy) / cy) ** 2), y) for y in range(0, height + 8, 8)]
        g.line(pts, fill=TEAL + (30,), width=3 if i == 0 else 2)
    grid = grid.filter(ImageFilter.GaussianBlur(0.8))
    ground = Image.alpha_composite(ground.convert("RGBA"), grid)

    # aged edges: a soft, uneven darkening towards the border
    yy, xx = np.mgrid[0:height, 0:width].astype(np.float32)
    dist = np.minimum.reduce([xx, width - 1 - xx, yy, height - 1 - yy]) / 95.0
    edge = np.clip(1.0 - dist, 0, 1) ** 2
    edge = edge * (0.5 + 0.5 * norm01(periodic_noise(max(width, height), 2.6, rng(3))
                                      [:height, :width]))
    arr = np.asarray(ground.convert("RGB"), np.float32)
    arr = arr * (1 - 0.13 * edge[..., None]) + np.array(INK)[None, None, :] * 0.13 * edge[..., None]
    ground = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB").convert("RGBA")

    f = ImageDraw.Draw(ground)
    f.rectangle([14, 14, width - 15, height - 15], outline=INK + (150,), width=3)
    f.rectangle([22, 22, width - 23, height - 23], outline=INK + (70,), width=1)
    return ground


def compose_banner(width: int = 1200, height: int = 320) -> tuple[Image.Image, Image.Image, tuple[int, int]]:
    """Returns (banner without needle, needle sprite, needle centre)."""
    ground = banner_ground(width, height)

    t = make_titan_sepia(int(height * 0.80))
    ground.alpha_composite(t, (72, (height - t.height) // 2))

    rose_px = 226
    rose = draw_rose(rose_px, north=INK)
    rose = ImageChops.multiply(rose, Image.new("RGBA", rose.size, (255, 255, 255, 205)))
    rx, ry = width - rose_px - 78, (height - rose_px) // 2
    ground.alpha_composite(rose, (rx, ry))

    needle = draw_needle(rose_px)
    return ground, needle, (rx, ry)


def draw_needle(size: int) -> Image.Image:
    """A slim magnetic needle that sits on the rose hub."""
    S = size * 4
    img, d = ink_layer(S)
    c = S / 2
    unit = S / 904.0
    reach = 300 * unit
    half = 20 * unit
    for deg, col in ((0, RED), (180, INK)):
        tip = polar(c, c, deg, reach)
        l = polar(c, c, deg - 90, half)
        r = polar(c, c, deg + 90, half)
        d.polygon([tip, l, (c, c), r], fill=col + (255,))
        d.line([tip, l, (c, c), r, tip], fill=INK + (255,), width=max(1, int(3 * unit)))
    d.ellipse([c - 30 * unit, c - 30 * unit, c + 30 * unit, c + 30 * unit],
              fill=GOLD + (255,), outline=INK + (255,), width=max(1, int(4 * unit)))
    img = img.filter(ImageFilter.GaussianBlur(1.3 * unit))
    return img.resize((size, size), Image.Resampling.LANCZOS)


def make_banner_gif(base: Image.Image, needle: Image.Image, at: tuple[int, int],
                    frames: int = 12) -> list[Image.Image]:
    out = []
    for i in range(frames):
        phase = 2 * math.pi * i / frames
        angle = 5.5 * math.sin(phase)  # the needle settles, it does not spin
        f = base.copy()
        rot = needle.rotate(-angle, resample=Image.Resampling.BICUBIC)
        f.alpha_composite(rot, at)
        out.append(f.convert("RGB"))
    return out


# --- main --------------------------------------------------------------------
def main() -> None:
    save(make_parchment(), "parchment-tile.png", colors=48)
    check_tileable("parchment-tile.png")

    save(draw_rose(480), "compass-rose.png", colors=64)
    save(make_divider(), "divider-ornament.png", colors=32)
    save(make_titan_sepia(), "titan-sepia.png", colors=64)

    base, needle, at = compose_banner()
    still = base.copy()
    still.alpha_composite(needle, at)
    save(still.convert("RGB"), "banner-atlas.png", colors=128, accents=True)

    frames = make_banner_gif(base, needle, at)
    pal = accent_palette(frames[0], 64)
    frames = [f.quantize(palette=pal, dither=Image.Dither.NONE) for f in frames]
    gif = OUT / "banner-atlas.gif"
    # no explicit disposal: Pillow then stores frames 1..n as cropped deltas,
    # and only the needle moves, so the file stays an order of magnitude smaller
    frames[0].save(gif, save_all=True, append_images=frames[1:], loop=0,
                   duration=220, optimize=True)
    print(f"{'banner-atlas.gif':24s} {frames[0].size[0]}x{frames[0].size[1]} "
          f"{len(frames)}f  {gif.stat().st_size / 1024:6.1f} KB")


if __name__ == "__main__":
    sys.exit(main())
