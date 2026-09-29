# Languages in Driftline

The game speaks English, Spanish, Portuguese (Brazil), French, German, Russian,
Chinese (Simplified), Japanese, Korean, Arabic and Hindi. It starts in the
phone's language and the player can change it in **Settings > General >
Language**.

## Where the words are

`Assets/Resources/Localization/strings.txt` holds every piece of text the
player reads. It is a tab-separated table, so it opens in Excel, Google Sheets
or Numbers (import as tab-separated, UTF-8), and saves back the same way.

- The first column is the English the game's code uses to find the text.
  **Don't change it**: if you do, the code can't find that row any more.
- The `en` column is only filled in where the text shown in English differs
  from the first column.
- An empty cell shows the English. `\n` starts a new line. `\0` shows nothing.
- `{0}` and `{1}` are filled in by the game (a number, a car's name...). Keep
  them, though they can move to wherever the language needs them.

### Adding new text in code

Plain text needs nothing special. The game's text boxes translate whatever is
put in them (`label.text = "PLAY"` shows JUGAR in Spanish), as long as that
English is a row in `strings.txt`. For text with numbers in it, use
`Loc.F("+{0} COINS", amount)` rather than joining strings together, and add
`+{0} COINS` as a row. Anything with no row simply shows in English.

## Fonts: after changing Russian, Chinese, Japanese, Korean, Arabic or Hindi

The game's own font only has Latin letters, so these languages have their own
fonts in `Assets/Resources/Fonts/Lang/`. They hold only the characters the game
uses, which keeps them small. Arabic and Hindi also need their letters joined
and arranged, which Unity's text can't do. So every Arabic word and every Hindi
syllable is prepared in advance and saved into the font.

**So whenever you change text in any of those six languages, run:**

    python3 -m pip install fonttools uharfbuzz     (once)
    python3 Tools/Localization/build_fonts.py

It rebuilds the fonts and the `shapes_ar.txt` / `shapes_hi.txt` files next to
`strings.txt`. The source fonts are downloaded the first time into
`Tools/Localization/.font-cache/`, which git ignores. Text in the Latin-script
languages (Spanish, Portuguese, French, German) needs no rebuild.

If a new character or word shows as blank or with disconnected letters, the
build hasn't been run since that text was added.

## Fonts and licences

- Noto Sans (Chinese, Japanese, Korean, Arabic, Devanagari, Latin/Cyrillic) by
  Google, SIL Open Font License 1.1: `OFL-Noto.txt`
- Russo One by Jovanny Lemonad, SIL Open Font License 1.1: `OFL-RussoOne.txt`

The built fonts are renamed ("Driftline ...") because they are modified. The
licence requires this, and "Russo" is a reserved name. They keep their
copyright and licence notices inside the files, and the in-game credits name
them.
