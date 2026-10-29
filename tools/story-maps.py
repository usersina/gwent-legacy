#!/usr/bin/env python3
"""The story's maps: The Witcher 3's own region maps, from witcher3map's tiles (github.com/witcher3map/witcher3map-maps,
CC BY-NC-SA; the maps are CD PROJEKT RED's), one zoom level stitched into one image per region, its empty margin
cropped, its longest side 4096 pixels and both sides multiples of 8: a texture compresses without mipmaps only when its
sides are multiples of 4, on Android and the web too, where it is capped at 2048 (exactly half: Editor/CardTextureImport.cs).
Into GwentCardGame/Assets/Content/Story/Maps/<map>.jpg (the credits beside the folder).

Each map's geometry goes into Resources/Story/story.json (its Maps), so the game places witcher3map's coordinates on it
(StoryMap.Position): witcher3map's flat maps (Velen, Toussaint: Leaflet's CRS.Simple) count 2^zoom pixels a unit from
the bottom tile row up; the others (White Orchard, Skellige) sit at the bottom left of a Web Mercator world of
256 * 2^zoom pixels. The tiles are gdal2tiles' TMS: row 0 at the bottom.

tools/story-maps.py <tiles dir: <map>/<zoom>/<x>/<y>.png> [zoom, default 4]
"""
import json
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "GwentCardGame/Assets/Content/Story/Maps")
CREDITS_FILE = os.path.join(ROOT, "GwentCardGame/Assets/Content/Story/CREDITS.md")
STORY = os.path.join(ROOT, "GwentCardGame/Assets/Resources/Story/story.json")
MAX = 4096
TILE = 256


def stitch(folder, zoom):
    columns = sorted(int(x) for x in os.listdir(os.path.join(folder, str(zoom))))
    tiles = {}
    for x in columns:
        for name in os.listdir(os.path.join(folder, str(zoom), str(x))):
            tiles[(x, int(name.split(".")[0]))] = os.path.join(folder, str(zoom), str(x), name)
    cols = max(x for x, _ in tiles) + 1
    rows = max(y for _, y in tiles) + 1
    canvas = Image.new("RGBA", (cols * TILE, rows * TILE), (0, 0, 0, 0))
    for (x, y), path in tiles.items():
        canvas.paste(Image.open(path).convert("RGBA"), (x * TILE, (rows - 1 - y) * TILE))
    return canvas, rows


def main(tiles, zoom):
    os.makedirs(OUT, exist_ok=True)
    story = json.load(open(STORY))
    for entry in story["Maps"]:
        folder = os.path.join(tiles, entry["Id"])
        if not os.path.isdir(folder):
            print("no tiles for", entry["Id"])
            continue
        canvas, rows = stitch(folder, zoom)
        left, top, right, bottom = canvas.getchannel("A").getbbox()
        image = canvas.crop((left, top, right, bottom)).convert("RGB")
        scale = MAX / max(image.size)
        width = int(round(image.size[0] * scale)) // 8 * 8
        height = int(round(image.size[1] * scale)) // 8 * 8
        image = image.resize((width, height), Image.LANCZOS)
        old = os.path.join(OUT, entry["Id"] + ".png")
        if os.path.exists(old):
            os.remove(old)
        image.save(os.path.join(OUT, entry["Id"] + ".jpg"), quality=90, optimize=True)  # a painting: a JPEG is a tenth of the PNG
        # Where the cropped image lies in the stitched one's pixels (the game scales them to the image it has)
        entry.update(Zoom=zoom, Rows=rows, Left=left, Top=top, Right=right, Bottom=bottom)
        print(entry["Id"], "%dx%d tiles of %d rows -> %dx%d" % (canvas.size[0] // TILE, rows, rows, width, height),
              "crop", (left, top, right, bottom))
    with open(STORY, "w") as f:
        json.dump(story, f, indent=1, ensure_ascii=False)
        f.write("\n")
    with open(CREDITS_FILE, "w") as f:
        f.write(CREDITS)


CREDITS = """# The Witcher 3's maps

These region maps are The Witcher 3: Wild Hunt's own (White Orchard; Velen and Novigrad with Hearts of Stone; Skellige;
Toussaint), property of CD PROJEKT RED, as extracted for the witcher3map project
(https://github.com/witcher3map/witcher3map, map tiles at https://github.com/witcher3map/witcher3map-maps), by untamed0
and its contributors, with thanks to DesignGears and hhrhhr for the extraction, licensed CC BY-NC-SA 4.0
(https://creativecommons.org/licenses/by-nc-sa/4.0/). Stitched from its zoom 4 tiles by tools/story-maps.py, and shared
under the same license. The story's places on them come from witcher3map's markers (tools/story-content.py).

Like the card art, they are used here in a free, non-commercial fan project and are not covered by this project's license.
"""

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    main(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 4)
