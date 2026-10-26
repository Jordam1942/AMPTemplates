using UnityEngine;

/// <summary>
/// Een upgrade die een gebouw kan onderzoeken. De effecten worden door andere systemen toegepast
/// via BuildingController.UpgradeCompleted. Die koppeling gebeurt op upgradeId.
/// </summary>
[CreateAssetMenu(fileName = "NewUpgradeData", menuName = "Generals/Upgrade Data")]
public sealed class UpgradeDataSO : ScriptableObject
{
    [Tooltip("Stabiele sleutel voor savegames en effecten. Wijzig deze niet na release.")]
    public string upgradeId = "upgrade_id";

    public string displayName = "Upgrade";
    public Sprite icon;
}
