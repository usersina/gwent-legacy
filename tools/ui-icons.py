#!/usr/bin/env python3
"""UI icons drawn here, as the others are: white on transparent PNGs that the UI tints (gold, muted...), in
Assets/UI/Sprites, drawn four times larger and scaled down:

- IconClose.png (48): a cross, as IconCheck's stroke (the deck builder's trash while a deck is being named: leave it)
- IconNetwork.png (96): Wi-Fi arcs over a dot (Multiplayer: Local network)
- IconGlobe.png (96): a globe (Multiplayer: Online)
- IconZoomOut.png (96): a magnifier with a minus (the battle's zoom out, once a pinch zoomed the board in)
- IconTalk.png (96): a speech bubble with two lines of words (the mark on your portrait in a LAN battle: the quick chat)
- IconLock.png, IconUnlock.png (96): a padlock, shut or open (the zoomed board's lock: taps on the board do nothing)
- IconPlay.png, IconPause.png, IconStepBack.png, IconStepForward.png, IconRoundBack.png, IconRoundForward.png (96): a
  replay's controls (a triangle, two bars; a triangle against a bar for a move; two triangles against a bar for a round)
- IconSwitch.png (96): an arrow up beside an arrow down (a replay's switch to the other side's view: the board turns over)
- The story's map (96): IconCards.png, two cards fanned (a Gwent player; the collection); IconCoin.png, a coin with a
  crown struck on it (crowns; a shop); IconJournal.png, an open book (the quests); IconTravel.png, a signpost with two
  boards (travel to another region); IconChest.png, a small chest (a card to find); IconQuest.png, The Witcher 3's
  exclamation mark (a card or deck given); IconGoblet.png, a goblet (a tournament); IconDeck.png, three cards
  stacked (the story's decks)
- The end screen (96): IconAgain.png, a round arrow (play again, rematch, watch again); IconFilm.png, a film strip
  (save replay; the replays); IconHome.png, a house (the main menu); IconMap.png, a map folded in three (the story's map)

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


def media(kind, size=96):
    S = size * SCALE
    big = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    d = ImageDraw.Draw(big)
    cy, h = S * 0.5, S * 0.30          # the triangles' middle and half height
    bar = S * 0.085                    # a bar's width

    def triangle(tip_x, back_x):
        d.polygon([(back_x, cy - h), (tip_x, cy), (back_x, cy + h)], fill="white")

    def upright(x):
        d.rounded_rectangle((x - bar / 2, cy - h, x + bar / 2, cy + h), radius=bar * 0.3, fill="white")

    if kind == "play":
        triangle(S * 0.78, S * 0.30)
    elif kind == "pause":
        for x in (S * 0.38, S * 0.62):
            d.rounded_rectangle((x - bar * 0.8, cy - h, x + bar * 0.8, cy + h), radius=bar * 0.4, fill="white")
    elif kind == "step_forward":
        triangle(S * 0.64, S * 0.26)
        upright(S * 0.72)
    elif kind == "step_back":
        triangle(S * 0.36, S * 0.74)
        upright(S * 0.28)
    elif kind == "round_forward":
        triangle(S * 0.50, S * 0.20)
        triangle(S * 0.76, S * 0.46)
        upright(S * 0.82)
    elif kind == "round_back":
        triangle(S * 0.50, S * 0.80)
        triangle(S * 0.24, S * 0.54)
        upright(S * 0.18)
    names = {"play": "IconPlay.png", "pause": "IconPause.png", "step_forward": "IconStepForward.png",
             "step_back": "IconStepBack.png", "round_forward": "IconRoundForward.png", "round_back": "IconRoundBack.png"}
    save(big.getchannel("A"), size, names[kind])


def switch(size=96):
    S = size * SCALE
    big = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    d = ImageDraw.Draw(big)
    shaft = S * 0.085                  # as the media icons' bars
    head_w, head_h = S * 0.30, S * 0.22
    top, bottom = S * 0.16, S * 0.84

    def arrow(x, up):
        tip = top if up else bottom
        base = tip + head_h if up else tip - head_h
        end = bottom if up else top
        d.rounded_rectangle((x - shaft / 2, min(base, end) - 1, x + shaft / 2, max(base, end) + 1), radius=shaft * 0.3, fill="white")
        d.polygon([(x - head_w / 2, base), (x, tip), (x + head_w / 2, base)], fill="white")

    arrow(S * 0.33, True)
    arrow(S * 0.67, False)
    save(big.getchannel("A"), size, "IconSwitch.png")


def story(size=96):
    S = size * SCALE
    line = S * 0.075

    def canvas():
        big = Image.new("RGBA", (S, S), (255, 255, 255, 0))
        return big, ImageDraw.Draw(big)

    def card(d, cx, cy, w, h, angle, hollow):
        # A card turned about its centre: its outline (a hollow card shows the one under it)
        a = math.radians(angle)
        pts = [(cx + x * math.cos(a) - y * math.sin(a), cy + x * math.sin(a) + y * math.cos(a))
               for x, y in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2))]
        d.polygon(pts, fill=(255, 255, 255, 0) if hollow else "white")
        d.line(pts + [pts[0]], fill="white", width=int(line), joint="curve")

    # Two cards fanned, the front one solid with a cut-out diamond
    big, d = canvas()
    card(d, S * 0.40, S * 0.52, S * 0.40, S * 0.58, -14, True)
    card(d, S * 0.60, S * 0.48, S * 0.40, S * 0.58, 12, False)
    cx, cy, r = S * 0.60, S * 0.48, S * 0.08
    d.polygon([(cx, cy - r * 1.5), (cx + r, cy), (cx, cy + r * 1.5), (cx - r, cy)], fill=(255, 255, 255, 0))
    save(big.getchannel("A"), size, "IconCards.png")

    # A coin: a rim, and a crown struck in its middle
    big, d = canvas()
    d.ellipse((S * 0.12, S * 0.12, S * 0.88, S * 0.88), fill="white")
    d.ellipse((S * 0.20, S * 0.20, S * 0.80, S * 0.80), fill=(255, 255, 255, 0))
    d.ellipse((S * 0.25, S * 0.25, S * 0.75, S * 0.75), fill="white")
    crown = [(S * 0.34, S * 0.60), (S * 0.34, S * 0.40), (S * 0.42, S * 0.50), (S * 0.50, S * 0.36), (S * 0.58, S * 0.50),
             (S * 0.66, S * 0.40), (S * 0.66, S * 0.60)]
    d.polygon(crown, fill=(255, 255, 255, 0))
    save(big.getchannel("A"), size, "IconCoin.png")

    # An open book: two pages bent from the spine, lines of writing cut out
    big, d = canvas()
    for side in (-1, 1):
        x0, x1 = S * 0.5, S * 0.5 + side * S * 0.40
        d.polygon([(x0, S * 0.30), (x1, S * 0.22), (x1, S * 0.74), (x0, S * 0.82)], fill="white")
        for k in range(3):
            y = S * (0.38 + k * 0.11)
            xa, xb = S * 0.5 + side * S * 0.10, S * 0.5 + side * S * 0.32
            d.line((min(xa, xb), y + (0.02 * S if side > 0 else -0.0), max(xa, xb), y), fill=(255, 255, 255, 0), width=int(S * 0.035))
    d.line((S * 0.5, S * 0.30, S * 0.5, S * 0.82), fill=(255, 255, 255, 0), width=int(S * 0.03))
    save(big.getchannel("A"), size, "IconJournal.png")

    # A signpost: a pole, a board pointing right and one pointing left
    big, d = canvas()
    d.rectangle((S * 0.46, S * 0.12, S * 0.54, S * 0.90), fill="white")
    d.polygon([(S * 0.22, S * 0.20), (S * 0.70, S * 0.20), (S * 0.82, S * 0.29), (S * 0.70, S * 0.38), (S * 0.22, S * 0.38)], fill="white")
    d.polygon([(S * 0.78, S * 0.46), (S * 0.30, S * 0.46), (S * 0.18, S * 0.55), (S * 0.30, S * 0.64), (S * 0.78, S * 0.64)], fill="white")
    save(big.getchannel("A"), size, "IconTravel.png")

    # A small chest: its rounded lid, its body, a lock between them
    big, d = canvas()
    d.rounded_rectangle((S * 0.14, S * 0.26, S * 0.86, S * 0.50), radius=S * 0.12, fill="white")
    d.rectangle((S * 0.14, S * 0.54, S * 0.86, S * 0.82), fill="white")
    d.rectangle((S * 0.43, S * 0.44, S * 0.57, S * 0.62), fill=(255, 255, 255, 0))
    d.rectangle((S * 0.46, S * 0.47, S * 0.54, S * 0.59), fill="white")
    save(big.getchannel("A"), size, "IconChest.png")

    # The Witcher 3's exclamation mark: a tapering bar over a dot
    big, d = canvas()
    d.polygon([(S * 0.40, S * 0.12), (S * 0.60, S * 0.12), (S * 0.55, S * 0.64), (S * 0.45, S * 0.64)], fill="white")
    d.ellipse((S * 0.41, S * 0.72, S * 0.59, S * 0.90), fill="white")
    save(big.getchannel("A"), size, "IconQuest.png")

    # A goblet: its cup, its stem, its foot
    big, d = canvas()
    d.chord((S * 0.22, S * 0.02, S * 0.78, S * 0.58), 0, 180, fill="white")
    d.rectangle((S * 0.22, S * 0.14, S * 0.78, S * 0.30), fill="white")
    d.rectangle((S * 0.46, S * 0.56, S * 0.54, S * 0.78), fill="white")
    d.rounded_rectangle((S * 0.30, S * 0.76, S * 0.70, S * 0.88), radius=S * 0.04, fill="white")
    save(big.getchannel("A"), size, "IconGoblet.png")

    # A deck: three cards stacked square, the top one with its diamond
    big, d = canvas()
    for k in (2, 1):
        o = k * S * 0.07
        d.rounded_rectangle((S * 0.26 + o, S * 0.16 - o * 0.2, S * 0.70 + o, S * 0.80 - o * 0.2), radius=S * 0.05,
                            outline="white", width=int(line * 0.8))
    d.rounded_rectangle((S * 0.22, S * 0.20, S * 0.66, S * 0.84), radius=S * 0.05, fill="white")
    cx, cy, r = S * 0.44, S * 0.52, S * 0.08
    d.polygon([(cx, cy - r * 1.5), (cx + r, cy), (cx, cy + r * 1.5), (cx - r, cy)], fill=(255, 255, 255, 0))
    save(big.getchannel("A"), size, "IconDeck.png")


def end(size=96):
    S = size * SCALE
    clear = (255, 255, 255, 0)

    def canvas():
        big = Image.new("RGBA", (S, S), clear)
        return big, ImageDraw.Draw(big)

    # Again: an arc almost round (clockwise, from one o'clock to eleven), its arrowhead at the end pointing on round
    big, d = canvas()
    w = S * 0.10
    d.arc((S * 0.16, S * 0.16, S * 0.84, S * 0.84), 300, 245, fill="white", width=int(w))
    a = math.radians(245)
    r = S * 0.34 - w / 2
    px, py = S * 0.5 + r * math.cos(a), S * 0.5 + r * math.sin(a)
    tx, ty = -math.sin(a), math.cos(a)  # clockwise along the arc
    nx, ny = math.cos(a), math.sin(a)   # outwards
    head, half = S * 0.20, S * 0.15
    d.polygon([(px + tx * head, py + ty * head), (px + nx * half, py + ny * half), (px - nx * half, py - ny * half)], fill="white")
    save(big.getchannel("A"), size, "IconAgain.png")

    # A film strip: a band with its holes along both edges and two frames between them
    big, d = canvas()
    d.rounded_rectangle((S * 0.14, S * 0.18, S * 0.86, S * 0.82), radius=S * 0.06, fill="white")
    for k in range(5):
        x = S * (0.205 + k * 0.14)
        for y in (S * 0.235, S * 0.705):
            d.rectangle((x, y, x + S * 0.07, y + S * 0.06), fill=clear)
    for x in (S * 0.22, S * 0.52):
        d.rectangle((x, S * 0.35, x + S * 0.26, S * 0.65), fill=clear)
    save(big.getchannel("A"), size, "IconFilm.png")

    # Home: a roof over a house, its door cut out
    big, d = canvas()
    d.polygon([(S * 0.50, S * 0.12), (S * 0.90, S * 0.48), (S * 0.10, S * 0.48)], fill="white")
    d.rectangle((S * 0.22, S * 0.44, S * 0.78, S * 0.86), fill="white")
    d.rectangle((S * 0.43, S * 0.60, S * 0.57, S * 0.86), fill=clear)
    save(big.getchannel("A"), size, "IconHome.png")

    # A map folded in three, the middle panel set back (a route drawn across it would be too fine at this size)
    big, d = canvas()
    panels = [(0.12, 0.24, 0.38, 0.16), (0.38, 0.16, 0.62, 0.24), (0.62, 0.24, 0.88, 0.16)]
    for x0, y0, x1, y1 in panels:
        d.polygon([(S * x0, S * y0), (S * x1, S * y1), (S * x1, S * (y1 + 0.62)), (S * x0, S * (y0 + 0.62))], fill="white")
    for x in (0.38, 0.62):
        d.line((S * x, S * 0.16, S * x, S * 0.86), fill=clear, width=int(S * 0.035))
    save(big.getchannel("A"), size, "IconMap.png")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    close()
    network()
    globe()
    zoom_out()
    talk()
    padlock(True)
    padlock(False)
    for kind in ("play", "pause", "step_forward", "step_back", "round_forward", "round_back"):
        media(kind)
    switch()
    story()
    end()
    print("written to", OUT)
