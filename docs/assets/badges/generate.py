#!/usr/bin/env python3
"""Generator for the "Tested with Atlas" badge kit.

Five formats (flat, plaque, square, seal, wide) times three palettes
(sepia, classic, dark), each as a source .svg plus rasterised .png at 1x
and 2x. Everything is built from scratch as SVG: the titan mark is copied
as vector paths straight out of the repository's own docs/assets/logo.svg
(no rasterising and re-tracing), and the wordmarks are set in Noto Serif
then baked to outline paths by inkscape, so the shipped .svg files carry
no <text> and render identically everywhere.

    python3 docs/assets/badges/generate.py

Needs inkscape and rsvg-convert on PATH. Output goes next to this script,
overwriting the existing badge files.
"""

from __future__ import annotations

import math
import pathlib
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

OUT = pathlib.Path(__file__).resolve().parent
LOGO = OUT.parent / "logo.svg"
FONT = "Noto Serif"

# --- palettes ----------------------------------------------------------------
# bg/edge/ink drive the plaque/square/seal/wide formats (a parchment-style
# ground with a thin frame and one ink colour for both rule and text).
# accent is a second colour used sparingly, as an ornament pip or rule.
# label_bg/label_fg/msg_bg/msg_fg/mark_fg are the shields.io-style two-chip
# colours for the flat format. titan is the two fills of logo.svg remapped
# (slate #56676B, green #9DB136) for the duo titan mark.
PALETTES = {
    "sepia": dict(
        bg="#F3E6C9", edge="#3B2A1A", ink="#3B2A1A", accent="#174F52",
        label_bg="#3B2A1A", label_fg="#F3E6C9",
        msg_bg="#174F52", msg_fg="#F3E6C9", mark_fg="#F3E6C9",
        titan={"#56676B": "#3B2A1A", "#9DB136": "#A37837"},
    ),
    "classic": dict(
        bg="#F4F4EF", edge="#56676B", ink="#56676B", accent="#9DB136",
        label_bg="#56676B", label_fg="#FFFFFF",
        msg_bg="#9DB136", msg_fg="#2B2B26", mark_fg="#FFFFFF",
        titan={"#56676B": "#56676B", "#9DB136": "#9DB136"},
    ),
    "dark": dict(
        bg="#1F1A15", edge="#F3E6C9", ink="#F3E6C9", accent="#9DB136",
        label_bg="#2A231C", label_fg="#F3E6C9",
        msg_bg="#9DB136", msg_fg="#1F1A15", mark_fg="#1F1A15",
        # slate lightened from the logo's #56676B: at 2.9:1 on this ground the
        # titan's body was nearly as dark as the background.
        titan={"#56676B": "#8FA0A5", "#9DB136": "#9DB136"},
    ),
}

# --- titan mark, lifted straight from logo.svg --------------------------------
_LOGO_SRC = LOGO.read_text()
_BOX = tuple(float(v) for v in re.search(r'viewBox="([\d. ]+)"', _LOGO_SRC)
             .group(1).split())  # (x, y, w, h)
_PATHS = re.findall(r'<path fill="(#[0-9A-Fa-f]{6})" d="([^"]+)"/>', _LOGO_SRC)
assert _PATHS, "no coloured paths found in logo.svg"


def _simplify_paths(paths: list[tuple[str, str]]) -> list[tuple[str, str]]:
    """Run the titan paths through inkscape's node-reduction pass once.

    The mark is only ever shown small (14-46 px tall in this kit); the full
    node detail traced for the 510 px-tall logo.svg is invisible at that size
    and just bloats the flat/seal SVGs. Used for those two formats only.
    """
    x, y, w, h = _BOX
    body = "".join(f'<path fill="{fill}" d="{d}"/>' for fill, d in paths)
    doc = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{x} {y} {w} {h}">{body}</svg>'
    with tempfile.TemporaryDirectory() as tmp:
        raw = pathlib.Path(tmp) / "raw.svg"
        out = pathlib.Path(tmp) / "out.svg"
        raw.write_text(doc)
        subprocess.run(
            ["inkscape", "--actions=select-all;path-simplify;export-plain-svg;export-do",
             f"--export-filename={out}", str(raw)],
            check=True, capture_output=True,
        )
        ns = {"svg": "http://www.w3.org/2000/svg"}
        simplified = [(el.get("fill").upper(), el.get("d"))
                      for el in ET.parse(out).getroot().findall(".//svg:path", ns)]
    assert len(simplified) == len(paths), "path-simplify changed the path count"
    return simplified


