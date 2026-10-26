public enum VeterancyRank { Regular, Veteran, Elite, Heroic }

/// <summary>
/// Balans van de unit-veterancy op één plek. Drempels zijn in XP, waarbij 1 kill = de kosten van het slachtoffer.
/// Elite en Heroic krijgen zelfherstel. Op Regular is er geen bonus.
/// </summary>
public static class VeterancyRules
{
    public static int XpThreshold(VeterancyRank rank)
    {
        switch (rank)
        {
            case VeterancyRank.Veteran: return 1000;
            case VeterancyRank.Elite: return 3000;
            case VeterancyRank.Heroic: return 6000;
            default: return 0;
        }
    }

    public static VeterancyRank RankForXp(int xp)
    {
        if (xp >= XpThreshold(VeterancyRank.Heroic)) return VeterancyRank.Heroic;
        if (xp >= XpThreshold(VeterancyRank.Elite)) return VeterancyRank.Elite;
        if (xp >= XpThreshold(VeterancyRank.Veteran)) return VeterancyRank.Veteran;
        return VeterancyRank.Regular;
    }

    public static float MaxHpMultiplier(VeterancyRank rank)
    {
        switch (rank)
        {
            case VeterancyRank.Veteran: return 1.10f;
            case VeterancyRank.Elite: return 1.20f;
            case VeterancyRank.Heroic: return 1.35f;
            default: return 1f;
        }
    }

    public static float FireRateMultiplier(VeterancyRank rank)
    {
        switch (rank)
        {
            case VeterancyRank.Veteran: return 1.10f;
            case VeterancyRank.Elite: return 1.20f;
            case VeterancyRank.Heroic: return 1.30f;
            default: return 1f;
        }
    }

    /// <summary>HP per seconde. Alleen Elite en Heroic herstellen zichzelf.</summary>
    public static float HealPerSecond(VeterancyRank rank)
    {
        switch (rank)
        {
            case VeterancyRank.Elite: return 1f;
            case VeterancyRank.Heroic: return 2f;
            default: return 0f;
        }
    }
}
