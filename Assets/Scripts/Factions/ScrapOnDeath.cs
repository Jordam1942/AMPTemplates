using UnityEngine;

/// <summary>
/// Laat bij het sneuvelen met de ingestelde kans een scrap-crate achter op de grond.
/// Hoort op GLA-voertuigen. Gebruikt het Died-event van UnitController, zodat geen enkele
/// doodsoorzaak wordt gemist: wapens, powers en mijnen.
/// </summary>
[RequireComponent(typeof(UnitController))]
[DisallowMultipleComponent]
public sealed class ScrapOnDeath : MonoBehaviour
{
    [SerializeField] private ScrapSettingsSO settings;

    [Tooltip("Optioneel prefab met ScrapCrate. Zonder prefab wordt een eenvoudige kubus gebruikt.")]
    [SerializeField] private GameObject cratePrefab;

    [SerializeField] private LayerMask groundMask;

    private UnitController unit;

    private void Awake()
    {
        unit = GetComponent<UnitController>();
    }

    private void OnEnable()
    {
        unit.Died += OnDied;
    }

    private void OnDisable()
    {
        unit.Died -= OnDied;
    }

    private void OnDied(UnitController dead)
    {
        if (settings == null || Random.value > settings.dropChance) return;

        // De crate komt op de grond, niet op het punt van het model.
        Vector3 position = dead.Position;
        if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, groundMask))
            position = hit.point;

        GameObject crate = cratePrefab != null
            ? Instantiate(cratePrefab, position, Quaternion.identity)
            : CreateDefaultCrate(position);

        if (crate.TryGetComponent(out ScrapCrate scrap)) scrap.Initialize(settings);
    }

    private static GameObject CreateDefaultCrate(Vector3 position)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "ScrapCrate";
        go.transform.position = position + Vector3.up * 0.5f;
        go.transform.localScale = Vector3.one * 1.5f;
        go.AddComponent<ScrapCrate>();
        return go;
    }
}
