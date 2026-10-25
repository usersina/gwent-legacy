#!/usr/bin/env python3
"""The game's app icons, made from its crown (Assets/Resources/Various/gwent_icon.png), with a gold contour:

- Assets/UI/AppIcon/AppIconDesktop.png (1024): Windows, macOS and Linux (Player Settings' default icon). Apple's icon
  grid: a rounded square (a superellipse) of 824 in the 1024 canvas, with its shadow, so macOS shows it as it is, not
  inside a grey plate. A thick gold frame, bevelled; the green field, lit in the middle; the crown on it.
- Assets/UI/AppIcon/AppIconBackground.png and AppIconForeground.png (432 each, 108 dp): Android's adaptive icon. The
  launcher cuts its own shape (a circle, a squircle...) out of the middle 72 dp: the background is gold all over, so that
  shape's edge is gold whatever it is; the foreground is the green disc (64 dp, inside the 66 dp every shape keeps) with
  the crown.
- Assets/WebGLTemplates/GwentLegacy (the web page): TemplateData/favicon.ico, the crown alone (16 to 64), faintly
  outlined so it reads on a light tab bar too; TemplateData/apple-touch-icon.png (180), Android's tile as a phone shows it,
  for a home screen; TemplateData/icon-192.png and icon-512.png, the same for the web app manifest (Android's home screen
  and its splash); thumbnail.png (128), the same, for the editor's template list.

tools/app-icons.py [project dir, default GwentCardGame]
"""
import math
import os
import random
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps

PROJECT = sys.argv[1] if len(sys.argv) > 1 else "GwentCardGame"
SOURCE = os.path.join(PROJECT, "Assets/Resources/Various/gwent_icon.png")
OUT = os.path.join(PROJECT, "Assets/UI/AppIcon")
WEB = os.path.join(PROJECT, "Assets/WebGLTemplates/GwentLegacy")

GOLD_LIGHT, GOLD_MID, GOLD_DARK = (250, 224, 136), (220, 176, 80), (146, 100, 34)
GREEN_CENTER, GREEN_EDGE = (20, 96, 74), (4, 34, 27)


# ---------------------------------------- The crown ----------------------------------------
def crown():
    """The crown and its gem out of the old icon: the field is dark green (little red, green above it), the crown gold,
    the gem red, blue and white. Alpha from the red, the blue above the green, the brightness, ramped: soft edges."""
    src = Image.open(SOURCE).convert("RGBA")
    w, h = src.size
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px, op = src.load(), out.load()

    def ramp(v, lo, hi):
        return 0.0 if v <= lo else 1.0 if v >= hi else (v - lo) / (hi - lo)

    for y in range(92, 413):  # the crown and its bars, clear of the old frame
        for x in range(70, 443):
            r, g, b, a = px[x, y]
            lum = 0.299 * r + 0.587 * g + 0.114 * b
            k = max(ramp(r - 0.35 * g, 6, 40), ramp(b - g, 18, 50), ramp(lum, 95, 140))
            op[x, y] = (r, g, b, int(255 * k * a / 255))
    alpha = out.getchannel("A").filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))  # specks of the field go
    out.putalpha(alpha)
    return out.crop(out.getbbox())


# ---------------------------------------- Materials ----------------------------------------
def diagonal(size):
    """0 in the top left corner to 255 in the bottom right"""
    g = Image.linear_gradient("L").resize((size * 2, size * 2)).rotate(45, resample=Image.BICUBIC)
    c = size // 2
    return g.crop((c, c, c + size, c + size))


def noise(size, sigma, seed):
    """Gaussian noise around 128, the same for the same seed (the icons come out the same every run)"""
    rng = random.Random(seed)
    w, h = size
    return Image.frombytes("L", size, bytes(max(0, min(255, int(rng.gauss(128, sigma)))) for _ in range(w * h)))


