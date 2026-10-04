using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Shunta Metro high-fidelity landmark and set-piece geometry (Tokyo Tower, Mt. Fuji, Rainbow Bridge, Odaiba ferris wheel,
/// zone-9 interchange decks, zone-4 tunnel arches, zone-8 railway arches, finish arch, sakura avenue).
/// Everything is generated at runtime/edit time (DontSave, no colliders) from a <see cref="ShuntaRouteBuilder"/>; it is applied to
/// the playable scene by Assets/Editor/ShuntaMetroLandmarks.cs. Positions are world space (the builder's positionBias is honoured).
/// The older look's own simple landmark meshes are switched off (renderers only) so the two do not z-fight.
/// </summary>
[ExecuteAlways]
public sealed partial class ShuntaLandmarkSet : MonoBehaviour
{
    public ShuntaRouteBuilder route;
    public bool hideLegacy = true;
    public string LastReport { get; private set; } = "";
    /// <summary>Key world positions (set while building) for captures and tooling.</summary>
    public Vector3 TowerBase, FujiBase, WheelCentre;

    const string GenName = "Shunta Landmarks Generated";
    static readonly string[] LegacyMats =
    {
        "TowerRed", "TowerWhite", "TowerLight", "FujiBody", "FujiSnow", "BridgeSteel", "BridgeCable", "BridgeLamp",
        "WheelRim", "WheelFrame", "Gondola0", "Gondola1", "Gondola2", "Gondola3", "Gondola4", "Gondola5"
    };

    GameObject gen;
    ShuntaLandmarkMotion motion;
    readonly List<Object> owned = new List<Object>();
    bool built; bool legacyDone; int legacyTries; float nextLegacy, nextTry;
    readonly StringBuilder rep = new StringBuilder();
    Texture2D roadAlbedo, roadMask;

    void OnEnable() { BuildAll(); }
    void OnDisable() { Teardown(); }

    void Update()
    {
        if (!built && Time.realtimeSinceStartup >= nextTry) { nextTry = Time.realtimeSinceStartup + 0.5f; BuildAll(); }
        if (built && hideLegacy && !legacyDone && Time.realtimeSinceStartup >= nextLegacy)
        {
            nextLegacy = Time.realtimeSinceStartup + 1f; legacyTries++;
            int n = HideLegacy();
            if (n > 0 || legacyTries > 20) legacyDone = true;
        }
    }

    [ContextMenu("Rebuild Landmarks")]
    public void BuildAll()
    {
        Teardown();
        if (route == null) route = GetComponent<ShuntaRouteBuilder>() ?? FindFirstObjectByType<ShuntaRouteBuilder>();
        if (route == null) { LastReport = "[shunta-landmarks] no ShuntaRouteBuilder"; return; }
        if (route.Positions == null || route.Positions.Length < 4) route.Rebuild();
        if (route.Positions == null || route.Positions.Length < 4 || route.Course == null) { LastReport = "[shunta-landmarks] route not ready"; return; }

        gen = new GameObject(GenName) { hideFlags = HideFlags.DontSave };
        gen.transform.SetParent(transform, false);
        motion = gen.AddComponent<ShuntaLandmarkMotion>();
        rep.Length = 0;
        Step("tower", BuildTokyoTower);
        Step("fuji", BuildFuji);
        Step("bridge", BuildRainbowBridge);
        Step("wheel", BuildFerrisWheel);
        Step("interchange", BuildInterchange);
        Step("tunnel", BuildTunnelArches);
        Step("railway", BuildRailwayArches);
        Step("finish", BuildFinishArch);
        Step("sakura", BuildSakura);
        motion.enabled = true;
        built = true; legacyDone = false; legacyTries = 0; nextLegacy = 0f;
        if (hideLegacy) HideLegacy();
        LastReport = "[shunta-landmarks] built: " + rep;
        Debug.Log(LastReport);
    }

    void Step(string name, System.Action a)
    {
        try { a(); rep.Append(name).Append(" ok; "); }
        catch (System.Exception e) { rep.Append(name).Append(" FAILED; "); Debug.LogError("[shunta-landmarks] " + name + ": " + e); }
    }

    int HideLegacy()
    {
        var scope = transform.parent != null ? transform.parent : transform;
        int n = 0;
        foreach (var mr in scope.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null || mr.sharedMaterial == null || mr.transform.IsChildOf(gen != null ? gen.transform : transform)) continue;
            string mn = mr.sharedMaterial.name;
            for (int i = 0; i < LegacyMats.Length; i++)
                if (mn == LegacyMats[i]) { mr.enabled = false; n++; break; }
        }
        return n;
    }

    void Teardown()
    {
        if (gen != null) DestroyNow(gen);
        gen = null; motion = null; built = false;
        foreach (var o in owned) if (o != null) DestroyNow(o);
        owned.Clear(); roadAlbedo = null; roadMask = null;
        // Destroy is deferred in play mode; traverse once even when duplicate roots exist.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name == GenName) DestroyNow(child.gameObject);
        }
    }

    static void DestroyNow(Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

    // ------------------------------------------------------------------ helpers

    float Half => route.roadWidth * 0.5f;
    static float GroundY => ShuntaLookScenery.GroundY;

    Transform Child(string name)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(gen.transform, false);
        return go.transform;
    }

    /// <summary>Lit material with a non-black base (HDRP gotcha) and optional emissive (rel 1 = pixel value 1 at the look's fixed EV).</summary>
    Material M(string n, Color albedo, float smooth, Color emit, float rel)
    {
        albedo = new Color(Mathf.Max(albedo.r, 0.06f), Mathf.Max(albedo.g, 0.06f), Mathf.Max(albedo.b, 0.06f), 1f);
        var m = ShuntaLookKit.Lit("LM_" + n, albedo, smooth);
        if (rel > 0f) ShuntaLookKit.SetEmissive(m, emit, rel);
        owned.Add(m);
        return m;
    }

    void Flush(ShuntaLookKit.MeshBag bag, Transform parent, string prefix)
    {
        foreach (var go in bag.Flush(parent, prefix))
        {
            var mf = go.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) owned.Add(mf.sharedMesh);
        }
    }

    Vector3 RightAt(int i)
    {
        var P = route.Positions; int n = P.Length;
        var f = P[Mathf.Min(i + 1, n - 1)] - P[Mathf.Max(i - 1, 0)]; f.y = 0f;
        f = f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
        return new Vector3(f.z, 0f, -f.x);
    }

    int IdxAtKm(float km)
    {
        int i = System.Array.BinarySearch(route.Km, km); if (i < 0) i = ~i;
        return Mathf.Clamp(i, 0, route.Km.Length - 1);
    }

    Vector3 Bias => route.positionBias;

    /// <summary>Quad with a desired facing; picks the winding so the front face looks along <paramref name="facing"/>.</summary>
    static void Quad(List<Vector3> v, List<Vector3> nrm, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        Vector3 facing, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        int i = v.Count;
        var n = Vector3.Cross(b - a, c - a).normalized;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d); uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
        bool flip = Vector3.Dot(n, facing) < 0f;
        var nn = flip ? -n : n;
        for (int k = 0; k < 4; k++) nrm.Add(nn);
        if (!flip) { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
        else { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
    }

    GameObject MeshObject(Transform parent, string name, Mesh mesh, Material mat)
    {
        mesh.hideFlags = HideFlags.DontSave; owned.Add(mesh);
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    Material RoadMat(string name, Color tint)
    {
        if (roadAlbedo == null)
        {
            ShuntaLookKit.MakeRoadTextures(out roadAlbedo, out roadMask);
            owned.Add(roadAlbedo); owned.Add(roadMask);
        }
        var m = ShuntaLookKit.Lit("LM_" + name, Color.white, 0.5f);
        ShuntaLookKit.SetBaseMap(m, roadAlbedo, tint);
        ShuntaLookKit.SetMaskMap(m, roadMask, 0.3f, 0.75f);
        owned.Add(m);
        return m;
    }

    /// <summary>Road ribbon along an arbitrary polyline (width w, lifted by lift), faces up. Skips segments where skip[i] is true.</summary>
    GameObject RibbonAlong(Transform parent, string name, Vector3[] pts, float w, float lift, Material mat, bool[] skip = null)
    {
        var v = new List<Vector3>(); var nr = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        float run = 0f;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector3 f0 = Tangent(pts, i), f1 = Tangent(pts, i + 1);
            Vector3 r0 = new Vector3(f0.z, 0f, -f0.x), r1 = new Vector3(f1.z, 0f, -f1.x);
            float len = Vector3.Distance(pts[i], pts[i + 1]);
            if (skip == null || !skip[i])
            {
                var up = Vector3.up * lift;
                Quad(v, nr, uv, t, pts[i] - r0 * w * 0.5f + up, pts[i] + r0 * w * 0.5f + up, pts[i + 1] + r1 * w * 0.5f + up, pts[i + 1] - r1 * w * 0.5f + up,
                    Vector3.up, new Vector2(0, run / w), new Vector2(1, run / w), new Vector2(1, (run + len) / w), new Vector2(0, (run + len) / w));
            }
            run += len;
        }
        if (v.Count == 0) return null;
        var mesh = new Mesh { name = "LM_" + name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetNormals(nr); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
        return MeshObject(parent, name, mesh, mat);
    }

    static Vector3 Tangent(Vector3[] p, int i)
    {
        var f = p[Mathf.Min(i + 1, p.Length - 1)] - p[Mathf.Max(i - 1, 0)]; f.y = 0f;
        return f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
    }

    static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
}