_PATHS_SIMPLE = _simplify_paths(_PATHS)


def titan_width(height: float) -> float:
    _, _, bw, bh = _BOX
    return height * bw / bh


def titan_group(x: float, y: float, height: float, colors: dict[str, str] | str,
                 simplify: bool = False) -> tuple[str, float]:
    """A <g> holding the titan, `height` tall, top-left corner at (x, y).

    `colors` is either the palette's two-tone remap dict (fill-for-fill) or
    a single hex string, which paints every path that one flat colour (the
    tiny mark used where there is no room to read two tones apart).
    `simplify` swaps in the node-reduced path set (see `_simplify_paths`),
    for the small marks where the extra detail would not show anyway.
    """
    bx, by, bw, bh = _BOX
    scale = height / bh
    mono = isinstance(colors, str)
    parts = [f'<g transform="translate({x:.2f},{y:.2f}) scale({scale:.5f}) '
              f'translate({-bx:.2f},{-by:.2f})">']
    for fill, d in (_PATHS_SIMPLE if simplify else _PATHS):
        out_fill = colors if mono else colors.get(fill, fill)
        parts.append(f'<path fill="{out_fill}" d="{d}"/>')
    parts.append("</g>")
    return "".join(parts), bw * scale


def polar(cx: float, cy: float, angle_deg: float, r: float) -> tuple[float, float]:
    """Angle 0 points north (up), growing clockwise, like a bearing."""
    a = math.radians(angle_deg - 90.0)
    return cx + r * math.cos(a), cy + r * math.sin(a)


def arc_path(cx: float, cy: float, r: float, start_deg: float, end_deg: float) -> str:
    sx, sy = polar(cx, cy, start_deg, r)
    ex, ey = polar(cx, cy, end_deg, r)
    large = 1 if (end_deg - start_deg) % 360 > 180 else 0
    return f"M {sx:.2f},{sy:.2f} A {r:.2f},{r:.2f} 0 {large} 1 {ex:.2f},{ey:.2f}"


def compass_star(cx: float, cy: float, r_long: float, r_short: float, fill: str) -> str:
    """An 8-point compass-rose fleuron, matching the kit's other map ornaments."""
    pts = [polar(cx, cy, i * 45, r_long if i % 2 == 0 else r_short) for i in range(8)]
    d = "M " + " L ".join(f"{x:.2f},{y:.2f}" for x, y in pts) + " Z"
    return f'<path d="{d}" fill="{fill}"/>'


def text_el(x, y, size, fill, text, anchor="start", weight="700", spacing=0,
            font=FONT) -> str:
    return (f'<text x="{x:.2f}" y="{y:.2f}" font-family="{font}" '
            f'font-weight="{weight}" font-size="{size}" text-anchor="{anchor}" '
            f'letter-spacing="{spacing}" fill="{fill}">{text}</text>')


def textpath_el(path_id, size, fill, text, weight="700", spacing=0, font=FONT) -> str:
    return (f'<text font-family="{font}" font-weight="{weight}" font-size="{size}" '
            f'letter-spacing="{spacing}" fill="{fill}" text-anchor="middle">'
            f'<textPath href="#{path_id}" startOffset="50%">{text}</textPath></text>')


def svg_doc(w: float, h: float, body: str) -> str:
    return (f'<?xml version="1.0" encoding="UTF-8"?>\n'
            f'<svg xmlns="http://www.w3.org/2000/svg" width="{w:.2f}" height="{h:.2f}" '
            f'viewBox="0 0 {w:.2f} {h:.2f}">{body}</svg>')


