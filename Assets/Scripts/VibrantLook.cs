using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The game's colour grade, layered over the scene's own Global Volume.
///
/// That volume's bloom has a threshold of 0, so the WHOLE picture glowed,
/// not just the bright bits - a grey haze that flattened every colour - and
/// neutral tonemapping plus a vignette dulled it further. This lifts it:
/// bloom only on real highlights, punchier colour and contrast, a lighter
/// vignette. Runs by itself at startup; tune the numbers below.
///
/// The time-rewind effect (priority 100) still turns the screen black and
/// white over the top of this.
/// </summary>
public static class VibrantLook
{
    // how much stronger than the original look - tweak to taste
    const float Saturation = 28f;      // -100..100; 0 = unchanged
    const float Contrast = 14f;        // -100..100
    const float Exposure = 0.12f;      // stops; a little brighter overall
    const float BloomThreshold = 1.1f; // only genuinely bright things glow
    const float BloomIntensity = 0.45f;
    const float VignetteIntensity = 0.08f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Apply()
    {
        var go = new GameObject("VibrantLook");
        Object.DontDestroyOnLoad(go);
        var vol = go.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;     // over the scene's volume, under the rewind effect

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();

        var color = profile.Add<ColorAdjustments>(true);
        color.saturation.Override(Saturation);
        color.contrast.Override(Contrast);
        color.postExposure.Override(Exposure);

        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(BloomThreshold);
        bloom.intensity.Override(BloomIntensity);

        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(VignetteIntensity);

        vol.profile = profile;
    }
}
