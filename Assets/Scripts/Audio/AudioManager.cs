using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centrale voice-router. Eén voice-kanaal, maximaal één clip per actie, en een minimale tussentijd.
/// Clipping ontstaat door gestapelde clips. Een groep van veertig units praat dus met één stem.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public sealed class AudioManager : MonoBehaviour
{
    private enum VoiceType { Select, Move, Attack }

    public static AudioManager Instance { get; private set; }

    [SerializeField] private AudioSource voiceSource;

    [Tooltip("Minimale stilte tussen twee voice-acties in seconden. Acties binnen dit interval worden weggelaten, niet gestapeld.")]
    [SerializeField, Min(0f)] private float minVoiceInterval = 0.4f;

    private float nextAllowedTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (voiceSource == null) voiceSource = GetComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.spatialBlend = 0f; // 2D: voice-lines komen niet uit een wereldpositie.
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Één voice-line voor de hele selectie-actie.</summary>
    public void PlaySelectionVoice(IReadOnlyList<UnitController> group) => PlayGroupVoice(group, VoiceType.Select);

    public void PlayMoveVoice(IReadOnlyList<UnitController> group) => PlayGroupVoice(group, VoiceType.Move);

    public void PlayAttackVoice(IReadOnlyList<UnitController> group) => PlayGroupVoice(group, VoiceType.Attack);

    private void PlayGroupVoice(IReadOnlyList<UnitController> group, VoiceType type)
    {
        if (group == null || group.Count == 0) return;
        if (Time.unscaledTime < nextAllowedTime) return;

        AudioClip clip = PickClip(group, type);
        if (clip == null) return;

        // Play() vervangt de lopende clip op dit kanaal. Er ontstaat dus nooit een tweede stem.
        voiceSource.clip = clip;
        voiceSource.Play();
        nextAllowedTime = Time.unscaledTime + minVoiceInterval;
    }

    /// <summary>De spreker is de eerste levende unit in de groep die clips voor dit type heeft.</summary>
    private static AudioClip PickClip(IReadOnlyList<UnitController> group, VoiceType type)
    {
        for (int i = 0; i < group.Count; i++)
        {
            UnitController unit = group[i];
            if (!unit.IsAlive || unit.Data == null) continue;

            AudioClip[] clips = ClipsFor(unit.Data, type);
            if (clips != null && clips.Length > 0)
                return clips[Random.Range(0, clips.Length)];
        }

        return null;
    }

    private static AudioClip[] ClipsFor(UnitDataSO data, VoiceType type) => type switch
    {
        VoiceType.Select => data.selectVoices,
        VoiceType.Move => data.moveVoices,
        _ => data.attackVoices,
    };
}