# --- formats -------------------------------------------------------------------
def build_flat(p: dict, uid: str) -> tuple[str, float, float]:
    h = 20.0
    label, msg = "tested with", "Atlas"
    label_w, msg_text_w = 86.0, 46.0
    pad = 6.0
    mark_w = titan_width(14)
    msg_w = pad + mark_w + 4 + msg_text_w + pad + 2
    w = label_w + msg_w
    body = [
        f'<clipPath id="{uid}-clip"><rect width="{w:.2f}" height="{h}" rx="3"/></clipPath>',
        f'<g clip-path="url(#{uid}-clip)">',
        f'<rect width="{label_w:.2f}" height="{h}" fill="{p["label_bg"]}"/>',
        f'<rect x="{label_w:.2f}" width="{msg_w:.2f}" height="{h}" fill="{p["msg_bg"]}"/>',
        f'</g>',
        f'<rect x="0.5" y="0.5" width="{w - 1:.2f}" height="{h - 1}" rx="3" '
        f'fill="none" stroke="{p["edge"]}" stroke-opacity="0.35"/>',
        text_el(label_w / 2, 14, 11, p["label_fg"], label, anchor="middle", spacing=0.2),
    ]
    mark, mark_actual_w = titan_group(label_w + pad, 3, 14, p["mark_fg"], simplify=True)
    body.append(mark)
    body.append(text_el(label_w + pad + mark_actual_w + 4, 14, 11, p["msg_fg"], msg,
                          anchor="start", spacing=0.2))
    return svg_doc(w, h, "".join(body)), w, h


def build_plaque(p: dict, uid: str) -> tuple[str, float, float]:
    w, h = 220.0, 60.0
    mark, mark_w = titan_group(14, h / 2 - 21, 42, p["titan"])
    body = [
        f'<rect width="{w}" height="{h}" fill="{p["bg"]}"/>',
        f'<rect x="1.5" y="1.5" width="{w - 3}" height="{h - 3}" fill="none" '
        f'stroke="{p["edge"]}" stroke-width="1.5"/>',
        f'<rect x="4.5" y="4.5" width="{w - 9}" height="{h - 9}" fill="none" '
        f'stroke="{p["edge"]}" stroke-width="0.6"/>',
        mark,
        text_el(14 + mark_w + 12, 30, 13.5, p["ink"], "TESTED WITH", spacing=2.2),
        text_el(14 + mark_w + 12, 47, 16, p["ink"], "ATLAS", weight="700", spacing=3.4),
    ]
    return svg_doc(w, h, "".join(body)), w, h


def build_square(p: dict, uid: str) -> tuple[str, float, float]:
    w = h = 160.0
    mark_w = titan_width(78)
    mark, _ = titan_group((w - mark_w) / 2, 16, 78, p["titan"])
    body = [
        f'<rect width="{w}" height="{h}" fill="{p["bg"]}"/>',
        f'<rect x="3" y="3" width="{w - 6}" height="{h - 6}" fill="none" '
        f'stroke="{p["edge"]}" stroke-width="1.4"/>',
        mark,
        text_el(w / 2, 118, 11, p["ink"], "TESTED WITH", anchor="middle", spacing=2.4),
        text_el(w / 2, 143, 22, p["ink"], "ATLAS", anchor="middle", weight="700",
                spacing=3.0),
    ]
    return svg_doc(w, h, "".join(body)), w, h


