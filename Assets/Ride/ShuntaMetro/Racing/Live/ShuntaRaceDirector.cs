using System.Collections.Generic;
using UnityEngine;

/// <summary>One line of the live standings board (struct, so reading it never allocates).</summary>
public struct ShuntaStanding
{
    public int place;
    public string name;
    public ShuntaArchetype archetype;
    public bool isPlayer;
    /// <summary>Distance along the course in km.</summary>
    public float km;
    /// <summary>Seconds behind the leader at current speed (0 for the leader).</summary>
    public float gapS;
    /// <summary>Metres ahead (+) / behind (-) the player.</summary>
    public float gapToPlayerM;
}

/// <summary>
/// Runtime AI race director for Shunta Metro. Self-bootstrapping, active ONLY while
/// RideSession.courseId == "shunta_metro". Spawns a pooled field of ~23 AI cyclists (clones of an existing
/// scene NPCCyclist rig, capsule fallback), moves them along the route each frame with ShuntaPacing
/// power -> speed, zone-modified draft and corner caps, nudges their lane choices through the ShuntaFairPlay
/// rules, publishes <see cref="Standings"/>, and deactivates far riders. Seeded and deterministic per run;
/// no per-frame allocations after the first activation. Disable with env MR_SHUNTA_RACERS=0.
/// </summary>
public sealed class ShuntaRaceDirector : MonoBehaviour
{
    public static ShuntaRaceDirector Instance { get; private set; }

    public const int AiCount = 23;
    public const int BaseSeed = 20261002;
    const float G = 9.80665f, Rho = 1.225f, Crr = 0.005f, Eff = 0.975f, BikeKg = 8.5f;
    const float DescentCapMps = 58f / 3.6f, DraftReachM = 15f;
    const float ActiveM = 450f, NearM = 120f;
    const float LaneChangeSpeed = 1.2f, MaxLateral = 3.4f;

    public bool Active { get; private set; }
    /// <summary>Live standings, leader first, includes the player ("You"). Reused list, do not modify.</summary>
    public List<ShuntaStanding> Standings => _standings;
    public int PlayerPlace { get; private set; }
    public int ActiveRiders { get; private set; }

    sealed class Racer
    {
        public ShuntaSimRider sim;
        public Transform tf;
        public GameObject go;
        public KuroBikeRig rig;
        public float lateral, laneTarget, heldLane, nextDecision;
        public int lane;
        public bool shown;
        public int lodLevel = -1;
    }

    RideSession _session;
    ShuntaSimCourse _course;
    ShuntaZoneModifiers _mods;
    Vector3[] _pos;
    float[] _km;
    float _lengthM;
    Racer[] _r;
    int[] _order;              // slot ids, leader first; slot AiCount is the player
    float[] _posM, _speed;     // length AiCount + 1
    readonly List<ShuntaStanding> _standings = new List<ShuntaStanding>(AiCount + 1);
    System.Random _rng;
    int _runSerial = -1;
    bool _built, _started;
    float _nextFind, _playerPosM, _playerSpeed, _lastPlayerM;


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        if (System.Environment.GetEnvironmentVariable("MR_SHUNTA_RACERS") == "0") return;
        var go = new GameObject("~ShuntaRaceDirector");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ShuntaRaceDirector>();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ------------------------------------------------------------------ setup

