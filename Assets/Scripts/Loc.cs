using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// The game's languages. Every word the player reads goes through here:
/// <c>Loc.T("PLAY")</c> gives it in the chosen language, and
/// <c>Loc.F("+{0} COINS", n)</c> fills numbers in the way string.Format does.
/// The English text is the key, so the code still reads naturally, and
/// anything without a translation simply shows in English.
///
/// The words live in Resources/Localization/strings.txt: one row per piece of
/// text, one column per language, separated by tabs, so it opens in any
/// spreadsheet. See Tools/Localization/README.md before editing it.
///
/// The language starts as the phone's own and can be changed in Settings.
///
/// Arabic and Hindi cannot be drawn by Unity's text as they are typed: Arabic
/// letters change shape to join up and run right to left, and Hindi letters
/// combine and reorder. So each Arabic word and each Hindi syllable is drawn
/// ahead of time into its language's font (Tools/Localization/build_fonts.py),
/// and <see cref="Display(string)"/> swaps the typed text for those shapes -
/// and puts Arabic in right-to-left order - just before it goes on screen.
/// </summary>
public static class Loc
{
    public enum Lang
    {
        English, Spanish, Portuguese, French, German, Russian,
        Chinese, Japanese, Korean, Arabic, Hindi,
    }
    public const int Count = 11;

    // column names in strings.txt, in Lang order
    static readonly string[] Codes = { "en", "es", "pt", "fr", "de", "ru", "zh", "ja", "ko", "ar", "hi" };

    // each language's own name for itself, as the language list shows it
    static readonly string[] Native =
    {
        "ENGLISH", "ESPAÑOL", "PORTUGUÊS", "FRANÇAIS", "DEUTSCH", "РУССКИЙ",
        "中文", "日本語", "한국어", "العربية", "हिन्दी",
    };

    // Fonts for the languages the game font cannot write. null = the game's
    // own fonts. Built by Tools/Localization/build_fonts.py.
    static readonly string[] DisplayFontPaths =
    {
        null, null, null, null, null, "Fonts/Lang/DriftlineRU-Display",
        "Fonts/Lang/DriftlineZH", "Fonts/Lang/DriftlineJA", "Fonts/Lang/DriftlineKO",
        "Fonts/Lang/DriftlineAR", "Fonts/Lang/DriftlineHI",
    };
    static readonly string[] PlainFontPaths =
    {
        null, null, null, null, null, "Fonts/Lang/DriftlineRU",
        "Fonts/Lang/DriftlineZH", "Fonts/Lang/DriftlineJA", "Fonts/Lang/DriftlineKO",
        "Fonts/Lang/DriftlineAR", "Fonts/Lang/DriftlineHI",
    };

    const string PrefKey = "Language";

    static Dictionary<string, string>[] tables;
    static Dictionary<string, char> arabicWords, hindiSyllables;
    static int hindiLongest;
    static Lang current;
    static bool ready;

    // ------------------------------------------------------------ public

    /// <summary>The language everything is shown in.</summary>
    public static Lang Current { get { EnsureReady(); return current; } }

