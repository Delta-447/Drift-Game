using UnityEngine;

/// <summary>
/// Permanent upgrades that make each power-up last longer. Seven levels per
/// item, bought with coins, stored in PlayerPrefs so they carry between runs.
/// </summary>
public static class ItemUpgrades
{
    public const int Count = 5;
    public const int MaxLevel = 6;          // levels 0-6, shown as 1/7 - 7/7

    public static readonly string[] Names =
        { "SHIELD", "DOUBLE COINS", "COIN MAGNET", "DOUBLE SCORE", "SPRINGS" };

    /// <summary>Duration at level 1, in seconds.</summary>
    static readonly float[] BaseSeconds = { 8f, 20f, 15f, 15f, 15f };

    /// <summary>Cost of the first upgrade; each level after costs more.</summary>
    static readonly int[] BaseCost = { 700, 450, 500, 800, 450 };

    /// <summary>Each level adds this share of the base duration.</summary>
    const float GainPerLevel = 0.2f;        // level 7 lasts 2.2x as long

    public static int Level(int item)
    {
        return Mathf.Clamp(PlayerPrefs.GetInt("ItemLvl" + item, 0), 0, MaxLevel);
    }

    public static bool IsMaxed(int item) { return Level(item) >= MaxLevel; }

    public static float Seconds(int item)
    {
        return SecondsAtLevel(item, Level(item));
    }

    public static float SecondsAtLevel(int item, int level)
    {
        return BaseSeconds[item] * (1f + GainPerLevel * Mathf.Clamp(level, 0, MaxLevel));
    }

    /// <summary>Cost of the NEXT level. Rounded to something readable.</summary>
    public static int Cost(int item)
    {
        float raw = BaseCost[item] * Mathf.Pow(1.75f, Level(item));
        return Mathf.RoundToInt(raw / 25f) * 25;
    }

    public static void Buy(int item)
    {
        if (IsMaxed(item)) return;
        PlayerPrefs.SetInt("ItemLvl" + item, Level(item) + 1);
        PlayerPrefs.Save();
    }

    /// <summary>Which upgrade row a collected power-up belongs to.</summary>
    public static int IndexOf(TrackGenerator.PowerUpType type)
    {
        switch (type)
        {
            case TrackGenerator.PowerUpType.Invincible: return 0;
            case TrackGenerator.PowerUpType.DoubleCoins: return 1;
            case TrackGenerator.PowerUpType.Magnet: return 2;
            case TrackGenerator.PowerUpType.DoubleScore: return 3;
            default: return 4;   // Springs
        }
    }
}
