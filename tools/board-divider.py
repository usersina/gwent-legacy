#!/usr/bin/env python3
"""The lines that divide the battle's board, drawn four times larger and scaled down: a shallow groove in the table with a
thin iron rod laid in it, as The Witcher 3's board has (a shadow along its top, a lit lip along its foot, a faint gold sheen
on the rod as the rows' gold edges). Into GwentCardGame/Assets/UI/Board:

- BoardDivider.png (1024x16): between the two halves of the board, fading out at both ends. A LAN battle's turn clock burns
  along it, and a replay's progress runs on it.
- BoardDividerVertical.png (1024x24): between the left column and the rows, from the top of the screen to its foot (no
  fade); the scene turns it upright (its top to the left). Along its top, a gold hairline on a dark edge: where the
  players' bands of the left column (and their turn's glowing lines) stop.

tools/board-divider.py [project dir, default GwentCardGame]
"""
import os
import sys

from PIL import Image, ImageDraw

PROJECT = sys.argv[1] if len(sys.argv) > 1 else "GwentCardGame"
OUT = os.path.join(PROJECT, "Assets/UI/Board")
SCALE = 4


# A divider width x height; its profile drawn for a height of 16 and scaled to the height; each end fading out over fade;
# stop: a gold hairline on a dark edge along its top
def divider(name, width, height, fade, stop=False):
    W, H = width * SCALE, height * SCALE
    k = height / 16 * SCALE                   # a profile pixel, in drawing pixels
    big = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)

    def band(top, bottom, colour):
        d.rectangle((0, round(top * k), W, round(bottom * k) - 1), fill=colour)

    # The groove: darker towards its top (the light comes from above), a sharp shadow at its top edge, a lit lip at its foot
    for y in range(round(3 * k), round(13 * k)):
        t = (y - 3 * k) / (10 * k)
        d.line((0, y, W, y), fill=(10, 5, 2, int(180 - 100 * t)))
    band(2, 3, (6, 3, 1, 190))
    band(13, 14, (231, 196, 128, 60))
    # The rod: dark iron, rounded by a highlight along its top and a shade along its foot, a faint gold sheen
    band(6.5, 9.5, (44, 38, 32, 245))
    band(6.5, 7.25, (196, 164, 104, 170))
    band(8.75, 9.5, (8, 5, 3, 200))
    if stop:
        # The edge: dark from the hairline to the groove, the hairline as the bands' lines (the theme's gold)
        band(0, 3, (8, 4, 2, 225))
        d.rectangle((0, 0, W, SCALE - 1), fill=(201, 164, 92, 175))

    if fade > 0:
        alpha = big.getchannel("A")
        mask = Image.new("L", (W, 1), 255)
        for x in range(fade * SCALE):
            v = int(255 * (x / (fade * SCALE)) ** 1.5)
            mask.putpixel((x, 0), v)
            mask.putpixel((W - 1 - x, 0), v)
        mask = mask.resize((W, H))
        big.putalpha(Image.composite(alpha, Image.new("L", (W, H), 0), mask))
    big.resize((width, height), Image.LANCZOS).save(os.path.join(OUT, name))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    divider("BoardDivider.png", 1024, 16, 56)
    divider("BoardDividerVertical.png", 1024, 24, 0, stop=True)
    print("written to", OUT)