    /// <summary>The text for <paramref name="key"/> (its English) in the current language.</summary>
    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        EnsureReady();
        string s;
        if (tables[(int)current].TryGetValue(key, out s)) return s;
        if (tables[0].TryGetValue(key, out s)) return s;
        return key;
    }

    /// <summary>Like string.Format, on the translated text.</summary>
    public static string F(string key, params object[] args)
    {
        try { return string.Format(CultureInfo.InvariantCulture, T(key), args); }
        catch (System.FormatException)
        {
            // a translation with a broken {0} falls back to the English
            return string.Format(CultureInfo.InvariantCulture, key, args);
        }
    }

    /// <summary>Saves a new language. The game reloads the menu to show it.</summary>
    public static void Set(Lang lang)
    {
        EnsureReady();
        current = Usable(lang) ? lang : Lang.English;
        PlayerPrefs.SetString(PrefKey, Codes[(int)current]);
        PlayerPrefs.Save();
    }

    /// <summary>A language's name for itself.</summary>
    public static string NativeName(Lang lang) { return Native[(int)lang]; }

    /// <summary>The display font for a language, or null for the game's own.</summary>
    public static Font DisplayFontFor(Lang lang) { return LoadFont(DisplayFontPaths[(int)lang]); }

    /// <summary>The plain (small text) font for a language, or null for the game's own.</summary>
    public static Font PlainFontFor(Lang lang) { return LoadFont(PlainFontPaths[(int)lang]); }

    /// <summary>
    /// How much bigger a script is set than the game font at the same size,
    /// so they read at a similar weight. The game font is very wide; Arabic
    /// and Hindi letters sit small in their line, so they get a lift.
    /// </summary>
    public static float SizeScale(Lang lang)
    {
        switch (lang)
        {
            case Lang.Arabic: return 1.2f;
            case Lang.Hindi: return 1.1f;
            default: return 1f;
        }
    }

    /// <summary>The text as it must be handed to Unity to show correctly.</summary>
    public static string Display(string s) { return Display(s, Current); }

    /// <summary>The text as it must be handed to Unity to show in <paramref name="lang"/>.</summary>
    public static string Display(string s, Lang lang)
    {
        if (string.IsNullOrEmpty(s)) return s;
        EnsureReady();
        if (lang == Lang.Arabic) return ArabicOnScreen(s);
        if (lang == Lang.Hindi) return HindiOnScreen(s);
        return s;
    }

    /// <summary>
    /// Whether a language can be shown: Arabic and Hindi need their prepared
    /// shapes and font, and every other script needs its font.
    /// </summary>
    public static bool Usable(Lang lang)
    {
        EnsureReady();
        if (DisplayFontPaths[(int)lang] != null && DisplayFontFor(lang) == null) return false;
        if (lang == Lang.Arabic && arabicWords == null) return false;
        if (lang == Lang.Hindi && hindiSyllables == null) return false;
        return tables[(int)lang].Count > 0 || lang == Lang.English;
    }

    // ------------------------------------------------------------ loading

    static void EnsureReady()
    {
        if (ready) return;
        ready = true;
        LoadTables();
        arabicWords = LoadShapes("Localization/shapes_ar", out int _);
        hindiSyllables = LoadShapes("Localization/shapes_hi", out hindiLongest);
        current = SavedOrSystem();
        if (!Usable(current)) current = Lang.English;
    }

    static void LoadTables()
    {
        tables = new Dictionary<string, string>[Count];
        for (int i = 0; i < Count; i++) tables[i] = new Dictionary<string, string>();

        var asset = Resources.Load<TextAsset>("Localization/strings");
        if (asset == null)
        {
            Debug.LogWarning("[Loc] Resources/Localization/strings.txt is missing - English only.");
            return;
        }

        string[] lines = asset.text.Split('\n');
        if (lines.Length == 0) return;

        // which column is which language
        string[] header = lines[0].TrimEnd('\r').Split('\t');
        var column = new int[header.Length];
        for (int c = 0; c < header.Length; c++)
        {
            column[c] = -1;
            for (int l = 0; l < Count; l++)
                if (header[c].Trim() == Codes[l]) column[c] = l;
        }

        for (int r = 1; r < lines.Length; r++)
        {
            string line = lines[r].TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            string[] cells = line.Split('\t');
            string key = Unescape(cells[0]);
            if (key.Length == 0) continue;
            for (int c = 1; c < cells.Length && c < column.Length; c++)
            {
                if (column[c] < 0 || cells[c].Length == 0) continue;
                // an empty cell means "no translation yet"; \0 means "nothing"
                tables[column[c]][key] = cells[c] == "\\0" ? "" : Unescape(cells[c]);
            }
        }
    }

    static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>"text TAB hex-codepoint" lines, as written by build_fonts.py.</summary>
    static Dictionary<string, char> LoadShapes(string path, out int longest)
    {
        longest = 0;
        var asset = Resources.Load<TextAsset>(path);
        if (asset == null) return null;
        var map = new Dictionary<string, char>();
        foreach (string raw in asset.text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            int code;
            if (!int.TryParse(line.Substring(tab + 1), NumberStyles.HexNumber,
                              CultureInfo.InvariantCulture, out code)) continue;
            string text = line.Substring(0, tab);
            map[text] = (char)code;
            if (text.Length > longest) longest = text.Length;
        }
        return map.Count > 0 ? map : null;
    }

    static Lang SavedOrSystem()
    {
        string saved = PlayerPrefs.GetString(PrefKey, "");
        for (int i = 0; i < Count; i++)
            if (Codes[i] == saved) return (Lang)i;

        switch (Application.systemLanguage)
        {
            case SystemLanguage.Spanish: return Lang.Spanish;
            case SystemLanguage.Portuguese: return Lang.Portuguese;
            case SystemLanguage.French: return Lang.French;
            case SystemLanguage.German: return Lang.German;
            case SystemLanguage.Russian: return Lang.Russian;
            case SystemLanguage.Chinese:
            case SystemLanguage.ChineseSimplified:
            case SystemLanguage.ChineseTraditional: return Lang.Chinese;
            case SystemLanguage.Japanese: return Lang.Japanese;
            case SystemLanguage.Korean: return Lang.Korean;
            case SystemLanguage.Arabic: return Lang.Arabic;
        }
        // Hindi is only known to newer versions of Unity, so it is checked by
        // name - this way the code compiles either way
        if (Application.systemLanguage.ToString() == "Hindi") return Lang.Hindi;
        return Lang.English;
    }

    static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();

    static Font LoadFont(string path)
    {
        if (path == null) return null;
        Font f;
        if (!fontCache.TryGetValue(path, out f) || f == null)
        {
            f = Resources.Load<Font>(path);
            fontCache[path] = f;
        }
        return f;
    }

    // ------------------------------------------------------------ Hindi

    static bool IsDevanagari(char c) { return c >= 'ऀ' && c <= 'ॿ'; }

    /// <summary>
    /// Swaps each Hindi syllable for its prepared shape. At every Devanagari
    /// letter the longest prepared syllable that starts there is used.
    /// </summary>
    static string HindiOnScreen(string s)
    {
        if (hindiSyllables == null) return s;
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (!IsDevanagari(s[i])) { sb.Append(s[i]); i++; continue; }
            int used = 0;
            char shape = '\0';
            for (int len = Mathf.Min(hindiLongest, s.Length - i); len >= 1; len--)
            {
                if (hindiSyllables.TryGetValue(s.Substring(i, len), out shape)) { used = len; break; }
            }
            if (used == 0) { sb.Append(s[i]); i++; }
            else { sb.Append(shape); i += used; }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------ Arabic

    static bool IsArabicLetter(char c)
    {
        // Arabic punctuation and digits are not letters - they do not join
        if (c == '،' || c == '؛' || c == '؟' || c == '۔') return false;
        if (c >= '٠' && c <= '٩') return false;
        if (c >= '۰' && c <= '۹') return false;
        return (c >= '؀' && c <= 'ۿ') || (c >= 'ݐ' && c <= 'ݿ')
            || (c >= 'ࢠ' && c <= 'ࣿ') || (c >= 'ﭐ' && c <= '﷿')
            || (c >= 'ﹰ' && c <= '﻿');
    }

    static bool IsLeftToRight(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
            || (c >= 'À' && c <= 'ɏ');
    }

    // punctuation that belongs to a number or name when it sits inside one: 1/7, 0.5, 12,345
    static bool JoinsLeftToRight(char c)
    {
        return c == '.' || c == ',' || c == ':' || c == '/' || c == '\'' || c == '-';
    }

    const int Neutral = 0, RightToLeft = 1, LeftToRight = 2;

    /// <summary>
    /// Arabic, ready for Unity: each word swapped for its joined-up shape and
    /// every line put in right-to-left order, with numbers and Latin names
    /// kept reading left to right inside it. Formatting tags at either end of
    /// a line (the size and colour tags the game uses) stay where they are.
    /// </summary>
    static string ArabicOnScreen(string s)
    {
        string[] lines = s.Split('\n');
        for (int i = 0; i < lines.Length; i++) lines[i] = ArabicLine(lines[i]);
        return string.Join("\n", lines);
    }

    static string ArabicLine(string line)
    {
        // leave <tags> at the start and end of the line alone
        int start = 0;
        while (start < line.Length && line[start] == '<')
        {
            int close = line.IndexOf('>', start);
            if (close < 0) break;
            start = close + 1;
        }
        int end = line.Length;
        while (end > start && line[end - 1] == '>')
        {
            int open = line.LastIndexOf('<', end - 1);
            if (open < start) break;
            end = open;
        }
        string body = line.Substring(start, end - start);
        if (body.Length == 0) return line;

        // split into words, runs of left-to-right text, and single neutral characters
        var parts = new List<string>();
        var dirs = new List<int>();
        int i = 0;
        while (i < body.Length)
        {
            char c = body[i];
            if (IsArabicLetter(c))
            {
                int j = i;
                while (j < body.Length && IsArabicLetter(body[j])) j++;
                string word = body.Substring(i, j - i);
                char shape;
                if (arabicWords != null && arabicWords.TryGetValue(word, out shape))
                    parts.Add(shape.ToString());
                else
                {
                    // not prepared: at least keep the letters in reading order
                    char[] rev = word.ToCharArray();
                    System.Array.Reverse(rev);
                    parts.Add(new string(rev));
                }
                dirs.Add(RightToLeft);
                i = j;
            }
            else if (IsLeftToRight(c) || ((c == '+' || c == '-' || c == '$' || c == '#')
                     && i + 1 < body.Length && IsLeftToRight(body[i + 1])))
            {
                int j = i + 1;
                while (j < body.Length)
                {
                    char d = body[j];
                    if (IsLeftToRight(d) || d == '%') { j++; continue; }
                    if (JoinsLeftToRight(d) && j + 1 < body.Length && IsLeftToRight(body[j + 1])) { j++; continue; }
                    break;
                }
                parts.Add(body.Substring(i, j - i));
                dirs.Add(LeftToRight);
                i = j;
            }
            else
            {
                parts.Add(c.ToString());
                dirs.Add(Neutral);
                i++;
            }
        }

        // a neutral between two left-to-right runs belongs to them; any other
        // takes the line's own direction, right to left
        for (int k = 0; k < parts.Count; k++)
        {
            if (dirs[k] != Neutral) continue;
            int before = RightToLeft, after = RightToLeft;
            for (int b = k - 1; b >= 0; b--) if (dirs[b] != Neutral) { before = dirs[b]; break; }
            for (int a = k + 1; a < parts.Count; a++) if (dirs[a] != Neutral) { after = dirs[a]; break; }
            dirs[k] = before == LeftToRight && after == LeftToRight ? -LeftToRight : -RightToLeft;
        }

        // lay the line out right to left, then turn each left-to-right run back round
        parts.Reverse();
        dirs.Reverse();
        int run = 0;
        while (run < parts.Count)
        {
            if (System.Math.Abs(dirs[run]) != LeftToRight) { run++; continue; }
            int stop = run;
            while (stop < parts.Count && System.Math.Abs(dirs[stop]) == LeftToRight) stop++;
            parts.Reverse(run, stop - run);
            run = stop;
        }

        var sb = new StringBuilder(line.Length);
        sb.Append(line, 0, start);
        for (int k = 0; k < parts.Count; k++)
        {
            string p = parts[k];
            // brackets in right-to-left text face the other way
            if (dirs[k] == -RightToLeft && p.Length == 1) p = Mirror(p[0]).ToString();
            sb.Append(p);
        }
        sb.Append(line, end, line.Length - end);
        return sb.ToString();
    }

    static char Mirror(char c)
    {
        switch (c)
        {
            case '(': return ')';
            case ')': return '(';
            case '[': return ']';
            case ']': return '[';
            case '<': return '>';
            case '>': return '<';
            case '«': return '»';
            case '»': return '«';
            default: return c;
        }
    }
}
