#!/usr/bin/env python3
"""
Builds the language fonts for Driftline from the words in
Assets/Resources/Localization/strings.txt.

Run it again whenever you change the Russian, Chinese, Japanese, Korean,
Arabic or Hindi text (see README.md in this folder). Nothing else needs it.

What it makes:
  Assets/Resources/Fonts/Lang/DriftlineZH.ttf, ...JA.ttf, ...KO.ttf
      Noto Sans, cut down to just the characters the game uses (the full
      fonts are 10-17 MB each; these are a few hundred KB).
  Assets/Resources/Fonts/Lang/DriftlineRU.ttf and DriftlineRU-Display.ttf
      Noto Sans and Russo One, cut down the same way.
  Assets/Resources/Fonts/Lang/DriftlineAR.ttf + Localization/shapes_ar.txt
  Assets/Resources/Fonts/Lang/DriftlineHI.ttf + Localization/shapes_hi.txt
      Unity's UI text draws one character after another and cannot join
      Arabic letters or combine Hindi ones. So every Arabic word and every
      Hindi syllable the game uses is shaped here with HarfBuzz (the same
      engine browsers and phones use) and saved into the font as a single
      ready-made character. shapes_*.txt tells the game which character is
      which; Loc.cs does the swap when text goes on screen.

Needs:  python3 -m pip install fonttools uharfbuzz
The source fonts are downloaded from the Google Fonts repository the first
time and kept in .font-cache/ next to this script. All are under the SIL Open
Font License; the built fonts are renamed, as that licence asks of modified
versions, and keep the original copyright and licence in their metadata.
"""
import os, sys, re, io, urllib.request, urllib.parse

from fontTools.ttLib import TTFont, newTable
from fontTools.ttLib.tables._g_l_y_f import Glyph, GlyphComponent
from fontTools import subset
from fontTools.varLib import instancer
import uharfbuzz as hb

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
STRINGS = os.path.join(ROOT, "Assets", "Resources", "Localization", "strings.txt")
OUT_FONTS = os.path.join(ROOT, "Assets", "Resources", "Fonts", "Lang")
OUT_SHAPES = os.path.join(ROOT, "Assets", "Resources", "Localization")
CACHE = os.path.join(HERE, ".font-cache")

GF = "https://raw.githubusercontent.com/google/fonts/main/ofl/"
SOURCES = {
    "sc": ("notosanssc", "NotoSansSC[wght].ttf"),
    "jp": ("notosansjp", "NotoSansJP[wght].ttf"),
    "kr": ("notosanskr", "NotoSansKR[wght].ttf"),
    "latin": ("notosans", "NotoSans[wdth,wght].ttf"),
    "arabic": ("notosansarabic", "NotoSansArabic[wdth,wght].ttf"),
    "deva": ("notosansdevanagari", "NotoSansDevanagari[wdth,wght].ttf"),
    "russo": ("russoone", "RussoOne-Regular.ttf"),
}

# each language's own name, shown in the language list in its own font
NATIVE = {"ru": "РУССКИЙ", "zh": "中文", "ja": "日本語", "ko": "한국어",
          "ar": "العربية", "hi": "हिन्दी"}

ASCII = "".join(chr(c) for c in range(0x20, 0x7F))
PUA_ARABIC = 0xE000     # Arabic words from here
PUA_HINDI = 0xE800      # Hindi syllables from here


# ------------------------------------------------------------------ input

def unescape(s):
    out, i = [], 0
    while i < len(s):
        c = s[i]
        if c == "\\" and i + 1 < len(s):
            n = s[i + 1]
            out.append("\n" if n == "n" else "\t" if n == "t" else n)
            i += 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def read_strings(path):
    """{language code: [texts]} from strings.txt."""
    with open(path, encoding="utf-8") as f:
        lines = f.read().split("\n")
    header = lines[0].rstrip("\r").split("\t")
    cols = {i: h.strip() for i, h in enumerate(header)}
    texts = {h.strip(): [] for h in header[1:]}
    for line in lines[1:]:
        line = line.rstrip("\r")
        if not line or line.startswith("#"):
            continue
        cells = line.split("\t")
        for i, cell in enumerate(cells[1:], start=1):
            if i in cols and cell and cell != "\\0":
                texts[cols[i]].append(unescape(cell))
    return texts


def source_font(key):
    folder, name = SOURCES[key]
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, name)
    if not os.path.exists(path):
        url = GF + folder + "/" + urllib.parse.quote(name)
        print("  downloading", name)
        urllib.request.urlretrieve(url, path)
    return path


