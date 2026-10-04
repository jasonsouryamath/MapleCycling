// Nagisa Bay visual-polish slice for the opening-stretch art-critic ticket.
// Owns only the idempotent NB VisualPolish stage: authored grass breakup, a soft
// tropical cloud bank, and small-prop rhythm.  Existing NB2/NB9/NB1/NB5 files stay untouched.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    private const int VisualPolishSeed = 12003;

    [NagisaStage(44, "VisualPolish")]
    private static void BuildVisualPolishStage(Transform group)
    {
        EnsureRouteGround();
        var grass = BuildGrassVariation(group);
        int props = BuildPropRhythm(group);
        int clouds = BuildCloudLayer(group);
        Debug.Log($"[nagisa-polish] grass clumps={grass}, authored verge props={props}, cloud puffs={clouds}; route corridor preserved.");
    }

    private static int BuildGrassVariation(Transform group)
    {
        var mats = new[]
        {
            // Tints stay bright: the grass texture is already dark and a dark tint rendered the clumps as pure black cones (2026-10-02).
            Cel("Nagisa_PolishGrass_Deep", new Color(0.46f, 0.84f, 0.35f), 0.02f, 0.03f, 0.02f,
                Tex(CoastTex, "Shiosai_Grass_Albedo.png"), Tex(CoastTex, "Shiosai_Grass_Normal.png"), 0.5f),
            Cel("Nagisa_PolishGrass_Sun", new Color(0.78f, 0.99f, 0.37f), 0.02f, 0.03f, 0.02f,
                Tex(CoastTex, "Shiosai_Grass_Albedo.png"), Tex(CoastTex, "Shiosai_Grass_Normal.png"), 0.5f),
            Cel("Nagisa_PolishGrass_Dry", new Color(0.97f, 1.00f, 0.38f), 0.01f, 0.02f, 0.01f,
                Tex(CoastTex, "Shiosai_Grass_Albedo.png"), Tex(CoastTex, "Shiosai_Grass_Normal.png"), 0.45f)
        };
        foreach (var m in mats)
        {
            m.SetFloat("_WindStrength", 0.16f);
            m.SetFloat("_Cutoff", 0.35f);
            m.enableInstancing = true;
        }

        var rng = new System.Random(VisualPolishSeed);
        int count = 0;
        for (float d = 80f; d < _route.Length - 80f; d += 32f)
        {
            int i = _route.IndexAt(d);
            if (_route.OnBridge(i)) continue;
            var p = _route.Position[i];
            var side = _route.SideFlat(i);
            for (int s = -1; s <= 1; s += 2)
            {
                float offset = 5.0f + (float)rng.NextDouble() * 2.0f;
                var q = p + side * (s * offset);
                if (!CanPlace(q.x, q.z, 0.45f, out float y, 2.8f)) continue;
                var t = new GameObject($"Grass Clump {count:D3}").transform;
                t.SetParent(group, false);
                t.position = new Vector3(q.x, y + 0.01f, q.z);
                t.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                t.localScale = new Vector3(0.55f + (float)rng.NextDouble() * 0.5f,
                                            0.55f + (float)rng.NextDouble() * 0.7f,
                                            0.55f + (float)rng.NextDouble() * 0.5f);
                var mf = t.gameObject.AddComponent<MeshFilter>();
                var mr = t.gameObject.AddComponent<MeshRenderer>();
                mf.sharedMesh = GrassClumpMesh($"Nagisa_PolishGrassMesh_{count % 4}", count % 4);
                mr.sharedMaterial = mats[(count + i) % mats.Length];
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
                count++;

                // A second, lower tuft sits on the outer verge rather than on the road
                // seam.  The existing tall clumps establish silhouette; this staggered
                // layer supplies the close-to-ground breakup that prevents the grass
                // band reading as a repeated card when the rider is moving quickly.
                float outerOffset = 8.05f + (float)rng.NextDouble() * 1.25f;
                var lowQ = p + side * (s * outerOffset);
                if (CanPlace(lowQ.x, lowQ.z, 0.55f, out float lowY, 2.8f))
                {
                    var low = new GameObject($"Grass Understory {count:D3}").transform;
                    low.SetParent(group, false);
                    low.position = new Vector3(lowQ.x, lowY + 0.012f, lowQ.z);
                    low.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    float scale = 0.34f + (float)rng.NextDouble() * 0.22f;
                    low.localScale = new Vector3(scale * 1.25f, scale * 0.72f, scale);
                    var lowMf = low.gameObject.AddComponent<MeshFilter>();
                    var lowMr = low.gameObject.AddComponent<MeshRenderer>();
                    lowMf.sharedMesh = GrassClumpMesh($"Nagisa_PolishUnderstory_{count % 4}", (count + 1) % 4);
                    lowMr.sharedMaterial = mats[(count + i + 1) % mats.Length];
                    lowMr.shadowCastingMode = ShadowCastingMode.Off;
                    lowMr.receiveShadows = true;
                    count++;
                }
            }
        }
        return count;
    }

    private static Mesh GrassClumpMesh(string name, int variant)
    {
        var v = new List<Vector3>
        {
            new Vector3(-0.45f, 0f, 0f), new Vector3(0.45f, 0f, 0f), new Vector3(0.12f, 1f + variant * 0.08f, 0f),
            new Vector3(0f, 0f, -0.42f), new Vector3(0f, 0f, 0.42f), new Vector3(0f, 0.92f + variant * 0.06f, 0.08f),
            new Vector3(-0.26f, 0f, 0.18f), new Vector3(0.32f, 0f, -0.18f), new Vector3(0.05f, 0.82f + variant * 0.1f, 0.02f)
        };
        var uv = new List<Vector2>
        {
            new Vector2(0,0),new Vector2(1,0),new Vector2(.5f,1),
            new Vector2(0,0),new Vector2(1,0),new Vector2(.5f,1),
            new Vector2(0,0),new Vector2(1,0),new Vector2(.5f,1)
        };
        var t = new List<int> { 0,2,1, 3,5,4, 6,8,7, 1,2,0, 4,5,3, 7,8,6 };
        var mesh = Finish(name, v, uv, t);
        return mesh;
    }

    private static int BuildPropRhythm(Transform group)
    {
        var rng = new System.Random(VisualPolishSeed + 9);
        var stems = new[] { "Nagisa_Bench", "Nagisa_FanPalm", "Nagisa_Hibiscus", "Nagisa_Bougainvillea", "Nagisa_PromenadeLamp" };
        int count = 0;
        for (float d = 140f; d < _route.Length - 140f; d += 118f)
        {
            int i = _route.IndexAt(d);
            if (_route.OnBridge(i)) continue;
            var p = _route.Position[i];
            var side = _route.SideFlat(i) * ((i / 7) % 2 == 0 ? 1f : -1f);
            float offset = 5.2f + (float)rng.NextDouble() * 1.2f;
            var q = p + side * offset;
            if (!CanPlace(q.x, q.z, 1.1f, out float y, 3.0f)) continue;
            string stem = stems[count % stems.Length];
            var placed = PlaceWorld(stem, group, new Vector3(q.x, y - 0.04f, q.z),
                                    Mathf.Atan2(side.x, side.z) * Mathf.Rad2Deg + (float)rng.NextDouble() * 20f - 10f,
                                    0.86f + (float)rng.NextDouble() * 0.28f, SmallPropCull);
            if (placed != null) count++;
        }
        return count;
    }

    private static int BuildCloudLayer(Transform group)
    {
        var shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
        if (shader == null) return 0;
        var mat = LoadOrCreate("Nagisa_PolishCloud", shader.name);
        mat.color = new Color(0.98f, 0.99f, 1f, 0.34f);
        mat.SetColor("_Color", new Color(0.98f, 0.99f, 1f, 0.34f));
        mat.SetColor("_UnlitColor", new Color(0.98f, 0.99f, 1f, 0.34f));
        mat.SetOverrideTag("RenderType", "Transparent");
        if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
        if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);

        var rng = new System.Random(VisualPolishSeed + 27);
        int puffs = 0;
        for (int band = 0; band < 3; band++)
        {
            for (int k = 0; k < 7; k++)
            {
                float x = -1400f + k * 430f + (float)rng.NextDouble() * 120f;
                float z = -900f + band * 900f + (float)rng.NextDouble() * 240f;
                float y = 105f + band * 12f + (float)rng.NextDouble() * 8f;
                var cloud = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cloud.name = $"Tropical Cloud {puffs:D2}";
                cloud.transform.SetParent(group, false);
                cloud.transform.position = new Vector3(x, y, z);
                cloud.transform.localScale = new Vector3(42f + (float)rng.NextDouble() * 26f,
                                                          7f + (float)rng.NextDouble() * 5f,
                                                          18f + (float)rng.NextDouble() * 15f);
                var collider = cloud.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
                var renderer = cloud.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                puffs++;
            }
        }
        return puffs;
    }
}
