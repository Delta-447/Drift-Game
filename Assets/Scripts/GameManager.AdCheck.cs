using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A hidden screen for finding out why ads do not show: tap the version
/// number on the main menu five times, then enter the password. It lists
/// what LevelPlay reported - whether it started, and why the last ad failed
/// to load - and opens LevelPlay's own test suite. Off when
/// AdManager.Diagnostics is false.
/// </summary>
public partial class GameManager
{
    GameObject adCheckPanel;
    Text adCheckText;
    int adCheckTaps;
    float adCheckLastTap;
    Transform adCheckRoot;
    bool unlockForAdCheck;        // the password box is guarding the ad check
    bool adCheckUnlocked;         // right password given this session

    const string AdCheckPassword = "sozjyd-xacto5-mihqoW";

    void HookAdCheck(Text version)
    {
        if (!AdManager.Diagnostics) return;
        version.raycastTarget = true;
        var btn = version.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() =>
        {
            if (Time.unscaledTime - adCheckLastTap > 1.5f) adCheckTaps = 0;
            adCheckLastTap = Time.unscaledTime;
            if (++adCheckTaps >= 5)
            {
                adCheckTaps = 0;
                adCheckRoot = version.transform.parent.parent;
                if (adCheckUnlocked) OpenAdCheck(adCheckRoot);
                else AskAdCheckPassword();
            }
        });
    }

    /// <summary>The unlock-code box, asking for the ad check's password instead.</summary>
    void AskAdCheckPassword()
    {
        if (unlockPanel == null) return;
        unlockForAdCheck = true;
        // masked, and no auto-capitals or autocorrect from the keyboard
        unlockInput.inputType = InputField.InputType.Password;
        unlockInput.characterLimit = 40;
        OpenUnlockPrompt();
        unlockPanel.transform.SetAsLastSibling();
    }

    void SubmitAdCheckPassword(string entered)
    {
        // Case is not held against you: a phone keyboard likes to capitalise
        // the first letter on its own.
        if (!string.Equals(entered, AdCheckPassword, System.StringComparison.OrdinalIgnoreCase))
        {
            unlockMsg.color = new Color(1f, 0.4f, 0.35f);
            unlockMsg.text = "WRONG PASSWORD";
            unlockInput.text = "";
            audioMan.PlayTap();
            return;
        }
        adCheckUnlocked = true;
        CloseUnlockPrompt();
        OpenAdCheck(adCheckRoot);
    }

    void OpenAdCheck(Transform uiRoot)
    {
        if (adCheckPanel == null) BuildAdCheck(uiRoot);
        adCheckPanel.SetActive(true);
        adCheckPanel.transform.SetAsLastSibling();
        RefreshAdCheck();
    }

    void BuildAdCheck(Transform uiRoot)
    {
        adCheckPanel = MakePanel(uiRoot, "AdCheck");
        var dim = new GameObject("Dim");
        dim.transform.SetParent(adCheckPanel.transform, false);
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.85f);
        var drt = dimImg.rectTransform;
        drt.anchorMin = Vector2.zero;
        drt.anchorMax = Vector2.one;
        drt.offsetMin = drt.offsetMax = Vector2.zero;

        MakeCard(adCheckPanel.transform, new Vector2(0f, 40f), new Vector2(1010f, 1500f),
            new Color(0.13f, 0.09f, 0.2f, 0.97f));
        var title = MakeText(adCheckPanel.transform, "AdCheckTitle", 64, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 700f), new Vector2(900f, 90f));
        title.text = "AD CHECK";
        title.color = new Color(1f, 0.72f, 0.12f);

        adCheckText = MakeText(adCheckPanel.transform, "AdCheckText", 30, TextAnchor.UpperLeft,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(920f, 820f), true);
        adCheckText.horizontalOverflow = HorizontalWrapMode.Wrap;
        adCheckText.verticalOverflow = VerticalWrapMode.Truncate;

        MakeButton(adCheckPanel.transform, "TEST SUITE", 44, new Vector2(0f, -300f),
            new Vector2(700f, 120f), () => AdManager.OpenTestSuite(), GoGreen);
        MakeButton(adCheckPanel.transform, "RELOAD AD", 40, new Vector2(-180f, -450f),
            new Vector2(340f, 110f), () => { AdManager.ReloadNow(); RefreshAdCheck(); });
        MakeButton(adCheckPanel.transform, "REFRESH", 40, new Vector2(180f, -450f),
            new Vector2(340f, 110f), RefreshAdCheck);
        MakeButton(adCheckPanel.transform, "CLOSE", 40, new Vector2(0f, -600f),
            new Vector2(500f, 110f), () => adCheckPanel.SetActive(false));
        adCheckPanel.SetActive(false);
    }

    void RefreshAdCheck()
    {
        if (adCheckText == null) return;
        // ads, then purchases (the tire packs)
        string report = AdManager.StatusReport() + "\n\n" + IapStore.StatusReport();
        // shown exactly as written: none of it is translated
        if (adCheckText is LocText lt) lt.SetShown(report);
        else adCheckText.text = report;
    }
}
