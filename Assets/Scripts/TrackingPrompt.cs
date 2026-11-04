using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Apple's "Allow Driftline Eternal to track your activity...?" popup (App
/// Tracking Transparency).
///
/// Apple requires the popup to appear before anything that could track the
/// player starts - here, the ad SDK - so it is asked as soon as the game first
/// opens, on the very first launch, and the ads wait for the answer. Anything
/// that must not start before the answer goes through <see cref="WhenAnswered"/>.
///
/// iOS shows the popup once per install; after that the answer lives in the
/// device's Settings and WhenAnswered runs at once. Anywhere but an iPhone or
/// iPad build there is nothing to ask, and nothing waits.
/// </summary>
public class TrackingPrompt : MonoBehaviour
{
    // a moment for the first frame to be up: iOS only shows the popup to an
    // app that is active on screen, and quietly drops a request made earlier
    const float DelaySeconds = 1f;
    // if iOS dropped the request (no popup came up), ask again - a few times
    const float RetryAfterSeconds = 3f;
    const int MaxRequests = 4;

#if UNITY_IOS && !UNITY_EDITOR
    // 0 not asked yet, 1 restricted, 2 denied, 3 allowed
    [DllImport("__Internal")] static extern int _Driftline_TrackingStatus();
    [DllImport("__Internal")] static extern void _Driftline_RequestTracking();
    // 1 while a request is waiting for the player (or for iOS) to answer
    [DllImport("__Internal")] static extern int _Driftline_TrackingRequestPending();
#endif

    static bool answered;
    static readonly List<Action> waiting = new List<Action>();
    static TrackingPrompt runner;

    /// <summary>
    /// Runs <paramref name="then"/> once the player has answered the popup:
    /// straight away if they answered on an earlier launch, or if there is
    /// nothing to ask on this device.
    /// </summary>
    public static void WhenAnswered(Action then)
    {
        if (then == null) return;
        if (answered)
        {
            then();
            return;
        }
        waiting.Add(then);
        StartAsking();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        StartAsking();
    }

    static void StartAsking()
    {
        if (answered || runner != null) return;
#if UNITY_IOS && !UNITY_EDITOR
        if (_Driftline_TrackingStatus() != 0)
        {
            Answered();      // answered on an earlier launch
            return;
        }
        var go = new GameObject("TrackingPrompt");
        DontDestroyOnLoad(go);
        runner = go.AddComponent<TrackingPrompt>();
#else
        Answered();
#endif
    }

    static void Answered()
    {
        answered = true;
        var run = waiting.ToArray();
        waiting.Clear();
        foreach (var then in run) then();
    }

    IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(DelaySeconds);
#if UNITY_IOS && !UNITY_EDITOR
        int requests = 0;
        float askedAt = -999f;
        while (_Driftline_TrackingStatus() == 0)
        {
            bool canAsk = _Driftline_TrackingRequestPending() == 0
                          && Application.isFocused
                          && requests < MaxRequests
                          && Time.realtimeSinceStartup - askedAt > RetryAfterSeconds;
            if (canAsk)
            {
                _Driftline_RequestTracking();
                requests++;
                askedAt = Time.realtimeSinceStartup;
            }
            yield return new WaitForSecondsRealtime(0.25f);
        }
#endif
        runner = null;
        Answered();
        Destroy(gameObject);
    }
}
