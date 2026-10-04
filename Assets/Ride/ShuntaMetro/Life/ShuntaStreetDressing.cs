using UnityEngine;

/// <summary>
/// Shunta street-level atmosphere (user, 2026-10-04: more life and look). A pooled, deterministic scatter along the street zones:
///   * steam vents  - soft white plumes rising from kerbside grates, lit pink/cyan by the nearest neon;
///   * alley glows  - a neon-lit alley mouth in the building line (two vertical tubes + a lintel + a haze card) so the street edge is not a wall;
///   * neon puddles - in the rain zones, flat glossy-looking neon streaks on the road edge (additive glint, no real reflections needed).
/// Cells are fixed 55 m slices of the route, so the same spot always carries the same dressing (stable between laps and across seeds);
/// a 20-slot pool follows the rider, so cost is constant. Visual only; self-booting like ShuntaStreetCrowd.
/// </summary>
[DefaultExecutionOrder(44)]
public sealed class ShuntaStreetDressing : MonoBehaviour
{
    public static ShuntaStreetDressing Instance { get; private set; }
    public int ActiveSlots { get; private set; }

    const int Pool = 20;
    const float CellM = 55f, ShowM = 260f;
    static readonly int[] DressZones = { 1, 2, 3, 7, 8, 11, 12 };
    static readonly int[] RainZones = { 7, 8 };

