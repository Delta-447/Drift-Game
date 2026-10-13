using System.Collections;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Shows Apple's "Allow Driftline Eternal to track your activity...?" popup,
/// once, on the second time the game is opened - not over the very first
/// launch, where a new player has not seen the game yet. iPhone only; it
/// does nothing anywhere else. Apple only ever shows it once per install;
/// after that the answer lives in the phone's Settings.
/// </summary>
public class TrackingPrompt : MonoBehaviour
{
    const string LaunchKey = "AppLaunches";
    const float DelaySeconds = 4f;      // let the menu come up first

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern int _Driftline_TrackingStatus();
    [DllImport("__Internal")] static extern void _Driftline_RequestTracking();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        int launches = PlayerPrefs.GetInt(LaunchKey, 0) + 1;
        PlayerPrefs.SetInt(LaunchKey, launches);
        PlayerPrefs.Save();
        if (launches < 2) return;      // first launch: leave the new player be

#if UNITY_IOS && !UNITY_EDITOR
        if (_Driftline_TrackingStatus() != 0) return;   // already answered
        var go = new GameObject("TrackingPrompt");
        DontDestroyOnLoad(go);
        go.AddComponent<TrackingPrompt>();
#endif
    }

    IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(DelaySeconds);
#if UNITY_IOS && !UNITY_EDITOR
        _Driftline_RequestTracking();
#endif
        Destroy(gameObject);
    }
}
