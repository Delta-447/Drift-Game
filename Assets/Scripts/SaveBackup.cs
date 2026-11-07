using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// Keeps the player's progress when the game is deleted and installed again.
///
/// iPhone: the game's save (PlayerPrefs) is wiped with the app, so a copy of
/// every saved value is kept in the keychain (DriftlineSave.mm), which iOS
/// keeps after the app is deleted. When a freshly installed game starts with
/// no save of its own, the copy is put back before anything reads the save.
/// The copy is refreshed whenever the game goes to the background or quits,
/// after a purchase, and on the way back to the menu.
///
/// Android needs none of this: Google's Auto Backup saves the PlayerPrefs file
/// with the phone's Google backup and puts it back when the game is
/// reinstalled. It is on unless a manifest sets android:allowBackup="false" -
/// nothing in the project does; keep it that way.
///
/// ANY NEW PlayerPrefs KEY MUST BE ADDED TO THE LISTS BELOW, or it will not
/// come back after a reinstall.
/// </summary>
public class SaveBackup : MonoBehaviour
{
    const string Header = "DLSAVE1";
    // the save of every player who has started the game has this; a freshly
    // installed game does not (GameManager.WipeSaveOnce writes it at start)
    const string ResetTokenKey = "SaveResetToken";
    // set when a fresh install could not read the keychain: the copy is left
    // alone and the next launch looks again
    const string RetryKey = "SaveRestoreRetry";

    static readonly string[] IntKeys =
    {
        "Coins", "Tires", "SelectedCar", "SpinsLeft", "QuestLevel", "QuestsInit",
        "LoginStreak", "HighScore", "MultReset", "InvertSteer", "UseMph",
        "UpgEngineSteps", "RoadBoxes", "TutorialSeen",
        "LbLeague", "LbWeekBest", "LbThird", "LbSeed", "LbRef", "LbLocked",
    };

    static readonly string[] FloatKeys =
    {
        "Volume", "VolSfx", "VolMusic", "VolEngine", "VolDrift", "VolCoins", "Sensitivity",
    };

    static readonly string[] StringKeys =
    {
        ResetTokenKey, "LastLogin", "SpinDay", "SpinAdDay", "UpgradeAdDay", "LoginCarWon",
        "Language", "Username", "LbWeek", "LbBots", "LbPrizes", "LbResult",
    };

    // numbered keys: room for more cars, quests, tokens and upgrades than the
    // game has, so new ones are covered without touching this file
    const int MaxCars = 64;
    const int MaxQuests = 8;
    const int MaxNumbered = 16;

    static IEnumerable<string> NumberedIntKeys()
    {
        for (int i = 0; i < MaxCars; i++)
        {
            yield return "CarOwned" + i;
            yield return "PaintBody" + i;
            yield return "PaintWheel" + i;
        }
        for (int i = 0; i < MaxQuests; i++)
        {
            yield return "Q" + i + "Type";
            yield return "Q" + i + "Target";
            yield return "Q" + i + "Prog";
        }
        for (int i = 0; i < RaceMode.TotalLevels + MaxNumbered; i++)
        {
            yield return "RaceDone" + i;
            yield return "RacePlace" + i;
        }
        for (int i = 0; i < ItemUpgrades.Count + MaxNumbered; i++) yield return "ItemLvl" + i;
        for (int i = 0; i < MaxNumbered; i++)
        {
            yield return "Token" + i;
            yield return "Upg" + i;
        }
    }

    static IEnumerable<string> AllIntKeys()
    {
        foreach (string key in IntKeys) yield return key;
        foreach (string key in NumberedIntKeys()) yield return key;
    }

#if UNITY_IOS && !UNITY_EDITOR
    const bool Supported = true;
    // 1 stored, 0 refused
    [DllImport("__Internal")] static extern int _Driftline_SaveWrite(string text);
    // the copy, "" when there is none, null when the keychain could not be read
    [DllImport("__Internal")] static extern string _Driftline_SaveRead();
    static string ReadCopy() { return _Driftline_SaveRead(); }
    static bool WriteCopy(string text) { return _Driftline_SaveWrite(text) == 1; }
#elif SAVE_CHECK
    public static bool Supported = true;
    public static string Keychain = "";            // stand-in for the real one
    public static bool KeychainLocked;
    static string ReadCopy() { return KeychainLocked ? null : Keychain; }
    static bool WriteCopy(string text) { if (KeychainLocked) return false; Keychain = text; return true; }
#else
    const bool Supported = false;
    static string ReadCopy() { return ""; }
    static bool WriteCopy(string text) { return false; }
#endif

    static bool ready;          // the restore check is done: copies may be written
    static bool retryPending;   // the keychain could not be read at launch
    static string lastWritten;

    /// <summary>
    /// Before the first scene - so before GameManager reads the save: a game
    /// that starts without a save of its own takes the keychain copy, if
    /// there is one.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if SAVE_CHECK
    public
#endif
    static void RestoreIfReinstalled()
    {
#pragma warning disable 162 // unreachable where the backup is not used
        if (!Supported) return;
        bool retry = PlayerPrefs.GetInt(RetryKey, 0) == 1;
        bool fresh = retry || !PlayerPrefs.HasKey(ResetTokenKey);
        if (!fresh)
        {
            ready = true;       // the game already has its save
            return;
        }

        string stored = ReadCopy();
        if (stored == null)
        {
            // could not look: keep the copy untouched and look next launch
            retryPending = true;
            PlayerPrefs.SetInt(RetryKey, 1);
            PlayerPrefs.Save();
            Debug.LogWarning("[SAVE] keychain could not be read - will try again next launch");
            return;
        }

        retryPending = false;
        PlayerPrefs.DeleteKey(RetryKey);
        List<Entry> entries = stored.Length > 0 ? Parse(stored) : null;
        if (entries != null)
        {
            if (retry) ClearKnownKeys();    // the stand-in save of the launch that could not look
            foreach (Entry e in entries) e.Apply();
            Debug.Log("[SAVE] progress restored from the keychain (" + entries.Count + " values)");
        }
        PlayerPrefs.Save();
        ready = true;
#pragma warning restore 162
    }

