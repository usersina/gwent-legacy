#!/usr/bin/env python3
"""UI icons drawn here, as the others are: white on transparent PNGs that the UI tints (gold, muted...), in
Assets/UI/Sprites, drawn four times larger and scaled down:

- IconClose.png (48): a cross, as IconCheck's stroke (the deck builder's trash while a deck is being named: leave it)
- IconNetwork.png (96): Wi-Fi arcs over a dot (Multiplayer: Local network)
- IconGlobe.png (96): a globe (Multiplayer: Online)
- IconZoomOut.png (96): a magnifier with a minus (the battle's zoom out, once a pinch zoomed the board in)
- IconTalk.png (96): a speech bubble with two lines of words (the mark on your portrait in a LAN battle: the quick chat)
- IconLock.png, IconUnlock.png (96): a padlock, shut or open (the zoomed board's lock: taps on the board do nothing)

tools/ui-icons.py [project dir, default GwentCardGame]
"""
import math
import os
import sys

from PIL import Image, ImageDraw

PROJECT = sys.argv[1] if len(sys.argv) > 1 else "GwentCardGame"
OUT = os.path.join(PROJECT, "Assets/UI/Sprites")
SCALE = 4


def save(alpha, size, name):
    icon = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    icon.putalpha(alpha.resize((size, size), Image.LANCZOS))
    icon.save(os.path.join(OUT, name))


def close(size=48):
    s = SCALE
    big = Image.new("L", (size * s, size * s), 0)
    d = ImageDraw.Draw(big)
    w = int(4.6 * s)
    lo, hi = 13 * s, 35 * s
    for (x1, y1, x2, y2) in ((lo, lo, hi, hi), (hi, lo, lo, hi)):
        d.line((x1, y1, x2, y2), fill=255, width=w)
        for x, y in ((x1, y1), (x2, y2)):
            d.ellipse((x - w / 2, y - w / 2, x + w / 2, y + w / 2), fill=255)  # round ends
    save(big, size, "IconClose.png")


def network(size=96):
    S = size * SCALE
    big = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    d = ImageDraw.Draw(big)
    cx, cy = S / 2, S * 0.80
    w = S * 0.085
    d.ellipse((cx - w * 1.15, cy - w * 1.15, cx + w * 1.15, cy + w * 1.15), fill="white")
    for r in (S * 0.24, S * 0.42, S * 0.60):
        d.arc((cx - r, cy - r, cx + r, cy + r), start=225, end=315, fill="white", width=int(w))
    for r in (S * 0.24, S * 0.42, S * 0.60):  # round ends
        rr = r - w / 2
        for a in (225, 315):
            x, y = cx + rr * math.cos(math.radians(a)), cy + rr * math.sin(math.radians(a))
            d.ellipse((x - w / 2, y - w / 2, x + w / 2, y + w / 2), fill="white")
    save(big.getchannel("A"), size, "IconNetwork.png")


def globe(size=96):
    S = size * SCALE
    big = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    d = ImageDraw.Draw(big)
    m = S * 0.08
    w = int(S * 0.06)
    r = (S - 2 * m) / 2
    c = S / 2
    d.ellipse((m, m, S - m, S - m), outline="white", width=w)
    d.ellipse((c - r * 0.45, m, c + r * 0.45, S - m), outline="white", width=w)  # a meridian
    d.line((c, m + w / 2, c, S - m - w / 2), fill="white", width=w)
    d.line((m + w / 2, c, S - m - w / 2, c), fill="white", width=w)  # the equator
    for dy in (-r * 0.5, r * 0.5):  # two parallels
        half = math.sqrt(r * r - dy * dy) - w * 0.6
        d.line((c - half, c + dy, c + half, c + dy), fill="white", width=w)
    save(big.getchannel("A"), size, "IconGlobe.png")


def zoom_out(size=96):
    S = size * SCALE
    big = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    d = ImageDraw.Draw(big)
    w = int(S * 0.085)
    c, r = S * 0.42, S * 0.27  # the lens, up and left, its handle down to the right
    d.ellipse((c - r, c - r, c + r, c + r), outline="white", width=w)
    a = math.radians(45)
    x1, y1 = c + (r - w * 0.2) * math.cos(a), c + (r - w * 0.2) * math.sin(a)
    x2, y2 = S * 0.84, S * 0.84
    hw = int(w * 1.35)
    d.line((x1, y1, x2, y2), fill="white", width=hw)
    d.ellipse((x2 - hw / 2, y2 - hw / 2, x2 + hw / 2, y2 + hw / 2), fill="white")  # round end
    m = r * 0.52
    d.line((c - m, c, c + m, c), fill="white", width=w)
    for x in (c - m, c + m):
        d.ellipse((x - w / 2, c - w / 2, x + w / 2, c + w / 2), fill="white")
    save(big.getchannel("A"), size, "IconZoomOut.png")


def talk(size=96):
    S = size * SCALE
    big = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    d = ImageDraw.Draw(big)
    w = int(S * 0.08)
    x1, y1, x2, y2 = S * 0.14, S * 0.18, S * 0.86, S * 0.66  # the bubble, its tail down on the left
    d.rounded_rectangle((x1, y1, x2, y2), radius=S * 0.12, outline="white", width=w)
    tail = [(S * 0.26, y2 - w / 2), (S * 0.24, S * 0.86), (S * 0.46, y2 - w / 2)]
    d.polygon(tail, fill="white")
    for y, right in ((S * 0.36, 0.70), (S * 0.50, 0.56)):  # two lines of words
        d.line((S * 0.30, y, S * right, y), fill="white", width=int(w * 0.85))
        for x in (S * 0.30, S * right):
            r = w * 0.85 / 2
            d.ellipse((x - r, y - r, x + r, y + r), fill="white")
    save(big.getchannel("A"), size, "IconTalk.png")


def padlock(shut, size=96):
    S = size * SCALE
    big = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    d = ImageDraw.Draw(big)
    w = int(S * 0.09)
    x1, y1, x2, y2 = S * 0.22, S * 0.46, S * 0.78, S * 0.88  # the body
    d.rounded_rectangle((x1, y1, x2, y2), radius=S * 0.08, fill="white")
    r = S * 0.19  # the shackle: an arch over the body, lifted out of its left hole when open
    cx = S * 0.5
    top = S * 0.14 if shut else S * 0.06
    d.arc((cx - r, top, cx + r, top + 2 * r), 180, 360, fill="white", width=w)
    legs = (cx - r + w / 2, cx + r - w / 2) if shut else (cx + r - w / 2,)
    for x in legs:
        d.line((x, top + r, x, y1 + w), fill="white", width=w)
    if not shut:
        x = cx - r + w / 2
        d.line((x, top + r, x, top + r + S * 0.06), fill="white", width=w)
        d.ellipse((x - w / 2, top + r + S * 0.06 - w / 2, x + w / 2, top + r + S * 0.06 + w / 2), fill="white")
    k = S * 0.05  # the keyhole, cut out
    cy = (y1 + y2) / 2 - S * 0.02
    d.ellipse((S / 2 - k, cy - k, S / 2 + k, cy + k), fill=(0, 0, 0, 0))
    d.rectangle((S / 2 - k * 0.45, cy, S / 2 + k * 0.45, cy + k * 2.2), fill=(0, 0, 0, 0))
    save(big.getchannel("A"), size, "IconLock.png" if shut else "IconUnlock.png")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    close()
    network()
    globe()
    zoom_out()
    talk()
    padlock(True)
    padlock(False)
    print("written to", OUT)
