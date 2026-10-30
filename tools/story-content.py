#!/usr/bin/env python3
"""The story's content, drafted from The Witcher 3's own data: who plays Gwent where, what each one sells or gives, the
Gwent quests and tournaments, and where every copy of every card comes from.

Its sources, read into a cache folder by `fetch` (never at build time):
- the Witcher wiki (witcher.fandom.com, CC BY-SA): each card's page (its infobox's `source` and `value`), the "Gwent
  players" table (where each player is, their deck, whether they have a unique card) and the Gwent quests' pages;
- witcher3map (github.com/witcher3map/witcher3map, CC BY-NC-SA): each map's markers (mapdata-<map>.js) and their English
  labels, for where the players stand.

What is curated by hand is below: the regions, the players (a name, a trade, a faction from the wiki's table, a tier, a
place on the map, when they appear), which player each source sentence of the wiki means, and the quests. `build`
joins them and checks the result: every copy of every card has one source, nothing can be missed.

tools/story-content.py fetch <cache dir>
tools/story-content.py build <cache dir> <draft.json> [GwentCardGame/Assets/Resources/Story/story.json]
"""
import json
import math
import os
import re
import sys
import time
import unicodedata
import urllib.parse
import urllib.request
from collections import Counter, defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CARDS_DB = os.path.join(ROOT, "GwentCardGame/Assets/Resources/Cards/cards_db.json")
WIKI = "https://witcher.fandom.com/api.php"
MAPS_RAW = "https://raw.githubusercontent.com/witcher3map/witcher3map/master/assets/"
AGENT = "Mozilla/5.0 (X11; Linux x86_64) gwent-legacy story content"
MAPS = ["white_orchard", "hos_velen", "skellige", "toussaint"]
LOCALES = ["general", "v", "s", "t", "w"]

# ---------------------------------------------------------------------------------------------------------------------
# Fetching

