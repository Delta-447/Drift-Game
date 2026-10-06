using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The weekly ranking. It is played against made-up rivals: the board is
/// drawn up from the player's own best score, so it always sits within
/// reach.
///
/// Leagues run ROOKIE, BRONZE, SILVER, GOLD, DIAMOND, DRIFT KING. The week
/// starts on Monday. Where the player stands when it ends is what counts:
/// the top three win prizes (coins for 3rd, a mystery or token box for 2nd,
/// spins or tires for 1st, set by the league) and 1st moves the player up a
/// league. The rivals do not sit still - after every run the one just behind
/// the player edges past, as far as that rival has it in them, so a place
/// has to be held by putting distance between you. A week's best of three
/// times 2nd place locks 1st in on the spot: its prize can be claimed, and
/// the promotion taken, straight away.
/// </summary>
public partial class GameManager
{
    // ------------------------------------------------------------ leagues

    static readonly string[] LeagueNames =
        { "ROOKIE", "BRONZE", "SILVER", "GOLD", "DIAMOND", "DRIFT KING" };

    static readonly Color[] LeagueColors =
    {
        new Color(0.45f, 0.82f, 0.45f),   // rookie green
        new Color(0.86f, 0.53f, 0.28f),   // bronze
        new Color(0.80f, 0.84f, 0.92f),   // silver
        new Color(1.00f, 0.80f, 0.20f),   // gold
        new Color(0.40f, 0.88f, 1.00f),   // diamond
        new Color(1.00f, 0.32f, 0.46f),   // drift king
    };

    const int TopLeague = 5;

    // Where 3rd place sits, as a share of the player's best score when the
    // board was drawn up, and how far past 3rd the winner is. The higher the
    // league, the further past your own best the podium is. (Tuned in a
    // simulation together with the rivals' reach below: Silver comes in the
    // first week, Gold after a week or two, and Diamond and Drift King only
    // for players who keep getting better.)
    static readonly float[] LeagueThird = { 0.40f, 0.62f, 0.82f, 0.98f, 1.10f, 1.22f };
    static readonly float[] LeagueFirstOverThird = { 1.12f, 1.15f, 1.18f, 1.22f, 1.26f, 1.32f };
    // after a mid-week promotion, how far above the week's best 3rd place starts
    static readonly float[] LeaguePromotionGap = { 0.05f, 0.08f, 0.12f, 0.16f, 0.20f, 0.25f };
    // how far past its starting score each rival can still push this week
    const float RivalReachMin = 0.06f, RivalReachMax = 0.18f;
    // a week's best this many times 2nd place locks 1st in
    const float LockInFactor = 3f;

    // prizes, by league
    static readonly int[] ThirdPlaceCoins = { 150, 250, 400, 600, 900, 1300 };
    static readonly int[] FirstPlaceSpins = { 1, 2, 0, 0, 0, 0 };
    static readonly int[] FirstPlaceTires = { 0, 0, 3, 5, 8, 12 };

    const int LbBots = 29;              // rivals on a board (plus the player)
    const float LbScoreFloor = 400f;    // a brand new player still gets a board

    // saved state
    const string LbWeekKey = "LbWeek";          // the Monday the board belongs to
    const string LbLeagueKey = "LbLeague";
    const string LbWeekBestKey = "LbWeekBest";
    const string LbSeedKey = "LbSeed";          // names and spread of the rivals
    const string LbRefKey = "LbRef";            // the score the board was drawn up from
    const string LbThirdKey = "LbThird";        // where 3rd place started on it
    const string LbBotsKey = "LbBots";          // the rivals' scores as they stand now
    const string LbLockedKey = "LbLocked";      // 1st locked in this week
    const string LbPrizesKey = "LbPrizes";      // prizes won and not yet claimed
    const string LbResultKey = "LbResult";      // last week's finish, still to be shown

    int lbLeague, lbWeekBest, lbRef, lbThird, lbSeed;
    bool lbLocked;
    readonly string[] lbNames = new string[LbBots];
    readonly int[] lbScores = new int[LbBots];     // as they stand
    readonly int[] lbBaseScores = new int[LbBots]; // as the board was drawn up
    readonly int[] lbReach = new int[LbBots];      // the most each can score this week
    int lbBuiltSeed = -1, lbBuiltThird = -1, lbBuiltLeague = -1;
    bool lbResultWaiting;

    // ------------------------------------------------------------ the week

    static System.DateTime WeekStart(System.DateTime now)
    {
        int back = ((int)now.DayOfWeek + 6) % 7;   // Monday = 0
        return now.Date.AddDays(-back);
    }

    static string WeekKeyNow()
    {
        return WeekStart(System.DateTime.Now).ToString("yyyyMMdd");
    }

    /// <summary>Reads the board in from the save.</summary>
    void LbLoad()
    {
        lbLeague = Mathf.Clamp(PlayerPrefs.GetInt(LbLeagueKey, 0), 0, TopLeague);
        lbWeekBest = PlayerPrefs.GetInt(LbWeekBestKey, 0);
        lbSeed = PlayerPrefs.GetInt(LbSeedKey, 1);
        lbRef = PlayerPrefs.GetInt(LbRefKey, Mathf.RoundToInt(LbScoreFloor));
        lbThird = PlayerPrefs.GetInt(LbThirdKey, Mathf.RoundToInt(lbRef * LeagueThird[lbLeague]));
        lbLocked = PlayerPrefs.GetInt(LbLockedKey, 0) == 1;
        lbResultWaiting = PlayerPrefs.GetString(LbResultKey, "") != "";
        if (lbSeed != lbBuiltSeed || lbThird != lbBuiltThird || lbLeague != lbBuiltLeague)
            LbGenerate();
    }

    /// <summary>
    /// Loads the week's board. When a new week has begun, the one that just
    /// ended is settled first, and a fresh board is drawn up.
    /// </summary>
    void LbEnsureCurrent()
    {
        LbLoad();
        string week = WeekKeyNow();
        string stored = PlayerPrefs.GetString(LbWeekKey, "");
        if (stored == week) return;

        if (stored != "") LbSettleWeek();
        PlayerPrefs.SetString(LbWeekKey, week);
        lbWeekBest = 0;
        PlayerPrefs.SetInt(LbWeekBestKey, 0);
        LbNewBoard();
        PlayerPrefs.Save();
    }

    /// <summary>
    /// The week is over: the place the player held pays out, and 1st moves
    /// them up a league. Shown the next time the menu is up.
    /// </summary>
    void LbSettleWeek()
    {
        if (lbWeekBest <= 0) return;   // never ran that week
        int rank = LbRankOf(lbWeekBest);
        bool promoted = false;
        if (lbLocked)
        {
            // 1st was locked in during the week and its prize is already
            // waiting; the move up is taken now if it was not claimed yet
            rank = 1;
            if (lbLeague < TopLeague) { lbLeague++; promoted = true; }
        }
        else if (rank <= 3)
        {
            LbQueuePrize(rank, lbLeague);
            if (rank == 1 && lbLeague < TopLeague) { lbLeague++; promoted = true; }
        }
        PlayerPrefs.SetInt(LbLeagueKey, lbLeague);
        PlayerPrefs.SetString(LbResultKey, rank + "|" + (promoted ? 1 : 0));
        lbResultWaiting = true;
    }