    bool BuildRoute()
    {
        var data = ShuntaCourseData.Load();
        if (data == null || data.zones == null || data.zones.Length == 0) return false;
        var go = new GameObject("shunta_racers_tmp") { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);                       // never activated: OnEnable cannot build a second time
        var b = go.AddComponent<ShuntaRouteBuilder>();
        b.buildRibbon = false; b.buildGates = false;
        b.Rebuild();
        if (b.Positions.Length < 2) { Destroy(go); return false; }
        _pos = (Vector3[])b.Positions.Clone();
        _km = (float[])b.Km.Clone();
        int[] zi = (int[])b.ZoneIndex.Clone();
        Destroy(go);
        for (int i = 0; i < _pos.Length; i++) _pos[i] += ShuntaRouteProvider.WorldOffset;
        _lengthM = _km[_km.Length - 1] * 1000f;

        _mods = new ShuntaZoneModifiers(data);
        _course = new ShuntaSimCourse { lengthM = _lengthM };
        int n = Mathf.CeilToInt(_lengthM / ShuntaSimCourse.StepM) + 1;
        _course.gradePct = new float[n];
        _course.zone = new byte[n];
        for (int z = 0; z < 13; z++) { _course.zoneGrip[z] = 1f; _course.zoneDraft[z] = 1f; }
        for (int i = 0; i < data.zones.Length; i++)
        {
            int zz = Mathf.Clamp(data.zones[i].index, 1, 12);
            _course.zoneGrip[zz] = data.zones[i].grip <= 0f ? 1f : data.zones[i].grip;
            _course.zoneDraft[zz] = data.zones[i].draftMultiplier <= 0f ? 1f : data.zones[i].draftMultiplier;
        }
        for (int j = 0; j < n; j++)
        {
            float m = j * ShuntaSimCourse.StepM;
            _course.gradePct[j] = Mathf.Clamp((SampleY(m + 5f) - SampleY(m - 5f)) / 10f * 100f, -20f, 20f);
            int hi = System.Array.BinarySearch(_km, m * 0.001f);
            if (hi < 0) hi = ~hi;
            hi = Mathf.Clamp(hi, 0, zi.Length - 1);
            _course.zone[j] = (byte)Mathf.Clamp(zi[hi], 1, 12);
        }
        return true;
    }

    float SampleY(float m)
    {
        Seg(Mathf.Clamp(m, 0f, _lengthM) * 0.001f, out int a, out float t);
        return Mathf.Lerp(_pos[a].y, _pos[a + 1].y, t);
    }

    void Seg(float km, out int a, out float t)
    {
        int hi = System.Array.BinarySearch(_km, km);
        if (hi < 0) hi = ~hi;
        hi = Mathf.Clamp(hi, 1, _km.Length - 1);
        a = hi - 1;
        float span = _km[hi] - _km[a];
        t = span > 1e-6f ? Mathf.Clamp01((km - _km[a]) / span) : 0f;
    }

    void BuildField()
    {
        var field = ShuntaRaceSimCore.BuildField(BaseSeed, 4, ShuntaRacerArchetypes.Nominal);   // 24, shuffled grid
        _r = new Racer[AiCount];
        _order = new int[AiCount + 1];
        _posM = new float[AiCount + 1];
        _speed = new float[AiCount + 1];
        for (int i = 0; i < AiCount; i++)
        {
            _r[i] = new Racer { sim = field[i] };
            _r[i].sim.name = "Rider " + (i + 1) + " " + field[i].profile.archetype;
            _order[i] = i;
        }
        _order[AiCount] = AiCount;
        for (int i = 0; i <= AiCount; i++) _standings.Add(default);
    }

    void SpawnPool()
    {
        var library = Resources.Load<ShuntaNpcLibrary>(ShuntaNpcLibrary.ResourcePath);
        if (library == null || library.riders.Length == 0) return;

        var parent = new GameObject("~ShuntaRacers").transform;
        parent.SetParent(transform, false);
        for (int i = 0; i < AiCount; i++)
        {
            GameObject go = ShuntaNpcLibrary.CloneRider(i, parent);
            if (go == null) { Destroy(parent.gameObject); return; } // wait for the real riding rig; no primitive people
            go.name = "~ShuntaAI_" + i;
            go.SetActive(false);
            _r[i].go = go;
            _r[i].tf = go.transform;
            _r[i].rig = go.GetComponentInChildren<KuroBikeRig>(true);
        }
    }

    static float Gauss(System.Random r)
    {
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
    }

