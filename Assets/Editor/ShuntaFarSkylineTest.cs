using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Headless checks for the Shunta far-skyline layout rules and mesh builder (pure maths, no scene, no GPU).
/// Run: Unity -batchmode -quit -executeMethod ShuntaFarSkylineTest.Run
/// Exits non-zero on failure. It cannot judge how the rings LOOK; that needs a person in Unity.
/// </summary>
public static class ShuntaFarSkylineTest
{
    public static void Run()
    {
        int fails = 0;
        void Check(bool ok, string what) { Debug.Log($"[shunta-farsky] {(ok ? "PASS" : "FAIL")} {what}"); if (!ok) fails++; }
        var layers = ShuntaFarSkyline.Layers;

        // Layer table: ordered bands that tile the range without gaps, with growing cells, heights and haze.
        bool tiled = true, growing = true;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].innerM >= layers[i].outerM) tiled = false;
            if (i > 0 && !Mathf.Approximately(layers[i].innerM, layers[i - 1].outerM)) tiled = false;
            if (i > 0 && (layers[i].cellM <= layers[i - 1].cellM || layers[i].haze <= layers[i - 1].haze || layers[i].maxH <= layers[i - 1].maxH)) growing = false;
        }
        Check(tiled, "layer bands tile 1.6-13 km with no gaps or overlaps");
        Check(growing, "farther layers have bigger cells, taller maximum towers and more haze");
        Check(layers[0].innerM >= 1500f, "the first ring starts beyond the streamed city (>= 1.5 km)");
        Check(ShuntaFarSkyline.FirstQueue > 3000, "rings draw after the night-sky dome (queue > 3000)");

        // Towers: deterministic, in bounds, absent when density or height is zero.
        bool det = true, inBounds = true, finite = true; int total = 0;
        for (int layer = 0; layer < layers.Length; layer++)
        {
            var L = layers[layer];
            var a = ShuntaFarSkyline.Plan(layer, 123f, -456f, 1f, 1f);
            var b = ShuntaFarSkyline.Plan(layer, 123f, -456f, 1f, 1f);
            if (a.Count != b.Count) det = false;
            for (int i = 0; i < a.Count && i < b.Count; i++) if (a[i].x != b[i].x || a[i].height != b[i].height) det = false;
            total += a.Count;
            float maxH = ShuntaFarSkyline.MaxHeight(layer, 1f) + .01f;
            foreach (var t in a)
            {
                float dx = t.x - 123f, dz = t.z + 456f, d = Mathf.Sqrt(dx * dx + dz * dz);
                // the cell centre is inside the band; the jittered tower may sit up to one cell off it
                if (d < L.innerM - L.cellM || d > L.outerM + L.cellM) inBounds = false;
                if (t.height < 20f || t.height > maxH || t.halfW <= 0f || t.halfD <= 0f) inBounds = false;
                if (float.IsNaN(t.x) || float.IsNaN(t.z) || float.IsNaN(t.height) || float.IsNaN(t.yaw)) finite = false;
            }
            Check(a.Count > 150 && a.Count < 1500, $"layer {layer} has a plausible tower count ({a.Count})");
        }
        Check(det, "planning is deterministic");
        Check(inBounds, "every tower is in its band and within the layer's height range");
        Check(finite, "no NaN in tower data");
        Check(ShuntaFarSkyline.Plan(0, 0f, 0f, 0f, 1f).Count == 0, "zero density gives no towers");
        Check(ShuntaFarSkyline.Plan(0, 0f, 0f, 1f, 0f).Count == 0, "zero height scale gives no towers");
        Check(ShuntaFarSkyline.Plan(0, 0f, 0f, .4f, 1f).Count < ShuntaFarSkyline.Plan(0, 0f, 0f, 1f, 1f).Count, "lower density gives fewer towers");

        // Parallax: moving the camera 1 km changes the near ring's towers but a far ring keeps most of its view.
        var near0 = ShuntaFarSkyline.Plan(0, 0f, 0f, 1f, 1f); var near1 = ShuntaFarSkyline.Plan(0, 1000f, 0f, 1f, 1f);
        var set = new HashSet<long>(); foreach (var t in near0) set.Add(Key(t));
        int shared = 0; foreach (var t in near1) if (set.Contains(Key(t))) shared++;
        Check(shared < near0.Count * .8f, "the near ring changes as the camera travels (parallax)");
        Check(shared > 0, "towers are world-fixed: the same tower can be seen from two camera positions");

        // Zone shaping: tunnel hidden, expressway tallest, coast and finish lower and sparser.
        ShuntaFarSkyline.ZoneShape(4, out float th, out float td);
        ShuntaFarSkyline.ZoneShape(5, out float eh, out float ed);
        ShuntaFarSkyline.ZoneShape(11, out float ch, out float cd);
        ShuntaFarSkyline.ZoneShape(12, out float fh, out float fd);
        Check(th == 0f && td == 0f, "the tunnel zone hides the rings");
        Check(eh > ch && eh > fh && ed > cd && ed > fd, "expressway skyline is taller and denser than the coast and finish");
        bool zonesOk = true;
        for (int z = 1; z <= 12; z++) { ShuntaFarSkyline.ZoneShape(z, out float zh, out float zd); if (zh < 0f || zh > 1.5f || zd < 0f || zd > 1f) zonesOk = false; }
        Check(zonesOk, "all 12 zone shapes are within range");

        // Mesh: valid, 20 vertices and 30 indices per tower, outward winding, far-to-near order.
        var plan = ShuntaFarSkyline.Plan(1, 0f, 0f, 1f, 1f);
        var mesh = ShuntaFarSkyline.BuildMesh(1, plan, 0f, 0f);
        Check(mesh.vertexCount == plan.Count * 20, $"mesh has 20 vertices per tower ({mesh.vertexCount} for {plan.Count})");
        Check((int)mesh.GetIndexCount(0) == plan.Count * 30, "mesh has 30 indices per tower");
        bool outward = true, farToNear = true; int checkedFaces = 0; float lastD = float.MaxValue;
        var verts = mesh.vertices; var idx = mesh.triangles;
        for (int t = 0; t < plan.Count; t++)
        {
            float d = plan[t].x * plan[t].x + plan[t].z * plan[t].z;
            if (d > lastD + 1f) farToNear = false;
            lastD = d;
            if (t % 25 != 0) continue;
            // first side face of this tower: its triangle's geometric normal must point away from the tower centre
            int tri = t * 30;
            Vector3 a = verts[idx[tri]], b = verts[idx[tri + 1]], c = verts[idx[tri + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            Vector3 centre = new Vector3(plan[t].x, a.y, plan[t].z);
            Vector3 mid = (a + b + c) / 3f;
            // Unity front faces are clockwise as seen, and for those cross(b-a, c-a) points at the viewer
            if (Vector3.Dot(n, new Vector3(mid.x - centre.x, 0f, mid.z - centre.z)) <= 0f) outward = false;
            checkedFaces++;
        }
        Check(checkedFaces > 0 && outward, $"side faces wind so they face outward ({checkedFaces} sampled)");
        Check(farToNear, "towers are emitted far to near (painter's order)");
        Object.DestroyImmediate(mesh);

        // Shader exists and compiled without errors (the GPU-dependent isSupported is deliberately not asserted).
        var shader = Shader.Find("MapleRide/Shunta/FarSkyline");
        Check(shader != null, "far skyline shader is found");
        if (shader != null) Check(!ShaderUtil.ShaderHasError(shader), "far skyline shader has no compile errors");

        if (fails > 0) { Debug.LogError($"[shunta-farsky] FAILED: {fails} check(s)"); if (Application.isBatchMode) EditorApplication.Exit(2); }
        else Debug.Log("[shunta-farsky] ALL PASS");
    }

    static long Key(ShuntaFarSkyline.Tower t) => ((long)Mathf.RoundToInt(t.x * 4f) << 32) ^ (uint)Mathf.RoundToInt(t.z * 4f);
}
