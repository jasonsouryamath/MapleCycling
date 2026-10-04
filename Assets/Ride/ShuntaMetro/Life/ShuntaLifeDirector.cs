using UnityEngine;

/// <summary>
/// Self-bootstrapping ambient-life director for Shunta Metro (active only while RideSession.courseId == "shunta_metro").
/// Builds its own world-space polyline from the JSON route (same builder + WorldOffset as ShuntaRouteProvider), then drives:
/// a commuter train on a parallel offset line through zone 8, pooled opposing traffic on expressway zones,
/// and camera-attached rain (zones 7-8) / sakura (zones 11-12) particle systems.
/// Everything is pooled up front; nothing is visible beyond activeRadius of the rider; no per-frame allocations.
/// Disable with env MR_SHUNTA_LIFE=0.
/// </summary>
public sealed class ShuntaLifeDirector : MonoBehaviour
{
    public static ShuntaLifeDirector Instance { get; private set; }
    public ShuntaLifeSettings settings = new ShuntaLifeSettings();
    public bool Active { get; private set; }
    public int VisibleTrainCars { get; private set; }
    public int VisibleTraffic { get; private set; }
    /// <summary>Diagnostics (smoke test).</summary>
    public float RiderOffsetM { get; private set; }   // distance between the director's route point for the rider and the real rider
    public bool RainOn => _rainOn;
    public bool SakuraOn => _sakuraOn;
    public int RainParticles => _rain != null ? _rain.particleCount : 0;
    public int SakuraParticles => _sakura != null ? _sakura.particleCount : 0;

    // route
    ShuntaCourseData _course;
    Vector3[] _pos; float[] _km;
    float _mPerKm, _maxKm;
    RideSession _session;
    float _nextFind, _nextCam;
    Camera _cam;
    bool _built;

    // shared materials
    Material _matTrainBody, _matTrainStripe, _matTrainWindow, _matHead, _matTail;
    Material[] _matCars;
    Mesh _cube;

    // train
    Transform[] _trainCars; bool[] _trainOn;
    float _trainHeadKm; bool _trainInRange;
    float _trainStartKm, _trainEndKm;

    // traffic
    Transform[] _tCars; bool[] _tOn; float[] _tKm, _tSpeed, _tRetry;
    Transform _root;

    // particles
    ParticleSystem _rain, _sakura; bool _rainOn, _sakuraOn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        if (System.Environment.GetEnvironmentVariable("MR_SHUNTA_LIFE") == "0") return;
        var go = new GameObject("~ShuntaLifeDirector");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ShuntaLifeDirector>();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void LateUpdate()
    {
        if (_session == null)
        {
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            _session = FindFirstObjectByType<RideSession>();
            if (_session == null) return;
        }
        bool on = _session.courseId == ShuntaRouteProvider.CourseId && _session.Course != null;
        if (!on) { if (Active) Deactivate(); return; }
        if (!_built && !Build()) { _nextFind = Time.unscaledTime + 5f; _session = null; return; }
        if (!Active) { Active = true; _root.gameObject.SetActive(true); _trainInRange = false; }

        float dt = Time.deltaTime;
        float riderKm = Mathf.Clamp(_session.DistanceM / 1000f, 0f, _maxKm);
        PointAt(riderKm, out var riderPos, out _);
        RiderOffsetM = (riderPos - _session.WorldPosition).magnitude;
        int zone = ZoneAt(riderKm);

        TickCamera();
        TickParticles(zone);
        if (settings.trainEnabled) TickTrain(dt, riderKm, riderPos);
        if (settings.trafficEnabled) TickTraffic(dt, riderKm, riderPos);
    }

    void Deactivate()
    {
        Active = false;
        if (_root != null) _root.gameObject.SetActive(false);
        SetParticles(ref _rainOn, _rain, false); SetParticles(ref _sakuraOn, _sakura, false);
        VisibleTrainCars = 0; VisibleTraffic = 0;
    }

    // ------------------------------------------------------------ route