    sealed class Slot
    {
        public int cell = int.MinValue; public int kind;     // 0 none, 1 steam, 2 alley, 3 puddle
        public GameObject root, alley, puddle; public ParticleSystem steam;
        public Renderer[] alleyTubes; public Renderer haze, puddleR;
    }
    Slot[] slots;
    RideSession session; ShuntaRouteBuilder route; float nextResolve;
    Mesh cube, quad; Material pink, cyan, amber, hazePink, hazeCyan, puffMat, puddlePink, puddleCyan;
    Texture2D soft;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null || System.Environment.GetEnvironmentVariable("MR_SHUNTA_DRESSING") == "0") return;
        var go = new GameObject("Shunta street dressing"); DontDestroyOnLoad(go);
        Instance = go.AddComponent<ShuntaStreetDressing>();
    }

    static bool In(int[] a, int v) { for (int i = 0; i < a.Length; i++) if (a[i] == v) return true; return false; }
    static uint Hash(int x) { unchecked { uint h = (uint)x * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return h; } }
    static float H01(int cell, int salt) => (Hash(cell * 31 + salt) & 0xFFFF) / 65535f;

    Texture2D SoftTex()
    {
        var t = new Texture2D(64, 64, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 32f, dy = (y - 31.5f) / 32f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)); a = a * a * (3f - 2f * a);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply(false, true);
        return t;
    }

    static Material Unlit(string n, Color c, Texture tex, bool additive)
    {
        Shader sh = tex != null ? Shader.Find("Sprites/Default") : Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("HDRP/Unlit");
        var m = new Material(sh) { name = n, hideFlags = HideFlags.DontSave };
        m.SetColor("_Color", c); m.SetColor("_BaseColor", c); m.SetColor("_UnlitColor", c);
        if (tex != null) { m.SetTexture("_MainTex", tex); m.SetTexture("_BaseColorMap", tex); }
        if (additive && m.HasProperty("_SrcBlend")) { m.SetFloat("_SrcBlend", 1f); m.SetFloat("_DstBlend", 1f); }
        return m;
    }

    GameObject Part(Transform parent, string n, Mesh mesh, Vector3 lp, Vector3 scale, Material mat, out Renderer rend)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp; go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        rend = go.AddComponent<MeshRenderer>();
        rend.sharedMaterial = mat; rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false;
        return go;
    }

    bool Build()
    {
        cube = PrimitiveMesh(PrimitiveType.Cube);
        quad = PrimitiveMesh(PrimitiveType.Quad);
        soft = SoftTex();
        pink = Unlit("ShuntaDressPink", new Color(2.3f, 0.30f, 1.15f, 1f), null, false);
        cyan = Unlit("ShuntaDressCyan", new Color(0.30f, 1.8f, 2.3f, 1f), null, false);
        amber = Unlit("ShuntaDressAmber", new Color(2.3f, 1.35f, 0.35f, 1f), null, false);
        hazePink = Unlit("ShuntaHazePink", new Color(1f, 0.25f, 0.65f, 0.28f), soft, false);
        hazeCyan = Unlit("ShuntaHazeCyan", new Color(0.2f, 0.8f, 1f, 0.28f), soft, false);
        puffMat = Unlit("ShuntaSteam", new Color(0.85f, 0.85f, 0.95f, 0.30f), soft, false);
        puddlePink = Unlit("ShuntaPuddlePink", new Color(1f, 0.25f, 0.65f, 0.55f), soft, false);
        puddleCyan = Unlit("ShuntaPuddleCyan", new Color(0.2f, 0.85f, 1f, 0.55f), soft, false);
        slots = new Slot[Pool];
        for (int i = 0; i < Pool; i++)
        {
            var s = new Slot { root = new GameObject("Dress " + i) };
            s.root.transform.SetParent(transform, false);
            // steam plume
            var ps = new GameObject("Steam"); ps.transform.SetParent(s.root.transform, false);
            s.steam = ps.AddComponent<ParticleSystem>();
            s.steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = s.steam.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 3.2f; main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 1.8f); main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.7f);
            main.maxParticles = 60; main.startColor = new Color(0.85f, 0.85f, 0.95f, 0.30f);
            var em = s.steam.emission; em.rateOverTime = 14f;
            var sh = s.steam.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 9f; sh.radius = 0.25f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var sz = s.steam.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 2.2f)));
            var col = s.steam.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                              new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = ps.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = puffMat; pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // alley mouth
            s.alley = new GameObject("Alley"); s.alley.transform.SetParent(s.root.transform, false);
            var tubes = new System.Collections.Generic.List<Renderer>(); Renderer rr;
            Part(s.alley.transform, "TubeL", cube, new Vector3(-2.4f, 3.4f, 0f), new Vector3(0.16f, 6.8f, 0.16f), pink, out rr); tubes.Add(rr);
            Part(s.alley.transform, "TubeR", cube, new Vector3(2.4f, 3.4f, 0f), new Vector3(0.16f, 6.8f, 0.16f), pink, out rr); tubes.Add(rr);
            Part(s.alley.transform, "Lintel", cube, new Vector3(0f, 6.8f, 0f), new Vector3(4.96f, 0.16f, 0.16f), cyan, out rr); tubes.Add(rr);
            Part(s.alley.transform, "Strip", cube, new Vector3(0f, 0.12f, 1.2f), new Vector3(0.12f, 0.04f, 3.4f), amber, out rr); tubes.Add(rr);
            s.alleyTubes = tubes.ToArray();
            var hz = Part(s.alley.transform, "Haze", quad, new Vector3(0f, 3.4f, 0.4f), new Vector3(5.2f, 7.2f, 1f), hazePink, out s.haze);
            hz.transform.localRotation = Quaternion.identity;
            // neon puddle
            s.puddle = new GameObject("Puddle"); s.puddle.transform.SetParent(s.root.transform, false);
            var pd = Part(s.puddle.transform, "Glint", quad, new Vector3(0f, 0.06f, 0f), new Vector3(4.2f, 7.5f, 1f), puddlePink, out s.puddleR);
            pd.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            s.root.SetActive(false);
            slots[i] = s;
        }
        return true;
    }

    void Hide()
    {
        ActiveSlots = 0;
        if (slots == null) return;
        foreach (var s in slots) if (s != null && s.root != null && s.root.activeSelf) { s.root.SetActive(false); s.cell = int.MinValue; }
    }

    void Update()
    {
        if (Time.unscaledTime >= nextResolve)
        {
            nextResolve = Time.unscaledTime + 1f;
            if (session == null) session = FindFirstObjectByType<RideSession>();
            if (route == null) route = FindFirstObjectByType<ShuntaRouteBuilder>();
        }
        bool active = session != null && session.Course != null && session.courseId == ShuntaRouteProvider.CourseId && route != null && route.Course != null;
        if (!active) { Hide(); return; }
        if (slots == null && !Build()) return;

        float totalM = route.Course.distanceKm * 1000f;
        float playerM = session.DistanceM / session.Course.Length * totalM;
        Transform cam = Camera.main != null ? Camera.main.transform : null;
        int first = Mathf.FloorToInt((playerM - ShowM) / CellM), last = Mathf.FloorToInt((playerM + ShowM) / CellM);
        ActiveSlots = 0;
        for (int cell = first; cell <= last; cell++)
        {
            var s = slots[((cell % Pool) + Pool) % Pool];
            if (s.cell == cell) { if (s.kind != 0) ActiveSlots++; continue; }
            // (re)assign this slot to the cell
            s.cell = cell;
            float m = cell * CellM + 8f + H01(cell, 1) * (CellM - 16f);
            var zone = (m > 5f && m < totalM - 5f) ? route.Course.ZoneAtKm(m * 0.001f) : null;
            int z = zone != null ? zone.index : 0;
            s.kind = 0;
            if (In(DressZones, z))
            {
                float r = H01(cell, 2);
                s.kind = r < 0.34f ? 1 : r < 0.62f ? 2 : (r < 0.82f && In(RainZones, z)) ? 3 : 0;
            }
            if (s.kind == 0) { s.root.SetActive(false); continue; }
            float km = m * 0.001f;
            Vector3 fwd = route.TangentAtKm(km); fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            Vector3 side = new Vector3(fwd.z, 0f, -fwd.x);
            float sg = H01(cell, 3) < 0.5f ? -1f : 1f;
            Vector3 centre = route.PositionAtKm(km);
            float half = route.roadWidth * 0.5f;
            s.root.SetActive(true);
            s.steam.gameObject.SetActive(s.kind == 1); s.alley.SetActive(s.kind == 2); s.puddle.SetActive(s.kind == 3);
            bool cyanTone = H01(cell, 4) < 0.5f;
            if (s.kind == 1)
            {
                s.root.transform.SetPositionAndRotation(centre + side * sg * (half + 1.1f) + Vector3.up * 0.1f, Quaternion.LookRotation(fwd, Vector3.up));
                s.steam.Clear(); s.steam.Play(true);
            }
            else if (s.kind == 2)
            {
                // alley mouth in the building line, facing the road
                s.root.transform.SetPositionAndRotation(centre + side * sg * (half + 6.5f) + Vector3.up * 0.0f, Quaternion.LookRotation(-side * sg, Vector3.up));
                foreach (var t in s.alleyTubes) t.sharedMaterial = t.name == "Lintel" ? (cyanTone ? pink : cyan) : (cyanTone ? cyan : pink);
                s.haze.sharedMaterial = cyanTone ? hazeCyan : hazePink;
            }
            else
            {
                s.root.transform.SetPositionAndRotation(centre + side * sg * (half - 1.6f - H01(cell, 5) * 1.2f) + Vector3.up * 0.04f, Quaternion.LookRotation(fwd, Vector3.up));
                s.puddleR.sharedMaterial = cyanTone ? puddleCyan : puddlePink;
            }
            ActiveSlots++;
        }
        // haze cards face the camera so they read from the road
        if (cam != null)
            foreach (var s in slots)
                if (s.kind == 2 && s.root.activeSelf && s.haze != null)
                {
                    var d = cam.position - s.haze.transform.position; d.y = 0f;
                    if (d.sqrMagnitude > 0.01f) s.haze.transform.rotation = Quaternion.LookRotation(-d.normalized, Vector3.up);
                }
    }

    // Resources.GetBuiltinResource<Mesh>("Cube.mesh") fails in Unity 6000.4 ("resource Cube.mesh could not be loaded") and returned null,
    // which left these objects invisible. Take the mesh from a throw-away primitive instead.
    static Mesh PrimitiveMesh(PrimitiveType t)
    {
        var g = GameObject.CreatePrimitive(t);
        var m = g.GetComponent<MeshFilter>().sharedMesh;
        var col = g.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
        Object.Destroy(g);
        return m;
    }
}
