using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shunta Metro far skyline: four rings of distant tower silhouettes (about 1.6-13 km out) that give the city depth,
/// like Sakura Pass's mountain ranges. The towers come from a world-space grid (one hash per cell), so they are
/// deterministic and stable: as the rider travels, nearer rings slide past (parallax) while the far ones barely move.
/// Each ring is one merged mesh, rebuilt only when the camera crosses a cell of that ring (at most one ring per frame).
/// <c>MapleRide/Shunta/FarSkyline</c> draws them pinned just inside the far plane, so they never clip, are hidden by
/// nearer buildings, and are not removed by HDRP fog; atmospheric perspective is applied per layer toward the sky's
/// horizon colour. <see cref="ShuntaLookDriver"/> calls <see cref="NotifyKm"/> whenever it applies a km.
/// Tower height/density follow the zone (tall and dense at Shibuya and the expressway, low and sparse toward Odaiba bay)
/// and the rings hide inside the tunnel zone. Nothing generated is saved in the scene.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(220)]
[DisallowMultipleComponent]
public sealed class ShuntaFarSkyline : MonoBehaviour
{
    [Header("Look (all live-tweakable)")]
    public Shader shader;
    [Range(0f, 2f)] public float heightScale = 1f;
    [Range(0f, 1.5f)] public float density = 1f;
    [Range(0f, 2f)] public float hazeStrength = 1f;
    [Range(0f, 3f)] public float windowGain = 1f;
    [Range(0f, 4f)] public float beaconGain = 1.2f;
    [Tooltip("Show the rings while in the underground tunnel zone (normally hidden).")] public bool visibleInTunnel = false;

    // ------------------------------------------------------------------ layout (pure, testable)
    public struct LayerDef { public float innerM, outerM, cellM, minH, maxH, haze, windowW, windowH; }

    /// <summary>Near -> far. Each ring owns the band [innerM, outerM); bands tile 1.6-13 km with no gaps.</summary>
    public static readonly LayerDef[] Layers =
    {
        new LayerDef { innerM = 1600f, outerM = 3200f,  cellM = 220f, minH = 90f,  maxH = 260f, haze = .30f, windowW = 3.4f, windowH = 4.0f },
        new LayerDef { innerM = 3200f, outerM = 5400f,  cellM = 320f, minH = 140f, maxH = 380f, haze = .52f, windowW = 4.5f, windowH = 5.0f },
        new LayerDef { innerM = 5400f, outerM = 8500f,  cellM = 480f, minH = 200f, maxH = 520f, haze = .72f, windowW = 6.0f, windowH = 6.0f },
        new LayerDef { innerM = 8500f, outerM = 13000f, cellM = 720f, minH = 260f, maxH = 680f, haze = .88f, windowW = 8.0f, windowH = 8.0f },
    };
    public const float BaseDropM = 40f;        // towers extend this far below their base so no ground gap ever shows
    public const float MaxHeightJitter = 1.25f;
    public const int FirstQueue = 3001;        // the night-sky dome is 3000; far ring first, near ring last

    public struct Tower { public float x, z, halfW, halfD, yaw, height, tint, seed; }

