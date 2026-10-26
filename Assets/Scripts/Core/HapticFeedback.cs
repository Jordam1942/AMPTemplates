using UnityEngine;

/// <summary>
/// Korte haptische pulsen. Op Android via VibrationEffect (API 26+), dat op Pixel gewoon beschikbaar is.
/// Het Vibrator-object wordt één keer opgevraagd en gecachet, zodat er geen JNI-overhead per tik is.
/// </summary>
public static class HapticFeedback
{
#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaObject vibrator;
    private static AndroidJavaClass effectClass;
#endif

    /// <summary>Lichte tik, bijvoorbeeld bij selecteren.</summary>
    public static void Tick() => Pulse(12, 70);

    /// <summary>Zwaardere puls, bijvoorbeeld bij een aanvalscommando.</summary>
    public static void Impact() => Pulse(25, 200);

    private static void Pulse(long durationMs, int amplitude)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (vibrator == null)
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                effectClass = new AndroidJavaClass("android.os.VibrationEffect");
            }

            if (vibrator == null || !vibrator.Call<bool>("hasVibrator")) return;

            using var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", durationMs, amplitude);
            vibrator.Call("vibrate", effect);
        }
        catch (AndroidJavaException e)
        {
            Debug.LogWarning($"Haptics mislukt: {e.Message}");
        }
#else
        Handheld.Vibrate();
#endif
    }
}