    void ResetRace()
    {
        _rng = new System.Random(BaseSeed + _runSerial * 131);
        for (int i = 0; i < AiCount; i++)
        {
            var r = _r[i]; var s = r.sim;
            s.posM = -(i / 3) * 2.2f - 1f; s.speed = 0f; s.wBalJ = s.profile.wPrimeJ; s.finishS = -1f;
            s.form = 1f + 0.04f * Gauss(_rng);
            s.sitsIn = _rng.NextDouble() < s.profile.draftUsage;
            s.lastWetZone = 0; s.powerNoise = 0f; s.draftNow = 0f;
            r.lane = (i % 3) - 1;
            r.lateral = r.laneTarget = r.heldLane = r.lane * 1.4f + 1.0f;
            r.nextDecision = Time.time + 1f + (i % 8) * 0.12f;
            _posM[i] = s.posM; _speed[i] = 0f;
        }
        _started = false;
        _lastPlayerM = 0f;
    }

    // ------------------------------------------------------------------ frame

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
        if (!_built)
        {
            if (!BuildRoute()) { _nextFind = Time.unscaledTime + 5f; return; }
            BuildField();
            _built = true;
        }
        if (_r[0].go == null) SpawnPool();
        Active = true;
        if (_session.RunSerial != _runSerial) { _runSerial = _session.RunSerial; ResetRace(); }

