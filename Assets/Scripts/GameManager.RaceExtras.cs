using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Race mode extras: the rivals' gamertags floating over their cars, and the
/// way out of a finished race - the place slides away and the black lifts
/// off the race list, ready for the next one.
/// </summary>
public partial class GameManager
{
    // ------------------------------------------------------------ name tags

    GameObject raceTagRoot;
    readonly RectTransform[] raceTagRts = new RectTransform[RaceMode.Opponents];
    readonly Text[] raceTagTexts = new Text[RaceMode.Opponents];

    const float RaceTagHeight = 2.2f;     // metres above the car's origin
    const float RaceTagMaxDistance = 150f;

    /// <summary>One dark pill per rival, kept under the windows like the rest of the HUD.</summary>
    void BuildRaceTags(Transform uiRoot)
    {
        raceTagRoot = new GameObject("RaceTags");
        raceTagRoot.transform.SetParent(uiRoot, false);
        var rootRt = raceTagRoot.AddComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;

        for (int i = 0; i < RaceMode.Opponents; i++)
        {
            var pill = MakeCard(raceTagRoot.transform, Vector2.zero, new Vector2(280f, 54f),
                new Color(0.05f, 0.04f, 0.09f, 0.72f));
            raceTagRts[i] = pill.rectTransform;
            raceTagRts[i].pivot = new Vector2(0.5f, 0f);   // sits on top of the car
            raceTagTexts[i] = MakeText(pill.transform, "Name", 32, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280f, 54f), true);
            pill.gameObject.SetActive(false);
        }
        raceTagRoot.SetActive(false);
    }

    /// <summary>
    /// The field for this race gets gamertags - the same names every time a
    /// given race is run, and never the player's own.
    /// </summary>
    void NameRaceRivals(int level)
    {
        if (race == null) return;
        var rng = new System.Random(level * 7919 + 17);
        var used = new HashSet<string> { Username().ToLowerInvariant() };
        var racers = race.Racers;
        for (int i = 0; i < racers.Count; i++)
        {
            string tag;
            int guard = 0;
            do { tag = MakeGamertag(rng); } while (!used.Add(tag.ToLowerInvariant()) && ++guard < 40);
            racers[i].name = tag;
            if (i >= RaceMode.Opponents || raceTagTexts[i] == null) continue;
            // shown as typed - a name is never translated
            if (raceTagTexts[i] is LocText lt) lt.SetShown(tag);
            else raceTagTexts[i].text = tag;
            float w = raceTagTexts[i].preferredWidth + 40f;
            raceTagRts[i].sizeDelta = new Vector2(Mathf.Max(140f, w), 54f);
            raceTagTexts[i].rectTransform.sizeDelta = raceTagRts[i].sizeDelta;
        }
    }

    /// <summary>Keeps each tag over its car, smaller the further away it is.</summary>
    void TickRaceTags()
    {
        if (raceTagRoot == null) return;
        bool show = mode == Mode.Race && race != null && !raceFinished
                    && state == State.Playing && mainCam != null;
        if (raceTagRoot.activeSelf != show) raceTagRoot.SetActive(show);
        if (!show) return;

        var racers = race.Racers;
        Vector3 camPos = mainCam.transform.position;
        for (int i = 0; i < RaceMode.Opponents; i++)
        {
            var rt = raceTagRts[i];
            if (rt == null) continue;
            bool vis = i < racers.Count && racers[i].go != null;
            Vector3 sp = Vector3.zero;
            float dist = 0f;
            if (vis)
            {
                Vector3 head = racers[i].go.transform.position + Vector3.up * RaceTagHeight;
                sp = mainCam.WorldToScreenPoint(head);
                dist = Vector3.Distance(camPos, head);
                vis = sp.z > 0f && dist < RaceTagMaxDistance
                      && sp.x > -200f && sp.x < Screen.width + 200f
                      && sp.y > -100f && sp.y < Screen.height + 100f;
            }
            if (rt.gameObject.activeSelf != vis) rt.gameObject.SetActive(vis);
            if (!vis) continue;
            rt.position = new Vector3(sp.x, sp.y, 0f);
            rt.localScale = Vector3.one * Mathf.Clamp(24f / Mathf.Max(1f, dist), 0.45f, 1.05f);
        }
    }

    // ------------------------------------------------------------ way out of a race

    // the place holds, slides away, and the black lifts off the race list
    const float FinishHold = 2.8f;
    const float FinishSlideOut = 0.35f;
    const float FinishReveal = 0.55f;
    float finishRevealT = -1f;
    Text finishCoinsText;

    /// <summary>The coins the race paid, under the place.</summary>
    void SetFinishCoins(int coins)
    {
        if (finishPlaceRt == null) return;
        if (finishCoinsText == null)
        {
            finishCoinsText = MakeText(finishPlaceRt, "Coins", 54, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(900f, 90f), true);
        }
        finishCoinsText.gameObject.SetActive(coins > 0);
        if (coins > 0) finishCoinsText.text = Loc.F("+{0} COINS", coins);
        finishCoinsText.color = new Color(1f, 0.86f, 0.3f, 0f);
    }

    /// <summary>
    /// The second half of the finish: a tap (or the hold running out) sends
    /// the place sliding off, then the race list comes up from the black.
    /// Called from TickFinishCinematic every frame of it.
    /// </summary>
    void TickFinishOut()
    {
        // a tap moves it on once the place has landed
        if (finishAnimT > 1.0f && finishAnimT < FinishHold && Input.GetMouseButtonDown(0))
            finishAnimT = FinishHold;

        float a = Mathf.Clamp01((finishAnimT - 0.75f) / 0.3f);   // the coins after the place
        float outP = Mathf.Clamp01((finishAnimT - FinishHold) / FinishSlideOut);
        if (outP > 0f)
        {
            float e = outP * outP;
            finishPlaceRt.anchoredPosition = new Vector2(Mathf.Lerp(0f, -1100f, e), 0f);
            float fade = 1f - outP;
            finishPlaceText.color = new Color(1f, 0.82f, 0.12f, fade);
            finishPlaceSuffix.color = new Color(1f, 0.82f, 0.12f, fade);
            a = fade;
        }
        if (finishCoinsText != null)
            finishCoinsText.color = new Color(1f, 0.86f, 0.3f, a);

        if (finishAnimT >= FinishHold + FinishSlideOut) FinishToRaceList();
    }

    /// <summary>Behind the black, back to the race list - then the black lifts off it.</summary>
    void FinishToRaceList()
    {
        finishAnimT = -1f;
        if (finishPlaceRt != null) finishPlaceRt.gameObject.SetActive(false);
        if (mode != Mode.Race || state != State.GameOver)
        {
            EndFinishCinematic();
            return;
        }

        GoToMenu();
        OpenRaces();

        // the screen was black the whole time - it lifts away now
        if (finishBlackout != null)
        {
            finishBlackout.gameObject.SetActive(true);
            finishBlackout.color = new Color(0f, 0f, 0f, 0.96f);
        }
        if (finishBarTop != null)
        {
            finishBarTop.gameObject.SetActive(true);
            finishBarBottom.gameObject.SetActive(true);
        }
        finishRevealT = 0f;
    }

    /// <summary>Runs in every state: the black and the bars clearing off the race list.</summary>
    void TickFinishReveal()
    {
        if (finishRevealT < 0f) return;
        finishRevealT += Time.unscaledDeltaTime;
        float r = Mathf.Clamp01(finishRevealT / FinishReveal);
        float e = 1f - Mathf.Pow(1f - r, 3f);
        if (finishBlackout != null) finishBlackout.color = new Color(0f, 0f, 0f, 0.96f * (1f - e));
        if (finishBarTop != null)
        {
            float h = Mathf.Lerp(1200f, 0f, e) + PanelBleed.y;
            finishBarTop.rectTransform.sizeDelta = new Vector2(400f, h);
            finishBarBottom.rectTransform.sizeDelta = new Vector2(400f, h);
        }
        if (r >= 1f)
        {
            finishRevealT = -1f;
            if (finishBlackout != null) finishBlackout.gameObject.SetActive(false);
            if (finishBarTop != null) finishBarTop.gameObject.SetActive(false);
            if (finishBarBottom != null) finishBarBottom.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// MENU on the results screen, and QUIT in the pause menu: a race goes
    /// back to the race list, an endless run to the main menu.
    /// </summary>
    void LeaveResults()
    {
        bool wasRace = mode == Mode.Race;
        GoToMenu();
        if (wasRace) OpenRaces();
    }
}
