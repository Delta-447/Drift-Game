using UnityEngine;

/// <summary>
/// On a 3D preview stage (the shop's boxes, the garage, the daily reward):
/// while the stage is hidden, its cameras' render textures give their memory
/// back. Unity creates a released render texture again by itself the next
/// time a camera draws into it, so nothing else has to know.
/// </summary>
public class ReleaseTargetsWhenHidden : MonoBehaviour
{
    void OnDisable()
    {
        foreach (var cam in GetComponentsInChildren<Camera>(true))
        {
            RenderTexture rt = cam.targetTexture;
            if (rt != null && rt.IsCreated()) rt.Release();
        }
    }
}
