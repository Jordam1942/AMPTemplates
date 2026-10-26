using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Past het URP-grafische profiel en de Android-instellingen voor een Google Pixel toe.
/// Menu: Tools > Generals > Apply Pixel Graphics Profile.
/// Alleen publieke URP-API's worden gebruikt. Bij een wijziging van die API kan het menu een waarschuwing geven,
/// maar het stopt niet de hele Editor-assembly.
/// </summary>
public static class PixelGraphicsProfile
{
    public const string SettingsFolder = "Assets/Settings";
    public const string VolumeProfilePath = "Assets/Settings/PixelVolumeProfile.asset";

    [MenuItem("Tools/Generals/Apply Pixel Graphics Profile")]
    public static void Apply()
    {
        ApplyRenderPipeline();
        ApplyVolumeProfile();
        ApplyAndroidSettings();

        AssetDatabase.SaveAssets();
        Debug.Log("Pixel-grafisch profiel toegepast. Zie Assets/Settings/PixelGraphicsProfile.md voor de onderbouwing.");
    }

    private static void ApplyRenderPipeline()
    {
        UniversalRenderPipelineAsset urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null)
        {
            Debug.LogError("Geen URP-asset gevonden. Zet de URP-asset op Project Settings > Graphics en draai dit menu opnieuw.");
            return;
        }

        // Shadows: één cascade tot 45 m. Dat is de goedkoopste optie die er op een Tensor-GPU nog goed uitziet.
        urp.shadowDistance = 45f;
        urp.shadowCascadeCount = 1;
        // Shadow-resolutie (1024) staat in het asset zelf; zie PixelGraphicsProfile.md.

        // HDR voor felle lasers en explosies. De Bloom-effecten staan in het volume-profiel.
        urp.supportsHDR = true;

        // MSAA uit: anti-aliasing loopt via FXAA op de camera. MSAA is op mobiel duurder dan FXAA.
        urp.msaaSampleCount = 1;
        urp.renderScale = 1f;

        EditorUtility.SetDirty(urp);
    }

    private static void ApplyVolumeProfile()
    {
        EnsureFolder();

        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);
        }

        // Bloom op felle randen. Drempel net onder 1, zodat alleen HDR-highlights gloeien en het UI niet.
        if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);

        bloom.active = true;

        bloom.threshold.overrideState = true;
        bloom.threshold.value = 0.95f;

        bloom.intensity.overrideState = true;
        bloom.intensity.value = 0.6f;

        bloom.scatter.overrideState = true;
        bloom.scatter.value = 0.6f;

        EditorUtility.SetDirty(profile);
    }

    private static void ApplyAndroidSettings()
    {
        // Vulkan eerst. Het is de snelste route op Tensor en ondersteunt HDR.
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
        {
            GraphicsDeviceType.Vulkan,
            GraphicsDeviceType.OpenGLES3,
        });

        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // Android 8.0 en hoger: nodig voor VibrationEffect in HapticFeedback.
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

        PlayerSettings.colorSpace = ColorSpace.Linear;

        // RTS speelt het best in landschap. Draaien naar portret staat uit.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToPortrait = false;
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(SettingsFolder))
            AssetDatabase.CreateFolder("Assets", "Settings");
    }
}