def http(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": AGENT}), timeout=30) as r:
        return r.read()


def wiki(params):
    return json.loads(http(WIKI + "?" + urllib.parse.urlencode(dict(params, format="json"))))


def category(name):
    titles, more = [], {}
    while True:
        j = wiki(dict(action="query", list="categorymembers", cmtitle=name, cmlimit=500, **more))
        titles += [m["title"] for m in j["query"]["categorymembers"] if m["ns"] == 0]
        if "continue" not in j:
            return titles
        more = {"cmcontinue": j["continue"]["cmcontinue"]}


def fetch(cache):
    os.makedirs(cache, exist_ok=True)
    titles = []
    for name in ["Category:The Witcher 3 gwent", "Category:Hearts of Stone gwent", "Category:Blood and Wine gwent"]:
        titles += category(name)
    titles += ["Gwent players", "A Dangerous Game", "Gwent: Skellige Style", "Gwent: Big City Players", "Gwent: Old Pals",
               "Gwent: Velen Players", "Gwent: Playing Innkeeps", "High Stakes", "Gwent: Never Fear, Skellige's Here",
               "Gwent: To Everything - Turn, Turn, Tournament!"]
    pages = {}
    for i in range(0, len(titles), 50):
        j = wiki(dict(action="query", prop="revisions", rvprop="content", rvslots="main", redirects=1,
                      titles="|".join(titles[i:i + 50])))
        for page in j["query"]["pages"].values():
            if "revisions" in page:
                pages[page["title"]] = page["revisions"][0]["slots"]["main"]["*"]
        time.sleep(1)
    json.dump(pages, open(os.path.join(cache, "pages.json"), "w"), indent=0)
    for m in MAPS:
        open(os.path.join(cache, "mapdata-" + m + ".js"), "wb").write(http(MAPS_RAW + "scripts/custom/mapdata-" + m + ".js"))
    for loc in LOCALES:
        open(os.path.join(cache, loc + ".json"), "wb").write(http(MAPS_RAW + "locales/en/" + loc + ".json"))
    print("cached", len(pages), "pages and", len(MAPS), "maps in", cache)

# ---------------------------------------------------------------------------------------------------------------------
# Reading the cache

def plain(text):
    text = re.sub(r"<gallery>.*?</gallery>", "", text, flags=re.S)
    text = re.sub(r"\[\[(?:[^|\]]*\|)?([^\]]*)\]\]", r"\1", text)
    text = re.sub(r"<br\s*/?>", " / ", text)
    text = re.sub(r"<[^>]+>", "", text)
    text = re.sub(r"\{\{[^}]*\}\}", "", text)
    return " ".join(text.replace("&amp;", "&").split()).strip()


def infobox(text, name):
    # Up to the next field or the box's end (an inline "{{HoS}}" in a value is no end)
    m = re.search(r"\|\s*" + name + r"\s*=(.*?)(?=\n\s*\||\n\}\}|\}\}\s*'''|\}\}\n)", text, re.S)
    return plain(m.group(1)) if m else ""


def key(name):
    name = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode().lower()
    name = re.sub(r"\(gwent card\)", "", name)
    name = re.sub(r"[^a-z0-9]+", " ", name).strip()
    return re.sub(r"\s+\d+$", "", name).replace(" ", "")


def wiki_cards(pages):
    cards = {}
    for title, text in pages.items():
        if "Infobox Gwent Card" in text:
            name = infobox(text, "name") or title
            cards[key(name)] = dict(name=name, source=infobox(text, "source"), value=infobox(text, "value"))
    return cards


def map_markers(cache, name):
    locale = {loc: json.load(open(os.path.join(cache, loc + ".json"))) for loc in LOCALES}

    def label(expr):
        def one(k):
            ns, _, path = k.rpartition(":")
            node = locale[ns or "general"]
            for part in path.split("."):
                node = node.get(part, k) if isinstance(node, dict) else k
            return node if isinstance(node, str) else k
        parts = re.findall(r'\$\.t\("([^"]+)"\)|\'([^\']*)\'', expr or "")
        return re.sub(r"<[^>]+>", "", "".join(one(a) if a else b for a, b in parts)).replace("&#39;", "'").replace("&amp;", "&").strip()

    js = open(os.path.join(cache, "mapdata-" + name + ".js")).read()
    sw = [float(v) for v in re.search(r"map_sWest\s*=\s*L\.latLng\(([-\d.]+),\s*([-\d.]+)\)", js).groups()]
    ne = [float(v) for v in re.search(r"map_nEast\s*=\s*L\.latLng\(([-\d.]+),\s*([-\d.]+)\)", js).groups()]
    markers = []
    heads = [(m.start(), m.group(1)) for m in re.finditer(r"\n\t(\w+): \[", js)]
    for i, (start, cat) in enumerate(heads):
        block = js[start: heads[i + 1][0] if i + 1 < len(heads) else len(js)]
        for obj in re.split(r"\n\t\}, ?\{|\n\t\}\]", block):
            at = obj.find("coords:")
            if at < 0:
                continue
            first = obj.index("[", at)
            depth = 0
            for last in range(first, len(obj)):
                depth += {"[": 1, "]": -1}.get(obj[last], 0)
                if depth == 0:
                    break
            title = re.search(r"popupTitle:\s*(.*?),?\s*\n", obj)
            text = re.search(r"\blabel:\s*(.*?),?\s*\n", obj)
            for lat, lng in re.findall(r"\[\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*,?\s*\]", obj[first + 1:last]):
                markers.append(dict(cat=cat, lat=float(lat), lng=float(lng), label=label(text.group(1) if text else ""),
                                    title=label(title.group(1) if title else "")))
    signs = [m for m in markers if m["cat"] == "signpost"]
    for m in markers:
        nearest = min(signs, key=lambda s: math.hypot(m["lat"] - s["lat"], m["lng"] - s["lng"]))
        m["near"] = nearest["label"]
    return dict(sw=sw, ne=ne, simple=name in ("hos_velen", "toussaint"), markers=markers)

# ---------------------------------------------------------------------------------------------------------------------
# The regions, in The Witcher 3's order: each opens when the step before it is done

REGIONS = [
    dict(id="white_orchard", name="White Orchard", map="white_orchard", opens=None),
    dict(id="vizima", name="Royal Palace in Vizima", map=None, opens="beat:vizima_nobleman_open"),
    dict(id="velen", name="Velen & Novigrad", map="hos_velen", opens="beat:vizima_nobleman"),
    dict(id="skellige", name="Skellige", map="skellige", opens="quest:velen_players"),
    dict(id="toussaint", name="Toussaint", map="toussaint", opens="quest:high_stakes"),
]
REGIONS[1]["opens"] = "beat:wo_scholar"

# Crowns a first win pays, by tier (a rematch pays a quarter); a tournament's round pays more
TIER_CROWNS = {1: 15, 2: 30, 3: 50}
ROUND_CROWNS = 60
DIFFICULTY = {1: "Easy", 2: "Normal", 3: "Hard"}

# The Witcher 3's masters of the game, and its tournaments: in the last tier they alone play Hard; the late game's
# innkeepers, merchants and craftsmen play Normal (with the last tier's decks and crowns)
MASTERS = {"roche", "zoltan", "thaler", "lambert", "vimme_vivaldi", "marquise_serenity", "dijkstra", "earl", "robert_hilbert",
           "shani", "olgierd", "ermion", "gremist", "crach", "sjusta", "madman_lugos", "masquerade", "high_stakes",
           "beauclair_tournament"}

# ---------------------------------------------------------------------------------------------------------------------
# The players. kind: player (plays Gwent), shop (sells only), found (a card to pick up), event (a card or deck given),
# tournament (rounds of battles, with an entry fee). at: where on the map, as witcher3map's markers:
#   ("gwent", near, title[, n])  the nth Gwent-player marker near that signpost whose title has that word
#   ("cat", category, label)     a marker of that category with that label (an inn, a brothel, a point of interest)
#   ("sign", signpost, dlat, dlng)  next to a signpost (approximate)
#   ("at", lat, lng)             a position read off the map (approximate)
# appears: when the marker shows (None: when the region opens), "beat:<id>" or "quest:<id>".

def P(id, region, name, trade, faction, tier, at, kind="player", appears=None, rounds=None, fee=0, deck=None, note=""):
    return dict(id=id, region=region, name=name, trade=trade, faction=faction, tier=tier, at=at, kind=kind,
                appears=appears, rounds=rounds or [], fee=fee, deck=deck, note=note)


PLAYERS = [
    # White Orchard
    P("wo_scholar", "white_orchard", "Aldert Geert", "a scholar at the White Orchard inn", "NF", 1, ("gwent", "Woesong Bridge", "")),
    P("wo_elsa", "white_orchard", "Elsa", "innkeeper of the White Orchard inn", None, 1, ("cat", "innkeep", "White Orchard Inn"), kind="shop"),
    # Vizima
    P("vizima_nobleman", "vizima", "A Nilfgaardian nobleman", "in the royal palace's garden", "NF", 1, None),
    # Velen: its players of no particular renown
    P("blackbough_merchant", "velen", "The merchant of Blackbough", "merchant", "NF", 1, ("gwent", "Blackbough", "Shopkeeper")),
    P("blackbough_blacksmith", "velen", "The blacksmith of Blackbough", "blacksmith", "SC", 1, ("gwent", "Blackbough", "Blacksmith")),
    P("claywich_merchant", "velen", "The merchant of Claywich", "craftsman", "NR", 1, ("gwent", "Claywich", "Shopkeeper")),
    P("crows_perch_merchant", "velen", "The trader of Crow's Perch", "trader", "NR", 1, ("gwent", "Crow's Perch", "Shopkeeper")),
    P("crows_perch_quartermaster", "velen", "The quartermaster of Crow's Perch", "quartermaster", "NR", 1, ("gwent", "Crow's Perch", "Quartermaster")),
    P("yoana", "velen", "Yoana", "the armorer's assistant at Crow's Perch", "M", 1, ("gwent", "Burned Ruins", "Yoana")),
    P("fergus_graem", "velen", "Fergus Graem", "blacksmith at Crow's Perch", "SC", 1, ("gwent", "Burned Ruins", "Shopkeeper")),
    P("downwarren_merchant", "velen", "The merchant of Downwarren", "merchant", "M", 1, ("gwent", "Downwarren", "")),
    P("lindenvale_blacksmith", "velen", "The blacksmith of Lindenvale", "blacksmith", "SC", 1, ("gwent", "Lindenvale", "Blacksmith")),
    P("lindenvale_merchant", "velen", "The merchant of Lindenvale", "merchant", "NR", 1, ("gwent", "Lindenvale", "Shopkeeper")),
    P("midcopse_armorer", "velen", "The armorer of Midcopse", "armorer", "NF", 1, ("gwent", "Midcopse", "Armorer")),
    P("midcopse_merchant", "velen", "The merchant of Midcopse", "merchant", "M", 1, ("gwent", "Midcopse", "Shopkeeper")),
    P("mulbrydale_armorer", "velen", "The swordsmith of Mulbrydale", "armorer", "M", 1, ("gwent", "Mulbrydale", "Armorer")),
    P("quartermaster_eggebracht", "velen", "Quartermaster Eggebracht", "of the Nilfgaardian army camp", "NR", 1, ("gwent", "Nilfgaardian Army Group 'Center' Camp", "Quartermaster")),
    P("oreton_merchant", "velen", "The merchant of Oreton", "merchant", "NR", 1, ("gwent", "Oreton", "Shopkeeper")),
    P("cunny_innkeep", "velen", "The innkeeper of the Cunny of the Goose", "innkeeper", "SC", 2, ("gwent", "Cunny of the Goose", "Innkeeper")),
    # Velen: its renowned players (Gwent: Velen Players; Gwent: Playing Innkeeps)
    P("bloody_baron", "velen", "The Bloody Baron", "Phillip Strenger, at Crow's Perch", "NR", 2, ("gwent", "Crow's Perch", "Bloody Baron")),
    P("haddy", "velen", "Haddy", "a boy of Midcopse", "NR", 2, ("sign", "Midcopse", 1.2, 1.4), appears="beat:bloody_baron"),
    P("boatwright", "velen", "The boatwright", "of Oreton", "NF", 2, ("sign", "Oreton", -1.6, 1.0), appears="beat:bloody_baron"),
    P("old_sage", "velen", "The Old Sage", "soothsayer of Benek", "M", 2, ("sign", "Benek", 1.0, -1.2), appears="beat:bloody_baron"),
    P("crossroads_innkeep", "velen", "The innkeeper of the Inn at the Crossroads", "innkeeper", "NF", 2, ("cat", "innkeep", "Inn at the Crossroads")),
    # Oxenfurt and the Gustfields
    P("stjepan", "velen", "Stjepan", "innkeeper of The Alchemy, Oxenfurt", "NF", 2, ("gwent", "Oxenfurt Harbor", "Innkeeper")),
    P("oxenfurt_armorer", "velen", "The armorer of Oxenfurt", "armorer", "SC", 2, ("gwent", "Oxenfurt Harbor", "Armorer")),
    P("oxenfurt_merchant", "velen", "The merchant of Oxenfurt", "merchant", "NR", 2, ("gwent", "Oxenfurt Harbor", "Shopkeeper")),
    P("oxenfurt_blacksmith", "velen", "The blacksmith of Oxenfurt", "blacksmith", "M", 2, ("gwent", "Novigrad Gate", "Blacksmith")),
    P("otto_bamber", "velen", "Otto Bamber", "the dwarven herbalist of the Gustfields", "SC", 2, ("gwent", "Herbalist's Hut", "Herbalist")),
    P("roche", "velen", "Vernon Roche", "at the Temerian partisans' hideout", "NR", 3, ("sign", "Temerian Partisan Hideout", 0.8, 0.9), appears="beat:zoltan"),
    # Novigrad: its players of no particular renown
    P("seven_cats_innkeep", "velen", "The innkeeper of the Seven Cats Inn", "innkeeper", "NR", 2, ("gwent", "Seven Cats Inn", "Innkeeper")),
    P("crematory_alchemist", "velen", "The alchemist at the crematory", "alchemist", "NF", 2, ("gwent", "Oxenfurt Gate", "Alchemist")),
    P("elihal", "velen", "Elihal", "the elven tailor", "NR", 2, ("gwent", "Tretogor Gate", "Shopkeeper")),
    P("tretogor_merchant", "velen", "The merchant by the Tretogor Gate", "merchant", "NF", 2, ("gwent", "Tretogor Gate", "Merchant")),
    P("putrid_grove_herbalist", "velen", "The herbalist of Glory Lane", "herbalist", "SC", 2, ("gwent", "Hierarch Square", "Herbalist")),
    P("putrid_grove_merchant", "velen", "The merchant of the Putrid Grove", "merchant", "M", 2, ("gwent", "Gate of the Hierarch", "Shopkeeper")),
    P("crippled_kate_madame", "velen", "The madame of Crippled Kate's", "madame", "NR", 2, ("gwent", "Hierarch Square", "Crippled Kate")),
    P("fish_market_blacksmith", "velen", "The blacksmith of the Fish Market", "blacksmith", "M", 2, ("gwent", "Hierarch Square", "Blacksmith")),
    P("fish_market_merchant", "velen", "The merchant of the Fish Market", "merchant", "NF", 2, ("gwent", "Hierarch Square", "Shopkeeper")),
    P("golden_sturgeon_innkeep", "velen", "The innkeeper of The Golden Sturgeon", "innkeeper", "SC", 2, ("gwent", "Hierarch Square", "Innkeeper", 0)),
    P("silverton_blacksmith", "velen", "The blacksmith of Silverton", "blacksmith", "NF", 2, ("gwent", "Hierarch Square", "Blacksmith", 1)),
    P("silverton_merchant", "velen", "The merchant of Silverton", "merchant", "SC", 2, ("gwent", "St. Gregory's Bridge", "Shopkeeper", 0)),
    P("silverton_fishmonger", "velen", "The fishmonger of Silverton", "fishmonger", "M", 2, ("gwent", "Hierarch Square", "Fishmonger")),
    P("marcus_hodgson", "velen", "Marcus T.K. Hodgson", "the bookseller of Hierarch Square", "SC", 2, ("gwent", "Hierarch Square", "Book Merchant")),
    P("rosemary_innkeep", "velen", "The innkeeper of the Rosemary and Thyme", "innkeeper", "M", 2, ("gwent", "Gate of the Hierarch", "Innkeeper")),
    P("loan_shark", "velen", "The loan shark", "of the Bits", "M", 2, ("gwent", "Oxenfurt Gate", "Loan Shark")),
    P("bits_merchant", "velen", "The merchant of the Bits", "merchant", "NR", 2, ("gwent", "Southern Gate", "Shopkeeper")),
    P("nowhere_innkeep", "velen", "The innkeeper of The Nowhere Inn", "innkeeper", "NF", 2, ("gwent", "Oxenfurt Gate", "Innkeeper")),
    P("gildorf_merchant", "velen", "The merchant of Gildorf", "merchant", "NF", 2, ("gwent", "St. Gregory's Bridge", "Shopkeeper", 1)),
    P("eternal_fire_priest", "velen", "A priest of the Eternal Fire", "on Electors' Square", "NF", 2, ("gwent", "Electors' Square", "Alchemist")),
    # Novigrad: its renowned players (Gwent: Big City Players, Old Pals, Playing Thaler, Playing Innkeeps, A Dangerous Game)
    P("olivier", "velen", "Olivier", "innkeeper of The Kingfisher", "M", 3, ("gwent", "Hierarch Square", "Innkeeper", 1)),
    P("zoltan", "velen", "Zoltan Chivay", "at the Rosemary and Thyme", "SC", 3, ("cat", "innkeep", "Rosemary & Thyme"), note="beside the inn"),
    P("thaler", "velen", "Thaler", "Bernard Dukat, at the Seven Cats Inn", "SC", 3, ("cat", "innkeep", "Seven Cats Inn"), appears="beat:roche", note="beside the inn"),
    P("lambert", "velen", "Lambert", "a witcher, at The Nowhere Inn", "SC", 3, ("cat", "innkeep", "The Nowhere"), appears="beat:roche", note="beside the inn"),
    P("vimme_vivaldi", "velen", "Vimme Vivaldi", "banker, of the Vivaldi Bank", "SC", 3, ("gwent", "Hierarch Square", "Banker")),
    P("marquise_serenity", "velen", "Marquise Serenity", "at the Passiflora", "NF", 3, ("cat", "brothel", "Passiflora"), appears="beat:vimme_vivaldi"),
    P("dijkstra", "velen", "Sigismund Dijkstra", "at his bathhouse", "NR", 3, ("at", 211.2, 118.6), appears="beat:marquise_serenity"),
    P("scoiatael_merchant", "velen", "A Scoia'tael merchant", "between the Loggers' Hut and Lucian's Windmill", "SC", 3, ("at", 187.9, 85.0), appears="beat:dijkstra"),
    P("zed_house", "velen", "Zed's house", "in the Bits: a card the bandits left", None, 3, ("at", 210.0, 112.0), kind="found", appears="beat:zoltan"),
    P("earl", "velen", "Earl", "a gangster, at The Golden Sturgeon", "NF", 3, ("cat", "innkeep", "The Golden Sturgeon"), appears="beat:zoltan", note="beside the inn"),
    P("bilzen_house", "velen", "Caesar Bilzen's house", "a collector's hidden cabinet", None, 3, ("sign", "Glory Gate", 1.0, 1.2), kind="found", appears="beat:zoltan"),
    P("masquerade", "velen", "The masquerade at the Vegelbud Residence", "A Matter of Life and Death: three rounds", None, 3,
      ("sign", "Vegelbud Residence", 0.6, -0.6), kind="tournament", appears="beat:zoltan",
      rounds=[("vladimir_de_cret", "Margrave Vladimir de Cret", "NF"), ("gomo_seeling", "Gomo Seeling", "SC"), ("marius_florin", "Marius Florin", "M")]),
    P("high_stakes", "velen", "High Stakes", "the Passiflora's tournament: four rounds", None, 3, ("gwent", "St. Gregory's Bridge", "Tournament"),
      kind="tournament", appears="quest:big_city_players", fee=300,
      rounds=[("bernard_tulle", "Bernard Tulle", "NR"), ("sasha", "Sasha", "NF"), ("finneas", "Finneas", "SC"), ("count_tybalt", "Count Tybalt", "M")]),
    # Hearts of Stone (on the same map; once Skellige Style is done)
    P("robert_hilbert", "velen", "Robert Hilbert", "of the Borsodi brothers' auction house, Oxenfurt", "NF", 3, ("at", 158.6, 167.9), appears="quest:skellige_style"),
    P("shani", "velen", "Shani", "at her clinic in Oxenfurt", "SC", 3, ("at", 155.1, 163.6), appears="quest:skellige_style"),
    P("olgierd", "velen", "Olgierd von Everec", "at The Alchemy, Oxenfurt", "M", 3, ("cat", "innkeep", "The Alchemy"), appears="beat:shani", note="beside the inn"),
    P("circus_merchant", "velen", "The merchant of the circus", "camped northwest of Carsten", "SC", 3, ("sign", "Carsten", 1.4, -1.8), appears="quest:skellige_style"),
    P("dulla", "velen", "Dulla kh'Amanni", "a merchant at Upper Mill", None, 3, ("at", 198.0, 170.0), kind="shop", appears="quest:skellige_style"),
    P("brunwich_halfling", "velen", "A halfling of Brunwich", "a farmer", "M", 3, ("at", 171.0, 190.0), appears="quest:skellige_style"),
    P("brunwich_barn", "velen", "The barn at Brunwich", "something moos in the loft", None, 3, ("at", 171.6, 191.3), kind="found", appears="quest:skellige_style"),
    # Skellige: its renowned players (Gwent: Skellige Style), its quests
    P("ermion", "skellige", "Ermion", "the druid, at Gedyneith", "M", 3, ("sign", "Gedyneith", 0.9, -1.2)),
    P("gremist", "skellige", "Gremist", "the alchemist, near Gedyneith", "NF", 3, ("gwent", "Gedyneith", "Gremist"), appears="beat:ermion"),
    P("crach", "skellige", "Crach an Craite", "jarl of Ard Skellig, at Kaer Trolde", "M", 3, ("sign", "Bridge to Kaer Trolde", 1.8, -0.6), appears="beat:ermion"),
    P("sjusta", "skellige", "Sjusta", "of Kaer Trolde's harbor", "NR", 3, ("sign", "Kaer Trolde Harbor", -1.0, 1.2), appears="beat:crach"),
    P("madman_lugos", "skellige", "Madman Lugos", "jarl of Kaer Muire", "M", 3, ("cat", "poi", "Jarl Madman Lugos"), appears="beat:gremist"),
    P("shock_therapy", "skellige", "A druid's request", "Shock Therapy, at Gedyneith", None, 3, ("sign", "Gedyneith", -0.8, 1.1), kind="event"),
    P("hammond", "skellige", "Hammond's camp", "on Faroe: Following the Thread", None, 3, ("sign", "Harviken", 1.3, 2.6), kind="found", appears="beat:lambert"),
    # Skellige: its players of no particular renown
    P("arinbjorn_blacksmith", "skellige", "The blacksmith of Arinbjorn", "blacksmith", "M", 2, ("gwent", "Arinbjorn", "Blacksmith")),
    P("arinbjorn_innkeep", "skellige", "The innkeeper of Arinbjorn", "innkeeper", "SC", 2, ("gwent", "Arinbjorn", "Innkeeper")),
    P("blandare_merchant", "skellige", "The merchant of Blandare", "merchant", "NR", 2, ("gwent", "Blandare", "Shopkeeper")),
    P("fayrlund_blacksmith", "skellige", "The blacksmith of Fayrlund", "blacksmith", "M", 2, ("gwent", "Fayrlund", "Blacksmith")),
    P("fayrlund_merchant", "skellige", "The merchant of Fayrlund", "merchant", "SC", 2, ("gwent", "Fayrlund", "Shopkeeper")),
    P("fyresdal_smith", "skellige", "The smith of Fyresdal", "smith", "SC", 2, ("gwent", "Fyresdal", "Blacksmith")),
    P("fyresdal_merchant", "skellige", "The merchant of Fyresdal", "merchant", "M", 2, ("gwent", "Fyresdal", "Shopkeeper")),
    P("harviken_blacksmith", "skellige", "The blacksmith of Harviken", "blacksmith", "SC", 2, ("gwent", "Harviken", "Blacksmith")),
    P("harviken_innkeep", "skellige", "The innkeeper of Harviken", "innkeeper", "M", 2, ("gwent", "Harviken", "Innkeeper")),
    P("holmstein_merchant", "skellige", "The merchant of Holmstein's Port", "merchant", "SC", 2, ("gwent", "Holmstein's Port", "Shopkeeper")),
    P("larvik_armorer", "skellige", "The armorer of Larvik", "armorer", "NF", 2, ("gwent", "Larvik", "Armorer")),
    P("larvik_blacksmith", "skellige", "The blacksmith of Larvik", "blacksmith", "M", 2, ("gwent", "Larvik", "Innkeeper", 1)),
    P("larvik_innkeep", "skellige", "The innkeeper of the House of Warriors", "innkeeper", "SC", 2, ("gwent", "Larvik", "Innkeeper", 0)),
    P("larvik_merchant", "skellige", "The merchant of Larvik", "merchant", "NR", 2, ("gwent", "Larvik", "Shopkeeper")),
    P("kaer_muire_armorer", "skellige", "The armorer of Kaer Muire", "armorer", "M", 2, ("gwent", "Kaer Muire", "Armorer")),
    P("kaer_muire_blacksmith", "skellige", "The blacksmith of Kaer Muire", "blacksmith", "SC", 2, ("gwent", "Kaer Muire", "Blacksmith")),
    P("kaer_trolde_herbalist", "skellige", "The herbalist of Kaer Trolde", "herbalist", "NF", 2, ("gwent", "Kaer Trolde Harbor", "Herbalist")),
    P("jonas", "skellige", "Jonas", "innkeeper of The New Port Inn", "M", 2, ("gwent", "Kaer Trolde Harbor", "Innkeeper")),
    P("kaer_trolde_armorer", "skellige", "The armorer of Kaer Trolde", "armorer", "NR", 2, ("gwent", "Bridge to Kaer Trolde", "Armorer")),
    P("yanne", "skellige", "Yanne", "blacksmith of Kaer Trolde's citadel", "SC", 2, ("gwent", "Bridge to Kaer Trolde", "Blacksmith")),
    P("svorlag_smith", "skellige", "The smith of Svorlag", "smith", "NF", 2, ("gwent", "Svorlag", "Blacksmith")),
    P("svorlag_innkeep", "skellige", "The innkeeper of Svorlag", "innkeeper", "NR", 2, ("gwent", "Svorlag", "Innkeeper")),
    P("svorlag_merchant", "skellige", "The merchant of Svorlag", "merchant", "SC", 2, ("gwent", "Svorlag", "Shopkeeper")),
    P("urialla_armorer", "skellige", "The armorer of Urialla Harbor", "armorer", "NF", 2, ("gwent", "Urialla Harbor", "Armorer")),
    P("urialla_blacksmith", "skellige", "The blacksmith of Urialla Harbor", "blacksmith", "M", 2, ("gwent", "Urialla Harbor", "Blacksmith")),
    P("urialla_innkeep", "skellige", "The innkeeper of Urialla Harbor", "innkeeper", "NR", 2, ("gwent", "Urialla Harbor", "Innkeeper")),
    # Toussaint: the Skellige deck (Gwent: Never Fear, Skellige's Here) and the Beauclair tournament
    P("monnier_deck", "toussaint", "Count Martin Monnier", "host of the tournament, at The Pheasantry", None, 3, ("cat", "innkeep", "The Pheasantry"), kind="event", note="beside the inn"),
    P("pheasantry_innkeep", "toussaint", "The innkeeper of The Pheasantry", "innkeeper", "M", 3, ("gwent", "Gran'Place", "", 0), appears="beat:monnier_deck"),
    P("barrel_and_bung_innkeep", "toussaint", "The innkeeper of The Barrel and Bung", "innkeeper, Flovive", "NR", 3, ("gwent", "Flovive", ""), appears="beat:monnier_deck"),
    P("castel_ravello_herbalist", "toussaint", "The herbalist of Castel Ravello", "herbalist", "NF", 3, ("gwent", "Castel Ravello Vineyard", ""), appears="beat:monnier_deck"),
    P("dupont_merchant", "toussaint", "The merchant of Dupont & Sons", "merchant, Beauclair", "M", 3, ("gwent", "Gran'Place", "", 1), appears="beat:monnier_deck"),
    P("cockatrice_innkeep", "toussaint", "The innkeeper of The Cockatrice Inn", "innkeeper", "SK", 3, ("gwent", "The Cockatrice Inn", ""), appears="beat:monnier_deck"),
    P("francollarts_armorer", "toussaint", "The armorer of Francollarts", "armorer", "NR", 3, ("gwent", "Francollarts", "", 1), appears="beat:monnier_deck"),
    P("francollarts_innkeep", "toussaint", "The innkeeper of The Scarlet Cardinal", "innkeeper, Francollarts", "NF", 3, ("gwent", "Francollarts", "", 0), appears="beat:monnier_deck"),
    P("hauteville_armorer", "toussaint", "The armorer of Hauteville", "armorer, Beauclair", "NR", 3, ("gwent", "Gran'Place", "", 2), appears="beat:monnier_deck"),
    P("perfumery_merchant", "toussaint", "The perfumer", "merchant, Beauclair", "SK", 3, ("gwent", "Gran'Place", "", 3), appears="beat:monnier_deck"),
    P("pierre", "toussaint", "Pierre", "tailor of Beauclair", "NF", 3, ("gwent", "Gran'Place", "", 4), appears="beat:monnier_deck"),
    P("adder_sommelier", "toussaint", "The sommelier of The Adder and Jewels", "a winery's sommelier", "SK", 3, ("at", 92.5, 62.0), appears="beat:monnier_deck"),
    P("tourney_innkeep", "toussaint", "The innkeeper of the Tourney Grounds", "innkeeper", "SC", 3, ("gwent", "Tourney Grounds", "", 0), appears="beat:monnier_deck"),
    P("tourney_blacksmith", "toussaint", "The blacksmith of the Tourney Grounds", "blacksmith", "SK", 3, ("gwent", "Tourney Grounds", "", 1), appears="beat:monnier_deck"),
    P("tourney_armorer", "toussaint", "The armorer of the Tourney Grounds", "armorer", "NF", 3, ("gwent", "Tourney Grounds", "", 2), appears="beat:monnier_deck"),
    P("tourney_barber", "toussaint", "The barber of the Tourney Grounds", "barber", "SK", 3, ("gwent", "Tourney Grounds", "", 3), appears="beat:monnier_deck"),
    P("belles_madame", "toussaint", "Madame Isabelle", "of The Belles of Beauclair", "SC", 3, ("gwent", "San Sebastian", ""), appears="beat:monnier_deck"),
    P("herb_store_herbalist", "toussaint", "The herbalist of the Herb Store", "herbalist, Beauclair", "SK", 3, ("gwent", "Metinna Gate", "", 0), appears="beat:monnier_deck"),
    P("beauclair_butcher", "toussaint", "The butcher of Beauclair Port", "butcher", "SK", 3, ("gwent", "Beauclair Port", ""), appears="beat:monnier_deck"),
    P("cianfanelli_customer", "toussaint", "A customer of the Cianfanelli bank", "Beauclair", "SC", 3, ("gwent", "Metinna Gate", "", 1)),
    P("rafael_de_surmann", "toussaint", "Rafael de Surmann", "the Ducal Camerlengo", "NF", 3, ("gwent", "Nilfgaardian Embassy", "")),
    P("beauclair_tournament", "toussaint", "The Great Beauclair Gwent Tournament", "at The Pheasantry: Skellige decks only", None, 3,
      ("sign", "Gran'Place", -1.6, -1.4), kind="tournament", appears="beat:monnier_deck", deck="SK",
      rounds=[("hamal", "Hamal ogn Dangbahli", "M"), ("von_hinn", "Ambassador von Hinn", "NF"), ("martin_monnier", "Count Martin Monnier", "SK"), ("yaki_rafiberg", "Yaki Rafiberg", "SC")]),
]

# Gwent quests. steps: the players to beat (a tournament: all its rounds); reward: given when every step is done
QUESTS = [
    dict(id="playing_innkeeps", name="Gwent: Playing Innkeeps", steps=["crossroads_innkeep", "stjepan", "olivier"]),
    dict(id="velen_players", name="Gwent: Velen Players", steps=["bloody_baron", "haddy", "old_sage", "boatwright"]),
    dict(id="big_city_players", name="Gwent: Big City Players", steps=["vimme_vivaldi", "marquise_serenity", "dijkstra", "scoiatael_merchant"]),
    dict(id="old_pals", name="Gwent: Old Pals", steps=["zoltan", "roche", "lambert", "thaler"]),
    dict(id="a_dangerous_game", name="A Dangerous Game", steps=["zed_house", "earl", "bilzen_house"]),
    dict(id="a_matter_of_life_and_death", name="A Matter of Life and Death", steps=["masquerade"]),
    dict(id="high_stakes", name="High Stakes", steps=["high_stakes"]),
    dict(id="hearts_of_stone", name="Hearts of Stone", steps=["robert_hilbert", "shani", "olgierd", "circus_merchant"]),
    dict(id="skellige_style", name="Gwent: Skellige Style", steps=["ermion", "gremist", "crach", "sjusta", "madman_lugos"]),
    dict(id="never_fear", name="Gwent: Never Fear, Skellige's Here", steps=["monnier_deck", "pheasantry_innkeep", "barrel_and_bung_innkeep",
         "castel_ravello_herbalist", "dupont_merchant", "cockatrice_innkeep", "francollarts_armorer", "francollarts_innkeep", "hauteville_armorer",
         "perfumery_merchant", "pierre", "adder_sommelier", "tourney_innkeep", "tourney_blacksmith", "tourney_armorer", "tourney_barber",
         "belles_madame", "herb_store_herbalist", "beauclair_butcher"]),
    dict(id="beauclair_tournament", name="Gwent: To Everything - Turn, Turn, Tournament!", steps=["beauclair_tournament"]),
]

# What each sentence of a card's `source` means: (pattern, action, who). action: start (the starter deck; Skellige's
# cards: Monnier's base deck), random (the pool of the players of no particular renown), buy, win, found, event, quest
# (a quest's reward), drop (another way to get the same copy, or a summoned card)
SOURCES = [
    (r"part of the base deck", "start", None),
    (r"random rewards? from gwent players", "random", None),
    (r"summoned by|transformed version", "drop", None),
    (r"Elsa or Bram", "buy", "wo_elsa"),
    (r"Aldert Geert", "win", "wo_scholar"),
    (r"Nilfgaardian nobleman", "win", "vizima_nobleman"),
    (r"Crow's Perch's quartermaster", "buy", "crows_perch_quartermaster"),
    (r"Crow's Perch's merchant", "buy", "crows_perch_merchant"),
    (r"Claywich's merchant", "buy", "claywich_merchant"),
    (r"Lindenvale's merchant", "buy", "lindenvale_merchant"),
    (r"Midcopse's merchant", "buy", "midcopse_merchant"),
    (r"Inn at the Crossroads'?s?'? innkeep.*Gwent: Playing Innkeeps", "win", "crossroads_innkeep"),
    (r"Inn at the Crossroads", "buy", "crossroads_innkeep"),
    (r"Lindenvale's innkeep", "drop", None),  # the Crossroads' innkeeper, as the wiki names him a second time
    (r"Cunny of the Goose", "buy", "cunny_innkeep"),
    (r"Seven Cats Inn", "buy", "seven_cats_innkeep"),
    (r"Golden Sturgeon", "buy", "golden_sturgeon_innkeep"),
    (r"Kingfisher Inn", "buy", "olivier"),
    (r"Won from Stjepan", "win", "stjepan"),
    (r"Stjepan at The Alchemy", "buy", "stjepan"),
    (r"Won from Marquise Serenity", "win", "marquise_serenity"),
    (r"Marquise Serenity", "buy", "marquise_serenity"),
    (r"Jonas at The New Port", "buy", "jonas"),
    (r"Arinbjorn's innkeep", "buy", "arinbjorn_innkeep"),
    (r"Harviken's innkeep", "buy", "harviken_innkeep"),
    (r"Svorlag's innkeep", "buy", "svorlag_innkeep"),
    (r"Urialla Harbor's innkeep", "buy", "urialla_innkeep"),
    (r"Won from the merchant at the circus", "win", "circus_merchant"),
    (r"merchant at the circus camp", "buy", "circus_merchant"),
    (r"Dulla kh'Amanni", "buy", "dulla"),
    (r"Robert Hilbert", "win", "robert_hilbert"),
    (r"Won from Shani", "win", "shani"),
    (r"Won from Olgierd", "win", "olgierd"),
    (r"barn in Brunwich", "found", "brunwich_barn"),
    (r"Phillip Strenger", "win", "bloody_baron"),
    (r"Haddy", "win", "haddy"),
    (r"Old Sage", "win", "old_sage"),
    (r"Boatwright", "win", "boatwright"),
    (r"Won during Gwent: Velen Players", "quest", "velen_players"),
    (r"Olivier", "win", "olivier"),
    (r"Vimme Vivaldi", "win", "vimme_vivaldi"),
    (r"Sigismund Dijkstra during", "win", "dijkstra"),
    (r"Scoia'tael merchant", "win", "scoiatael_merchant"),
    (r"Won during Gwent: Big City Players", "quest", "big_city_players"),
    (r"Won from Zoltan", "win", "zoltan"),
    (r"Vernon Roche during", "win", "roche"),
    (r"Lambert during", "win", "lambert"),
    (r"Thaler during", "win", "thaler"),
    (r"Won from Sasha", "win", "high_stakes:sasha"),
    (r"Won from Finneas", "win", "high_stakes:finneas"),
    (r"Won from Count Tybalt", "win", "high_stakes:count_tybalt"),
    (r"Won during High Stakes", "win", "high_stakes:bernard_tulle"),
    (r"Won during Gwent: Skellige Style", "quest", "skellige_style"),
    (r"Won from Ermion", "win", "ermion"),
    (r"Won from Gremist", "win", "gremist"),
    (r"Won from Crach an Craite", "win", "crach"),
    (r"Won from Sjusta", "win", "sjusta"),
    (r"Madman Lugos during", "win", "madman_lugos"),
    (r"Shock Therapy", "event", "shock_therapy"),
    (r"Hammond's corpse", "found", "hammond"),
    (r"Barrel and Bung", "win", "barrel_and_bung_innkeep"),
    (r"Castel Ravello", "win", "castel_ravello_herbalist"),
    (r"Dupont & Sons", "win", "dupont_merchant"),
    (r"Cockatrice Inn", "win", "cockatrice_innkeep"),
    (r"Francollarts' armorer", "win", "francollarts_armorer"),
    (r"Francollarts' innkeep", "win", "francollarts_innkeep"),
    (r"Pheasantry's innkeep", "win", "pheasantry_innkeep"),
    (r"Hauteville's armorer", "win", "hauteville_armorer"),
    (r"Ducal Camerlengo", "drop", None),  # King Bran: the tournament's prize here
    (r"Turn, Turn, Tournament", "quest", "beauclair_tournament"),
    (r"Perfumery", "win", "perfumery_merchant"),
    (r"Adder and Jewels", "win", "adder_sommelier"),
    (r"Tourney Grounds' innkeep", "win", "tourney_innkeep"),
    (r"Tourney Grounds' blacksmith", "win", "tourney_blacksmith"),
    (r"Tourney Grounds' armorer", "win", "tourney_armorer"),
    (r"Tourney Grounds' barber", "win", "tourney_barber"),
    (r"Pierre at Tailor", "win", "pierre"),
    (r"Belles of Beauclair", "win", "belles_madame"),
    (r"Herb Store", "win", "herb_store_herbalist"),
    (r"Beauclair Port's butcher", "win", "beauclair_butcher"),
    (r"Hugo Monnart|any innkeeper in Toussaint|Till Death Do You Part|Louis' urn|loot in house", "drop", None),
]

# Cards whose source names a whole quest: which of its players or places gives which card
BY_CARD = {
    "Isengrim Faoiltiarna": ("found", "zed_house"),
    "John Natalis": ("win", "earl"),
    "Fringilla Vigo": ("found", "bilzen_house"),
    "Vampire Bruxa": ("win", "masquerade:marius_florin"),
    "Milva": ("win", "masquerade:gomo_seeling"),
    "Dandelion 1": ("win", "masquerade:vladimir_de_cret"),
}

# The Witcher 3's starter deck, as the Card Collector guide lists it (the wiki says "part of the base deck" without
# counts): 22 units, six weather cards, a leader of each of the four factions
STARTER = {
    "Biting Frost": 2, "Clear Weather": 1, "Impenetrable Fog": 2, "Torrential Rain": 1, "Ballista 1": 1,
    "Blue Stripes Commando 3": 2, "Dethmold": 1, "Dun Banner Medic": 1, "Kaedweni Siege Expert 3": 3, "Keira Metz": 1,
    "Poor Fucking Infantry 1": 2, "Prince Stennis": 1, "Redanian Foot Soldier 1": 2, "Sabrina Glevissig": 1,
    "Sheldon Skaggs": 1, "Siegfried of Denesle": 1, "Sile de Tansarville": 1, "Trebuchet 1": 2, "Ves": 1,
    "Yarpen Zigrin": 1, "Foltest King of Temeria": 1, "Emhyr var Emreis His Imperial Majesty": 1,
    "Eredin King of the Wild Hunt": 1, "Francesca Findabair Pureblood Elf": 1,
}

# The leaders the wiki names for players who do not give one of their own faction (those play the one they give)
LEADERS = {"stjepan": 37, "crossroads_innkeep": 33}

# Prices: the card's value in The Witcher 3; where the wiki gives none, by kind
DEFAULT_PRICE = {"unit": 20, "special": 30, "leader": 50}

# ---------------------------------------------------------------------------------------------------------------------
# Building

def anchor(maps, player):
    """The marker a player stands at: (lat, lng, approximate)"""
    region = next(r for r in REGIONS if r["id"] == player["region"])
    at = player["at"]
    if at is None or region["map"] is None:
        return None
    markers = maps[region["map"]]["markers"]
    if at[0] == "gwent":
        _, near, title, *n = at
        found = [m for m in markers if m["cat"] == "gwent" and m["near"] == near and title.lower() in (m["title"] or m["label"]).lower()]
        index = n[0] if n else 0
        if len(found) <= index:
            raise ValueError(player["id"] + ": no Gwent marker near " + near + " titled " + repr(title))
        return found[index]["lat"], found[index]["lng"], False
    if at[0] == "cat":
        found = [m for m in markers if m["cat"] == at[1] and (m["label"].startswith(at[2]) or m["title"].startswith(at[2]))]
        if not found:
            raise ValueError(player["id"] + ": no " + at[1] + " marker " + at[2])
        return found[0]["lat"], found[0]["lng"] + (0.5 if player["note"] else 0), bool(player["note"])
    if at[0] == "sign":
        found = [m for m in markers if m["cat"] == "signpost" and m["label"] == at[1]]
        if not found:
            raise ValueError(player["id"] + ": no signpost " + at[1])
        return found[0]["lat"] + at[2], found[0]["lng"] + at[3], True
    return at[1], at[2], True


def leader_of(prize, faction, by_id, fixed=0):
    """The leader a player plays: the one the wiki names, else the leader of their faction they give, else none (0: the
    game picks one)"""
    if fixed:
        return fixed
    for card in prize:
        if by_id[card]["ability"] == "leader" and by_id[card]["faction"] == faction:
            return card
    return 0


def export(draft, by_id, path):
    """The game's own file (Resources/Story/story.json, read by StoryFile): the draft without the review's extras"""
    places = []
    for p in draft["players"]:
        at = p["at"] or {}
        places.append(dict(
            Id=p["id"], Region=p["region"], Name=p["name"], Trade=p["trade"], Kind=p["kind"], Faction=p["faction"] or "",
            Tier=p["tier"], Leader=leader_of(p["prize"], p["faction"], by_id, LEADERS.get(p["id"], 0)), Appears=p["appears"] or "",
            Lat=round(at.get("lat", 0), 3), Lng=round(at.get("lng", 0), 3), Approximate=bool(at.get("approximate")), Placed=bool(at),
            Crowns=p["crowns"], Fee=p["fee"], Deck=p["deck"] or "", Random=p["random"], Master=p["id"] in MASTERS,
            Sells=[dict(Card=s["card"], Price=s["price"]) for s in p["sells"]], Prize=p["prize"], Gives=p.get("gives", []),
            Rounds=[dict(Id=r["id"], Name=r["name"], Faction=r["faction"], Leader=leader_of(r["prize"], r["faction"], by_id),
                         Prize=r["prize"], Crowns=r["crowns"]) for r in p.get("rounds", [])]))
    story = dict(
        Regions=[dict(Id=r["id"], Name=r["name"], Map=r["map"] or "", Opens=r["opens"] or "") for r in draft["regions"]],
        Places=places,
        Quests=[dict(Id=q["id"], Name=q["name"], Steps=q["steps"], Reward=q["reward"]) for q in draft["quests"]],
        Starter=draft["starter"], Pool=draft["pool"],
        Maps=[dict(Id=k, South=v["sw"][0], West=v["sw"][1], North=v["ne"][0], East=v["ne"][1], Mercator=not v["simple"])
              for k, v in draft["maps"].items()])
    # Each map's geometry is tools/story-maps.py's (from its tiles): kept
    if os.path.exists(path):
        before = {m["Id"]: m for m in json.load(open(path)).get("Maps", [])}
        for m in story["Maps"]:
            m.update({k: v for k, v in before.get(m["Id"], {}).items() if k in ("Zoom", "Rows", "Left", "Top", "Right", "Bottom")})
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        json.dump(story, f, indent=1, ensure_ascii=False)
        f.write("\n")
    print("wrote", path)


def build(cache, out, story=None):
    pages = json.load(open(os.path.join(cache, "pages.json")))
    maps = {m: map_markers(cache, m) for m in MAPS}
    wcards = wiki_cards(pages)
    db = json.load(open(CARDS_DB))["Cards"]
    players = {p["id"]: p for p in PLAYERS}
    rounds = {p["id"] + ":" + r[0]: (p, r) for p in PLAYERS for r in p["rounds"]}
    problems = []

    def who_ok(who):
        return who in players or who in rounds or any(q["id"] == who for q in QUESTS)

    # Every copy of every card: its source
    copies, sold, prizes, rewards, starter, monnier, pool, unparsed = [], defaultdict(list), defaultdict(list), defaultdict(list), [], [], [], []
    for card in db:
        if card["copies"] == 0:
            continue
        w = wcards.get(key(card["name"]))
        if w is None:
            problems.append("no wiki page for " + card["name"])
            continue
        parts = []
        if card["name"] in BY_CARD:
            parts = [(1,) + BY_CARD[card["name"]] + ("(" + w["source"] + ")",)]
        else:
            for part in re.split(r"\s*/\s*", w["source"]):
                part = part.strip(" :")
                if not part:
                    continue
                m = re.match(r"(\d+)\s+(?:is|are|as|can be|purchased|won|obtained|part)\b", part)
                count = int(m.group(1)) if m else 1
                for pattern, action, who in SOURCES:
                    if re.search(pattern, part, re.I):
                        if who and not who_ok(who):
                            problems.append(card["name"] + ": unknown player " + who)
                        parts.append((count, action, who, part))
                        break
                else:
                    unparsed.append((card["name"], part))
        given = 0
        for _ in range(min(STARTER.get(card["name"], 0), card["copies"])):
            given += 1
            starter.append(card["_id"])
            copies.append(dict(card=card["_id"], name=card["name"], action="start", who=None, text="The starter deck"))
        for count, action, who, text in parts:
            if action == "drop" or (action == "start" and card["faction"] != "SK" and card["_id"] != 207):
                continue  # another way to the same copy; the starter deck's counts are the guide's
            for _ in range(count):
                if given >= card["copies"]:
                    break
                given += 1
                if action == "start":
                    (monnier if card["faction"] == "SK" or card["_id"] == 207 else starter).append(card["_id"])
                elif action == "random":
                    pool.append(card["_id"])
                elif action == "buy":
                    sold[who].append(card["_id"])
                elif action == "quest":
                    rewards[who].append(card["_id"])
                else:
                    prizes[who].append(card["_id"])
                copies.append(dict(card=card["_id"], name=card["name"], action=action, who=who, text=text))
        while given < card["copies"]:  # our database has more copies than The Witcher 3 hands out: the rest are random
            given += 1
            pool.append(card["_id"])
            copies.append(dict(card=card["_id"], name=card["name"], action="random", who=None, text="(an extra copy)"))

    # Prices
    def price(card):
        w = wcards.get(key(card["name"]))
        value = re.sub(r"Versions? [\d,]+:", "", (w or {}).get("value", ""))  # "Version 1: 20 / Versions 2,3: 50": the first
        digits = re.findall(r"\d+", value)
        if digits:
            return int(digits[0])
        kind = "leader" if card["ability"] == "leader" else "special" if card["faction"] == "Special" else "unit"
        return DEFAULT_PRICE[kind]
    by_id = {c["_id"]: c for c in db}

    # Players, with their place on the map
    out_players = []
    for p in PLAYERS:
        try:
            spot = anchor(maps, p)
        except ValueError as e:
            problems.append(str(e))
            spot = None
        # A shop is open from the start, as in The Witcher 3: the quests' order (a "beat:" condition) only reveals the
        # players who sell nothing; a shop that waits for a quest (Hearts of Stone's) still waits
        appears = None if sold.get(p["id"]) and (p["appears"] or "").startswith("beat:") else p["appears"]
        entry = dict(id=p["id"], region=p["region"], name=p["name"], trade=p["trade"], kind=p["kind"], faction=p["faction"],
                     tier=p["tier"], difficulty=DIFFICULTY[p["tier"] if p["id"] in MASTERS else min(p["tier"], 2)], appears=appears, note=p["note"],
                     at=None if spot is None else dict(lat=spot[0], lng=spot[1], approximate=spot[2]),
                     crowns=TIER_CROWNS[p["tier"]] if p["kind"] == "player" else 0, fee=p["fee"], deck=p["deck"],
                     sells=[dict(card=c, price=price(by_id[c])) for c in sold.get(p["id"], [])],
                     prize=prizes.get(p["id"], []), random=False)
        if p["kind"] == "player" and not entry["prize"]:
            entry["random"] = True  # a player of no particular renown: a card from the pool, the first win
        if p["kind"] == "tournament":
            entry["rounds"] = [dict(id=r[0], name=r[1], faction=r[2], prize=prizes.get(p["id"] + ":" + r[0], []), crowns=ROUND_CROWNS)
                               for r in p["rounds"]]
        if p["id"] == "monnier_deck":
            entry["gives"] = monnier
        out_players.append(entry)

    out_quests = [dict(q, reward=rewards.get(q["id"], [])) for q in QUESTS]
    for q in QUESTS:
        for step in q["steps"]:
            if step not in players:
                problems.append(q["id"] + ": unknown step " + step)

    # Checks: one source per copy (above); the starter deck; enough players for the random pool; the economy
    starter_units = [c for c in starter if by_id[c]["faction"] not in ("Special",) and by_id[c]["ability"] != "leader"]
    generic = [p for p in out_players if p["random"]]
    first_wins = sum(p["crowns"] for p in out_players) + sum(r["crowns"] for p in out_players for r in p.get("rounds", []))
    shop_total = sum(s["price"] for p in out_players for s in p["sells"])
    fees = sum(p["fee"] for p in out_players)
    if len(starter_units) < 22:
        problems.append("the starter deck has %d units" % len(starter_units))
    if len(generic) < len(pool):
        problems.append("%d random copies but %d players to give them" % (len(pool), len(generic)))
    if first_wins < shop_total + fees:
        problems.append("first wins pay %d crowns; the shops and fees cost %d" % (first_wins, shop_total + fees))
    for name, part in unparsed:
        problems.append("unread source of %s: %s" % (name, part))
    for p in out_players:
        if p["kind"] == "shop" and not p["sells"]:
            problems.append(p["id"] + " sells nothing")
        if p["kind"] in ("found", "event") and not p["prize"] and p["id"] != "monnier_deck":
            problems.append(p["id"] + " gives nothing")

    draft = dict(regions=REGIONS, players=out_players, quests=out_quests, starter=starter,
                 starter_leaders=[c for c in starter if by_id[c]["ability"] == "leader"], monnier=monnier, pool=pool,
                 copies=copies, economy=dict(first_wins=first_wins, shops=shop_total, fees=fees, tier_crowns=TIER_CROWNS,
                 round_crowns=ROUND_CROWNS), maps={k: dict(sw=v["sw"], ne=v["ne"], simple=v["simple"]) for k, v in maps.items()},
                 problems=problems)
    json.dump(draft, open(out, "w"), indent=1, ensure_ascii=False)
    if story:
        export(draft, by_id, story)
    actions = Counter(c["action"] for c in copies)
    print("copies:", len(copies), dict(actions))
    print("players:", Counter(p["kind"] for p in out_players), "random givers:", len(generic), "random copies:", len(pool))
    print("economy: first wins", first_wins, "shops", shop_total, "fees", fees)
    print("problems:", len(problems))
    for line in problems:
        print("  ", line)
    return draft


if __name__ == "__main__":
    if len(sys.argv) >= 3 and sys.argv[1] == "fetch":
        fetch(sys.argv[2])
    elif len(sys.argv) >= 4 and sys.argv[1] == "build":
        build(sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else None)
    else:
        print(__doc__)
        sys.exit(1)
