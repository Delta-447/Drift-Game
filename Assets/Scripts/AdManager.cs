using System;
using UnityEngine;

/// <summary>
/// Rewarded ads, wrapped so the rest of the game never touches the SDK.
///
/// The whole LevelPlay half is behind LEVELPLAY_DEPENDENCIES_INSTALLED, which
/// the Ads Mediation package defines for itself once its dependencies are in
/// place. Nothing to set by hand: install the package and this wakes up,
/// remove it and the game falls back to granting rewards straight away, so the
/// project still compiles either way.
/// </summary>
public class AdManager : MonoBehaviour
{
    // From the LevelPlay dashboard. Both of these are per-platform: the same
    // game has a different app key and a different ad unit id on each store.
    const string AppKeyIOS = "283051645";
    const string AppKeyAndroid = "2830635ed";

    const string RewardedUnitIOS = "ng5z57picl1gk02o";
    const string RewardedUnitAndroid = "eyetjzbm5dqx7hbk";

    static string AppKey
    {
        get
        {
            return Application.platform == RuntimePlatform.Android
                ? AppKeyAndroid : AppKeyIOS;
        }
    }

    static string RewardedUnitId
    {
        get
        {
            return Application.platform == RuntimePlatform.Android
                ? RewardedUnitAndroid : RewardedUnitIOS;
        }
    }

    static AdManager instance;

    /// <summary>
    /// The hidden ad check (tap the version number on the main menu five
    /// times): what LevelPlay is doing, and its test suite. SET THIS TO FALSE
    /// BEFORE RELEASE.
    /// </summary>
    public static readonly bool Diagnostics = true;   // readonly, not const: no "unreachable code" warning

    // what happened, for the ad check screen
    static string initState = "NOT STARTED";
    static string loadState = "-";
    static string showState = "-";
    static int loadFails;

    static string Stamp() { return System.DateTime.Now.ToString("HH.mm.ss"); }

    /// <summary>Everything worth knowing about why an ad does or does not show.</summary>
    public static string StatusReport()
    {
        var sb = new System.Text.StringBuilder();
#if LEVELPLAY_DEPENDENCIES_INSTALLED
        sb.Append("SDK  LevelPlay in the build\n");
#else
        sb.Append("SDK  NOT IN THIS BUILD (ads are skipped)\n");
#endif
        sb.Append("PLATFORM  ").Append(Application.platform).Append('\n');
        sb.Append("APP KEY  ").Append(AppKey).Append('\n');
        sb.Append("AD UNIT  ").Append(RewardedUnitId).Append('\n');
        sb.Append("INTERNET  ").Append(Application.internetReachability).Append("\n\n");
        sb.Append("INIT  ").Append(initState).Append("\n\n");
        sb.Append("LOAD  ").Append(loadState).Append('\n');
        sb.Append("LOAD FAILS SO FAR  ").Append(loadFails).Append("\n\n");
        sb.Append("LAST SHOW  ").Append(showState).Append('\n');
        sb.Append("READY NOW  ").Append(RewardedReady ? "YES" : "NO");
        return sb.ToString();
    }

    /// <summary>LevelPlay's own test screen: each network, live or test ads.</summary>
    public static void OpenTestSuite()
    {
#if LEVELPLAY_DEPENDENCIES_INSTALLED
        Unity.Services.LevelPlay.LevelPlay.LaunchTestSuite();
#endif
    }

    /// <summary>Asks for an ad again right now (the check screen's RELOAD).</summary>
    public static void ReloadNow()
    {
#if LEVELPLAY_DEPENDENCIES_INSTALLED
        if (instance != null) instance.Reload();
#endif
    }

    /// <summary>True when an ad is loaded and ready to show right now.</summary>
    public static bool RewardedReady
    {
        get
        {
#if LEVELPLAY_DEPENDENCIES_INSTALLED
            return instance != null && instance.rewardedLoaded;
#else
            return true;    // no SDK: the reward is always available
#endif
        }
    }

    public static void Init()
    {
        if (instance != null) return;
        var go = new GameObject("AdManager");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AdManager>();
        instance.Begin();
    }

    /// <summary>
    /// Shows a rewarded ad. onReward runs ONLY if the ad reports a completed
    /// view - never on show, or closing an ad early would still pay out.
    /// onUnavailable runs if there was nothing to show, so the caller can say
    /// so rather than leaving a dead button.
    /// </summary>
    static float savedTimeScale = 1f;

    /// <summary>
    /// An ad is shown from screens where the game is frozen - the revive offer
    /// runs at timeScale 0. A stopped clock stops the ad's own countdown with
    /// it, which is why the "closes in 5 seconds" label never moved, and the
    /// game's audio keeps playing over the ad because sound does not care
    /// about timeScale at all. So: real time and silence for the duration.
    /// </summary>
    static void BeginAdBreak()
    {
        savedTimeScale = Time.timeScale;
        Time.timeScale = 1f;
        AudioListener.pause = true;
    }