    bool Build()
    {
        _course = ShuntaCourseData.Load();
        if (_course == null || _course.zones.Length == 0) return false;
        var go = new GameObject("shunta_life_tmp") { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);
        var b = go.AddComponent<ShuntaRouteBuilder>();
        b.buildRibbon = false; b.buildGates = false;
        go.SetActive(true);
        b.Rebuild();
        bool ok = b.Positions.Length >= 2;
        if (ok)
        {
            int n = b.Positions.Length;
            _pos = new Vector3[n]; _km = new float[n];
            float len = 0f;
            for (int i = 0; i < n; i++)
            {
                _pos[i] = b.Positions[i] + ShuntaRouteProvider.WorldOffset; _km[i] = b.Km[i];
                if (i > 0) len += Vector3.Distance(_pos[i - 1], _pos[i]);
            }
            _maxKm = _km[n - 1];
            _mPerKm = len / Mathf.Max(_maxKm, 0.001f);
        }
        if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        if (!ok) return false;

        _trainStartKm = 0f; _trainEndKm = 0f;
        for (int i = 0; i < _course.zones.Length; i++)
            if (_course.zones[i].index == settings.trainZone) { _trainStartKm = _course.zones[i].startKm; _trainEndKm = _course.zones[i].endKm; }

        _root = new GameObject("Shunta Life").transform;
        _root.SetParent(transform, false);
        Random.InitState(settings.seed);
        _cube = PrimitiveMesh(PrimitiveType.Cube);
        BuildMaterials();
        BuildTrain();
        BuildTraffic();
        BuildParticles();
        _built = true;
        Debug.Log($"[shunta-life] ready: train cars {settings.trainCars}, traffic {settings.trafficCars}, route {_maxKm:F1} km");
        return true;
    }

    /// <summary>World position and unit tangent at route km (binary search, no allocation).</summary>
    void PointAt(float km, out Vector3 p, out Vector3 tan)
    {
        int n = _km.Length;
        int hi = System.Array.BinarySearch(_km, km);
        if (hi < 0) hi = ~hi;
        hi = Mathf.Clamp(hi, 1, n - 1);
        float t = Mathf.InverseLerp(_km[hi - 1], _km[hi], km);
        p = Vector3.Lerp(_pos[hi - 1], _pos[hi], t);
        var d = _pos[hi] - _pos[hi - 1];
        tan = d.sqrMagnitude < 1e-6f ? Vector3.forward : d.normalized;
    }

    static Vector3 RightOf(Vector3 tan)
    {
        var h = new Vector3(tan.x, 0f, tan.z);
        h = h.sqrMagnitude < 1e-6f ? Vector3.forward : h.normalized;
        return new Vector3(h.z, 0f, -h.x);
    }

    int ZoneAt(float km)
    {
        var z = _course.ZoneAtKm(km);
        return z != null ? z.index : 0;
    }

    // ------------------------------------------------------------ build pools

    void BuildMaterials()
    {
        var s = settings;
        _matTrainBody = Mat(s.trainBody, Color.black, 0.6f);
        _matTrainStripe = Mat(s.trainStripe, s.trainStripe * 0.8f, 0.5f);
        _matTrainWindow = Mat(s.trainWindow * 0.3f, s.trainWindow * s.windowIntensity, 0.2f);
        _matHead = Mat(Color.white, new Color(1f, 0.95f, 0.8f) * s.headlightIntensity, 0.2f);
        _matTail = Mat(new Color(0.5f, 0.02f, 0.02f), new Color(1f, 0.05f, 0.05f) * s.taillightIntensity, 0.2f);
        var cols = new[] { new Color(0.08f, 0.08f, 0.1f), new Color(0.75f, 0.75f, 0.78f), new Color(0.6f, 0.08f, 0.1f), new Color(0.1f, 0.2f, 0.45f), new Color(0.85f, 0.8f, 0.7f) };
        _matCars = new Material[cols.Length];
        for (int i = 0; i < cols.Length; i++) _matCars[i] = Mat(cols[i], Color.black, 0.75f);
    }

    static Material Mat(Color albedo, Color emission, float smooth)
    {
        var sh = Shader.Find("HDRP/Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh) { name = "ShuntaLifeMat", hideFlags = HideFlags.DontSave };
        m.SetColor("_BaseColor", albedo); m.SetColor("_Color", albedo);
        m.SetFloat("_Smoothness", smooth);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissiveColor", emission); m.SetColor("_EmissionColor", emission);
        }
        return m;
    }

    Transform Box(Transform parent, string n, Vector3 lp, Vector3 scale, Material mat)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp; go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = _cube;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    void BuildTrain()
    {
        int n = settings.trainCars;
        _trainCars = new Transform[n]; _trainOn = new bool[n];
        float L = settings.carLength;
        for (int i = 0; i < n; i++)
        {
            var c = new GameObject("TrainCar" + i).transform;
            c.SetParent(_root, false);
            Box(c, "Body", new Vector3(0f, 1.65f, 0f), new Vector3(3.0f, 3.3f, L), _matTrainBody);
            Box(c, "Stripe", new Vector3(0f, 1.0f, 0f), new Vector3(3.06f, 0.35f, L * 0.99f), _matTrainStripe);
            Box(c, "WindowsL", new Vector3(-1.52f, 2.15f, 0f), new Vector3(0.05f, 1.0f, L * 0.9f), _matTrainWindow);
            Box(c, "WindowsR", new Vector3(1.52f, 2.15f, 0f), new Vector3(0.05f, 1.0f, L * 0.9f), _matTrainWindow);
            c.gameObject.SetActive(false);
            _trainCars[i] = c;
        }
    }