    public static uint Hash(int layer, int cx, int cz)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)layer) * 16777619u; h = (h ^ (uint)cx) * 16777619u; h = (h ^ (uint)cz) * 16777619u;
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return h;
        }
    }
    static float Rand(ref uint s) { unchecked { s = s * 1664525u + 1013904223u; return (s >> 8) * (1f / 16777216f); } }

    /// <summary>Low-frequency "downtown cluster" noise, 0..1, smooth over ~3 km.</summary>
    public static float Cluster(float x, float z)
    {
        float fx = x / 3000f, fz = z / 3000f;
        int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
        float tx = Mathf.SmoothStep(0f, 1f, fx - ix), tz = Mathf.SmoothStep(0f, 1f, fz - iz);
        float V(int a, int b) { uint s = Hash(99, a, b); return Rand(ref s); }
        return Mathf.Lerp(Mathf.Lerp(V(ix, iz), V(ix + 1, iz), tx), Mathf.Lerp(V(ix, iz + 1), V(ix + 1, iz + 1), tx), tz);
    }

    /// <summary>The tower (if any) that lives in world grid cell (cx, cz) of a layer. Deterministic.</summary>
    public static bool TryTower(int layer, int cx, int cz, float density, float heightScale, out Tower t)
    {
        t = default;
        if (layer < 0 || layer >= Layers.Length || density <= 0f || heightScale <= 0f) return false;
        var L = Layers[layer];
        uint s = Hash(layer, cx, cz);
        float presence = Rand(ref s);
        float cluster = Cluster((cx + .5f) * L.cellM, (cz + .5f) * L.cellM);
        float p = Mathf.Clamp01(density * .78f * (.6f + .5f * cluster));
        if (presence > p) return false;
        float jx = (Rand(ref s) - .5f) * .5f, jz = (Rand(ref s) - .5f) * .5f;
        t.x = (cx + .5f + jx) * L.cellM; t.z = (cz + .5f + jz) * L.cellM;
        t.halfW = Mathf.Lerp(.28f, .62f, Rand(ref s)) * L.cellM * .5f;
        t.halfD = Mathf.Lerp(.28f, .62f, Rand(ref s)) * L.cellM * .5f;
        float hr = Mathf.Pow(Rand(ref s), 1.6f);
        t.height = Mathf.Max(20f, Mathf.Lerp(L.minH, L.maxH, hr) * heightScale * Mathf.Lerp(.75f, MaxHeightJitter, cluster));
        t.yaw = Rand(ref s) * Mathf.PI; t.tint = Rand(ref s); t.seed = Rand(ref s);
        return true;
    }

    /// <summary>Tallest tower a layer can produce at a height scale.</summary>
    public static float MaxHeight(int layer, float heightScale) => Layers[layer].maxH * heightScale * MaxHeightJitter;

    /// <summary>All towers of a layer whose cell centre lies in the layer's band around (camX, camZ).</summary>
    public static List<Tower> Plan(int layer, float camX, float camZ, float density, float heightScale)
    {
        var list = new List<Tower>();
        var L = Layers[layer];
        int n = Mathf.CeilToInt(L.outerM / L.cellM) + 1;
        int cx0 = Mathf.FloorToInt(camX / L.cellM), cz0 = Mathf.FloorToInt(camZ / L.cellM);
        for (int dx = -n; dx <= n; dx++)
            for (int dz = -n; dz <= n; dz++)
            {
                int cx = cx0 + dx, cz = cz0 + dz;
                float mx = (cx + .5f) * L.cellM - camX, mz = (cz + .5f) * L.cellM - camZ;
                float d = Mathf.Sqrt(mx * mx + mz * mz);
                if (d < L.innerM || d >= L.outerM) continue;
                if (TryTower(layer, cx, cz, density, heightScale, out var t)) list.Add(t);
            }
        return list;
    }

    // ------------------------------------------------------------------ zone shaping
    /// <summary>Height scale and density per course zone index (1-12). Tunnel (4) hides the rings.</summary>
    public static void ZoneShape(int zoneIndex, out float height, out float dens)
    {
        switch (zoneIndex)
        {
            case 1: height = 1.20f; dens = 1.00f; break;   // Shibuya crossing: tall and dense
            case 2: height = 0.85f; dens = 1.00f; break;   // narrow streets
            case 3: height = 1.00f; dens = 0.90f; break;   // elevated ramp
            case 4: height = 0.00f; dens = 0.00f; break;   // underground tunnel: nothing to see
            case 5: height = 1.35f; dens = 1.00f; break;   // skyline expressway: the big skyline
            case 6: height = 1.15f; dens = 0.90f; break;
            case 7: height = 0.95f; dens = 1.00f; break;   // rain streets
            case 8: height = 0.85f; dens = 0.90f; break;   // under railway
            case 9: height = 1.05f; dens = 0.85f; break;   // interchange
            case 10: height = 0.80f; dens = 0.55f; break;  // Rainbow Bridge: open water around
            case 11: height = 0.55f; dens = 0.40f; break;  // Odaiba coastline: lower, sparser
            default: height = 0.50f; dens = 0.35f; break;  // finish
        }
    }

    /// <summary>Zone shape at a km, lightly smoothed across zone borders.</summary>
    public static void ShapeAtKm(ShuntaCourseData c, float km, out float height, out float dens)
    {
        float h = 0f, d = 0f, wsum = 0f;
        foreach (var o in new[] { -.4f, 0f, .4f })
        {
            var z = c != null ? c.ZoneAtKm(Mathf.Max(0f, km + o)) : null;
            ZoneShape(z != null ? z.index : 12, out float zh, out float zd);
            float w = o == 0f ? 2f : 1f;
            h += zh * w; d += zd * w; wsum += w;
        }
        height = h / wsum; dens = d / wsum;
    }

    // ------------------------------------------------------------------ mesh
    /// <summary>One merged mesh for a layer (local space: x/z world, y above the base). Towers are emitted far -> near.</summary>
    public static Mesh BuildMesh(int layer, List<Tower> towers, float camX, float camZ, Mesh reuse = null)
    {
        towers.Sort((a, b) => Dist2(b, camX, camZ).CompareTo(Dist2(a, camX, camZ)));
        var v = new List<Vector3>(towers.Count * 20); var uv = new List<Vector2>(towers.Count * 20);
        var col = new List<Color32>(towers.Count * 20); var idx = new List<int>(towers.Count * 30);
        Vector3 key = new Vector3(.6f, 0f, .8f);
        foreach (var t in towers)
        {
            float c = Mathf.Cos(t.yaw), s = Mathf.Sin(t.yaw);
            var corner = new Vector2[4];
            float[] sx = { -1f, 1f, 1f, -1f }, sz = { -1f, -1f, 1f, 1f };      // counter-clockwise seen from above
            for (int k = 0; k < 4; k++)
            {
                float lx = sx[k] * t.halfW, lz = sz[k] * t.halfD;
                corner[k] = new Vector2(t.x + lx * c - lz * s, t.z + lx * s + lz * c);
            }
            byte hb = (byte)Mathf.Clamp(Mathf.RoundToInt(t.height / 1000f * 255f), 0, 255);
            byte tint = (byte)(t.tint * 255f), seed = (byte)(t.seed * 255f);
            float uOff = t.seed * 97f;
            for (int k = 0; k < 4; k++)
            {
                Vector2 a = corner[k], b = corner[(k + 1) % 4];
                Vector2 e = (b - a); float len = e.magnitude;
                Vector3 n = new Vector3(e.y, 0f, -e.x).normalized;               // outward (ccw footprint, x right / z up)
                float shade = .70f + .30f * Mathf.Clamp01(.5f + .5f * Vector3.Dot(n, key));
                var sc = new Color32((byte)(shade * 255f), tint, seed, hb);
                int i0 = v.Count;
                v.Add(new Vector3(a.x, -BaseDropM, a.y)); uv.Add(new Vector2(uOff, -BaseDropM));
                v.Add(new Vector3(a.x, t.height, a.y));   uv.Add(new Vector2(uOff, t.height));
                v.Add(new Vector3(b.x, t.height, b.y));   uv.Add(new Vector2(uOff + len, t.height));
                v.Add(new Vector3(b.x, -BaseDropM, b.y)); uv.Add(new Vector2(uOff + len, -BaseDropM));
                for (int q = 0; q < 4; q++) col.Add(sc);
                idx.Add(i0); idx.Add(i0 + 1); idx.Add(i0 + 2); idx.Add(i0); idx.Add(i0 + 2); idx.Add(i0 + 3);
            }
            // flat roof (clockwise seen from above)
            int r0 = v.Count; var rc = new Color32((byte)(.55f * 255f), tint, seed, hb);
            for (int k = 0; k < 4; k++) { v.Add(new Vector3(corner[k].x, t.height, corner[k].y)); uv.Add(new Vector2(corner[k].x, corner[k].y)); col.Add(rc); }
            idx.Add(r0); idx.Add(r0 + 3); idx.Add(r0 + 2); idx.Add(r0); idx.Add(r0 + 2); idx.Add(r0 + 1);
        }
        var m = reuse != null ? reuse : new Mesh();
        m.Clear();
        m.name = "ShuntaFarSkyline" + layer; m.hideFlags = HideFlags.DontSave;
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(idx, 0, false);
        float reach = Layers[layer].outerM + Layers[layer].cellM;
        m.bounds = new Bounds(new Vector3(camX, MaxHeight(layer, 2f) * .5f, camZ), new Vector3(reach * 2f, MaxHeight(layer, 2f) + BaseDropM * 2f, reach * 2f));
        return m;
    }
    static float Dist2(Tower t, float x, float z) { float dx = t.x - x, dz = t.z - z; return dx * dx + dz * dz; }

    // ------------------------------------------------------------------ runtime
    sealed class LayerState { public GameObject go; public Mesh mesh; public Material mat; public int cx = int.MinValue, cz = int.MinValue, paramKey = int.MinValue; public bool dirty = true; public int towers; }
    LayerState[] states;
    static readonly List<ShuntaFarSkyline> All = new List<ShuntaFarSkyline>();
    const string RootName = "Shunta Far Skyline Layers";

    float curHeight = 1f, curDensity = 1f, curNeon = 1f; Color curHaze = new Color(.1f, .12f, .25f); bool tunnel;
    public string LastReport { get; private set; } = "";
    public int TowerCount { get { int n = 0; if (states != null) foreach (var s in states) n += s.towers; return n; } }

    /// <summary>Hook called from <see cref="ShuntaLookDriver.ApplyKm"/>.</summary>
    public static void NotifyKm(ShuntaRouteBuilder route, float km, Color horizon, float neon)
    {
        for (int i = 0; i < All.Count; i++) if (All[i] != null) All[i].ApplyKm(route, km, horizon, neon);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); Build(); }
    void OnDisable() { All.Remove(this); Teardown(); }

    void Build()
    {
        Teardown();
        if (shader == null) shader = Shader.Find("MapleRide/Shunta/FarSkyline");
        if (shader == null) { LastReport = "FAILED: no shader MapleRide/Shunta/FarSkyline"; Debug.LogError("[shunta-farsky] " + LastReport); return; }
        states = new LayerState[Layers.Length];
        for (int i = 0; i < Layers.Length; i++)
        {
            var st = new LayerState();
            st.mat = new Material(shader) { name = "ShuntaFarSkyline" + i, hideFlags = HideFlags.DontSave };
            st.mat.renderQueue = FirstQueue + (Layers.Length - 1 - i);      // far first
            st.mesh = new Mesh { name = "ShuntaFarSkyline" + i, hideFlags = HideFlags.DontSave };
            st.go = new GameObject(RootName + " " + i) { hideFlags = HideFlags.DontSave };
            st.go.transform.SetParent(transform, false);
            st.go.AddComponent<MeshFilter>().sharedMesh = st.mesh;
            var mr = st.go.AddComponent<MeshRenderer>(); mr.sharedMaterial = st.mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;
            states[i] = st;
        }
        PushMaterials();
        LastReport = $"[shunta-farsky] {Layers.Length} rings built";
    }

    void Teardown()
    {
        if (states != null)
            foreach (var st in states)
            {
                if (st == null) continue;
                DestroyNow(st.go); DestroyNow(st.mesh); DestroyNow(st.mat);
            }
        states = null;
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith(RootName)) DestroyNow(child.gameObject);
        }
    }
    static void DestroyNow(Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

    public void ApplyKm(ShuntaRouteBuilder route, float km, Color horizon, float neon)
    {
        if (states == null || route == null || route.Course == null) return;
        ShapeAtKm(route.Course, km, out float h, out float d);
        var z = route.Course.ZoneAtKm(km);
        tunnel = z != null && z.index == 4;
        curHeight = h; curDensity = d; curHaze = horizon; curNeon = neon;
        PushMaterials();
    }

    /// <summary>Re-push inspector tuning without waiting for the next km change.</summary>
    [ContextMenu("Refresh")]
    public void Refresh() { if (states != null) { foreach (var s in states) s.dirty = true; PushMaterials(); } }
    void OnValidate() { if (isActiveAndEnabled && states != null) Refresh(); }

    void PushMaterials()
    {
        if (states == null) return;
        float neon = Mathf.Clamp(curNeon, .3f, 1.5f);
        for (int i = 0; i < states.Length; i++)
        {
            var st = states[i]; var L = Layers[i];
            if (st.mat == null) continue;
            st.mat.SetVector("_TowerA", new Vector4(.030f, .040f, .070f, 1f));
            st.mat.SetVector("_TowerB", new Vector4(.075f, .095f, .150f, 1f));
            st.mat.SetVector("_Haze", new Vector4(curHaze.r, curHaze.g, curHaze.b, 1f));
            st.mat.SetVector("_Layer", new Vector4((L.innerM + L.outerM) * .5f, Mathf.Clamp(L.haze * hazeStrength, 0f, .96f), 90f, 1f));
            st.mat.SetVector("_Win", new Vector4(L.windowW, L.windowH, .5f, windowGain * neon * .9f));
            st.mat.SetVector("_WarmWin", new Vector4(1f, .72f, .38f, 1f));
            st.mat.SetVector("_CoolWin", new Vector4(.50f, .80f, 1f, 1f));
            var pin = st.mat.GetVector("_Pin");
            st.mat.SetVector("_Pin", new Vector4(pin.x, ShuntaLookKit.NitsPerRel, .985f, beaconGain));
            // a ring is rebuilt when its quantised zone shape changes (not on every frame of a zone blend)
            int key = Mathf.RoundToInt(curHeight * heightScale * 10f) * 1000 + Mathf.RoundToInt(curDensity * density * 10f);
            if (key != st.paramKey) { st.paramKey = key; st.dirty = true; }
        }
    }

    void LateUpdate()
    {
        if (states == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 p = cam.transform.position;
        bool show = !(tunnel && !visibleInTunnel) && curHeight * heightScale > .01f && curDensity * density > .01f;
        float t = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
        bool rebuiltOne = false;
        for (int i = 0; i < states.Length; i++)
        {
            var st = states[i]; var L = Layers[i];
            if (st.go == null) continue;
            if (st.go.activeSelf != show) st.go.SetActive(show);
            if (!show) continue;
            int cx = Mathf.RoundToInt(p.x / L.cellM), cz = Mathf.RoundToInt(p.z / L.cellM);
            if (cx != st.cx || cz != st.cz) { st.cx = cx; st.cz = cz; st.dirty = true; }
            if (st.dirty && !rebuiltOne)
            {
                var list = Plan(i, p.x, p.z, Mathf.Clamp(curDensity * density, 0f, 1.5f), curHeight * heightScale);
                BuildMesh(i, list, p.x, p.z, st.mesh);
                st.towers = list.Count; st.dirty = false; rebuiltOne = true;
            }
            st.go.transform.position = new Vector3(0f, p.y - 10f, 0f);      // meshes are y-relative to the base
            var pin = st.mat.GetVector("_Pin"); pin.x = t; st.mat.SetVector("_Pin", pin);
        }
    }
}
