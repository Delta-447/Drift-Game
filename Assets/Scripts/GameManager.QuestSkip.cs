using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// With two quests done and one to go, the quests window offers to finish
/// the last one: watch an ad, or spend 5 tires. Either completes the set
/// exactly as playing it out would - +1 multiplier and +6 tires.
/// </summary>
public partial class GameManager
{
    const int QuestSkipTireCost = 5;

    Text questHint;                 // "COMPLETE ALL 3 ..." - stands aside for the offer
    GameObject questSkipRoot;
    Text questSkipCaption, questSkipTireLabel, questSkipAdLabel;
    bool questSkipAdPending;

    void BuildQuestSkip(Transform panel)
    {
        var root = new GameObject("QuestSkip");
        root.transform.SetParent(panel, false);
        var rt = root.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        questSkipRoot = root;

        questSkipCaption = MakeText(root.transform, "QSCaption", 36, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -378f), new Vector2(920f, 52f), true);
        questSkipCaption.color = new Color(1f, 0.85f, 0.35f);

        // watch an ad
        questSkipAdLabel = MakeButton(root.transform, "WATCH AD", 38,
            new Vector2(-215f, -452f), new Vector2(400f, 96f), QuestSkipWithAd,
            new Color(0.2f, 0.45f, 0.85f), true);

        // or 5 tires: the number, then the tire
        questSkipTireLabel = MakeButton(root.transform, QuestSkipTireCost.ToString(), 46,
            new Vector2(215f, -452f), new Vector2(400f, 96f), QuestSkipWithTires, null, true);
        IconAfterText(MakeCurrencyIcon(questSkipTireLabel.transform.parent, new Vector2(0.5f, 0.5f),
            Vector2.zero, 56f, true), questSkipTireLabel);

        root.SetActive(false);
    }

    /// <summary>How many of the three quests are done.</summary>
    int QuestsDoneCount()
    {
        int done = 0;
        for (int i = 0; i < 3; i++) if (questProgress[i] >= questTarget[i]) done++;
        return done;
    }

    /// <summary>Shows the offer only while exactly one quest is left.</summary>
    void RefreshQuestSkip()
    {
        if (questSkipRoot == null) return;
        if (questHint != null)
        {
            // back to the usual line (it may still be saying QUESTS DONE)
            questHint.text = "COMPLETE ALL 3\n+1 MULTIPLIER   +6 TIRES";
            questHint.color = new Color(1f, 1f, 1f, 0.9f);
        }
        bool offer = QuestsDoneCount() == 2;
        questSkipRoot.SetActive(offer);
        if (questHint != null) questHint.gameObject.SetActive(!offer);
        if (!offer) return;
        questSkipCaption.text = "FINISH THE LAST QUEST NOW";
        questSkipCaption.color = new Color(1f, 0.85f, 0.35f);
        questSkipAdLabel.text = "WATCH AD";
        questSkipTireLabel.color = tires >= QuestSkipTireCost ? Color.white : new Color(1f, 0.55f, 0.5f);
    }

    void QuestSkipWithTires()
    {
        if (QuestsDoneCount() != 2) return;
        if (tires < QuestSkipTireCost)
        {
            questSkipCaption.text = Loc.F("NEED   {0}", QuestSkipTireCost);
            questSkipCaption.color = new Color(1f, 0.55f, 0.5f);
            audioMan.PlayTap();
            return;
        }
        tires -= QuestSkipTireCost;
        FinishLastQuest();
    }

    void QuestSkipWithAd()
    {
        if (QuestsDoneCount() != 2 || questSkipAdPending) return;
        questSkipAdPending = true;
        questSkipAdLabel.text = "LOADING AD...";
        AdManager.ShowRewarded("quest_skip", () =>
        {
            questSkipAdPending = false;
            FinishLastQuest();
        },
        () =>
        {
            // nothing to show - say so, and leave the tires as the way to do it
            questSkipAdPending = false;
            questSkipAdLabel.text = "WATCH AD";
            questSkipCaption.text = "NO AD AVAILABLE";
            questSkipCaption.color = new Color(1f, 0.55f, 0.5f);
        });
    }

    /// <summary>Marks the open quest done, which completes the set and pays out.</summary>
    void FinishLastQuest()
    {
        for (int i = 0; i < 3; i++)
            if (questProgress[i] < questTarget[i]) questProgress[i] = questTarget[i];
        int newMult = Mathf.Min(30, Mathf.Min(questLevel + 1, 29) + 1);
        CompleteQuestSet();
        SaveQuests();
        // the window says it itself; the in-run banner is not wanted here
        if (bonusText != null) bonusText.gameObject.SetActive(false);

        // the window shows the new set straight away
        if (questsPanel != null && questsPanel.activeSelf)
        {
            questMultText.text = Loc.F("SCORE MULTIPLIER  X{0}", Mathf.Min(30, 1 + questLevel));
            for (int i = 0; i < 3; i++) questRowTexts[i].text = QuestLine(i);
        }
        RefreshQuestSkip();
        if (questHint != null)
        {
            questHint.gameObject.SetActive(true);
            questHint.text = Loc.F("QUESTS DONE! X{0}  +6 TIRES", newMult);
            questHint.color = new Color(0.6f, 0.9f, 1f);
        }
    }
}