        float dt = Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f);
        _playerPosM = _session.DistanceM;
        _playerSpeed = Mathf.Max(0f, (_playerPosM - _lastPlayerM) / dt);
        _lastPlayerM = _playerPosM;
        if (!_started && _session.ElapsedSeconds > 0.05f) _started = true;

        _posM[AiCount] = _playerPosM; _speed[AiCount] = _playerSpeed;
        SortOrder();
        if (_started) StepRiders(dt);
        for (int i = 0; i < AiCount; i++) { _posM[i] = _r[i].sim.posM; _speed[i] = _r[i].sim.speed; }
        SortOrder();
        Pose();
        PublishStandings();
    }

    void Deactivate()
    {
        Active = false; ActiveRiders = 0;
        if (_r == null) return;
        for (int i = 0; i < AiCount; i++)
            if (_r[i].go != null) { _r[i].go.SetActive(false); _r[i].shown = false; }
    }

    void SortOrder()      // insertion sort, nearly sorted every frame, no allocation
    {
        for (int i = 1; i <= AiCount; i++)
        {
            int v = _order[i]; float p = _posM[v]; int j = i - 1;
            while (j >= 0 && _posM[_order[j]] < p) { _order[j + 1] = _order[j]; j--; }
            _order[j + 1] = v;
        }
    }

    void StepRiders(float dt)
    {
        float lenM = _course.lengthM;
        for (int oi = 0; oi <= AiCount; oi++)
        {
            int id = _order[oi];
            if (id == AiCount) continue;
            var r = _r[id]; var s = r.sim; var p = s.profile;
            if (s.finishS >= 0f)
            {
                s.speed = Mathf.Max(0f, s.speed - 3f * dt);
                s.posM += s.speed * dt;
                continue;
            }
            int idx = _course.Index(s.posM);
            int zone = _course.zone[idx];
            float grade = _course.gradePct[idx] * 0.01f;
            float mass = p.massKg + BikeKg;
            var mod = _mods.Sample(Mathf.Max(0f, s.posM) * 0.001f);

            float gap = 999f, leadSpeed = 0f;
            for (int k = oi - 1; k >= 0; k--)
            {
                int oid = _order[k];
                if (oid != AiCount && _r[oid].sim.finishS >= 0f) continue;
                gap = _posM[oid] - s.posM; leadSpeed = _speed[oid];
                float lat = oid == AiCount ? 0.5f : Mathf.Abs(_r[oid].lateral - r.lateral);
                if (lat > 1.6f) gap = 999f;       // wheel ahead must be roughly in line to draft
                break;
            }
            float draft = 0f;
            if (gap >= 0.3f && gap < DraftReachM)
                draft = Mathf.Min(0.5f, 0.34f * (0.35f + 0.65f * p.draftUsage) * mod.draftMultiplier * (1f - gap / DraftReachM));
            s.draftNow = draft;

            var ctx = new ShuntaPacingContext
            {
                kmDone = s.posM / 1000f, kmToGo = (lenM - s.posM) / 1000f, gradePercent = grade * 100f,
                zoneIndex = zone, wBalJ = s.wBalJ, drafting = draft > 0.10f
            };
            float watts = ShuntaPacing.TargetWatts(p, ctx) * s.form;
            bool sprinting = ctx.kmToGo * 1000f <= ShuntaPacing.SprintDistanceM * 1.1f;
            if (s.sitsIn && draft > 0.10f && !sprinting && ctx.kmToGo > 0.9f &&
                !(ShuntaPacing.IsSpendZone(zone) && s.wBalJ > 0.3f * p.wPrimeJ && p.spendBias > 0.6f))
            {
                float hold = PowerToHold(leadSpeed + 0.05f, p.CdA * (1f - draft), mass, grade);
                if (hold < watts) watts = hold;
            }
            s.powerNoise = Mathf.Lerp(s.powerNoise, 0.03f * Gauss(_rng), dt * 0.2f);
            watts *= 1f + s.powerNoise;

            float ftp = p.ftpW;
            if (watts > ftp)
            {
                if (s.wBalJ <= 0f) watts = ftp * 0.95f;
                else s.wBalJ = Mathf.Max(0f, s.wBalJ - (watts - ftp) * dt);
            }
            else s.wBalJ = Mathf.Min(p.wPrimeJ, s.wBalJ + (ftp - watts) * 0.35f * dt);

            float v = s.speed, vd = Mathf.Max(v, 3f);
            float acc = (watts * Eff / vd - 0.5f * Rho * p.CdA * (1f - draft) * v * v - Crr * mass * G - mass * G * grade) / mass;
            float vNew = Mathf.Max(v + acc * dt, 1.5f);

            float cap = DescentCapMps;
            float grip = Mathf.Min(_course.zoneGrip[zone], Mathf.Max(0.01f, mod.gripMultiplier));
            if (grip < 0.999f)
            {
                cap = Mathf.Min(cap, ShuntaPacing.CornerSpeedCapMps(p, grip));
                if ((zone == 7 || zone == 8) && s.lastWetZone != zone)
                {
                    s.lastWetZone = zone;
                    if (_rng.NextDouble() < ShuntaPacing.WetMishapChance(p)) vNew = 2f;
                }
            }
            if (vNew > cap) vNew = Mathf.Max(cap, v - 3f * dt);
            s.speed = vNew;
            s.posM += vNew * dt;
            if (s.posM >= lenM) s.finishS = _session.ElapsedSeconds;

            Lateral(r, oi, id, zone, mod, dt);
        }
    }

    void Lateral(Racer r, int oi, int id, int zone, ShuntaModifiers mod, float dt)
    {
        float spacing = Mathf.Lerp(1.4f, 0.85f, mod.packTightness);
        float now = Time.time;
        if (now >= r.nextDecision)
        {
            r.nextDecision = now + 0.6f + 0.1f * (id % 5);
            int want = r.lane;
            if (oi > 0)
            {
                int ahead = _order[oi - 1];
                float aheadLat = ahead == AiCount ? 0f : _r[ahead].laneTarget;
                float gapAhead = _posM[ahead] - r.sim.posM;
                if (gapAhead < 25f)
                    want = r.sim.sitsIn ? Mathf.RoundToInt(aheadLat / spacing)
                                        : r.lane + (aheadLat > r.laneTarget - 0.3f ? (id % 2 == 0 ? 1 : -1) : 0);
            }
            want = Mathf.Clamp(want, -2, 2);
            if (zone == ShuntaFairPlay.TunnelZone) want = r.lane;            // hold line in the tunnel
            if (want != r.lane)
            {
                float cand = want * spacing;
                float side = 99f, cut = 99f;
                for (int k = 0; k <= AiCount; k++)
                {
                    if (k == id) continue;
                    float dl = _posM[k] - r.sim.posM;
                    float lat = k == AiCount ? 0f : _r[k].lateral;
                    if (Mathf.Abs(dl) < 3f) side = Mathf.Min(side, Mathf.Abs(lat - cand));
                    if (dl < 0f && -dl < cut && Mathf.Abs(lat - cand) < 1.0f) cut = -dl;
                }
                var fp = new ShuntaFairPlayContext
                {
                    zoneIndex = zone, lateralDriftM = Mathf.Abs(cand - r.heldLane), lateralSpeedMps = LaneChangeSpeed,
                    sideGapM = side, cutInGapBehindM = cut, hardBrakeWithFollower = false, riskAppetite = r.sim.profile.riskAppetite
                };
                if (FairPlayOk(fp)) { r.lane = want; r.laneTarget = cand; }
            }
            else r.laneTarget = r.lane * spacing;
            if (zone != ShuntaFairPlay.TunnelZone) r.heldLane = r.laneTarget;
        }
        r.lateral = Mathf.MoveTowards(r.lateral, Mathf.Clamp(r.laneTarget, -MaxLateral, MaxLateral), LaneChangeSpeed * dt);
    }

    /// <summary>Allocation-free mirror of ShuntaFairPlay.Score (which builds a reasons list) against its AcceptThreshold.</summary>
    static bool FairPlayOk(in ShuntaFairPlayContext c)
    {
        float s = 1f;
        if (c.zoneIndex == ShuntaFairPlay.TunnelZone && c.lateralDriftM > 0.5f) s -= Mathf.Min(0.55f, 0.35f + (c.lateralDriftM - 0.5f) * 0.2f);
        if (c.zoneIndex == ShuntaFairPlay.InterchangeZone && c.sideGapM < 0.9f) s -= Mathf.Min(0.5f, 0.3f + (0.9f - c.sideGapM) * 0.4f);
        if (c.cutInGapBehindM < 1.5f && c.lateralSpeedMps > 0.6f) s -= 0.45f;
        if (c.hardBrakeWithFollower) s -= 0.2f;
        s += (0.5f - c.riskAppetite) * 0.04f;
        return s >= ShuntaFairPlay.AcceptThreshold;
    }

    static float PowerToHold(float v, float cdA, float mass, float grade)
        => Mathf.Max(0f, (0.5f * Rho * cdA * v * v + Crr * mass * G + mass * G * grade) * v / Eff);

    void Pose()
    {
        int shown = 0;
        for (int i = 0; i < AiCount; i++)
        {
            var r = _r[i];
            if (r.go == null) continue;
            float d = Mathf.Abs(r.sim.posM - _playerPosM);
            bool show = d < ActiveM;
            if (show != r.shown) { r.go.SetActive(show); r.shown = show; r.lodLevel = -1; }
            if (!show) continue;
            shown++;
            int lod = d < NearM ? 0 : 1;
            if (lod != r.lodLevel)
            {
                r.lodLevel = lod;
                if (r.rig != null) { r.rig.solveEveryNFrames = lod == 0 ? 1 : 6; r.rig.solvePhase = i % 6; }
            }
            Seg(Mathf.Clamp(r.sim.posM, 0f, _lengthM) * 0.001f, out int a, out float t);
            Vector3 c = Vector3.Lerp(_pos[a], _pos[a + 1], t);
            Vector3 fwd = _pos[a + 1] - _pos[a];
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 side = new Vector3(fwd.z, 0f, -fwd.x);
            r.tf.SetPositionAndRotation(c + side * r.lateral + Vector3.up * 0.05f, Quaternion.LookRotation(fwd, Vector3.up));
        }
        ActiveRiders = shown;
    }

    void PublishStandings()
    {
        float leadM = _posM[_order[0]];
        for (int i = 0; i <= AiCount; i++)
        {
            int id = _order[i];
            bool player = id == AiCount;
            if (player) PlayerPlace = i + 1;
            _standings[i] = new ShuntaStanding
            {
                place = i + 1,
                name = player ? "You" : _r[id].sim.name,
                archetype = player ? ShuntaArchetype.Rouleur : _r[id].sim.profile.archetype,
                isPlayer = player,
                km = Mathf.Max(0f, _posM[id]) * 0.001f,
                gapS = (leadM - _posM[id]) / Mathf.Max(3f, _speed[id]),
                gapToPlayerM = _posM[id] - _playerPosM
            };
        }
    }
}