    /// <summary>
    /// A fresh board for the current league, drawn up from the player's best.
    /// The week's best so far never starts on the podium: a new board is
    /// always something to climb.
    /// </summary>
    void LbNewBoard()
    {
        lbSeed = Random.Range(1, int.MaxValue);
        float reference = Mathf.Max(best, lbWeekBest, LbScoreFloor);
        float third = reference * LeagueThird[lbLeague];
        if (lbWeekBest > 0 && lbWeekBest >= third * 0.97f)
            third = lbWeekBest * (1f + LeaguePromotionGap[lbLeague]);
        lbRef = Mathf.RoundToInt(reference);
        lbThird = Mathf.RoundToInt(third);
        lbLocked = false;
        PlayerPrefs.SetInt(LbSeedKey, lbSeed);
        PlayerPrefs.SetInt(LbRefKey, lbRef);
        PlayerPrefs.SetInt(LbThirdKey, lbThird);
        PlayerPrefs.SetInt(LbLeagueKey, lbLeague);
        PlayerPrefs.SetInt(LbLockedKey, 0);
        PlayerPrefs.DeleteKey(LbBotsKey);
        LbGenerate();
    }

    /// <summary>The rivals' names, scores and reach - the same every time for a board.</summary>
    void LbGenerate()
    {
        var rng = new System.Random(lbSeed);
        var used = new HashSet<string>();
        for (int i = 0; i < LbBots; i++)
        {
            string n;
            int guard = 0;
            do { n = MakeGamertag(rng); } while (!used.Add(n.ToLowerInvariant()) && ++guard < 50);
            lbNames[i] = n;
        }

        float r = lbRef;
        float third = lbThird;
        float first = third * LeagueFirstOverThird[lbLeague] * (1f + 0.03f * (float)rng.NextDouble());
        float second = Mathf.Lerp(third, first, 0.35f + 0.2f * (float)rng.NextDouble());
        lbBaseScores[0] = Mathf.RoundToInt(first);
        lbBaseScores[1] = Mathf.RoundToInt(second);
        lbBaseScores[2] = Mathf.RoundToInt(third);

        // below the podium the field thins out fast, so a decent run climbs
        // a long way and only the top few are hard to pass
        float hi = third * 0.96f;
        float lo = Mathf.Max(r * 0.05f, 12f);
        for (int i = 3; i < LbBots; i++)
        {
            float f = (i - 3) / (float)(LbBots - 4);
            float s = hi * Mathf.Pow(lo / hi, Mathf.Pow(f, 0.85f));
            s *= 1f + 0.05f * ((float)rng.NextDouble() - 0.5f);
            lbBaseScores[i] = Mathf.RoundToInt(s);
        }
        for (int i = 1; i < LbBots; i++)
            lbBaseScores[i] = Mathf.Max(1, Mathf.Min(lbBaseScores[i], lbBaseScores[i - 1] - 1));
        for (int i = 0; i < LbBots; i++)
        {
            float reach = Mathf.Lerp(RivalReachMin, RivalReachMax, (float)rng.NextDouble());
            lbReach[i] = Mathf.RoundToInt(lbBaseScores[i] * (1f + reach));
            lbScores[i] = lbBaseScores[i];
        }

        // the rivals that have moved since: "seed|score,score,..."
        string saved = PlayerPrefs.GetString(LbBotsKey, "");
        int bar = saved.IndexOf('|');
        if (bar > 0 && saved.Substring(0, bar) == lbSeed.ToString())
        {
            string[] parts = saved.Substring(bar + 1).Split(',');
            for (int i = 0; i < LbBots && i < parts.Length; i++)
                if (int.TryParse(parts[i], out int v)) lbScores[i] = v;
        }

        lbBuiltSeed = lbSeed;
        lbBuiltThird = lbThird;
        lbBuiltLeague = lbLeague;
    }

