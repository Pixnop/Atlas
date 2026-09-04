# Mod DB page assets

Six images backing the redesigned Mod DB page, all in an old-map register:
parchment ground, sepia and cartographic inks, gold used sparingly. Everything
is generated procedurally by `generate.py`, apart from the titan, which is
rasterised from the repository's own `docs/assets/logo.svg` and tinted. Nothing
here was downloaded, so there is no third party licensing to track.

## Files

| File | Size | Use on the page |
| --- | --- | --- |
| `parchment-tile.png` | 512x512, 144 KB | Page background, `background-repeat: repeat`. Seamless on both axes. |
| `compass-rose.png` | 480x480, 25 KB | Section marker or empty state mark. Drawn for display at 120 to 200 px. |
| `divider-ornament.png` | 800x40, 1 KB | Horizontal rule between sections, centred, transparent background. |
| `banner-atlas.png` | 1200x320, 133 KB | Static page header. The middle is deliberately empty: the title is HTML text on top, not baked into the image. |
| `banner-atlas.gif` | 1200x320, 155 KB | Animated variant of the same header, 12 frames at 220 ms. The magnetic needle swings 5.5 degrees either side of north and nothing else moves. |
| `titan-sepia.png` | 400x400, 6 KB | The logo on its own, transparent background, for a card or a footer mark. |

The parchment holds a worst case contrast ratio of 9.2:1 against the dark ink,
so body text in `#3B2A1A` clears WCAG AA and AAA on it.

## Palette

| Role | Hex |
| --- | --- |
| Parchment, nominal | `#F3E6C9` |
| Parchment, shaded | `#D6BE92` |
| Parchment, highlight | `#FAF1DC` |
| Dark sepia ink | `#3B2A1A` |
| Diluted ink, hatching | `#6E5434` |
| Faded red brown, north point and needle | `#9C3B2E` |
| Deep blue teal, graticule | `#1F4E5A` |
| Gold leaf | `#B0862B` |
| Warm ochre, the earth in the titan's cube | `#A37837` |

## Regenerating

```sh
python3 docs/assets/moddb/generate.py
```

Needs Pillow, numpy and `rsvg-convert`, plus the URW Palatino clone
(`/usr/share/fonts/gsfonts/P052-Roman.otf`) for the N/E/S/W letters on the rose.
The letters are skipped if that font is missing, everything else still renders.
Output goes next to the script, overwriting the six files above.

The generator is seeded, so a rerun reproduces the same images byte for byte. It
also asserts that the parchment tile still wraps cleanly, and fails loudly if a
change puts a seam back in.
