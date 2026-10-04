using UnityEngine;

/// <summary>
/// Shunta Metro elevated bullet-train lines (user, 2026-10-04: make Shunta match Maple City's train network). Three straddle-beam lines
/// run alongside the road at different heights, lateral offsets, speeds and directions through the street zones; pooled beam segments,
/// pylons and 5-car trains follow the rider, so the cost is constant. Self-booting like ShuntaStreetCrowd; purely visual (no colliders).
/// </summary>
[DefaultExecutionOrder(46)]
public sealed class ShuntaSkyTrains : MonoBehaviour
{
    public static ShuntaSkyTrains Instance { get; private set; }
    public int VisibleCars { get; private set; }
    /// <summary>Distance (m) from the rider to the nearest visible sky-train car; large when none. Drives the audio pass-by swell.</summary>
    public float NearestTrainM { get; private set; } = 999f;

    const int Cars = 5, Segs = 22;
    const float CarLen = 16f, CarGap = 0.8f, SegM = 14f, ShowM = 320f;
    static readonly int[] StreetZones = { 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12 };

    sealed class Line
    {
        public float lateral, height, speed; public int dir; public float headM; public bool init;
        public Transform[] cars, beams, pylons;
    }
    Line[] lines;
    RideSession session; ShuntaRouteBuilder route; float nextResolve;
    Material body, stripeA, stripeB, window, beamMat;
    Mesh cube;
    const int CrossCars = 8;
    Transform[] crossCars; float crossTimer, crossM, crossHeight, crossSpeed, crossT; int crossDir; bool crossActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null || System.Environment.GetEnvironmentVariable("MR_SHUNTA_SKYTRAINS") == "0") return;
        var go = new GameObject("Shunta sky trains"); DontDestroyOnLoad(go);
        Instance = go.AddComponent<ShuntaSkyTrains>();
    }

    static bool Street(int zone) { for (int i = 0; i < StreetZones.Length; i++) if (StreetZones[i] == zone) return true; return false; }

    static Material Mat(Color albedo, Color emission, float smooth)
    {
        var sh = Shader.Find("HDRP/Lit"); if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh) { name = "ShuntaSkyTrainMat", hideFlags = HideFlags.DontSave };
        m.SetColor("_BaseColor", albedo); m.SetColor("_Color", albedo); m.SetFloat("_Smoothness", smooth);
        if (emission.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissiveColor", emission); m.SetColor("_EmissionColor", emission); }
        return m;
    }

    Transform Box(Transform parent, string n, Vector3 lp, Vector3 scale, Material mat)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp; go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    bool Build()
    {
        cube = PrimitiveMesh(PrimitiveType.Cube);
        body = Mat(new Color(0.84f, 0.82f, 0.94f), Color.black, 0.5f);
        stripeA = Mat(new Color(0.9f, 0.1f, 0.5f), new Color(6f, 0.8f, 3f), 0.3f);
        stripeB = Mat(new Color(0.1f, 0.6f, 0.9f), new Color(0.8f, 4.5f, 6f), 0.3f);
        window = Mat(new Color(0.2f, 0.3f, 0.4f), new Color(1.8f, 4f, 5.5f), 0.2f);
        beamMat = Mat(new Color(0.36f, 0.37f, 0.46f), Color.black, 0.25f);
        // 2026-10-04: lines moved ABOVE the road canyon (62/88/116 m) - at 19-41 m beside the road they sat inside the building rows and were never seen.
        float[] lat = { -4f, 5f, 0f }, hgt = { 62f, 88f, 116f }, spd = { 38f, 50f, 62f };
        int[] dir = { 1, -1, 1 };
        lines = new Line[3];
        for (int L = 0; L < 3; L++)
        {
            var ln = new Line { lateral = lat[L], height = hgt[L], speed = spd[L], dir = dir[L], cars = new Transform[Cars], beams = new Transform[Segs], pylons = new Transform[Segs / 4 + 2] };
            float half = route.roadWidth * 0.5f + 1.9f;
            var root = new GameObject("Line " + L).transform; root.SetParent(transform, false);
            for (int s = 0; s < Segs; s++) { ln.beams[s] = Box(root, "Beam", Vector3.zero, new Vector3(2.0f, 1.2f, SegM + 0.6f), beamMat); ln.beams[s].gameObject.SetActive(false); }
            for (int p = 0; p < ln.pylons.Length; p++)
            {
                // portal gantry: a column on each pavement edge and a cross beam carrying the line above the road
                var portal = new GameObject("Portal").transform; portal.SetParent(root, false);
                Box(portal, "ColL", new Vector3(-half, ln.height * 0.5f - 1.2f, 0f), new Vector3(1.1f, ln.height, 1.1f), beamMat);
                Box(portal, "ColR", new Vector3(half, ln.height * 0.5f - 1.2f, 0f), new Vector3(1.1f, ln.height, 1.1f), beamMat);
                Box(portal, "Cross", new Vector3(0f, ln.height - 1.5f, 0f), new Vector3(half * 2f + 1.1f, 1.0f, 1.4f), beamMat);
                portal.gameObject.SetActive(false);
                ln.pylons[p] = portal;
            }
            var stripe = L == 1 ? stripeB : stripeA;
            for (int c = 0; c < Cars; c++) ln.cars[c] = MakeCar(root, c, Cars, stripe);
            lines[L] = ln;
        }
        var croot = new GameObject("Crossers").transform; croot.SetParent(transform, false);
        crossCars = new Transform[CrossCars];
        for (int c = 0; c < CrossCars; c++) crossCars[c] = MakeCar(croot, c, CrossCars, c % 2 == 0 ? stripeA : stripeB);
        crossTimer = 4f;
        Debug.Log("[shunta-skytrains] built: 3 elevated lines (62/88/116 m) + crossing bullet trains");
        return true;
    }

    Transform MakeCar(Transform root, int c, int total, Material stripe)
    {
        var car = new GameObject("Car " + c).transform; car.SetParent(root, false);
        Box(car, "Body", new Vector3(0f, 1.7f, 0f), new Vector3(3.1f, 3.3f, CarLen), body);
        Box(car, "Stripe", new Vector3(0f, 1.0f, 0f), new Vector3(3.16f, 0.35f, CarLen * 0.99f), stripe);
        Box(car, "WinL", new Vector3(-1.58f, 2.2f, 0f), new Vector3(0.05f, 0.9f, CarLen * 0.88f), window);
        Box(car, "WinR", new Vector3(1.58f, 2.2f, 0f), new Vector3(0.05f, 0.9f, CarLen * 0.88f), window);
        if (c == 0 || c == total - 1)
        {
            float z = c == 0 ? 1f : -1f;
            Box(car, "Nose1", new Vector3(0f, 1.5f, z * (CarLen * 0.5f + 0.9f)), new Vector3(2.7f, 2.7f, 1.8f), body);
            Box(car, "Nose2", new Vector3(0f, 1.2f, z * (CarLen * 0.5f + 2.3f)), new Vector3(2.0f, 2.0f, 1.2f), body);
            Box(car, "Glass", new Vector3(0f, 1.9f, z * (CarLen * 0.5f + 1.85f)), new Vector3(1.6f, 0.8f, 0.2f), window);
        }
        car.gameObject.SetActive(false);
        return car;
    }

    void Hide()
    {
        VisibleCars = 0; NearestTrainM = 999f; crossActive = false;
        if (crossCars != null) foreach (var t in crossCars) if (t != null) t.gameObject.SetActive(false);
        if (lines == null) return;
        foreach (var ln in lines)
        {
            foreach (var t in ln.cars) if (t != null) t.gameObject.SetActive(false);
            foreach (var t in ln.beams) if (t != null) t.gameObject.SetActive(false);
            foreach (var t in ln.pylons) if (t != null) t.gameObject.SetActive(false);
            ln.init = false;
        }
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
        if (lines == null && !Build()) return;

        float totalKm = route.Course.distanceKm;
        float playerM = session.DistanceM / session.Course.Length * totalKm * 1000f;
        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        VisibleCars = 0; float nearest = 999f;
        for (int L = 0; L < lines.Length; L++)
        {
            var ln = lines[L];
            if (!ln.init || Mathf.Abs(ln.headM - playerM) > 400f)
            {
                ln.headM = playerM - ln.dir * (80f + L * 90f) - ln.dir * (L * 37f);
                ln.init = true;
            }
            ln.headM += ln.dir * ln.speed * dt;

            // beam + pylons on a fixed metre grid around the rider
            int baseSeg = Mathf.FloorToInt(playerM / SegM);
            int py = 0;
            for (int s = 0; s < Segs; s++)
            {
                int gi = baseSeg + s - Segs / 2;
                float m = gi * SegM + SegM * 0.5f;
                bool show = m > 5f && m < totalKm * 1000f - 5f && Street(ZoneIndex(m * 0.001f));
                ln.beams[s].gameObject.SetActive(show);
                if (!show) continue;
                Vector3 fwd = route.TangentAtKm(m * 0.001f); fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
                Vector3 side = new Vector3(fwd.z, 0f, -fwd.x);
                Vector3 p = route.PositionAtKm(m * 0.001f) + side * ln.lateral;
                ln.beams[s].SetPositionAndRotation(p + Vector3.up * (ln.height - 0.6f), Quaternion.LookRotation(fwd, Vector3.up));
                if ((gi & 3) == 0 && py < ln.pylons.Length)
                {
                    var t = ln.pylons[py++];
                    t.gameObject.SetActive(true);
                    t.SetPositionAndRotation(route.PositionAtKm(m * 0.001f), Quaternion.LookRotation(fwd, Vector3.up));
                }
            }
            for (; py < ln.pylons.Length; py++) ln.pylons[py].gameObject.SetActive(false);

            // train
            for (int c = 0; c < Cars; c++)
            {
                float m = ln.headM - ln.dir * c * (CarLen + CarGap);
                bool show = m > 5f && m < totalKm * 1000f - 5f && Mathf.Abs(m - playerM) < ShowM && Street(ZoneIndex(m * 0.001f));
                var car = ln.cars[c];
                if (car.gameObject.activeSelf != show) car.gameObject.SetActive(show);
                if (!show) continue;
                Vector3 fwd = route.TangentAtKm(m * 0.001f); fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
                Vector3 side = new Vector3(fwd.z, 0f, -fwd.x);
                Vector3 p = route.PositionAtKm(m * 0.001f) + side * ln.lateral + Vector3.up * ln.height;
                car.SetPositionAndRotation(p, Quaternion.LookRotation(fwd * ln.dir, Vector3.up));
                VisibleCars++;
                float along = Mathf.Abs(m - playerM);
                nearest = Mathf.Min(nearest, Mathf.Sqrt(along * along + ln.lateral * ln.lateral + ln.height * ln.height));
            }
        }
        // crossing bullet train: every 9-16 s one 8-car train streaks sideways across the road 130-230 m ahead, high over the roofs
        crossTimer -= dt;
        if (!crossActive && crossTimer <= 0f)
        {
            float cm = playerM + 130f + Random.value * 100f;
            if (cm < totalKm * 1000f - 20f && Street(ZoneIndex(cm * 0.001f)))
            {
                crossActive = true; crossM = cm; crossHeight = 70f + Random.value * 60f; crossSpeed = 85f + Random.value * 30f;
                crossDir = Random.value < 0.5f ? -1 : 1; crossT = -260f;
            }
            crossTimer = 9f + Random.value * 7f;
        }
        if (crossActive)
        {
            crossT += crossSpeed * dt;
            Vector3 cf = route.TangentAtKm(crossM * 0.001f); cf.y = 0f; if (cf.sqrMagnitude < 1e-4f) cf = Vector3.forward; cf.Normalize();
            Vector3 cs = new Vector3(cf.z, 0f, -cf.x);
            Vector3 cp = route.PositionAtKm(crossM * 0.001f);
            bool any = false;
            for (int c = 0; c < crossCars.Length; c++)
            {
                float lat = (crossT - c * (CarLen + CarGap)) * crossDir;
                bool show = Mathf.Abs(lat) < 280f;
                var car = crossCars[c];
                if (car.gameObject.activeSelf != show) car.gameObject.SetActive(show);
                if (!show) continue;
                any = true;
                car.SetPositionAndRotation(cp + cs * lat + Vector3.up * crossHeight, Quaternion.LookRotation(cs * crossDir, Vector3.up));
                VisibleCars++;
                nearest = Mathf.Min(nearest, Mathf.Sqrt(lat * lat + crossHeight * crossHeight + (crossM - playerM) * (crossM - playerM)));
            }
            if (!any && crossT > 280f + CrossCars * (CarLen + CarGap)) crossActive = false;
        }
        NearestTrainM = nearest;
    }

    int ZoneIndex(float km) { var z = route.Course.ZoneAtKm(km); return z != null ? z.index : 0; }

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