def build_seal(p: dict, uid: str) -> tuple[str, float, float]:
    w = h = 112.0
    cx, cy = w / 2, h / 2
    mark_w = titan_width(44)
    mark, _ = titan_group(cx - mark_w / 2, cy - 22, 44, p["titan"], simplify=True)
    # Baseline at r=36.5 puts the caps' outer edge (~7.5 px tall) near r=44,
    # clear of the inner ring at r=49 by 5 px, and of the outer ring at
    # r=53.8 by almost 10 px: a band of its own, not crowding either rule.
    ring_path = arc_path(cx, cy, 36.5, -125, 125)
    body = [
        f'<circle cx="{cx}" cy="{cy}" r="{w/2 - 1}" fill="{p["bg"]}"/>',
        f'<circle cx="{cx}" cy="{cy}" r="{w/2 - 2.2}" fill="none" '
        f'stroke="{p["edge"]}" stroke-width="1.6"/>',
        f'<circle cx="{cx}" cy="{cy}" r="{w/2 - 7}" fill="none" '
        f'stroke="{p["edge"]}" stroke-width="0.6"/>',
        f'<defs><path id="{uid}-ring" d="{ring_path}"/></defs>',
        textpath_el(f"{uid}-ring", 9.5, p["ink"], "TESTED WITH ATLAS", spacing=0.5),
        mark,
        # Mirrors the ring text's weight on the lower arc instead of a single dot.
        compass_star(cx, cy + 32, 6.5, 2.4, p["accent"]),
    ]
    return svg_doc(w, h, "".join(body)), w, h


def build_wide(p: dict, uid: str) -> tuple[str, float, float]:
    w, h = 600.0, 100.0
    mark, mark_w = titan_group(24, h / 2 - 33, 66, p["titan"])
    tx = 24 + mark_w + 26
    body = [
        f'<rect width="{w}" height="{h}" fill="{p["bg"]}"/>',
        f'<rect x="2" y="2" width="{w - 4}" height="{h - 4}" fill="none" '
        f'stroke="{p["edge"]}" stroke-width="1.6"/>',
        f'<line x1="{tx - 13}" y1="24" x2="{tx - 13}" y2="{h - 24}" '
        f'stroke="{p["accent"]}" stroke-width="1.4"/>',
        mark,
        text_el(tx, 50, 25, p["ink"], "TESTED WITH ATLAS", weight="700", spacing=2.4),
        text_el(tx, 74, 13, p["ink"], "integration tests on a real Vintage Story server",
                weight="400", spacing=0.2),
        # Balances the empty right third of the strip left by the text block.
        compass_star(w - 40, h / 2, 10, 3.6, p["accent"]),
    ]
    return svg_doc(w, h, "".join(body)), w, h


FORMATS = {
    "flat": build_flat,
    "plaque": build_plaque,
    "square": build_square,
    "seal": build_seal,
    "wide": build_wide,
}


# --- pipeline: text SVG -> inkscape (paths only) -> rsvg-convert (PNGs) -------
def render(fmt: str, palette: str) -> None:
    p = PALETTES[palette]
    raw_svg, w, h = FORMATS[fmt](p, f"{fmt}-{palette}")

    final_svg = OUT / f"tested-with-atlas-{fmt}-{palette}.svg"
    with tempfile.TemporaryDirectory() as tmp:
        raw_path = pathlib.Path(tmp) / f"raw-{fmt}-{palette}.svg"
        raw_path.write_text(raw_svg)
        subprocess.run(
            ["inkscape", "--export-type=svg", "--export-text-to-path",
             "--export-plain-svg", "-o", str(final_svg), str(raw_path)],
            check=True, capture_output=True,
        )

    text_left = re.search(r"<text[ >]", final_svg.read_text())
    assert not text_left, f"{final_svg.name} still has a <text> element"

    png1 = OUT / f"tested-with-atlas-{fmt}-{palette}.png"
    png2 = OUT / f"tested-with-atlas-{fmt}-{palette}@2x.png"
    subprocess.run(["rsvg-convert", "-w", str(round(w)), "-h", str(round(h)),
                    str(final_svg), "-o", str(png1)], check=True)
    subprocess.run(["rsvg-convert", "-w", str(round(w * 2)), "-h", str(round(h * 2)),
                    str(final_svg), "-o", str(png2)], check=True)

    for f in (final_svg, png1, png2):
        print(f"{f.name:42s} {f.stat().st_size / 1024:6.1f} KB")


def main() -> None:
    for fmt in FORMATS:
        for palette in PALETTES:
            render(fmt, palette)


if __name__ == "__main__":
    sys.exit(main())