# ------------------------------------------------------------------ font helpers

def static_bold(path, weight=800):
    """The font at a heavy weight, full width, as a plain (non-variable) font."""
    return static_bold_from(TTFont(path), weight)


def subset_to(font, codepoints):
    opts = subset.Options()
    opts.layout_features = []            # Unity does not use them
    opts.drop_tables += ["GSUB", "GPOS", "GDEF", "BASE", "JSTF", "MATH", "STAT", "DSIG"]
    opts.name_IDs = ["*"]
    opts.name_languages = ["*"]
    opts.notdef_outline = True
    opts.glyph_names = False
    opts.hinting = False
    sub = subset.Subsetter(opts)
    sub.populate(unicodes=sorted(codepoints))
    sub.subset(font)
    return font


def rename(font, family):
    """New family name: required of modified OFL fonts, and keeps Unity from
    confusing them with any installed Noto."""
    name = font["name"]
    ps = re.sub(r"[^A-Za-z0-9-]", "", family.replace(" ", "-"))
    for rec in list(name.names):
        if rec.nameID in (1, 16):
            rec.string = family
        elif rec.nameID in (2, 17):
            rec.string = "Regular"
        elif rec.nameID == 4:
            rec.string = family
        elif rec.nameID == 6:
            rec.string = ps
        elif rec.nameID == 3:
            rec.string = ps + "-driftline"
    for nid in (16, 17, 25):
        name.removeNames(nameID=nid)


def set_metrics(font, ascent, descent):
    """Line height Unity lays text out with. The source fonts reserve room for
    the tallest marks in any language they cover, which in Unity means extra
    space above and below every line; these values fit the game's text."""
    for tbl, a, d in (("hhea", "ascent", "descent"), ("OS/2", "sTypoAscender", "sTypoDescender")):
        setattr(font[tbl], a, ascent)
        setattr(font[tbl], d, descent)
    font["hhea"].lineGap = 0
    font["OS/2"].sTypoLineGap = 0
    font["OS/2"].usWinAscent = ascent
    font["OS/2"].usWinDescent = -descent