    void BuildTraffic()
    {
        int n = settings.trafficCars;
        _tCars = new Transform[n]; _tOn = new bool[n];
        _tKm = new float[n]; _tSpeed = new float[n]; _tRetry = new float[n];
        float w = settings.trafficSize.x, l = settings.trafficSize.y;
        for (int i = 0; i < n; i++)
        {
            var c = new GameObject("Car" + i).transform;
            c.SetParent(_root, false);
            Box(c, "Body", new Vector3(0f, 0.55f, 0f), new Vector3(w, 0.8f, l), _matCars[i % _matCars.Length]);
            Box(c, "Cabin", new Vector3(0f, 1.2f, -0.2f), new Vector3(w * 0.85f, 0.6f, l * 0.5f), _matCars[(i + 2) % _matCars.Length]);
            // front = +Z (headlights), back = -Z (taillights)
            Box(c, "HeadL", new Vector3(-w * 0.35f, 0.6f, l * 0.5f), new Vector3(0.4f, 0.2f, 0.08f), _matHead);
            Box(c, "HeadR", new Vector3(w * 0.35f, 0.6f, l * 0.5f), new Vector3(0.4f, 0.2f, 0.08f), _matHead);
            Box(c, "TailL", new Vector3(-w * 0.35f, 0.6f, -l * 0.5f), new Vector3(0.4f, 0.2f, 0.08f), _matTail);
            Box(c, "TailR", new Vector3(w * 0.35f, 0.6f, -l * 0.5f), new Vector3(0.4f, 0.2f, 0.08f), _matTail);
            c.gameObject.SetActive(false);
            _tCars[i] = c;
            _tSpeed[i] = Random.Range(settings.trafficSpeed.x, settings.trafficSpeed.y);
            _tRetry[i] = Random.value * settings.respawnInterval;
        }
    }

    void BuildParticles()
    {
        _rain = MakeParticles("Shunta Rain", false);
        _sakura = MakeParticles("Shunta Sakura", true);
    }