    static void EndAdBreak()
    {
        Time.timeScale = savedTimeScale;
        AudioListener.pause = false;
    }

    public static void ShowRewarded(string placement, Action onReward, Action onUnavailable = null)
    {
        if (instance == null) Init();

#if LEVELPLAY_DEPENDENCIES_INSTALLED
        instance.Present(placement, onReward, onUnavailable);
#else
        // Editor and any build without the package: behave as the game always
        // has, so the flow can be tested without an SDK in the way.
        if (onReward != null) onReward();
#endif
    }

#if LEVELPLAY_DEPENDENCIES_INSTALLED
    Unity.Services.LevelPlay.LevelPlayRewardedAd rewarded;
    bool rewardedLoaded;
    bool rewardPending;              // this view has earned the reward
    Action pendingReward;
    Action pendingUnavailable;
    bool showing;                    // an ad is up, or its result is still on its way
    bool adClosed;                   // the ad has closed

    void Begin()
    {
        // the test suite has to be switched on before init
        if (Diagnostics) Unity.Services.LevelPlay.LevelPlay.SetMetaData("is_test_suite", "enable");
        initState = "STARTING " + Stamp();
        Unity.Services.LevelPlay.LevelPlay.OnInitSuccess += OnInitSuccess;
        Unity.Services.LevelPlay.LevelPlay.OnInitFailed += OnInitFailed;
        Unity.Services.LevelPlay.LevelPlay.Init(AppKey);
    }

    void OnInitSuccess(Unity.Services.LevelPlay.LevelPlayConfiguration config)
    {
        initState = "OK " + Stamp();
        // the ad object must be created AFTER init reports success
        rewarded = new Unity.Services.LevelPlay.LevelPlayRewardedAd(RewardedUnitId);

        rewarded.OnAdLoaded += info =>
        {
            rewardedLoaded = true;
            loadState = "LOADED " + Stamp() + "  FROM " + info.AdNetwork;
        };
        rewarded.OnAdLoadFailed += err =>
        {
            rewardedLoaded = false;
            loadFails++;
            loadState = "FAILED " + Stamp() + "  CODE " + err.ErrorCode + "\n" + err.ErrorMessage;
            Invoke(nameof(Reload), 20f);
        };
        rewarded.OnAdDisplayed += _ => rewardedLoaded = false;

        // the only place a reward is ever recorded. It can arrive before OR
        // after the ad closes, so if the ad is already gone, pay out now.
        rewarded.OnAdRewarded += (_, __) =>
        {
            rewardPending = true;
            if (adClosed) Finish();
        };

        rewarded.OnAdClosed += _ => AdClosed();
        rewarded.OnAdDisplayFailed += (_, err) =>
        {
            showState = "DISPLAY FAILED " + Stamp() + "  CODE " + err.ErrorCode + "\n" + err.ErrorMessage;
            AdClosed();
        };
        rewarded.OnAdDisplayed += _ => showState = "SHOWN " + Stamp();

        Reload();
    }

    void OnInitFailed(Unity.Services.LevelPlay.LevelPlayInitError error)
    {
        Debug.LogWarning("[Ads] init failed: " + error);
        initState = "FAILED " + Stamp() + "  CODE " + error.ErrorCode + "\n" + error.ErrorMessage;
    }

    void Reload()
    {
        if (rewarded == null) return;
        loadState = "LOADING " + Stamp();
        rewarded.LoadAd();
    }

    void Present(string placement, Action onReward, Action onUnavailable)
    {
        if (rewarded == null || !rewardedLoaded)
        {
            showState = "NOTHING LOADED " + Stamp();
            if (onUnavailable != null) onUnavailable();
            Reload();
            return;
        }

        pendingReward = onReward;
        pendingUnavailable = onUnavailable;
        rewardPending = false;
        showing = true;
        adClosed = false;
        StopAllCoroutines();
        BeginAdBreak();
        rewarded.ShowAd(placement);
    }

    /// <summary>
    /// The ad closed, one way or another. If the reward has not arrived yet,
    /// give it a moment - LevelPlay can report the reward after the close.
    /// </summary>
    void AdClosed()
    {
        if (!showing || adClosed) return;
        adClosed = true;
        EndAdBreak();

        if (rewardPending) Finish();
        else StartCoroutine(WaitForLateReward());
    }

    System.Collections.IEnumerator WaitForLateReward()
    {
        // real time: the game behind the ad may be paused
        yield return new WaitForSecondsRealtime(2f);
        Finish();
    }

    /// <summary>Pay out if the ad earned it - once, whichever way it ended.</summary>
    void Finish()
    {
        if (!showing) return;            // already settled
        showing = false;

        Action reward = pendingReward;
        Action missed = pendingUnavailable;
        bool earned = rewardPending;

        pendingReward = null;
        pendingUnavailable = null;
        rewardPending = false;

        if (earned && reward != null) reward();
        else if (!earned && missed != null) missed();

        Reload();       // have the next one ready
    }
#else
    void Begin() { }
#endif
}
