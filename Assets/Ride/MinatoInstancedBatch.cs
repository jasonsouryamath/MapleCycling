using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Serialized GPU-instanced render batch used by Minato's repeated decorative ground cover.
///
/// The old scene expanded every tuft into a prefab hierarchy (and, for LOD-derived sources,
/// several renderer children). Tens of thousands of tiny decorations therefore became millions
/// of loaded Unity objects. This component stores only the shared mesh/material plus transforms,
/// splits them into DrawMeshInstanced-sized pages, and frustum/distance culls the whole route
/// cell before issuing a draw.
/// </summary>
[ExecuteAlways]
public sealed class MinatoInstancedBatch : MonoBehaviour
{
    public Mesh mesh;
    public int subMeshIndex;
    public Material material;
    public Matrix4x4[] matrices = Array.Empty<Matrix4x4>();
    public Bounds worldBounds;
    public float maxDistanceM = 650f;
    public bool castShadows;

    private const int PageSize = 1023;
    [NonSerialized] private Matrix4x4[][] _pages;
    [NonSerialized] private Plane[] _planes;

    private void OnEnable()
    {
        if (material != null) material.enableInstancing = true;
        RebuildPages();
    }

    private void OnValidate()
    {
        if (material != null) material.enableInstancing = true;
        _pages = null;
    }

    private void RebuildPages()
    {
        int count = matrices != null ? matrices.Length : 0;
        int pages = (count + PageSize - 1) / PageSize;
        _pages = new Matrix4x4[pages][];
        for (int p = 0; p < pages; p++)
        {
            int n = Mathf.Min(PageSize, count - p * PageSize);
            var page = new Matrix4x4[n];
            Array.Copy(matrices, p * PageSize, page, 0, n);
            _pages[p] = page;
        }
    }

    // FIX (2026-09-25): drawing from OnRenderObject never reached HDRP (SRP does not call it the
    // way Built-in did), so ~34k instanced grass/pampas/scrub/armour transforms placed by the
    // Shores pass were invisible - the "bare green hills" on the headland climb. Submit once per
    // frame for ALL cameras with RenderMeshInstanced; worldBounds gives the engine per-camera
    // frustum culling, and the distance cap is taken from the gameplay camera.
    // Update, not LateUpdate: coroutine-driven captures render between the two.
    private void Update()
    {
        if (mesh == null || material == null || matrices == null || matrices.Length == 0 ||
            !isActiveAndEnabled)
            return;

        var cam = Camera.main;
#if UNITY_EDITOR
        if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null)
            cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
        if (cam != null && maxDistanceM > 0f)
        {
            float d = Vector3.Distance(cam.transform.position, worldBounds.ClosestPoint(cam.transform.position));
            if (d > maxDistanceM) return;
        }
        if (_pages == null) RebuildPages();

        var rp = new RenderParams(material)
        {
            worldBounds = worldBounds,
            layer = gameObject.layer,
            shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
            receiveShadows = true,
            lightProbeUsage = LightProbeUsage.Off,
        };
        for (int i = 0; i < _pages.Length; i++)
            Graphics.RenderMeshInstanced(rp, mesh, subMeshIndex, _pages[i]);
    }
}
