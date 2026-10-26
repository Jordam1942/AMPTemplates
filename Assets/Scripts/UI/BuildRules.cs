/// <summary>
/// Eén bron van waarheid voor "kan dit gebouw nu geplaatst worden?". De HUD gebruikt dit om knoppen grijs te zetten.
/// ConstructionSystem controleert kosten al bij het plaatsen. Deze regel moet daarmee overeenkomen.
/// </summary>
public static class BuildRules
{
    public static bool CanBuild(BuildingDataSO data, PlayerEconomy economy)
    {
        if (data == null || economy == null) return false;
        if (!economy.CanAfford(data.cost)) return false;

        // Een gebouw dat stroom verbruikt, mag alleen als de huidige overschot het kan dragen.
        if (data.powerConsumed <= 0) return true;

        int surplus = economy.PowerProduced - economy.PowerConsumed;
        return data.powerConsumed <= surplus;
    }
}