    /// <summary>
    /// GameManager.WipeSaveOnce clears the whole save; this keeps the note
    /// that the keychain still has to be looked at.
    /// </summary>
    public static void AfterWipe()
    {
        if (!retryPending) return;
        PlayerPrefs.SetInt(RetryKey, 1);
        PlayerPrefs.Save();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartWatching()
    {
#pragma warning disable 162
        if (!Supported) return;
        var go = new GameObject("SaveBackup");
        DontDestroyOnLoad(go);
        go.AddComponent<SaveBackup>();
#pragma warning restore 162
    }

    // after every Awake, so after GameManager has set the save up
    void Start() { Write(); }

    void OnApplicationPause(bool paused) { if (paused) Write(); }

    void OnApplicationQuit() { Write(); }

    /// <summary>
    /// Refreshes the keychain copy with the save as it is now. Cheap when
    /// nothing changed since the last copy.
    /// </summary>
    public static void Write()
    {
#pragma warning disable 162
        if (!Supported || !ready || retryPending) return;
        // a save without the reset token would be wiped when it came back
        if (!PlayerPrefs.HasKey(ResetTokenKey)) return;
        string text = Snapshot();
        if (text == lastWritten) return;
        if (WriteCopy(text)) lastWritten = text;
        else Debug.LogWarning("[SAVE] keychain refused the progress copy");
#pragma warning restore 162
    }

#if SAVE_CHECK
    public static void ResetForCheck() { ready = false; retryPending = false; lastWritten = null; }
    public static string SnapshotForCheck() { return Snapshot(); }
#endif

    // ---- the copy's format ----
    // first line "DLSAVE1", then one "type<TAB>key<TAB>value" line per value
    // (type i, f or s). Anything outside plain ASCII, tabs and line breaks
    // included, is written as \uXXXX, so the text survives any trip.

    static string Snapshot()
    {
        var sb = new StringBuilder(4096);
        sb.Append(Header).Append('\n');
        foreach (string key in AllIntKeys())
        {
            if (!PlayerPrefs.HasKey(key)) continue;
            Line(sb, 'i', key, PlayerPrefs.GetInt(key).ToString(CultureInfo.InvariantCulture));
        }
        foreach (string key in FloatKeys)
        {
            if (!PlayerPrefs.HasKey(key)) continue;
            Line(sb, 'f', key, PlayerPrefs.GetFloat(key).ToString("R", CultureInfo.InvariantCulture));
        }
        foreach (string key in StringKeys)
        {
            if (!PlayerPrefs.HasKey(key)) continue;
            Line(sb, 's', key, PlayerPrefs.GetString(key));
        }
        return sb.ToString();
    }

    static void Line(StringBuilder sb, char type, string key, string value)
    {
        sb.Append(type).Append('\t');
        Escape(sb, key);
        sb.Append('\t');
        Escape(sb, value ?? "");
        sb.Append('\n');
    }

    static void Escape(StringBuilder sb, string s)
    {
        foreach (char c in s)
        {
            if (c == '\\') sb.Append("\\\\");
            else if (c >= 32 && c < 127) sb.Append(c);
            else sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }
    }

    static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            char next = s[i + 1];
            if (next == 'u' && i + 5 < s.Length &&
                int.TryParse(s.Substring(i + 2, 4), NumberStyles.HexNumber,
                             CultureInfo.InvariantCulture, out int code))
            {
                sb.Append((char)code);
                i += 5;
            }
            else
            {
                sb.Append(next);
                i += 1;
            }
        }
        return sb.ToString();
    }

    struct Entry
    {
        public char type;
        public string key;
        public string value;

        public void Apply()
        {
            if (type == 'i' && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                PlayerPrefs.SetInt(key, n);
            else if (type == 'f' && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                PlayerPrefs.SetFloat(key, f);
            else if (type == 's')
                PlayerPrefs.SetString(key, value);
        }
    }

    /// <summary>The copy's values, or null if it is not a whole, usable copy.</summary>
    static List<Entry> Parse(string text)
    {
        string[] lines = text.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != Header) return null;
        var entries = new List<Entry>();
        bool hasToken = false;
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0) continue;
            string[] parts = line.Split('\t');
            if (parts.Length != 3 || parts[0].Length != 1) continue;
            var e = new Entry { type = parts[0][0], key = Unescape(parts[1]), value = Unescape(parts[2]) };
            if (e.key.Length == 0) continue;
            if (e.type == 's' && e.key == ResetTokenKey) hasToken = true;
            entries.Add(e);
        }
        // without the token the game would wipe it straight away
        return hasToken ? entries : null;
    }

    static void ClearKnownKeys()
    {
        foreach (string key in AllIntKeys()) PlayerPrefs.DeleteKey(key);
        foreach (string key in FloatKeys) PlayerPrefs.DeleteKey(key);
        foreach (string key in StringKeys) PlayerPrefs.DeleteKey(key);
    }
}