def mottle(size, scale, sigma, seed):
    """Soft noise around 128: blotches about size/scale wide"""
    small = max(2, size // scale)
    return noise((small, small), sigma, seed).resize((size, size), Image.BICUBIC)


def gold(size, seed=1):
    """Polished gold: lit from the top left, a broad sheen, fine brushed grain"""
    base = ImageOps.colorize(diagonal(size), black=GOLD_LIGHT, mid=GOLD_MID, white=GOLD_DARK)
    sheen = diagonal(size).point(lambda v: int(70 * max(0.0, 1 - abs(v - 96) / 70.0)))  # a light band across
    base = ImageChops.add(base, Image.merge("RGB", [sheen, sheen, sheen.point(lambda v: v // 2)]))
    blotch = mottle(size, 10, 24, seed)
    grain = noise((size, size // 3), 18, seed + 100).resize((size, size), Image.BILINEAR)  # stretched: brushed
    tone = ImageChops.add(blotch, grain, scale=2.0, offset=0)
    tone = tone.point(lambda v: int(128 + (v - 128) * 0.55))
    return ImageChops.overlay(base, Image.merge("RGB", [tone, tone, tone]))


def field(size, seed=2):
    """The green field: lit in the middle, darker to its edge, faintly clouded, a few specks of light"""
    radial = Image.radial_gradient("L").resize((size, size), Image.BICUBIC)
    base = ImageOps.colorize(radial, black=GREEN_CENTER, white=GREEN_EDGE)
    cloud = mottle(size, 6, 30, seed).point(lambda v: int(128 + (v - 128) * 0.35))
    base = ImageChops.overlay(base, Image.merge("RGB", [cloud, cloud, cloud]))
    random.seed(seed)
    stars = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(stars)
    for _ in range(int(size / 30)):
        x, y, r = random.uniform(0, size), random.uniform(0, size), random.uniform(0.6, 1.6) * size / 512
        d.ellipse((x - r, y - r, x + r, y + r), fill=random.randint(60, 140))
    stars = stars.filter(ImageFilter.GaussianBlur(size / 700))
    return Image.composite(Image.new("RGB", (size, size), (120, 220, 190)), base, stars)


# ---------------------------------------- Shapes ----------------------------------------
def superellipse(size, half, n=5.0, supersample=4):
    """Apple's rounded square, near enough: |x|^n + |y|^n <= half^n, centred, anti-aliased"""
    big = size * supersample
    c, a = big / 2, half * supersample
    pts = []
    for i in range(720):
        t = 2 * math.pi * i / 720
        ct, st = math.cos(t), math.sin(t)
        x = a * math.copysign(abs(ct) ** (2 / n), ct)
        y = a * math.copysign(abs(st) ** (2 / n), st)
        pts.append((c + x, c + y))
    m = Image.new("L", (big, big), 0)
    ImageDraw.Draw(m).polygon(pts, fill=255)
    return m.resize((size, size), Image.LANCZOS)


def disc(size, radius, supersample=4):
    big = size * supersample
    c, r = big / 2, radius * supersample
    m = Image.new("L", (big, big), 0)
    ImageDraw.Draw(m).ellipse((c - r, c - r, c + r, c + r), fill=255)
    return m.resize((size, size), Image.LANCZOS)


def grow(mask, px):
    return mask.filter(ImageFilter.MaxFilter(2 * px + 1)) if px > 0 else mask


def shrink(mask, px):
    return mask.filter(ImageFilter.MinFilter(2 * px + 1)) if px > 0 else mask


def band(outer, inner):
    return ImageChops.subtract(outer, inner)


def paint(canvas, color, mask, opacity=1.0):
    layer = Image.new("RGBA", canvas.size, color + (255,))
    layer.putalpha(mask.point(lambda v: int(v * opacity)))
    canvas.alpha_composite(layer)


def put(canvas, image, mask):
    layer = image.convert("RGBA")
    layer.putalpha(ImageChops.multiply(mask, layer.getchannel("A")))
    canvas.alpha_composite(layer)


def inner_field(canvas, inner, size, seed):
    """The green in its shape, its edge shadowed (a bevel's depth, not a black band), a dark seam against the gold"""
    put(canvas, field(size, seed), inner)
    depth = ImageChops.invert(inner).filter(ImageFilter.GaussianBlur(size * 0.03))
    paint(canvas, (0, 14, 10), ImageChops.multiply(depth, inner), 0.55)
    paint(canvas, (40, 26, 6), band(grow(inner, max(1, size // 300)), inner), 0.9)


def place_crown(canvas, crown_img, width, center, size, sharpen):
    scale = width / crown_img.width
    c = crown_img.resize((round(crown_img.width * scale), round(crown_img.height * scale)), Image.LANCZOS)
    if sharpen:
        rgb = c.convert("RGB").filter(ImageFilter.UnsharpMask(radius=1.2, percent=55, threshold=2))
        rgb.putalpha(c.getchannel("A"))
        c = rgb
    x, y = round(center[0] - c.width / 2), round(center[1] - c.height / 2)
    shadow = Image.new("L", canvas.size, 0)
    shadow.paste(c.getchannel("A"), (x, y + round(size * 0.012)))
    paint(canvas, (0, 0, 0), shadow.filter(ImageFilter.GaussianBlur(size * 0.012)), 0.6)
    canvas.alpha_composite(c, (x, y))


# ---------------------------------------- The icons ----------------------------------------
def desktop(crown_img, size=1024):
    """Apple's grid: the shape 824 of 1024 (100 around), its shadow below it"""
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    half = size * 412 / 1024
    rim = size * 50 / 1024
    shape = superellipse(size, half)
    inner = superellipse(size, half - rim)
    drop = Image.new("L", (size, size), 0)
    drop.paste(shape, (0, round(size * 0.012)))
    paint(canvas, (0, 0, 0), drop.filter(ImageFilter.GaussianBlur(size * 0.02)), 0.4)
    put(canvas, gold(size), shape)
    # The frame's bevel: a darker outer edge, a lit ridge along it, a lit lip by the field
    edge = max(2, size // 340)
    paint(canvas, GOLD_DARK, band(shape, shrink(shape, edge)), 0.85)
    paint(canvas, (255, 246, 200), band(shrink(shape, edge * 2), shrink(shape, edge * 3)), 0.55)
    paint(canvas, (255, 240, 180), band(grow(inner, edge * 2), grow(inner, edge)), 0.6)
    inner_field(canvas, inner, size, 2)
    place_crown(canvas, crown_img, (half - rim) * 2 * 0.8, (size / 2, size / 2 + size * 0.004), size, True)
    return canvas


def android_background(size=432):
    """Gold all over, deepening towards the edge of what a launcher shows (its shape's contour, darker gold)"""
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 255))
    canvas.alpha_composite(gold(size, 3).convert("RGBA"))
    viewport = size * 72 / 108 / 2
    edge = Image.radial_gradient("L").resize((size, size), Image.BICUBIC)  # 0 in the middle, 255 at half the size
    edge = edge.point(lambda v: int(255 * min(1.0, max(0.0, (v / 255 * size / 2 - viewport * 0.72) / (viewport * 0.3)))))
    paint(canvas, (96, 60, 14), edge, 0.6)
    return canvas


def android_foreground(crown_img, size=432):
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    radius = size * 128 / 432  # 64 dp across
    inner = disc(size, radius)
    # The disc sits in the gold: its shadow on the gold, a lit lip around it
    edge = max(2, size // 160)
    paint(canvas, (70, 44, 8), band(grow(inner, edge * 3), inner).filter(ImageFilter.GaussianBlur(edge)), 0.55)
    paint(canvas, (255, 240, 180), band(grow(inner, edge * 2), grow(inner, edge)), 0.7)
    inner_field(canvas, inner, size, 4)
    place_crown(canvas, crown_img, radius * 2 * 0.76, (size / 2, size / 2 + size * 0.006), size, False)
    return canvas


def favicon(crown_img, size=256):
    """The crown alone, as wide as the square, a faint dark edge around it"""
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    scale = size * 0.96 / crown_img.width
    c = crown_img.resize((round(crown_img.width * scale), round(crown_img.height * scale)), Image.LANCZOS)
    x, y = (size - c.width) // 2, (size - c.height) // 2
    silhouette = Image.new("L", (size, size), 0)
    silhouette.paste(c.getchannel("A"), (x, y))
    paint(canvas, (52, 32, 6), grow(silhouette, max(1, size // 64)).filter(ImageFilter.GaussianBlur(size / 256)), 0.75)
    canvas.alpha_composite(c, (x, y))
    return canvas


def tile(background, foreground, size):
    """Android's icon as a launcher shows it (the middle 72 dp of 108), square and opaque"""
    t = background.copy()
    t.alpha_composite(foreground)
    v = t.width * 72 / 108 / 2
    c = t.width / 2
    return t.crop((round(c - v), round(c - v), round(c + v), round(c + v))).resize((size, size), Image.LANCZOS).convert("RGB")


if __name__ == "__main__":
    c = crown()
    os.makedirs(OUT, exist_ok=True)
    desktop(c).save(os.path.join(OUT, "AppIconDesktop.png"))
    background, foreground = android_background(), android_foreground(c)
    background.convert("RGB").save(os.path.join(OUT, "AppIconBackground.png"))
    foreground.save(os.path.join(OUT, "AppIconForeground.png"))
    os.makedirs(os.path.join(WEB, "TemplateData"), exist_ok=True)
    favicon(c).save(os.path.join(WEB, "TemplateData", "favicon.ico"), sizes=[(16, 16), (32, 32), (48, 48), (64, 64)])
    tile(background, foreground, 180).save(os.path.join(WEB, "TemplateData", "apple-touch-icon.png"))
    for size in (192, 512):
        tile(background, foreground, size).save(os.path.join(WEB, "TemplateData", "icon-%d.png" % size))
    tile(background, foreground, 128).save(os.path.join(WEB, "thumbnail.png"))
    print("written to", OUT, "and", WEB)