META = """fileFormatVersion: 2
guid: %s
TrueTypeFontImporter:
  externalObjects: {}
  serializedVersion: 4
  fontSize: 16
  forceTextureCase: -2
  characterSpacing: 0
  characterPadding: 1
  includeFontData: 1
  fontNames:
  - %s
  fallbackFontReferences: []
  customCharacters: 
  fontRenderingMode: 0
  ascentCalculationMode: 1
  useLegacyBoundsCalculation: 0
  shouldRoundAdvanceValue: 1
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def write_meta(path, family):
    """Unity import settings, written once so Unity uses the font's own line
    height (the same setting as the game font). Never overwritten, so the
    font keeps its identity in the project across rebuilds."""
    meta = path + ".meta"
    if os.path.exists(meta):
        return
    import uuid
    with open(meta, "w", encoding="utf-8", newline="\n") as f:
        f.write(META % (uuid.uuid4().hex, family))


def save(font, filename):
    os.makedirs(OUT_FONTS, exist_ok=True)
    path = os.path.join(OUT_FONTS, filename)
    font.save(path)
    print("  %-28s %7.1f KB" % (filename, os.path.getsize(path) / 1024))
    write_meta(path, font["name"].getDebugName(1))
    return path


def chars_of(texts):
    s = set()
    for t in texts:
        s.update(t)
    s.discard("\n")
    return s


# ------------------------------------------------------------------ plain subsets

def build_plain(lang, source, filename, family, ascent, descent, weight=800):
    texts = TEXTS.get(lang, []) + [NATIVE[lang]]
    cps = {ord(c) for c in chars_of(texts) | set(ASCII)}
    font = TTFont(source_font(source))
    font = subset_to(font, cps)          # subset first: far faster to instance
    font = static_bold_from(font, weight)
    missing = [chr(c) for c in cps if c not in font.getBestCmap()]
    if missing:
        print("  ! %s font lacks: %s" % (lang, "".join(sorted(missing))))
    set_metrics(font, ascent, descent)
    rename(font, family)
    save(font, filename)


def static_bold_from(font, weight=800):
    # heavy, to sit alongside the game's chunky racing font
    if "fvar" in font:
        axes = {"wght": weight}
        if any(a.axisTag == "wdth" for a in font["fvar"].axes):
            axes["wdth"] = 100
        font = instancer.instantiateVariableFont(font, axes)
    return font


# ------------------------------------------------------------------ shaped scripts

def hb_font_of(ttfont):
    buf = io.BytesIO()
    ttfont.save(buf)
    face = hb.Face(buf.getvalue())
    return hb.Font(face)


def shape(hbfont, text, direction, script, language):
    buf = hb.Buffer()
    buf.add_str(text)
    buf.direction = direction
    buf.script = script
    buf.language = language
    hb.shape(hbfont, buf, {"kern": True, "liga": True})
    return buf.glyph_infos, buf.glyph_positions


def add_composite(font, glyph_order_names, parts, advance, codepoint, name):
    """A new glyph made of the shaped pieces, mapped to codepoint."""
    glyf = font["glyf"]
    hmtx = font["hmtx"]
    g = Glyph()
    g.components = []
    for gname, x, y in parts:
        comp = GlyphComponent()
        comp.glyphName = gname
        comp.x, comp.y = int(round(x)), int(round(y))
        comp.flags = 0x0004    # ROUND_XY_TO_GRID
        g.components.append(comp)
    g.numberOfContours = -1
    font.glyphOrder.append(name)
    glyf.glyphs[name] = g
    g.recalcBounds(glyf)
    lsb = g.xMin if hasattr(g, "xMin") else 0
    hmtx.metrics[name] = (int(round(advance)), int(lsb))
    for table in font["cmap"].tables:
        if table.isUnicode():
            table.cmap[codepoint] = name


def finish_glyph_order(font):
    """After adding glyphs: tell every part of the font about them."""
    order = font.glyphOrder
    font.setGlyphOrder(order)
    font["glyf"].setGlyphOrder(order)
    font.getReverseGlyphMap(rebuild=True)
    font["maxp"].numGlyphs = len(order)


def prepare_shaping_font(source):
    font = static_bold(source_font(source))
    # decompile everything now so glyphs can be added
    font["glyf"]; font["hmtx"]; font["cmap"]
    font.glyphOrder = list(font.getGlyphOrder())
    return font


def is_arabic_letter(c):
    o = ord(c)
    if c in "،؛؟۔":
        return False
    if 0x0660 <= o <= 0x0669 or 0x06F0 <= o <= 0x06F9:
        return False
    return (0x0600 <= o <= 0x06FF or 0x0750 <= o <= 0x077F or 0x08A0 <= o <= 0x08FF
            or 0xFB50 <= o <= 0xFDFF or 0xFE70 <= o <= 0xFEFF)


def build_arabic():
    texts = TEXTS.get("ar", []) + [NATIVE["ar"]]
    words = []
    for t in texts:
        i = 0
        while i < len(t):
            if is_arabic_letter(t[i]):
                j = i
                while j < len(t) and is_arabic_letter(t[j]):
                    j += 1
                if t[i:j] not in words:
                    words.append(t[i:j])
                i = j
            else:
                i += 1

    font = prepare_shaping_font("arabic")
    hbf = hb_font_of(font)
    order = font.getGlyphOrder()
    keep = set(ord(c) for c in ASCII) | {0x060C, 0x061B, 0x061F}
    keep |= set(range(0x0621, 0x064B))      # bare letters, only as a fallback
    shapes = []
    for n, word in enumerate(words):
        infos, pos = shape(hbf, word, "rtl", "Arab", "ar")
        parts, pen = [], 0
        for info, p in zip(infos, pos):
            gname = order[info.codepoint]
            parts.append((gname, pen + p.x_offset, p.y_offset))
            pen += p.x_advance
        cp = PUA_ARABIC + n
        add_composite(font, order, parts, pen, cp, "arw%04d" % n)
        shapes.append((word, cp))
        keep.add(cp)
    finish_glyph_order(font)
    font = subset_to(font, keep)
    set_metrics(font, 950, -380)
    rename(font, "Driftline Arabic")
    save(font, "DriftlineAR.ttf")
    write_shapes("shapes_ar.txt", shapes)
    return len(shapes)


def is_devanagari(c):
    return 0x0900 <= ord(c) <= 0x097F


def is_consonant(c):
    o = ord(c)
    return 0x0915 <= o <= 0x0939 or 0x0958 <= o <= 0x095F or 0x0978 <= o <= 0x097F


def is_mark(c):
    o = ord(c)
    return (0x0900 <= o <= 0x0903 or 0x093A <= o <= 0x093C or 0x093E <= o <= 0x094F
            or 0x0951 <= o <= 0x0957 or 0x0962 <= o <= 0x0963 or o in (0x200C, 0x200D))


def syllables(line):
    """(start, end) of each unit: a whole Hindi syllable - consonants joined by
    virama, with their vowel signs and marks - or any single other character."""
    units, i, n = [], 0, len(line)
    while i < n:
        if not is_devanagari(line[i]) or is_mark(line[i]):
            units.append((i, i + 1))
            i += 1
            continue
        j = i + 1
        while j < n and is_mark(line[j]):
            virama = line[j] == "\u094D"
            j += 1
            if virama and j < n and is_consonant(line[j]):
                j += 1
        units.append((i, j))
        i = j
    return units


def build_hindi():
    texts = TEXTS.get("hi", []) + [NATIVE["hi"]]
    font = prepare_shaping_font("deva")
    hbf = hb_font_of(font)
    order = font.getGlyphOrder()
    keep = set(ord(c) for c in ASCII) | set(range(0x0900, 0x0980))
    seen = {}
    shapes = []
    for text in texts:
        for line in text.split("\n"):
            if not any(is_devanagari(c) for c in line):
                continue
            units = syllables(line)
            unit_of = {}
            for u, (s, e) in enumerate(units):
                for k in range(s, e):
                    unit_of[k] = u
            buf = hb.Buffer()
            buf.add_str(line)
            buf.direction, buf.script, buf.language = "ltr", "Deva", "hi"
            # no kerning between syllables: each is drawn on its own
            hb.shape(hbf, buf, {"kern": False})
            groups = []
            for info, p in zip(buf.glyph_infos, buf.glyph_positions):
                u = unit_of[info.cluster]
                if groups and groups[-1][0] == u:
                    groups[-1][1].append((info, p))
                else:
                    groups.append((u, [(info, p)]))
            for u, glyphs in groups:
                s, e = units[u]
                piece = line[s:e]
                if not any(is_devanagari(c) for c in piece):
                    continue
                parts, pen = [], 0
                for info, p in glyphs:
                    parts.append((order[info.codepoint], pen + p.x_offset, p.y_offset))
                    pen += p.x_advance
                sig = (tuple(parts), pen)
                if piece in seen:
                    if seen[piece] != sig:
                        print("  ! syllable %r shapes differently in two places; keeping the first" % piece)
                    continue
                seen[piece] = sig
                cp = PUA_HINDI + len(shapes)
                add_composite(font, order, parts, pen, cp, "his%04d" % len(shapes))
                shapes.append((piece, cp))
                keep.add(cp)
    finish_glyph_order(font)
    font = subset_to(font, keep)
    set_metrics(font, 900, -350)
    rename(font, "Driftline Hindi")
    save(font, "DriftlineHI.ttf")
    write_shapes("shapes_hi.txt", shapes)
    return len(shapes)


def write_shapes(filename, shapes):
    os.makedirs(OUT_SHAPES, exist_ok=True)
    with open(os.path.join(OUT_SHAPES, filename), "w", encoding="utf-8", newline="\n") as f:
        for text, cp in shapes:
            f.write("%s\t%04X\n" % (text, cp))


# ------------------------------------------------------------------ main

if __name__ == "__main__":
    if len(sys.argv) > 1:
        ROOT = os.path.abspath(sys.argv[1])
        STRINGS = os.path.join(ROOT, "Assets", "Resources", "Localization", "strings.txt")
        OUT_FONTS = os.path.join(ROOT, "Assets", "Resources", "Fonts", "Lang")
        OUT_SHAPES = os.path.join(ROOT, "Assets", "Resources", "Localization")
    print("reading", STRINGS)
    TEXTS = read_strings(STRINGS)
    print("building fonts into", OUT_FONTS)
    build_plain("zh", "sc", "DriftlineZH.ttf", "Driftline Chinese", 880, -120)
    build_plain("ja", "jp", "DriftlineJA.ttf", "Driftline Japanese", 880, -120)
    build_plain("ko", "kr", "DriftlineKO.ttf", "Driftline Korean", 880, -120)
    build_plain("ru", "latin", "DriftlineRU.ttf", "Driftline Cyrillic", 1000, -280, weight=700)
    build_plain("ru", "russo", "DriftlineRU-Display.ttf", "Driftline Display Cyrillic", 926, -279)
    print("  Arabic words:", build_arabic())
    print("  Hindi syllables:", build_hindi())
    print("done")
