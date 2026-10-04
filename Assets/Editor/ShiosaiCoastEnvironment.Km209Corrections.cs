using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Focused art-direction correction for the upper switchback review frame at chainage 2.09 km.
/// The route remains authoritative: this pass only adds a grounded outcrop and a colliderless
/// rock-faced slope closure outside the ride corridor, then tightens the far-range material
/// response so the peaks carry tonal texture instead of reading as white low-poly cards.
/// </summary>
public static partial class ShiosaiCoastEnvironment
{
    private const float Km209ChainageM = 2090f;
    private const float Km209SlopeStartM = 1985f;
    private const float Km209SlopeEndM = 2225f;

    private static void BuildKm209VisualCorrections(Transform root, CoastRoute route)
    {
        var group = new GameObject("Shiosai km209 Visual Corrections").transform;
        group.SetParent(root, false);

        BuildKm209GroundedOutcrop(group, route);
        BuildKm209SlopeClosure(group, route);
        TuneKm209PeakMaterials(root);
    }

    private static void BuildKm209GroundedOutcrop(Transform group, CoastRoute route)
    {
        int i = route.IndexAt(Km209ChainageM);
        var verts = new List<Vector3>();
        var uv = new List<Vector2>();
        var tri = new List<int>();
        var rng = new System.Random(ScatterSeed + 2090);

        // The old scatter rock was anchored to the folded profile's outer triangle. This
        // replacement is deliberately inside the local inland face, then sunk below the
        // sampled surface so its contact shadow and silhouette agree with the slope.
        float offset = ClampToTerrain(route, i, 34f, 4f);
        Vector3 basePoint = GroundedPoint(route, i, offset, 4f);
        float size = 3.6f;
        Rock(verts, uv, tri, basePoint + Vector3.down * (size * 0.55f), size, rng);

        // Two smaller companion stones make the hero outcrop read as geology rather than a
        // single black prop, while staying well outside the 6 m ride corridor.
        for (int k = 0; k < 2; k++)
        {
            int j = Mathf.Clamp(i + (k == 0 ? -7 : 8), 0, route.Count - 1);
            float o = ClampToTerrain(route, j, offset + (k == 0 ? -3.5f : 4.5f), 3f);
            var p = GroundedPoint(route, j, o, 3f);
            float s = k == 0 ? 1.35f : 1.65f;
            Rock(verts, uv, tri, p + Vector3.down * (s * 0.52f), s, rng);
        }

        AddMesh(group, "km209 Grounded Outcrop",
                Finish("Shiosai_km209_GroundedOutcrop", verts.ToArray(), uv.ToArray(), tri),
                RockPropMaterial(), collider: false);
        Debug.Log($"[shiosai] km209 grounded outcrop: d={route.Distance[i]:0} m, offset={offset:0.0} m, " +
                  $"baseY={basePoint.y:0.0} m.");
    }

    private static void BuildKm209SlopeClosure(Transform group, CoastRoute route)
    {
        var verts = new List<Vector3>();
        var uv = new List<Vector2>();
        var tri = new List<int>();
        int first = route.IndexAt(Km209SlopeStartM);
        int last = route.IndexAt(Km209SlopeEndM);
        const float topOffset = -8.8f;   // just beyond the seaward guardrail foot
        const float toeOffset = -34f;    // closes the exposed lower switchback sightline

        for (int i = first; i <= last; i += 3)
        {
            float topO = ClampToTerrain(route, i, topOffset, 1.0f);
            float toeO = ClampToTerrain(route, i, toeOffset, 2.0f);
            var top = GroundedPoint(route, i, topO, 1.0f) + Vector3.up * 1.25f;
            var toe = GroundedPoint(route, i, toeO, 2.0f) + Vector3.down * 2.2f;
            int b = verts.Count;
            verts.Add(top); verts.Add(toe);
            uv.Add(new Vector2(route.Distance[i] * 0.045f, 0f));
            uv.Add(new Vector2(route.Distance[i] * 0.045f, 1f));
            if (b >= 2)
            {
                tri.Add(b - 2); tri.Add(b); tri.Add(b - 1);
                tri.Add(b - 1); tri.Add(b); tri.Add(b + 1);
            }
        }

        if (verts.Count >= 4)
        {
            AddMesh(group, "km209 Switchback Slope Closure",
                    Finish("Shiosai_km209_SwitchbackSlopeClosure", verts.ToArray(), uv.ToArray(), tri),
                    CliffRockMaterial(), collider: false);
            Debug.Log($"[shiosai] km209 slope closure: {verts.Count / 2} grounded sections, " +
                      $"d={Km209SlopeStartM:0}-{Km209SlopeEndM:0} m.");
        }
    }

    private static void TuneKm209PeakMaterials(Transform root)
    {
        var distance = root.Find("Coast Distance");
        if (distance == null) return;
        int tuned = 0;
        foreach (var renderer in distance.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null || mat.shader == null || !mat.shader.name.Contains("RidgeHaze")) continue;
                mat.SetFloat("_MacroNoise", 0.52f);
                mat.SetFloat("_MacroScale", 210f);
                mat.SetFloat("_FormShading", 0.48f);
                EditorUtility.SetDirty(mat);
                tuned++;
            }
        }
        Debug.Log($"[shiosai] km209 peak texture response: tuned {tuned} RidgeHaze materials " +
                  "(_MacroNoise=0.52, _MacroScale=210, _FormShading=0.48).");
    }
}
