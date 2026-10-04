using UnityEngine;

/// <summary>Idempotent visual replacement marker, including baked traffic prefabs.</summary>
[DefaultExecutionOrder(10000)]
public sealed class RefinedNpcApplied : MonoBehaviour
{
    public string identity;
    public SkinnedMeshRenderer primary;
    public SkinnedMeshRenderer[] extras = System.Array.Empty<SkinnedMeshRenderer>();
    public Renderer[] obsolete = System.Array.Empty<Renderer>();
    void LateUpdate() { SyncVisibility(); }
    public void SyncVisibility()
    {
        if (primary == null) return;
        foreach (var renderer in obsolete)
            if (renderer != null) { renderer.enabled = false; renderer.forceRenderingOff = true; }
        foreach (var skin in extras)
        {
            if (skin == null) continue;
            skin.enabled = primary.enabled; skin.forceRenderingOff = primary.forceRenderingOff;
            skin.shadowCastingMode = primary.shadowCastingMode;
        }
    }
}