    ParticleSystem MakeParticles(string n, bool sakura)
    {
        var go = new GameObject(n);
        go.transform.SetParent(_root, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = sakura ? 400 : 1200;
        var emis = ps.emission; emis.enabled = true; emis.rateOverTime = 0f;
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        var sh = Shader.Find("HDRP/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        var m = new Material(sh) { name = n + "Mat", hideFlags = HideFlags.DontSave };
        m.SetColor("_UnlitColor", Color.white); m.SetColor("_BaseColor", Color.white); m.SetColor("_Color", Color.white);
        r.sharedMaterial = m;

        if (sakura)
        {
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startColor = new ParticleSystem.MinMaxGradient(settings.sakuraColorA, settings.sakuraColorB);
            main.gravityModifier = 0.02f;
            shape.scale = new Vector3(40f, 1f, 40f); shape.position = new Vector3(0f, 9f, 12f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-1.8f, -0.4f); vel.y = new ParticleSystem.MinMaxCurve(-1.2f, -0.5f); vel.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.4f;
            r.renderMode = ParticleSystemRenderMode.Billboard;
        }
        else
        {
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.035f);
            main.startColor = settings.rainColor;
            main.gravityModifier = 0f;
            shape.scale = new Vector3(30f, 1f, 30f); shape.position = new Vector3(0f, 10f, 8f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(settings.rainWind); vel.y = new ParticleSystem.MinMaxCurve(-22f, -16f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 1.5f; r.velocityScale = 0.04f;
        }
        return ps;
    }

    // ------------------------------------------------------------ ticks

    void TickCamera()
    {
        if (_cam != null && Time.unscaledTime < _nextCam) return;
        _nextCam = Time.unscaledTime + 1f;
        var c = Camera.main;
        if (c == null || c == _cam) return;
        _cam = c;
        AttachToCamera(_rain, c);
        AttachToCamera(_sakura, c);
    }

    static void AttachToCamera(ParticleSystem ps, Camera c)
    {
        ps.transform.SetParent(c.transform, false);
        ps.transform.localPosition = Vector3.zero;
    }

    void TickParticles(int zone)
    {
        // keep emitter boxes world-aligned (the camera rotates with the rider)
        _rain.transform.rotation = Quaternion.identity;
        _sakura.transform.rotation = Quaternion.identity;
        bool wantRain = settings.rainEnabled && _cam != null && settings.InZones(settings.rainZones, zone);
        bool wantSakura = settings.sakuraEnabled && _cam != null && settings.InZones(settings.sakuraZones, zone);
        if (wantRain != _rainOn) { var e = _rain.emission; e.rateOverTime = wantRain ? settings.rainRate : 0f; }
        if (wantSakura != _sakuraOn) { var e = _sakura.emission; e.rateOverTime = wantSakura ? settings.sakuraRate : 0f; }
        SetParticles(ref _rainOn, _rain, wantRain);
        SetParticles(ref _sakuraOn, _sakura, wantSakura);
    }

    static void SetParticles(ref bool state, ParticleSystem ps, bool want)
    {
        if (ps == null || state == want) return;
        state = want;
        if (want) ps.Play(true); else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void TickTrain(float dt, float riderKm, Vector3 riderPos)
    {
        float lenKm = (settings.trainCars * (settings.carLength + settings.carGap)) / _mPerKm;
        float radKm = settings.activeRadius / _mPerKm;
        bool inRange = riderKm > _trainStartKm - radKm && riderKm < _trainEndKm + radKm;
        if (inRange && !_trainInRange)   // rider just arrived: launch a train from behind so it overtakes
            _trainHeadKm = Mathf.Max(_trainStartKm, riderKm - (60f + Random.value * 120f) / _mPerKm);
        _trainInRange = inRange;
        if (!inRange) { HideTrain(); return; }

        _trainHeadKm += settings.trainSpeed * dt / _mPerKm;
        if (_trainHeadKm > _trainEndKm + lenKm) _trainHeadKm = _trainStartKm;   // loop

        float r2 = settings.activeRadius * settings.activeRadius;
        float step = (settings.carLength + settings.carGap) / _mPerKm;
        int shown = 0;
        for (int i = 0; i < _trainCars.Length; i++)
        {
            float km = _trainHeadKm - i * step - 0.5f * settings.carLength / _mPerKm;
            bool vis = km >= _trainStartKm && km <= _trainEndKm;
            if (vis)
            {
                PointAt(km, out var p, out var tan);
                p += RightOf(tan) * settings.trainLateral + Vector3.up * settings.trainHeight;
                vis = (p - riderPos).sqrMagnitude <= r2;
                if (vis)
                {
                    _trainCars[i].SetPositionAndRotation(p, Quaternion.LookRotation(tan, Vector3.up));
                    shown++;
                }
            }
            if (vis != _trainOn[i]) { _trainOn[i] = vis; _trainCars[i].gameObject.SetActive(vis); }
        }
        VisibleTrainCars = shown;
    }

    void HideTrain()
    {
        for (int i = 0; i < _trainCars.Length; i++)
            if (_trainOn[i]) { _trainOn[i] = false; _trainCars[i].gameObject.SetActive(false); }
        VisibleTrainCars = 0;
    }

    void TickTraffic(float dt, float riderKm, Vector3 riderPos)
    {
        float r2 = settings.activeRadius * settings.activeRadius;
        int shown = 0;
        for (int i = 0; i < _tCars.Length; i++)
        {
            if (_tOn[i])
            {
                _tKm[i] -= _tSpeed[i] * dt / _mPerKm;   // opposite direction of travel
                bool ok = _tKm[i] > 0f;
                if (ok)
                {
                    PointAt(_tKm[i], out var p, out var tan);
                    p += RightOf(tan) * settings.trafficLateral;
                    ok = (p - riderPos).sqrMagnitude <= r2 && settings.InZones(settings.trafficZones, ZoneAt(_tKm[i]));
                    if (ok)
                    {
                        _tCars[i].SetPositionAndRotation(p + Vector3.up * 0.05f, Quaternion.LookRotation(-tan, Vector3.up));
                        shown++;
                    }
                }
                if (!ok) { _tOn[i] = false; _tCars[i].gameObject.SetActive(false); _tRetry[i] = 0f; }
            }
            else
            {
                _tRetry[i] -= dt;
                if (_tRetry[i] > 0f) continue;
                _tRetry[i] = settings.respawnInterval * (0.7f + Random.value);
                float ahead = Mathf.Lerp(settings.minSpawnAhead, settings.activeRadius * 0.95f, Random.value);
                float km = riderKm + ahead / _mPerKm;
                if (km >= _maxKm || !settings.InZones(settings.trafficZones, ZoneAt(km))) continue;
                _tKm[i] = km; _tOn[i] = true;
                _tSpeed[i] = Random.Range(settings.trafficSpeed.x, settings.trafficSpeed.y);
                if (settings.InZones(settings.slowZones, ZoneAt(km))) _tSpeed[i] *= settings.slowScale;
                _tCars[i].gameObject.SetActive(true);
            }
        }
        VisibleTraffic = shown;
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