    void LbSaveBots()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(lbSeed).Append('|');
        for (int i = 0; i < LbBots; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(lbScores[i]);
        }
        PlayerPrefs.SetString(LbBotsKey, sb.ToString());
    }

    /// <summary>Where a score places on the board (LbBots + 1 = not ranked yet).</summary>
    int LbRankOf(int score)
    {
        if (score <= 0) return LbBots + 1;
        int above = 0;
        for (int i = 0; i < LbBots; i++) if (lbScores[i] >= score) above++;
        return above + 1;
    }

    /// <summary>The best rival's score - 2nd place whenever the player is 1st.</summary>
    int LbTopRival()
    {
        int top = 0;
        for (int i = 0; i < LbBots; i++) top = Mathf.Max(top, lbScores[i]);
        return top;
    }

    /// <summary>What the week's best has to reach to lock 1st in.</summary>
    int LbLockInScore() { return Mathf.CeilToInt(LbTopRival() * LockInFactor); }

    // ------------------------------------------------------------ prizes

    /// <summary>Prizes won and not yet claimed, oldest first: "place:league;...".</summary>
    List<Vector2Int> LbPrizeQueue()
    {
        var list = new List<Vector2Int>();
        foreach (string e in PlayerPrefs.GetString(LbPrizesKey, "").Split(';'))
        {
            string[] p = e.Split(':');
            if (p.Length == 2 && int.TryParse(p[0], out int place) && int.TryParse(p[1], out int league))
                list.Add(new Vector2Int(place, Mathf.Clamp(league, 0, TopLeague)));
        }
        return list;
    }

    void LbSetPrizeQueue(List<Vector2Int> list)
    {
        var parts = new List<string>();
        foreach (var v in list) parts.Add(v.x + ":" + v.y);
        PlayerPrefs.SetString(LbPrizesKey, string.Join(";", parts));
    }

    void LbQueuePrize(int place, int league)
    {
        var q = LbPrizeQueue();
        q.Add(new Vector2Int(place, league));
        LbSetPrizeQueue(q);
    }

    bool LbPrizeWaiting() { return PlayerPrefs.GetString(LbPrizesKey, "") != ""; }

    // ------------------------------------------------------------ gamertags

    static readonly string[] TagAdjectives =
    {
        "crazy", "sneaky", "silent", "angry", "happy", "lazy", "wild", "salty",
        "spicy", "frozen", "rusty", "golden", "shadow", "turbo", "epic", "mighty",
        "lucky", "grumpy", "fuzzy", "sticky", "dizzy", "cosmic", "atomic", "savage",
        "rapid", "silly", "bouncy", "mad", "dark", "iron", "electric", "phantom",
        "jolly", "mega", "ultra", "hyper", "sly", "bold", "chunky", "crispy",
        "funky", "sleepy", "stinky", "nuclear", "radical", "wicked", "toxic", "royal",
    };

    static readonly string[] TagNouns =
    {
        "elephant", "knife", "panda", "tiger", "ninja", "wizard", "toaster", "pickle",
        "falcon", "viper", "wolf", "bear", "potato", "waffle", "monkey", "dragon",
        "cobra", "rhino", "goat", "llama", "hamster", "taco", "burrito", "rocket",
        "hawk", "shark", "badger", "moose", "penguin", "raptor", "muffin", "noodle",
        "walrus", "gecko", "ferret", "ghost", "pirate", "samurai", "cowboy", "bandit",
        "phoenix", "kraken", "yeti", "banana", "nugget", "spartan", "gorilla", "biscuit",
    };

    static readonly string[] TagDrivers = { "drifter", "racer", "burnout", "piston", "nitro", "clutch", "sideways" };

    static readonly string[] TagPrefixes = { "Mr", "Dr", "Sir", "Lord", "TheReal", "Captain", "Big", "Lil", "iTz" };

    static string Cap(string s) { return char.ToUpperInvariant(s[0]) + s.Substring(1); }

    static string TagNumber(System.Random rng)
    {
        double p = rng.NextDouble();
        if (p < 0.55)
        {
            int n = rng.Next(1, 100);
            return (n == 69 ? 68 : n).ToString();                   // 7, 55, 99
        }
        if (p < 0.75) return rng.Next(1, 100).ToString("00");      // 07
        if (p < 0.90) return rng.Next(84, 100).ToString();         // the year they were born
        string[] classics = { "360", "007", "101", "1337", "247", "777", "123", "2k5" };
        return classics[rng.Next(classics.Length)];
    }

    /// <summary>A made-up gamertag in the style of the old Xbox Live days.</summary>
    static string MakeGamertag(System.Random rng)
    {
        // gamertags were capped at 15 characters: a long one is drawn again
        // rather than cut off mid-word
        for (int tries = 0; tries < 12; tries++)
        {
            string tag = GamertagAttempt(rng);
            if (tag.Length <= 15) return tag;
        }
        return TagNouns[rng.Next(TagNouns.Length)] + rng.Next(1, 100);
    }

    static string GamertagAttempt(System.Random rng)
    {
        string adj = TagAdjectives[rng.Next(TagAdjectives.Length)];
        string noun = TagNouns[rng.Next(TagNouns.Length)];
        string tag;
        double p = rng.NextDouble();
        if (p < 0.32) tag = adj + noun + TagNumber(rng);                              // crazyelephant55
        else if (p < 0.52) tag = Cap(adj) + Cap(noun) + TagNumber(rng);               // SneakyPanda07
        else if (p < 0.60) tag = "xX" + Cap(adj) + Cap(noun) + "Xx";                  // xXShadowWolfXx
        else if (p < 0.70) tag = adj + "_" + noun + TagNumber(rng);                   // silent_ninja42
        else if (p < 0.78)
        {
            string noun2 = TagNouns[rng.Next(TagNouns.Length)];
            tag = noun + noun2 + TagNumber(rng);                                      // tacorocket360
        }
        else if (p < 0.88)
            tag = TagPrefixes[rng.Next(TagPrefixes.Length)] + Cap(noun) + (rng.NextDouble() < 0.5 ? TagNumber(rng) : "");
        else if (p < 0.96)
            tag = Cap(adj) + Cap(TagDrivers[rng.Next(TagDrivers.Length)]) + TagNumber(rng); // TurboDrifter88
        else tag = "bowey" + noun + TagNumber(rng);                                   // boweyknife99
        return tag;
    }

    // ------------------------------------------------------------ after a run

    bool lbShowAfterRun;      // open the ranking over the results
    bool lbRunCounted;        // this run's result is on the board (game over line)
    int lbRunOldScore;        // the week's best before this run
    int lbOvertaker = -1;     // the rival that edged past after this run
    int lbOvertakerFrom;      // its score before it did
    bool lbJustLocked;        // this run locked 1st in

    /// <summary>
    /// Checks an endless run against the week, then lets the rival just
    /// behind the player answer it. Called as the run ends.
    /// </summary>
    void LbRecordRun(int finalScore)
    {
        LbEnsureCurrent();
        lbOvertaker = -1;
        lbJustLocked = false;
        lbRunCounted = true;
        bool improved = finalScore > lbWeekBest;
        lbRunOldScore = lbWeekBest;
        if (improved)
        {
            lbWeekBest = finalScore;
            PlayerPrefs.SetInt(LbWeekBestKey, lbWeekBest);
        }

        if (!lbLocked && lbWeekBest > 0)
        {
            if (LbRankOf(lbWeekBest) == 1 && lbWeekBest >= LbLockInScore())
            {
                // so far clear that nobody can catch up: 1st is won now
                lbLocked = true;
                lbJustLocked = true;
                PlayerPrefs.SetInt(LbLockedKey, 1);
                LbQueuePrize(1, lbLeague);
            }
            else LbRivalsAnswer();
        }
        PlayerPrefs.Save();
        // the ranking comes up after a new weekly best, and whenever a rival
        // has just edged past
        lbShowAfterRun = improved || lbOvertaker >= 0;
    }

    /// <summary>
    /// The rival just behind the player edges past them - by a hair, and only
    /// if it has that score in it this week.
    /// </summary>
    void LbRivalsAnswer()
    {
        int chaser = -1, above = int.MaxValue;
        for (int i = 0; i < LbBots; i++)
        {
            int s = lbScores[i];
            if (s < lbWeekBest) { if (chaser < 0 || s > lbScores[chaser]) chaser = i; }
            else above = Mathf.Min(above, s);
        }
        if (chaser < 0) return;

        int target = Mathf.CeilToInt(lbWeekBest * (1f + Random.Range(0.004f, 0.02f))) + 1;
        // it squeezes in just ahead of the player, not past whoever is next
        if (above != int.MaxValue) target = Mathf.Min(target, Mathf.Max(lbWeekBest, above - 1));
        if (target > lbReach[chaser]) return;   // the player has got away from it

        lbOvertakerFrom = lbScores[chaser];
        lbScores[chaser] = target;
        lbOvertaker = chaser;
        LbSaveBots();
    }

    // ------------------------------------------------------------ results line

    Text lbGameOverLine;

    /// <summary>The weekly line on the results screen, between the stats and RETRY.</summary>
    void BuildLbGameOverLine()
    {
        lbGameOverLine = MakeText(goCard, "GOWeekly", 34, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -192f), new Vector2(940f, 56f), true);
        lbGameOverLine.gameObject.SetActive(false);
    }

    void LbFillGameOverLine()
    {
        if (lbGameOverLine == null) return;
        bool show = lbRunCounted && lbWeekBest > 0;
        lbRunCounted = false;
        lbGameOverLine.gameObject.SetActive(show);
        if (!show) return;
        int rank = LbRankOf(lbWeekBest);
        if (lbJustLocked)
        {
            lbGameOverLine.text = "1ST PLACE LOCKED IN!";
            lbGameOverLine.color = new Color(1f, 0.82f, 0.15f);
        }
        else if (lbOvertaker >= 0)
        {
            lbGameOverLine.text = Loc.F("{0} PASSED YOU  -  NOW #{1}", lbNames[lbOvertaker], rank);
            lbGameOverLine.color = new Color(1f, 0.55f, 0.45f);
        }
        else
        {
            lbGameOverLine.text = Loc.F("WEEKLY RANK  #{0}", lbLocked ? 1 : rank);
            lbGameOverLine.color = new Color(1f, 1f, 1f, 0.85f);
        }
        FitToWidth(lbGameOverLine, 34, 900f, true);
    }

    // ------------------------------------------------------------ in-run rival

    GameObject lbHudRoot;
    CanvasGroup lbHudGroup;
    Text lbHudRival, lbHudCount;
    int lbHudTarget = -2, lbHudLastScore = -1;
    float lbHudPunch, lbHudAlpha;

    /// <summary>
    /// The rival to beat, under BEST during an endless run: their place and
    /// name, and a countdown of the points still needed to pass them. It
    /// moves on to the next one up each time one is passed, and goes away
    /// once there is nobody left in front.
    /// </summary>
    void BuildRivalHud(Transform uiRoot)
    {
        var pill = MakeCard(uiRoot, Vector2.zero, new Vector2(560f, 118f),
            new Color(0.05f, 0.04f, 0.09f, 0.72f));
        var rt = pill.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -312f);
        lbHudRoot = pill.gameObject;
        lbHudGroup = lbHudRoot.AddComponent<CanvasGroup>();
        lbHudGroup.blocksRaycasts = false;
        lbHudGroup.interactable = false;

        lbHudRival = MakeText(pill.transform, "Rival", 32, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(540f, 46f), true);
        lbHudCount = MakeText(pill.transform, "Countdown", 52, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(540f, 64f), true);
        lbHudCount.color = new Color(1f, 0.82f, 0.15f);
        lbHudRoot.SetActive(false);
    }

    /// <summary>Shows or hides the rival line with the rest of the HUD.</summary>
    void ShowRivalHud(bool visible)
    {
        if (lbHudRoot == null) return;
        bool show = visible && mode == Mode.Endless && !coachDrive;
        if (show && !lbHudRoot.activeSelf)
        {
            LbEnsureCurrent();
            lbHudTarget = -2;
            lbHudLastScore = -1;
            lbHudAlpha = 0f;
            lbHudGroup.alpha = 0f;
        }
        lbHudRoot.SetActive(show);
    }

    void TickRivalHud()
    {
        if (lbHudRoot == null || !lbHudRoot.activeSelf) return;

        // fades in while there is someone to chase, out once there is not
        bool chasing = lbHudTarget >= 0;
        lbHudAlpha = Mathf.MoveTowards(lbHudAlpha, chasing ? 1f : 0f, Time.deltaTime * 4f);
        lbHudGroup.alpha = lbHudAlpha;

        // a pass gives it a little punch
        if (lbHudPunch > 0f)
        {
            lbHudPunch = Mathf.Max(0f, lbHudPunch - Time.deltaTime * 3f);
            lbHudRoot.transform.localScale = Vector3.one * (1f + 0.12f * lbHudPunch);
        }

        int run = Mathf.RoundToInt(score);
        if (run == lbHudLastScore) return;
        lbHudLastScore = run;

        // the rival directly above where the player stands: the lowest
        // score still in front of both this run and the week's best
        int standing = Mathf.Max(run, lbWeekBest);
        int next = -1;
        if (!lbLocked)
            for (int i = 0; i < LbBots; i++)
                if (lbScores[i] >= standing && (next < 0 || lbScores[i] < lbScores[next])) next = i;

        if (next != lbHudTarget)
        {
            // passed someone
            if (lbHudTarget >= 0) { lbHudPunch = 1f; audioMan.PlayPop(); }
            lbHudTarget = next;
            if (next >= 0)
            {
                int place = 1;
                for (int i = 0; i < LbBots; i++) if (lbScores[i] > lbScores[next]) place++;
                lbHudRival.text = "#" + place + "   " + lbNames[next];
            }
        }
        // what this run still needs to get past them
        if (next >= 0) lbHudCount.text = (lbScores[next] + 1 - run).ToString();
    }

    // ------------------------------------------------------------ menu button

    Text weeklyMenuLabel;
    Image weeklyRankChip;
    Text weeklyRankText;
    GameObject weeklyBadge;

    /// <summary>The menu's WEEKLY BEST line, under BEST. Tapping it opens the ranking.</summary>
    void BuildWeeklyMenuButton(Transform menu)
    {
        // plain bold lettering: the racing font gets hard to read this small
        weeklyMenuLabel = MakeButton(menu, "", 40, new Vector2(0f, 172f), new Vector2(700f, 78f),
            OpenLeaderboardFromMenu, new Color(0.16f, 0.11f, 0.26f, 0.92f), true);
        weeklyMenuLabel.color = new Color(1f, 0.86f, 0.3f);
        var lrt = weeklyMenuLabel.rectTransform;
        lrt.anchoredPosition = new Vector2(-72f, 0f);
        lrt.sizeDelta = new Vector2(530f, 78f);

        // the place, in the league's colour, at the right-hand end
        weeklyRankChip = MakeCard(weeklyMenuLabel.transform.parent, new Vector2(272f, 0f),
            new Vector2(118f, 56f), LeagueColors[0]);
        weeklyRankText = MakeText(weeklyRankChip.transform, "Rank", 36, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(118f, 56f), true);
        weeklyRankText.color = new Color(0.08f, 0.05f, 0.12f);
        var ol = weeklyRankText.GetComponent<Outline>();
        if (ol != null) ol.enabled = false;
        // a red dot while a prize is waiting to be claimed
        weeklyBadge = MakeBadge(weeklyMenuLabel.transform.parent);
        RefreshWeeklyMenu();
    }

    void RefreshWeeklyMenu()
    {
        if (weeklyMenuLabel == null) return;
        LbEnsureCurrent();
        weeklyMenuLabel.text = Loc.F("WEEKLY HIGH SCORE  {0}", lbWeekBest);
        FitToWidth(weeklyMenuLabel, 40, 510f, true);
        int rank = lbLocked ? 1 : LbRankOf(lbWeekBest);
        weeklyRankText.text = rank > LbBots ? "-" : "#" + rank;
        weeklyRankChip.color = LeagueColors[lbLeague];
        weeklyBadge.SetActive(LbPrizeWaiting());
    }

    void OpenLeaderboardFromMenu()
    {
        // in the tutorial it is only pointed at
        if (coachPhase != CoachPhase.None) { coachTapped = true; return; }
        if (lbPanel == null || lbPanel.activeSelf) return;
        OpenLeaderboard(false);
    }

    // ------------------------------------------------------------ the board screen

    class LbRow
    {
        public RectTransform rt;
        public Image back, medal;
        public Text rank, name, score;
    }

    GameObject lbPanel;
    Text lbTitle, lbLeagueText, lbHintText, lbResetText, lbButtonLabel;
    Image lbLeaguePill, lbButtonImage;
    Text[] lbPrizeCaption = new Text[3];
    RectTransform lbViewport, lbContent;
    ScrollRect lbScroll;
    readonly List<LbRow> lbRows = new List<LbRow>();
    float[] lbFromY, lbToY;
    float lbAnimT = -1f;
    int lbAnimFromScore, lbAnimToScore;          // the player's row counts between these
    int lbAnimMover = -1, lbAnimMoverFrom, lbAnimMoverTo;   // a rival's row, the same
    bool lbOvertakeToShow;
    bool lbWaitingForPrize, lbPromotionToShow;
    float lbResetRefreshAt;

    const int LbTitleSize = 62;
    const float LbRowStep = 92f;
    const float LbRowHeight = 82f;
    const float LbViewTop = 372f, LbViewBottom = -640f;
    const float LbAnimDelay = 0.45f, LbAnimTime = 1.2f;

    static readonly Color[] MedalColors =
    {
        new Color(1f, 0.80f, 0.20f), new Color(0.82f, 0.86f, 0.92f), new Color(0.86f, 0.55f, 0.30f)
    };
    static readonly Color LbRowColor = new Color(0.21f, 0.16f, 0.32f, 1f);
    static readonly Color LbRowColorAlt = new Color(0.17f, 0.13f, 0.27f, 1f);
    static readonly Color LbYouColor = new Color(1f, 0.78f, 0.15f, 1f);
    static readonly Color LbDarkText = new Color(0.10f, 0.06f, 0.02f);
    static readonly Color LbHintColor = new Color(1f, 1f, 1f, 0.85f);
    static readonly Color LbWarnColor = new Color(1f, 0.55f, 0.45f);

    void BuildLeaderboardPanel(Transform uiRoot)
    {
        lbPanel = MakePanel(uiRoot, "LeaderboardPanel");

        var dimGo = new GameObject("Dim");
        dimGo.transform.SetParent(lbPanel.transform, false);
        var dim = dimGo.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.8f);
        var dimRt = dim.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;   // blocks the screen behind

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(lbPanel.transform, false);
        var bodyRt = bodyGo.AddComponent<RectTransform>();
        bodyRt.anchorMin = bodyRt.anchorMax = bodyRt.pivot = new Vector2(0.5f, 0.5f);
        bodyRt.sizeDelta = Vector2.zero;
        Transform body = bodyGo.transform;
        var pop = bodyGo.AddComponent<PopIn>();
        pop.dimmer = dim;
        pop.dimmerAlpha = 0.8f;

        MakeCard(body, new Vector2(0f, 10f), new Vector2(1010f, 1660f),
            new Color(0.13f, 0.09f, 0.2f, 0.97f));

        lbTitle = MakeText(body, "LbTitle", LbTitleSize, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 760f), new Vector2(940f, 100f));
        lbTitle.color = new Color(1f, 0.72f, 0.12f);

        // the league, as a coloured pill
        lbLeaguePill = MakeCard(body, new Vector2(0f, 666f), new Vector2(560f, 88f), LeagueColors[0]);
        lbLeagueText = MakeText(lbLeaguePill.transform, "League", 52, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(540f, 88f));
        lbLeagueText.color = new Color(0.08f, 0.05f, 0.12f);
        var lol = lbLeagueText.GetComponent<Outline>();
        if (lol != null) lol.enabled = false;

        lbHintText = MakeText(body, "LbHint", 34, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 592f), new Vector2(940f, 50f), true);
        lbHintText.color = LbHintColor;
        lbResetText = MakeText(body, "LbReset", 30, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 548f), new Vector2(940f, 44f), true);
        lbResetText.color = new Color(1f, 1f, 1f, 0.55f);

        // the podium's prizes
        string[] icons = { TireIconPath, "UI/wheel_token", CoinIconPath };
        for (int i = 0; i < 3; i++)
        {
            var card = MakeCard(body, new Vector2(-315f + 315f * i, 452f), new Vector2(300f, 124f),
                new Color(0.21f, 0.16f, 0.32f, 1f));
            var medal = MakeCard(card.transform, new Vector2(-112f, 0f), new Vector2(58f, 58f), MedalColors[i]);
            var num = MakeText(medal.transform, "Num", 40, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(58f, 58f), true);
            num.text = (i + 1).ToString();
            num.color = LbDarkText;
            var nol = num.GetComponent<Outline>();
            if (nol != null) nol.enabled = false;

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(card.transform, false);
            var icon = iconGo.AddComponent<RawImage>();
            icon.texture = Resources.Load<Texture2D>(icons[i]);
            icon.raycastTarget = false;
            var irt = icon.rectTransform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = new Vector2(-50f, 0f);
            irt.sizeDelta = new Vector2(52f, 52f);

            lbPrizeCaption[i] = MakeText(card.transform, "Caption", 28, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(58f, 0f), new Vector2(160f, 110f), true);
            lbPrizeCaption[i].horizontalOverflow = HorizontalWrapMode.Wrap;
            lbPrizeCaption[i].lineSpacing = 0.95f;
        }

        // the list itself scrolls inside its own window
        var vpGo = new GameObject("LbViewport");
        vpGo.transform.SetParent(body, false);
        lbViewport = vpGo.AddComponent<RectTransform>();
        lbViewport.anchorMin = lbViewport.anchorMax = lbViewport.pivot = new Vector2(0.5f, 0.5f);
        lbViewport.anchoredPosition = new Vector2(0f, (LbViewTop + LbViewBottom) * 0.5f);
        lbViewport.sizeDelta = new Vector2(960f, LbViewTop - LbViewBottom);
        var vpHit = vpGo.AddComponent<Image>();
        vpHit.color = new Color(0f, 0f, 0f, 0f);   // drags register on the gaps
        vpGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("LbContent");
        contentGo.transform.SetParent(vpGo.transform, false);
        lbContent = contentGo.AddComponent<RectTransform>();
        lbContent.anchorMin = lbContent.anchorMax = lbContent.pivot = new Vector2(0.5f, 1f);
        lbContent.sizeDelta = new Vector2(960f, (LbBots + 1) * LbRowStep + 8f);
        lbContent.anchoredPosition = Vector2.zero;

        lbScroll = vpGo.AddComponent<ScrollRect>();
        lbScroll.content = lbContent;
        lbScroll.viewport = lbViewport;
        lbScroll.horizontal = false;
        lbScroll.vertical = true;
        lbScroll.movementType = ScrollRect.MovementType.Clamped;
        lbScroll.inertia = true;
        lbScroll.decelerationRate = 0.135f;
        lbScroll.scrollSensitivity = 40f;

        for (int i = 0; i <= LbBots; i++) lbRows.Add(MakeLbRow(i == LbBots));

        lbButtonLabel = MakeButton(body, "CONTINUE", 48, new Vector2(0f, -718f),
            new Vector2(620f, 112f), LeaderboardButton, GoGreen);
        lbButtonImage = lbButtonLabel.transform.parent.GetComponent<Image>();

        lbPanel.SetActive(false);

        // the weekly line on the results screen lives with the rest of this
        BuildLbGameOverLine();
    }

    LbRow MakeLbRow(bool you)
    {
        var row = new LbRow();
        row.back = MakeCard(lbContent, Vector2.zero, new Vector2(940f, LbRowHeight),
            you ? LbYouColor : LbRowColor);
        row.rt = row.back.rectTransform;
        row.rt.anchorMin = row.rt.anchorMax = new Vector2(0.5f, 1f);
        row.rt.pivot = new Vector2(0.5f, 0.5f);

        row.medal = MakeCard(row.back.transform, new Vector2(-412f, 0f), new Vector2(64f, 64f), MedalColors[0]);
        row.rank = MakeText(row.back.transform, "Rank", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(-412f, 0f), new Vector2(110f, LbRowHeight), true);
        row.name = MakeText(row.back.transform, "Name", 40, TextAnchor.MiddleLeft,
            new Vector2(0.5f, 0.5f), new Vector2(-108f, 0f), new Vector2(460f, LbRowHeight), true);
        row.score = MakeText(row.back.transform, "Score", 40, TextAnchor.MiddleRight,
            new Vector2(0.5f, 0.5f), new Vector2(305f, 0f), new Vector2(230f, LbRowHeight), true);
        if (you)
        {
            // dark lettering on gold reads best without the black outline
            foreach (var t in new[] { row.rank, row.name, row.score })
            {
                t.color = LbDarkText;
                var ol = t.GetComponent<Outline>();
                if (ol != null) ol.enabled = false;
            }
        }
        return row;
    }

    /// <summary>
    /// The order the board stands in for a given score of the player's.
    /// <paramref name="mover"/> (if not -1) is a rival taken at
    /// <paramref name="moverScore"/> instead of its score now.
    /// </summary>
    int[] LbSlots(int playerScore, int mover = -1, int moverScore = 0)
    {
        // entries: rivals 0..LbBots-1, the player at LbBots
        var order = new List<int>();
        for (int i = 0; i <= LbBots; i++) order.Add(i);
        int ScoreOf(int e)
        {
            if (e == LbBots) return playerScore;
            return e == mover ? moverScore : lbScores[e];
        }
        order.Sort((a, b) =>
        {
            int sa = ScoreOf(a), sb = ScoreOf(b);
            if (sa != sb) return sb.CompareTo(sa);
            // the player has to beat a score, not equal it
            if (a == LbBots) return 1;
            if (b == LbBots) return -1;
            return a.CompareTo(b);
        });
        var slot = new int[LbBots + 1];
        for (int s = 0; s < order.Count; s++) slot[order[s]] = s;
        return slot;
    }

    static float LbSlotY(int slot) { return -LbRowStep * 0.5f - 4f - slot * LbRowStep; }

    /// <summary>Lays the rows out (no animation).</summary>
    void LbLayout(int playerScore, int mover = -1, int moverScore = 0)
    {
        int[] slot = LbSlots(playerScore, mover, moverScore);
        for (int i = 0; i <= LbBots; i++)
        {
            int s = i == LbBots ? playerScore : i == mover ? moverScore : lbScores[i];
            lbRows[i].rt.anchoredPosition = new Vector2(0f, LbSlotY(slot[i]));
            lbRows[i].rt.localScale = Vector3.one;
            LbFillRow(i, slot[i], s);
        }
        if (mover >= 0) lbRows[mover].rt.SetAsLastSibling();
        lbRows[LbBots].rt.SetAsLastSibling();
    }

    void LbFillRow(int entry, int slot, int score)
    {
        var row = lbRows[entry];
        bool you = entry == LbBots;
        bool unranked = you && score <= 0;
        row.rank.text = unranked ? "-" : (slot + 1).ToString();
        if (!you) row.name.text = lbNames[entry];
        // shown exactly as typed - a name is never run through the translations
        else if (row.name is LocText lt) lt.SetShown(Loc.Display(Loc.F("{0} (YOU)", Username())));
        row.score.text = unranked ? "-" : score.ToString();
        bool podium = !unranked && slot < 3;
        row.medal.gameObject.SetActive(podium);
        if (podium) row.medal.color = MedalColors[slot];
        // the number on a medal is dark; elsewhere it matches the row
        row.rank.color = podium || you ? LbDarkText : Color.white;
        var ol = row.rank.GetComponent<Outline>();
        if (ol != null) ol.enabled = !(podium || you);
        if (!you) row.back.color = slot % 2 == 0 ? LbRowColor : LbRowColorAlt;
    }

    /// <summary>Scrolls the list so the player's row sits in the middle of the window.</summary>
    void LbCenterOnPlayer()
    {
        float viewH = LbViewTop - LbViewBottom;
        float rowY = -lbRows[LbBots].rt.anchoredPosition.y;   // from the top of the list
        float maxY = Mathf.Max(0f, lbContent.sizeDelta.y - viewH);
        float y = Mathf.Clamp(rowY - viewH * 0.5f, 0f, maxY);
        lbContent.anchoredPosition = new Vector2(0f, y);
    }

    void LbRefreshHeader()
    {
        lbLeaguePill.color = LeagueColors[lbLeague];
        lbLeagueText.text = LeagueNames[lbLeague];
        lbPrizeCaption[0].text = "TIRES OR SPINS";
        lbPrizeCaption[1].text = "MYSTERY OR TOKEN BOX";
        lbPrizeCaption[2].text = Loc.F("+{0} COINS", ThirdPlaceCoins[lbLeague]);
        LbRefreshHint();
        LbRefreshReset();
    }

    /// <summary>What the player needs to know about where they stand.</summary>
    void LbRefreshHint()
    {
        lbHintText.color = LbHintColor;
        int rank = LbRankOf(lbWeekBest);
        if (lbLocked) lbHintText.text = "NOBODY CAN CATCH YOU THIS WEEK";
        else if (rank == 1) lbHintText.text = Loc.F("SCORE {0} TO LOCK IN 1ST", LbLockInScore());
        else if (rank <= 3) lbHintText.text = "HOLD YOUR PLACE UNTIL THE RESET TO WIN";
        else lbHintText.text = lbLeague < TopLeague
            ? Loc.F("FINISH 1ST TO REACH {0}", Loc.T(LeagueNames[lbLeague + 1]))
            : Loc.T("THE TOP LEAGUE");
        FitToWidth(lbHintText, 34, 920f, true);
    }

    void SetLbTitle(string s)
    {
        lbTitle.text = s;
        FitToWidth(lbTitle, LbTitleSize, 940f);
    }

    /// <summary>
    /// Sets a label back to its size and, if the words run wider than
    /// <paramref name="room"/>, shrinks it until they fit. (LocText does this
    /// for the other languages; English needs it here too, as the league
    /// names make some of these lines long.)
    /// </summary>
    void FitToWidth(Text t, int size, float room, bool plainFont = false)
    {
        // the same size MakeText gave it (plain text is set a little larger)
        int full = Mathf.RoundToInt(size * FontScale * (plainFont ? 1.2f : 1f) * Loc.SizeScale(Loc.Current));
        t.fontSize = full;
        float w = t.preferredWidth;
        if (w > room) t.fontSize = Mathf.Max(8, Mathf.FloorToInt(full * room / w));
    }

    void LbRefreshReset()
    {
        var now = System.DateTime.Now;
        System.TimeSpan left = WeekStart(now).AddDays(7) - now;
        lbResetText.text = left.TotalHours >= 24
            ? Loc.F("RESETS IN {0}D {1}H", left.Days, left.Hours)
            : Loc.F("RESETS IN {0}H {1}M", left.Hours, Mathf.Max(1, left.Minutes));
        lbResetRefreshAt = Time.unscaledTime + 20f;
    }

    void LbRefreshButton()
    {
        bool prize = LbPrizeWaiting();
        lbButtonLabel.text = prize ? "CLAIM PRIZE" : "CONTINUE";
        lbButtonImage.color = prize ? new Color(1f, 0.72f, 0.12f) : GoGreen;
        lbButtonLabel.transform.parent.localScale = Vector3.one;
    }

    /// <summary>
    /// Opens the ranking. After a run that beat the week's best, the
    /// player's row climbs from where it stood to where it stands now - and
    /// then the rival behind it edges past, if one did.
    /// </summary>
    void OpenLeaderboard(bool afterRun)
    {
        LbEnsureCurrent();
        lbWaitingForPrize = false;
        lbPromotionToShow = false;
        lbOvertakeToShow = false;
        bool improved = afterRun && lbWeekBest > lbRunOldScore;
        SetLbTitle(!afterRun ? "WEEKLY RANKING"
                 : lbJustLocked ? "1ST PLACE LOCKED IN!"
                 : improved ? "NEW WEEKLY HIGH SCORE" : "OVERTAKEN!");
        LbRefreshHeader();

        lbPanel.SetActive(true);
        lbPanel.transform.SetAsLastSibling();
        audioMan.PlayPop();

        if (afterRun && improved)
        {
            // the climb is played against the board as it stood before the
            // rival answered - and then the rival answers
            int mover = lbOvertaker;
            int moverFrom = mover >= 0 ? lbOvertakerFrom : 0;
            LbStartAnim(lbRunOldScore, lbWeekBest, mover, moverFrom, moverFrom);
            lbOvertakeToShow = mover >= 0;
        }
        else if (afterRun && lbOvertaker >= 0)
        {
            // no new best this time: straight to the rival edging past
            LbShowOvertake();
        }
        else
        {
            lbAnimT = -1f;
            LbLayout(lbWeekBest);
            LbCenterOnPlayer();
        }
        LbRefreshButton();
    }

    /// <summary>
    /// Moves the rows from one standing to another: the player's score goes
    /// from <paramref name="fromScore"/> to <paramref name="toScore"/>, and a
    /// rival (<paramref name="mover"/>, or -1) from one score to another.
    /// </summary>
    void LbStartAnim(int fromScore, int toScore, int mover, int moverFrom, int moverTo)
    {
        int[] from = LbSlots(fromScore, mover, moverFrom);
        int[] to = LbSlots(toScore, mover, moverTo);
        lbFromY = new float[LbBots + 1];
        lbToY = new float[LbBots + 1];
        for (int i = 0; i <= LbBots; i++)
        {
            lbFromY[i] = LbSlotY(from[i]);
            lbToY[i] = LbSlotY(to[i]);
        }
        LbLayout(fromScore, mover, moverFrom);
        LbCenterOnPlayer();
        lbAnimFromScore = fromScore;
        lbAnimToScore = toScore;
        lbAnimMover = mover;
        lbAnimMoverFrom = moverFrom;
        lbAnimMoverTo = moverTo;
        lbAnimT = 0f;
        lbScroll.StopMovement();
        lbScroll.enabled = false;   // the list is driven until it lands
    }

    void LbFinishAnim()
    {
        lbAnimT = -1f;
        lbScroll.enabled = true;
        LbLayout(lbAnimToScore, lbAnimMover, lbAnimMoverTo);
        LbCenterOnPlayer();

        if (lbOvertakeToShow)
        {
            // and now the rival behind answers, edging past by a hair
            lbOvertakeToShow = false;
            LbShowOvertake();
            return;
        }
        if (LbRankOf(lbAnimToScore) <= 3 || lbJustLocked) audioMan.PlayPowerUp();
        LbRefreshButton();
    }

    /// <summary>The rival behind the player slides up past their row.</summary>
    void LbShowOvertake()
    {
        int m = lbOvertaker;
        lbHintText.text = Loc.F("{0} PASSED YOU", lbNames[m]);
        lbHintText.color = LbWarnColor;
        FitToWidth(lbHintText, 34, 920f, true);
        LbStartAnim(lbWeekBest, lbWeekBest, m, lbOvertakerFrom, lbScores[m]);
        audioMan.PlayNearMiss();
    }

    void LeaderboardButton()
    {
        // a tap while the rows move skips to the end of it all
        if (lbAnimT >= 0f)
        {
            if (lbOvertakeToShow)
            {
                LbFinishAnim();     // straight on into the overtake...
            }
            LbFinishAnim();         // ...and to where it ends
            return;
        }
        if (lbWaitingForPrize) return;

        if (LbPrizeWaiting()) { LbGrantPrize(); return; }

        lbPanel.SetActive(false);
        audioMan.PlayTap();
        RefreshWeeklyMenu();
    }

    /// <summary>Hands over the oldest waiting prize on its own reveal screen.</summary>
    void LbGrantPrize()
    {
        var queue = LbPrizeQueue();
        if (queue.Count == 0) return;
        int place = queue[0].x, league = queue[0].y;
        queue.RemoveAt(0);
        LbSetPrizeQueue(queue);

        switch (place)
        {
            case 3:
            {
                int coins = ThirdPlaceCoins[league];
                totalCoins += coins;
                PlayerPrefs.SetInt("Coins", totalCoins);
                ShowPrizeReveal(Loc.F("+{0} COINS", coins), false, CoinIconPath);
                break;
            }
            case 2:
                if (Random.value < 0.5f) OpenMysteryBox();
                else OpenTokenBox();
                break;
            default:
                // 1st: spins in the lower leagues, tires higher up
                if (FirstPlaceSpins[league] > 0)
                {
                    int s = FirstPlaceSpins[league];
                    SpinsLeft = SpinsLeft + s;
                    RefreshSpinBadge();
                    ShowPrizeReveal(s == 1 ? Loc.T("+1 SPIN") : Loc.F("+{0} SPINS", s),
                                    false, null, wheelFaceTex);
                }
                else
                {
                    int t = FirstPlaceTires[league];
                    tires += t;
                    PlayerPrefs.SetInt("Tires", tires);
                    ShowPrizeReveal(Loc.F("+{0} TIRES!", t), false, TireIconPath);
                }
                // 1st locked in this week: the move up comes with the prize
                if (lbLocked && league == lbLeague && lbLeague < TopLeague)
                {
                    lbLeague++;
                    PlayerPrefs.SetInt(LbLeagueKey, lbLeague);
                    LbNewBoard();
                    lbPromotionToShow = true;
                }
                break;
        }
        PlayerPrefs.Save();
        lbWaitingForPrize = true;
        LbRefreshButton();
    }

    /// <summary>A prize is on screen until its reveal (and any box) has closed.</summary>
    bool PrizeStillShowing()
    {
        return (revealPanel != null && revealPanel.activeSelf)
            || (boxFocusPanel != null && boxFocusPanel.activeSelf)
            || boxRevealAt > 0f;
    }

    /// <summary>
    /// Last week's finish, the first time the menu is free after the reset:
    /// the place it ended in, any promotion, and the prize to claim.
    /// </summary>
    void LbShowResult()
    {
        string[] r = PlayerPrefs.GetString(LbResultKey, "").Split('|');
        PlayerPrefs.DeleteKey(LbResultKey);
        PlayerPrefs.Save();
        lbResultWaiting = false;
        int rank = r.Length > 0 && int.TryParse(r[0], out int rk) ? rk : 0;
        bool promoted = r.Length > 1 && r[1] == "1";

        OpenLeaderboard(false);
        if (promoted)
        {
            SetLbTitle(Loc.F("PROMOTED TO {0}!", Loc.T(LeagueNames[lbLeague])));
            lbLeaguePill.rectTransform.localScale = Vector3.one * 1.35f;
            audioMan.PlayFinishWhoosh();
        }
        else if (rank > 0 && rank <= LbBots)
            SetLbTitle(Loc.F("LAST WEEK YOU FINISHED #{0}", rank));
    }

    void TickLeaderboard()
    {
        // a name comes first: the ranking is shown with it
        if (NameWantedNow()) OpenNamePanel(true);

        // last week's result comes up once the menu is free
        if (lbResultWaiting && !NeedsUsername() && (namePanel == null || !namePanel.activeSelf)
            && state == State.Menu && menuPanel.activeSelf && !introRunning
            && lbPanel != null && !lbPanel.activeSelf && !loginPending
            && (loginPanel == null || !loginPanel.activeSelf)
            && coachPhase == CoachPhase.None && !PrizeStillShowing())
            LbShowResult();

        if (lbPanel == null || !lbPanel.activeSelf) return;

        if (Time.unscaledTime >= lbResetRefreshAt) LbRefreshReset();

        if (lbWaitingForPrize && !PrizeStillShowing())
        {
            lbWaitingForPrize = false;
            if (lbPromotionToShow)
            {
                // the new league: a fresh board, climbed from the bottom
                lbPromotionToShow = false;
                LbRefreshHeader();
                SetLbTitle(Loc.F("PROMOTED TO {0}!", Loc.T(LeagueNames[lbLeague])));
                lbLeaguePill.rectTransform.localScale = Vector3.one * 1.35f;
                audioMan.PlayFinishWhoosh();
                LbStartAnim(0, lbWeekBest, -1, 0, 0);
            }
            else LbRefreshHint();
            LbRefreshButton();
            RefreshWeeklyMenu();
        }

        // the league pill settles after a promotion
        var pillRt = lbLeaguePill.rectTransform;
        if (pillRt.localScale.x > 1.001f)
            pillRt.localScale = Vector3.one * Mathf.Lerp(pillRt.localScale.x, 1f, Time.unscaledDeltaTime * 6f);

        // a prize to claim makes the button breathe
        if (lbAnimT < 0f && !lbWaitingForPrize && LbPrizeWaiting())
            lbButtonLabel.transform.parent.localScale =
                Vector3.one * (1f + 0.045f * Mathf.Sin(Time.unscaledTime * 5f));

        if (lbAnimT < 0f) return;
        lbAnimT += Time.unscaledDeltaTime;
        float p = Mathf.Clamp01((lbAnimT - LbAnimDelay) / LbAnimTime);
        float e = p < 0.5f ? 4f * p * p * p : 1f - Mathf.Pow(-2f * p + 2f, 3f) * 0.5f;
        for (int i = 0; i <= LbBots; i++)
        {
            var rt = lbRows[i].rt;
            rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(lbFromY[i], lbToY[i], e));
        }

        // the rows that move lift as they go, and their scores count
        float lift = 1f + 0.06f * Mathf.Sin(p * Mathf.PI);
        var you = lbRows[LbBots];
        if (lbAnimFromScore != lbAnimToScore)
        {
            you.rt.localScale = Vector3.one * lift;
            you.score.text = Mathf.RoundToInt(Mathf.Lerp(lbAnimFromScore, lbAnimToScore, e)).ToString();
        }
        you.rank.text = (LbSlotAt(you.rt) + 1).ToString();
        if (lbAnimMover >= 0 && lbAnimMoverFrom != lbAnimMoverTo)
        {
            var m = lbRows[lbAnimMover];
            m.rt.localScale = Vector3.one * lift;
            m.score.text = Mathf.RoundToInt(Mathf.Lerp(lbAnimMoverFrom, lbAnimMoverTo, e)).ToString();
            m.rank.text = (LbSlotAt(m.rt) + 1).ToString();
        }
        LbCenterOnPlayer();

        if (p >= 1f) LbFinishAnim();
    }

    static int LbSlotAt(RectTransform rt)
    {
        return Mathf.Clamp(Mathf.RoundToInt((-rt.anchoredPosition.y - LbRowStep * 0.5f - 4f) / LbRowStep), 0, LbBots);
    }

    // ------------------------------------------------------------ username

    const string UsernameKey = "Username";
    const int UsernameMin = 3, UsernameMax = 15;

    GameObject namePanel;
    InputField nameField;
    Text nameError;
    GameObject nameBackButton;
    Text usernameBtnLabel;     // Settings > General
    bool nameIsFirstPick;

    /// <summary>The player's name on the weekly ranking.</summary>
    string Username() { return PlayerPrefs.GetString(UsernameKey, ""); }

    bool NeedsUsername() { return Username().Length < UsernameMin; }

    static bool UsernameChar(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
    }

    static bool UsernameValid(string name)
    {
        if (name == null || name.Length < UsernameMin || name.Length > UsernameMax) return false;
        foreach (char c in name) if (!UsernameChar(c)) return false;
        return true;
    }

    /// <summary>
    /// PICK A USERNAME: a box to type in (letters, numbers and _), a RANDOM
    /// button that deals out a gamertag, and OK. Asked once, in the
    /// tutorial; changed later from Settings.
    /// </summary>
    void BuildNamePanel(Transform uiRoot)
    {
        namePanel = MakePanel(uiRoot, "NamePanel");

        var dimGo = new GameObject("Dim");
        dimGo.transform.SetParent(namePanel.transform, false);
        var dim = dimGo.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.8f);
        var dimRt = dim.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(namePanel.transform, false);
        var bodyRt = bodyGo.AddComponent<RectTransform>();
        bodyRt.anchorMin = bodyRt.anchorMax = bodyRt.pivot = new Vector2(0.5f, 0.5f);
        bodyRt.sizeDelta = Vector2.zero;
        Transform body = bodyGo.transform;
        var pop = bodyGo.AddComponent<PopIn>();
        pop.dimmer = dim;
        pop.dimmerAlpha = 0.8f;

        MakeCard(body, new Vector2(0f, 60f), new Vector2(900f, 860f),
            new Color(0.13f, 0.09f, 0.2f, 1f));
        var title = MakeText(body, "NameTitle", 58, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 400f), new Vector2(860f, 90f));
        title.text = "PICK A USERNAME";
        title.color = new Color(1f, 0.72f, 0.12f);
        var info = MakeText(body, "NameInfo", 36, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 290f), new Vector2(820f, 110f), true);
        info.text = "This is how you show up\nin the weekly ranking.";
        info.horizontalOverflow = HorizontalWrapMode.Wrap;
        info.lineSpacing = 1.1f;

        // the box to type in, on a gold frame
        MakeCard(body, new Vector2(0f, 150f), new Vector2(692f, 124f),
            new Color(1f, 0.72f, 0.12f, 0.9f));
        var fieldImg = MakeCard(body, new Vector2(0f, 150f), new Vector2(680f, 112f),
            new Color(0.05f, 0.03f, 0.09f, 1f));
        fieldImg.raycastTarget = true;

        // plain Unity text inside: what is typed is never translated
        Font plain = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(fieldImg.transform, false);
        var typed = textGo.AddComponent<Text>();
        typed.font = plain;
        typed.fontSize = Mathf.RoundToInt(50 * FontScale * 1.2f);
        typed.fontStyle = FontStyle.Bold;
        typed.alignment = TextAnchor.MiddleCenter;
        typed.color = Color.white;
        typed.supportRichText = false;
        var trt = typed.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(24f, 6f);
        trt.offsetMax = new Vector2(-24f, -6f);

        nameField = fieldImg.gameObject.AddComponent<InputField>();
        nameField.textComponent = typed;
        nameField.targetGraphic = fieldImg;
        nameField.characterLimit = UsernameMax;
        nameField.lineType = InputField.LineType.SingleLine;
        nameField.contentType = InputField.ContentType.Standard;
        nameField.caretColor = new Color(1f, 0.82f, 0.15f);
        nameField.selectionColor = new Color(1f, 0.72f, 0.12f, 0.4f);
        nameField.onValidateInput += (string text, int index, char c) => UsernameChar(c) ? c : '\0';
        nameField.onValueChanged.AddListener(_ => RefreshNameError());

        nameError = MakeText(body, "NameError", 30, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 62f), new Vector2(820f, 50f), true);
        nameError.text = "3 TO 15 LETTERS, NUMBERS OR _";
        nameError.color = new Color(1f, 1f, 1f, 0.55f);

        MakeButton(body, "RANDOM", 40, new Vector2(0f, -50f), new Vector2(400f, 100f),
            RandomUsername, new Color(0.40f, 0.33f, 0.62f));
        MakeButton(body, "OK", 54, new Vector2(0f, -190f), new Vector2(560f, 130f),
            ConfirmUsername, GoGreen);
        nameBackButton = MakeButton(body, "BACK", 40, new Vector2(0f, -320f),
            new Vector2(400f, 96f), CloseNamePanel, LockedGrey).transform.parent.gameObject;

        namePanel.SetActive(false);
    }

    /// <summary>
    /// Opens the name picker. The first time it cannot be backed out of -
    /// there has to be a name for the ranking.
    /// </summary>
    void OpenNamePanel(bool firstPick)
    {
        if (namePanel == null) return;
        nameIsFirstPick = firstPick;
        nameBackButton.SetActive(!firstPick);
        string current = Username();
        nameField.text = current.Length > 0 ? current : MakeGamertag(new System.Random());
        RefreshNameError();
        namePanel.SetActive(true);
        namePanel.transform.SetAsLastSibling();
        if (audioMan != null) audioMan.PlayPop();
    }

    void RandomUsername()
    {
        nameField.text = MakeGamertag(new System.Random());
        audioMan.PlayTap();
    }

    void RefreshNameError()
    {
        bool ok = UsernameValid(nameField.text);
        nameError.color = ok ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 0.55f, 0.45f);
    }

    void ConfirmUsername()
    {
        string name = nameField.text.Trim();
        if (!UsernameValid(name))
        {
            RefreshNameError();
            audioMan.PlayBoing();
            return;
        }
        PlayerPrefs.SetString(UsernameKey, name);
        PlayerPrefs.Save();
        audioMan.PlayCoin();
        CloseNamePanel();
    }

    void CloseNamePanel()
    {
        namePanel.SetActive(false);
        if (!nameIsFirstPick && audioMan != null) audioMan.PlayTap();
        RefreshUsernameButton();
        // the ranking behind it shows the new name straight away
        if (lbPanel != null && lbPanel.activeSelf && lbAnimT < 0f) LbLayout(lbWeekBest);
    }

    /// <summary>Settings > General: USERNAME, which opens the picker.</summary>
    void BuildUsernameButton(Transform generalTab)
    {
        // plain lettering: the racing font has no _ for names like silent_ninja42
        usernameBtnLabel = MakeButton(generalTab, "", 34, new Vector2(0f, -350f),
            new Vector2(640f, 100f), () => OpenNamePanel(false), null, true);
        RefreshUsernameButton();
    }

    void RefreshUsernameButton()
    {
        if (usernameBtnLabel == null) return;
        string name = Username();
        usernameBtnLabel.text = name.Length > 0 ? Loc.F("USERNAME  {0}", name) : Loc.T("PICK A USERNAME");
    }

    /// <summary>A player from before usernames is asked for one once the menu is free.</summary>
    bool NameWantedNow()
    {
        return NeedsUsername() && !coachWanted && coachPhase == CoachPhase.None
            && CoachMenuFree() && namePanel != null && !namePanel.activeSelf
            && (lbPanel == null || !lbPanel.activeSelf) && !PrizeStillShowing();
    }
}
