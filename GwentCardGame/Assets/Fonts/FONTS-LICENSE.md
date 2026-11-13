# Fonts

| File | Font | Copyright | License |
|---|---|---|---|
| `Cinzel.ttf` (variable, `Cinzel[wght].ttf` upstream) | Cinzel | Copyright 2020 The Cinzel Project Authors (https://github.com/NDISCOVER/Cinzel) | SIL Open Font License 1.1, full text in `OFL-Cinzel.txt` |
| `AlegreyaSans-Regular.ttf`, `AlegreyaSans-Medium.ttf` | Alegreya Sans | Copyright 2013 The Alegreya Sans Project Authors (https://github.com/huertatipografica/Alegreya-Sans) | SIL Open Font License 1.1, full text in `OFL-AlegreyaSans.txt` |
| `CormorantSC-SemiBold.ttf` | Cormorant SC | Copyright 2015 The Cormorant Project Authors (https://github.com/CatharsisFonts/Cormorant) | SIL Open Font License 1.1, full text in `OFL-CormorantSC.txt` |

Cinzel, Alegreya Sans and Cormorant SC come from the official Google Fonts repository (https://github.com/google/fonts, `ofl/cinzel`,
`ofl/alegreyasans` and `ofl/cormorantsc`), together with their `OFL.txt` license files (copied here as `OFL-Cinzel.txt`,
`OFL-AlegreyaSans.txt` and `OFL-CormorantSC.txt`). Cinzel has no Cyrillic: Cormorant SC stands in for it in Russian.

The `* SDF.asset` files next to them are TextMesh Pro font assets generated from these fonts (static atlases: ASCII, Latin-1 and common punctuation). Each has a dynamic fallback
(`* Cyrillic SDF.asset`) that draws the Cyrillic letters as they are needed: Alegreya Sans's own, Cormorant SC's for Cinzel.
