using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Layered POSITIONAL ambience for Nagisa Bay (brief section 14). Nothing here is a global bed: every sound is a 3D
/// AudioSource placed in the world and culled by distance from the listener.
///
///  * Landmark loops   (marina water, rigging, engines, cafes, plazas, beach crowds, bike parks) at fixed places
///                     along the route; only the nearest <see cref="maxLoops"/> are ever audible/playing.
///  * Shore sources    surf, distant breakers and seawall hits sit on the real shoreline nearest the rider
///                     (baked table NagisaAudioMap.ShoreX/Z, every 50 m of route), so the ocean is always in the
///                     direction of the water and gets quieter as the road climbs away from it. If the NagisaSurf
///                     wave field is loaded, surf loudness follows BreakAt() at the source.
///  * Route beds       wind, summer cicadas and far traffic hug the road beside the rider with zone-driven levels.
///  * Pass-bys         pooled moving sources: oncoming/overtaking cyclists (freehub, drivetrain, tyres, groups)
///                     and traffic (kei/luxury cars, scooters, rare buses) with Doppler.
///  * One-shots        gulls, terns, ship/yacht horns, children and laughter, bike bells and clacks, spawned
///                     around landmarks on random timers.
///
/// Budget: one tick every 0.2 s; at most maxLoops (12) loops, maxOneShots (6) one-shots and maxPassers (4)
/// pass-bys sound at once; everything is pooled, no per-frame allocation. Clips are mono 32 kHz from
/// tools/audio/build_nagisa_ambience.py (Assets/Resources/Audio/Nagisa/Amb).
/// </summary>
[DisallowMultipleComponent]
public sealed class NagisaAmbienceDirector : MonoBehaviour
{
    public const string Folder = "Audio/Nagisa/Amb/";
    [Range(0f, 1.5f)] public float master = 1f;
    public int maxLoops = 12, maxOneShots = 6, maxPassers = 4;

    private INagisaAudioContext _ctx;
    private RouteCourse _course;
    private float _acc;
    private float _gate = 1f;
    private Vector3 _lp;
    private float _d;

    // ------------------------------------------------------------------------------------- data tables
    private struct LoopDef
    {
        public string clip; public float d, lat, up, vol, minDist, radius;
        public LoopDef(string clip, float d, float lat, float up, float vol, float minDist, float radius)
        { this.clip = clip; this.d = d; this.lat = lat; this.up = up; this.vol = vol; this.minDist = minDist; this.radius = radius; }
    }

    // Distances are metres along the Nagisa course; lat is metres to the rider's right (+, the sea side on the
    // beach/marina stretch) or left (-, the town side). Pad/landmark distances come from NagisaRoute.json.
    private static readonly LoopDef[] LoopDefs =
    {
        // marina (route start): calm water, working harbour engines, rigging, waterfront cafe
        new LoopDef("marina_lap", 130, 70, .4f, .80f, 8, 190), new LoopDef("marina_lap", 330, 62, .4f, .80f, 8, 190),
        new LoopDef("marina_lap", 520, 46, .4f, .85f, 8, 190), new LoopDef("marina_lap", 720, 62, .4f, .80f, 8, 190),
        new LoopDef("rigging", 280, 55, 6, .70f, 10, 190), new LoopDef("rigging", 480, 50, 6, .70f, 10, 190),
        new LoopDef("rigging", 690, 56, 6, .70f, 10, 190),
        new LoopDef("marina_engine", 90, 66, .8f, .55f, 8, 170), new LoopDef("marina_outboard", 420, 78, .5f, .40f, 8, 150),
        new LoopDef("marina_engine", 610, 72, .8f, .35f, 8, 140),
        new LoopDef("cafe_loop", 492, 22, 1.5f, .70f, 8, 130), new LoopDef("plaza_loop", 412, -60, 1.5f, .55f, 8, 140),
        new LoopDef("bike_park_loop", 60, -8, 1f, .50f, 6, 100),
        // town centre (town side = left)
        new LoopDef("plaza_loop", 1068, -80, 1.5f, .55f, 8, 150), new LoopDef("cafe_loop", 1180, -45, 1.5f, .60f, 8, 130),
        new LoopDef("cafe_loop", 1300, -50, 1.5f, .60f, 8, 130), new LoopDef("bike_park_loop", 1500, -35, 1f, .55f, 6, 100),
        new LoopDef("plaza_loop", 1660, -90, 1.5f, .55f, 8, 150), new LoopDef("cafe_loop", 1860, -40, 1.5f, .60f, 8, 130),
        new LoopDef("beach_crowd", 1980, 75, 1.2f, .60f, 10, 220), new LoopDef("cafe_loop", 2080, -35, 1.5f, .55f, 8, 130),
        // promenade + beach resort: crowds on the sand, cafes behind the promenade, resort plaza, cycling cafe
        new LoopDef("cafe_loop", 2500, -30, 1.5f, .60f, 8, 130), new LoopDef("cafe_loop", 2684, -70, 1.5f, .55f, 8, 130),
        new LoopDef("beach_crowd", 2300, 60, 1.2f, .75f, 10, 230), new LoopDef("beach_crowd", 2650, 55, 1.2f, .75f, 10, 230),
        new LoopDef("beach_crowd", 2900, 70, 1.2f, .75f, 10, 230), new LoopDef("beach_crowd", 3200, 80, 1.2f, .80f, 10, 240),
        new LoopDef("beach_crowd", 3550, 85, 1.2f, .80f, 10, 240), new LoopDef("beach_crowd", 3900, 70, 1.2f, .75f, 10, 230),
        new LoopDef("beach_crowd", 4200, 65, 1.2f, .70f, 10, 220),
        new LoopDef("plaza_loop", 3368, 60, 1.5f, .60f, 8, 170),
        new LoopDef("bike_park_loop", 3992, 40, 1f, .75f, 6, 120), new LoopDef("cafe_loop", 3992, 54, 1.5f, .60f, 8, 130),
        // climb foot meeting point and the overlook kiosk + bike racks
        new LoopDef("bike_park_loop", 4640, 8, 1f, .55f, 6, 100),
        new LoopDef("cafe_loop", 8251, -12, 1.5f, .50f, 6, 100), new LoopDef("bike_park_loop", 8251, 10, 1f, .55f, 6, 100),
    };

    private struct ShotDef
    {
        public string[] clips; public float d, lat, up, vol, minDist, radius, minGap, maxGap, ringMin, ringMax, pitchLo, pitchHi;
    }

    private static ShotDef Shot(string[] clips, float d, float lat, float up, float vol, float radius, float minGap, float maxGap,
                                float minDist = 12f, float ring = 18f, float pLo = .95f, float pHi = 1.05f)
        => new ShotDef { clips = clips, d = d, lat = lat, up = up, vol = vol, minDist = minDist, radius = radius, minGap = minGap, maxGap = maxGap, ringMin = 4f, ringMax = ring, pitchLo = pLo, pitchHi = pHi };

    private static readonly string[] Gulls = { "gull_cry", "gull_short", "seabird_tern" };
    private static readonly string[] HighBirds = { "seabird_distant", "seabird_tern" };
    private static readonly string[] Crowd = { "laugh_0", "laugh_1", "laugh_2", "child_shout" };
    private static readonly string[] Horn = { "ship_horn" };
    private static readonly string[] Toot = { "yacht_toot" };
    private static readonly string[] Bell = { "bike_bell" };
    private static readonly string[] Clack = { "bike_clack_0", "bike_clack_1", "bike_clack_2" };

    private static readonly ShotDef[] ShotDefs =
    {
        Shot(Gulls, 200, 50, 9, .85f, 230, 7, 17), Shot(Gulls, 450, 55, 9, .85f, 230, 6, 15), Shot(Gulls, 800, 50, 9, .8f, 230, 8, 18),
        Shot(Horn, 480, 95, 6, .9f, 800, 70, 140, 60, 25, .97f, 1.0f), Shot(Toot, 640, 85, 6, .75f, 450, 55, 110, 40, 20),
        Shot(Gulls, 2400, 50, 10, .8f, 250, 9, 22), Shot(Gulls, 3300, 55, 10, .8f, 250, 9, 22), Shot(Gulls, 4100, 50, 10, .8f, 250, 10, 24),
        Shot(Crowd, 2400, 60, 1.5f, .85f, 170, 8, 20, 6, 25), Shot(Crowd, 2900, 70, 1.5f, .85f, 170, 8, 20, 6, 25),
        Shot(Crowd, 3300, 75, 1.5f, .85f, 170, 7, 18, 6, 25), Shot(Crowd, 3800, 68, 1.5f, .85f, 170, 8, 20, 6, 25),
        Shot(Bell, 1700, 2, 1.2f, .7f, 110, 14, 30, 6, 12), Shot(Bell, 2200, 2, 1.2f, .7f, 110, 14, 30, 6, 12),
        Shot(Clack, 3992, 44, 1f, .75f, 70, 3, 9, 4, 8), Shot(Clack, 4640, 8, 1f, .7f, 70, 4, 11, 4, 8), Shot(Clack, 8251, 10, 1f, .7f, 70, 4, 11, 4, 8),
        Shot(HighBirds, 5500, 70, 20, .7f, 320, 10, 26, 30, 40), Shot(HighBirds, 6500, 75, 24, .7f, 320, 10, 26, 30, 40),
        Shot(HighBirds, 7500, 70, 22, .7f, 320, 10, 26, 30, 40), Shot(HighBirds, 8251, 80, 26, .75f, 320, 9, 22, 30, 40),
        Shot(Gulls, 12500, -70, 14, .75f, 320, 10, 24, 20, 40), Shot(Gulls, 13800, -70, 14, .75f, 320, 10, 24, 20, 40),
        Shot(Gulls, 15400, 40, 14, .8f, 320, 8, 20, 20, 40), Shot(HighBirds, 15800, -40, 16, .75f, 320, 9, 22, 20, 40),
    };

    // Beds that follow the rider's stretch of road: levels are route-distance keyframes (d, v pairs).
    private const float Mar = NagisaAudioMap.Zone_marinaEnd, Bch = NagisaAudioMap.Zone_beachStart, BchE = NagisaAudioMap.Zone_beachEnd,
        Clb = NagisaAudioMap.Zone_climbStart, Kom = NagisaAudioMap.Zone_komM, Rdg = NagisaAudioMap.Zone_ridgeEnd,
        Cst = NagisaAudioMap.Zone_coastStart, Brg = NagisaAudioMap.Zone_bridgeStart, Fin = NagisaAudioMap.Zone_finish;

    private static readonly float[] WindCurve = { 0, .30f, Bch, .45f, BchE, .5f, Clb, .7f, Kom - 400f, .75f, Kom - 80f, .2f, Rdg, .45f, Cst, .55f, Brg - 200f, .35f, Brg + 200f, 0f, Fin, 0f };
    private static readonly float[] RidgeWindCurve = { 0, 0, Kom - 700f, 0, Kom - 100f, .8f, Kom + 700f, .9f, Rdg - 500f, .6f, Rdg + 200f, .2f, Cst, 0 };
    private static readonly float[] SeaWindCurve = { 0, 0, Cst + 400f, 0, Brg - 300f, .4f, Brg + 200f, .85f, Fin, 1f };
    private static readonly float[] CicadaCurve = { 0, 0, Bch + 600f, 0, BchE, .18f, Clb - 100f, .35f, Clb + 400f, .85f, Rdg, .85f, Cst, .5f, Cst + 700f, .12f, Brg - 700f, 0f, Fin, 0f };
    private static readonly float[] TrafficCurve = { 0, .45f, BchE, .6f, Clb - 80f, .35f, Clb + 300f, 0f, Cst - 250f, 0f, Cst + 250f, .5f, Brg + 100f, .6f, Fin, .5f };

    // Shore-source zone curves (surf is muted in the sheltered marina and rises along the beach).
    private static readonly float[] SurfCurve = { 0, 0f, Mar - 150f, 0f, Mar + 220f, .6f, Bch, 1f, BchE, 1f, Clb, .5f, Clb + 400f, 0f, Cst - 100f, 0f, Cst + 200f, .55f, Brg + 100f, .6f, Fin, .3f };
    private static readonly float[] FarCurve = { 0, .55f, BchE, .9f, Clb, 1f, Rdg, .9f, Cst, .8f, Fin, .8f };
    private static readonly float[] WallCurve = { 0, 0f, Mar - 100f, 0f, Mar + 250f, .7f, Bch, 1f, BchE, 1f, BchE + 250f, 0f, Fin, 0f };

    // Cyclist / traffic pass-by density in events per minute along the route.
    private static readonly float[] RiderRate = { 0, 3.5f, Mar, 5f, Bch, 8.5f, BchE, 9f, Clb, 4f, Kom - 200f, 3f, Kom + 200f, 2f, Kom + 900f, 6f, Rdg, 7f, Cst, 8f, Brg, 6.5f, Fin, 6f };
    private static readonly float[] CarRate = { 0, 6f, Bch, 7f, BchE, 7.5f, Clb - 120f, 2f, Clb + 200f, 0f, Cst - 150f, 0f, Cst + 200f, 6.5f, Brg + 200f, 6f, Fin, 4f };
    private static readonly float[] PelotonShare = { 0, .05f, Mar, .12f, Bch, .22f, BchE, .22f, Clb, .1f, Kom, .05f, Rdg, .15f, Cst, .22f, Fin, .12f };

    // ------------------------------------------------------------------------------------- runtime state
    private sealed class Loop
    {
        public LoopDef def; public Vector3 pos; public AudioSource src; public AudioClip clip;
        public float cur, want, score; public bool on;
    }

    private sealed class Bed
    {
        public string clip; public float[] curve; public float lat, up, vol, minDist, pitch;
        public AudioSource src; public float cur; public bool on;
    }

    private sealed class ShotState { public ShotDef def; public Vector3 anchor; public float timer; }

    private sealed class Passer
    {
        public AudioSource src; public bool active; public float pos, dir, speed, lat, life, t; public bool isVehicle;
    }

    private readonly List<Loop> _loops = new List<Loop>();
    private readonly List<Bed> _beds = new List<Bed>();
    private readonly List<ShotState> _shots = new List<ShotState>();
    private readonly List<AudioSource> _oneShotPool = new List<AudioSource>();
    private readonly List<Passer> _passers = new List<Passer>();
    private readonly Queue<string> _preload = new Queue<string>();
    private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
    private static readonly System.Comparison<Loop> ByScore = (a, b) => b.score.CompareTo(a.score);

    // shore sources
    private AudioSource _surf, _far;
    private float _surfCur, _farCur, _wallTimer = 3f; private bool _surfOn, _farOn;
    private Vector3 _surfPos, _farPos; private bool _surfPlaced, _farPlaced;
    private float _break;
    private float _nextRider = 4f, _nextCar = 6f;
    private int _activeLoops, _activeBeds, _activeShots, _activePassers;
    private bool _shoreOk;

    public int ActiveLoops => _activeLoops;
    public int ActivePassers => _activePassers;
    public int ActiveOneShots => _activeShots;
    public int LoopCount => _loops.Count;

    // ------------------------------------------------------------------------------------- setup
    public void Init(INagisaAudioContext ctx)
    {
        _ctx = ctx; _course = ctx.Course;
        float L = _course != null ? _course.Length : 0f;
        foreach (var def in LoopDefs)
        {
            if (def.d > L) continue;
            _loops.Add(new Loop { def = def, pos = Place(def.d, def.lat, def.up) });
        }
        foreach (var def in ShotDefs)
        {
            if (def.d > L) continue;
            _shots.Add(new ShotState { def = def, anchor = Place(def.d, def.lat, def.up), timer = Random.Range(def.minGap * 0.3f, def.maxGap) });
        }
        // Rider-hugging beds (two cicada voices left/right so they pan, wind on the sea side, traffic inland).
        _beds.Add(new Bed { clip = "wind_loop", curve = WindCurve, lat = 14, up = 1.5f, vol = .55f, minDist = 8, pitch = 1f });
        _beds.Add(new Bed { clip = "wind_ridge", curve = RidgeWindCurve, lat = -10, up = 2f, vol = .7f, minDist = 8, pitch = 1f });
        _beds.Add(new Bed { clip = "wind_sea", curve = SeaWindCurve, lat = 12, up = 1f, vol = .7f, minDist = 8, pitch = 1f });
        _beds.Add(new Bed { clip = "cicadas", curve = CicadaCurve, lat = 16, up = 4, vol = .6f, minDist = 10, pitch = 1f });
        _beds.Add(new Bed { clip = "cicadas", curve = CicadaCurve, lat = -18, up = 5, vol = .55f, minDist = 10, pitch = 1.07f });
        _beds.Add(new Bed { clip = "traffic_far", curve = TrafficCurve, lat = -45, up = 1, vol = .9f, minDist = 25, pitch = 1f });

        _surf = NewSource("Surf", 16f, 2f); _far = NewSource("DistantBreakers", 90f, 2f);
        for (int i = 0; i < maxOneShots; i++) _oneShotPool.Add(NewSource("Shot" + i, 10f, 0f));
        for (int i = 0; i < maxPassers; i++)
        {
            var s = NewSource("Pass" + i, 6f, 0.7f); s.loop = false;
            _passers.Add(new Passer { src = s });
        }
        _lp = ctx.ListenerPos; _d = ctx.RouteD;
        foreach (var n in AllClipNames()) _preload.Enqueue(n);
    }

    private Vector3 Place(float d, float lat, float up)
    {
        if (_course == null || _course.Count < 2) return Vector3.zero;
        return _course.PositionAt(d) + _course.SideAt(d) * lat + Vector3.up * up;
    }

    private AudioSource NewSource(string name, float minDist, float doppler)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false; s.loop = true; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic;
        s.minDistance = minDist; s.maxDistance = 1500f; s.dopplerLevel = doppler; s.spread = 20f; s.priority = 150;
        s.volume = 0f; s.bypassReverbZones = true;
        go.SetActive(false);
        return s;
    }

    private AudioClip Clip(string name)
    {
        if (_clips.TryGetValue(name, out var c)) return c;
        c = Resources.Load<AudioClip>(Folder + name);
        if (c == null) Debug.LogWarning($"[nagisa-audio] missing ambience clip {name}");
        _clips[name] = c;
        return c;
    }

    private static void StartLoop(AudioSource s, AudioClip c, float pitch)
    {
        s.clip = c; s.pitch = pitch; s.volume = 0f;
        s.gameObject.SetActive(true);
        s.timeSamples = Random.Range(0, Mathf.Max(1, c.samples - 1));
        s.Play();
    }

    private static void StopLoop(AudioSource s)
    {
        s.Stop(); s.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------------------------- update
    private void Update() => Frame(Time.unscaledDeltaTime);

    /// <summary>One full frame: gate, 5 Hz decisions, smoothing, pass-by motion. Also driven by the editor self-test.</summary>
    public void Frame(float dt)
    {
        if (_ctx == null) return;
        _gate = Mathf.MoveTowards(_gate, _ctx.Gate, dt / 0.5f);
        _lp = _ctx.ListenerPos; _d = _ctx.RouteD;
        if (_course == null) { _course = _ctx.Course; if (_course == null) return; }

        _acc += dt;
        if (_acc >= 0.2f) { float step = _acc; _acc = 0f; Decide(step); }
        Smooth(dt);
        UpdatePassers(dt);
    }

    /// <summary>The 5 Hz brain: choose which loops/beds/shore sources should sound and schedule events.</summary>
    private void Decide(float step)
    {
        float g = _gate * master;
        bool quiet = g < 0.02f;
        if (_preload.Count > 0) Clip(_preload.Dequeue());      // stagger clip loading (one per tick) so nothing hitches mid-ride

        // 1) landmark loops: rank by how audible they would be, keep the best maxLoops.
        for (int i = 0; i < _loops.Count; i++)
        {
            var l = _loops[i];
            float dist = Vector3.Distance(_lp, l.pos);
            float gain = NagisaAudioMath.DistGain(dist, l.def.radius);
            l.score = gain * l.def.vol / Mathf.Max(1f, dist / Mathf.Max(1f, l.def.minDist));
            l.want = gain > 0.001f ? l.def.vol * gain : 0f;
        }
        _loops.Sort(ByScore);
        int active = 0;
        for (int i = 0; i < _loops.Count; i++)
        {
            var l = _loops[i];
            if (l.want > 0f && active < maxLoops && !quiet) active++;
            else l.want = 0f;
        }
        _activeLoops = active;

        // 2) rider-hugging beds
        int bedsOn = 0;
        for (int i = 0; i < _beds.Count; i++) if (_beds[i].cur > 0.001f || BedWanted(_beds[i]) > 0.001f) bedsOn++;
        _activeBeds = bedsOn;

        // 3) shore sources (ocean is placed on the real shoreline nearest the rider)
        DecideShore(step, quiet);

        // 4) one-shots (gulls, horns, laughter, bells ...)
        _activeShots = 0;
        for (int i = 0; i < _oneShotPool.Count; i++) if (_oneShotPool[i].isPlaying) _activeShots++;
        if (!quiet)
        {
            for (int i = 0; i < _shots.Count; i++)
            {
                var s = _shots[i];
                if (Vector3.Distance(_lp, s.anchor) > s.def.radius) { s.timer = Mathf.Max(s.timer, 1f); continue; }
                s.timer -= step;
                if (s.timer > 0f) continue;
                s.timer = Random.Range(s.def.minGap, s.def.maxGap);
                FireShot(s);
            }
        }

        // 5) pass-bys (moving sources)
        if (!quiet) SchedulePassers(step);
    }

    private float BedWanted(Bed b) => NagisaAudioMath.Curve(b.curve, _d) * b.vol;

    private void DecideShore(float step, bool quiet)
    {
        _shoreOk = NearestShore(_lp, _d, 2800f, out int idx, out float dist, out Vector3 shorePos);
        if (!_shoreOk || quiet) { _surfWant = 0f; _farWant = 0f; return; }
        // surf: a loop parked on the shoreline point; only audible within ~340 m (distant breakers carry ~2.3 km)
        _surfWant = NagisaAudioMath.Curve(SurfCurve, _d) * NagisaAudioMath.DistGain(dist, 340f) * 1.0f;
        _farWant = NagisaAudioMath.Curve(FarCurve, _d) * NagisaAudioMath.DistGain(dist, 2300f) * .8f;
        if (!_surfPlaced) { _surfPos = shorePos; _surfPlaced = true; }
        if (!_farPlaced) { _farPos = shorePos; _farPlaced = true; }
        _surfTarget = shorePos;
        // distant breakers sit a little further out to sea
        Vector3 toSea = shorePos - _course.PositionAt(NagisaAudioMap.StepM * idx);
        toSea.y = 0f; toSea = toSea.sqrMagnitude > 1f ? toSea.normalized : Vector3.zero;
        _farTarget = shorePos + toSea * 140f;

        // surf loudness follows the wave field when it is loaded
        if (NagisaSurf.HasField)
        {
            float br = NagisaSurf.BreakAt(_surfPos.x, _surfPos.z, Time.time, out _);
            _break = Mathf.Lerp(_break, br, 0.25f);
        }

        // seawall hits: random thumps on the nearest wall when the sea is close
        _wallTimer -= step;
        float wallWant = NagisaAudioMath.Curve(WallCurve, _d) * NagisaAudioMath.DistGain(dist, 150f);
        if (_wallTimer <= 0f)
        {
            _wallTimer = Random.Range(3.5f, 9f);
            if (wallWant > 0.05f)
            {
                var src = FreeShot();
                var c = Clip("seawall_hit_" + Random.Range(0, 4));
                if (src != null && c != null)
                {
                    Vector3 tan = _course.TangentAt(_d); tan.y = 0f;
                    src.transform.position = shorePos + tan.normalized * Random.Range(-25f, 25f) + Vector3.up * 0.4f;
                    FireOnce(src, c, (0.55f + 0.45f * _break) * wallWant, Random.Range(0.92f, 1.06f), 12f);
                }
            }
        }
    }

    private float _surfWant, _farWant; private Vector3 _surfTarget, _farTarget;

    private bool NearestShore(Vector3 lp, float d, float window, out int index, out float dist, out Vector3 pos)
    {
        index = 0; dist = float.MaxValue; pos = Vector3.zero;
        var sx = NagisaAudioMap.ShoreX; var sz = NagisaAudioMap.ShoreZ;
        int i0 = Mathf.Max(0, Mathf.FloorToInt((d - window) / NagisaAudioMap.StepM));
        int i1 = Mathf.Min(sx.Length - 1, Mathf.CeilToInt((d + window) / NagisaAudioMap.StepM));
        float best = float.MaxValue;
        for (int i = i0; i <= i1; i++)
        {
            float dx = sx[i] - lp.x, dz = sz[i] - lp.z;
            float q = dx * dx + dz * dz;
            if (q < best) { best = q; index = i; }
        }
        if (best == float.MaxValue) return false;
        dist = Mathf.Sqrt(best);
        pos = new Vector3(sx[index], NagisaAudioMap.SeaLevelY + 0.4f, sz[index]);
        return true;
    }

    // ------------------------------------------------------------------------------------- one-shots
    private AudioSource FreeShot()
    {
        for (int i = 0; i < _oneShotPool.Count; i++)
            if (!_oneShotPool[i].isPlaying) return _oneShotPool[i];
        return null;
    }

    private void FireOnce(AudioSource s, AudioClip c, float vol, float pitch, float minDist)
    {
        s.gameObject.SetActive(true);
        s.loop = false; s.clip = c; s.pitch = pitch; s.minDistance = minDist;
        s.volume = Mathf.Clamp01(vol * _gate * master);
        s.Play();
    }

    private void FireShot(ShotState st)
    {
        var src = FreeShot(); if (src == null) return;
        var c = Clip(st.def.clips[Random.Range(0, st.def.clips.Length)]);
        if (c == null) return;
        Vector2 r = Random.insideUnitCircle.normalized * Random.Range(st.def.ringMin, st.def.ringMax);
        src.transform.position = st.anchor + new Vector3(r.x, Random.Range(-0.5f, 2.5f), r.y);
        float dist = Vector3.Distance(_lp, st.anchor);
        FireOnce(src, c, st.def.vol * NagisaAudioMath.DistGain(dist, st.def.radius) + 0.02f, Random.Range(st.def.pitchLo, st.def.pitchHi), st.def.minDist);
    }

    // ------------------------------------------------------------------------------------- pass-bys
    private void SchedulePassers(float step)
    {
        _nextRider -= step; _nextCar -= step;
        if (_nextRider <= 0f)
        {
            float rate = NagisaAudioMath.Curve(RiderRate, _d);
            _nextRider = rate < 0.3f ? 5f : 60f / rate * Random.Range(0.6f, 1.5f);
            if (rate >= 0.3f) SpawnRider();
        }
        if (_nextCar <= 0f)
        {
            float rate = NagisaAudioMath.Curve(CarRate, _d);
            _nextCar = rate < 0.3f ? 5f : 60f / rate * Random.Range(0.6f, 1.5f);
            if (rate >= 0.3f) SpawnVehicle();
        }
    }

    private Passer FreePasser()
    {
        for (int i = 0; i < _passers.Count; i++) if (!_passers[i].active) return _passers[i];
        return null;
    }

    private void SpawnRider()
    {
        var p = FreePasser(); if (p == null) return;
        float roll = Random.value, peloton = NagisaAudioMath.Curve(PelotonShare, _d);
        string name = roll < peloton ? "peloton_pass" : (roll < peloton + 0.45f ? "freehub_pass_a" : "drivetrain_pass");
        var c = Clip(name); if (c == null) return;
        float mine = _ctx.SpeedMps, sp = Random.Range(6f, 11.5f);
        bool overtake = mine > 2f && sp > mine + 3f && Random.value < 0.35f;
        float rel = overtake ? sp - mine : sp + mine;
        float half = Mathf.Clamp(0.5f * c.length * rel, 25f, 160f);
        Launch(p, c, overtake ? _d - half : _d + half, overtake ? 1f : -1f, sp, overtake ? -2.4f : 3.2f, c.length + 0.5f, false, 0.75f, 5f);
    }

    private void SpawnVehicle()
    {
        var p = FreePasser(); if (p == null) return;
        float r = Random.value;
        string name = r < .42f ? "veh_car" : r < .60f ? "veh_luxury" : r < .88f ? "veh_scooter" : (Random.value < .35f ? "veh_bus" : "veh_car");
        var c = Clip(name); if (c == null) return;
        float mine = _ctx.SpeedMps, sp = Random.Range(11f, 20f);
        bool overtake = Random.value < 0.4f;
        float rel = overtake ? Mathf.Max(4f, sp - mine) : sp + mine;
        float half = Mathf.Clamp(0.5f * c.length * rel, 35f, 190f);
        float inland = _d > NagisaAudioMap.Zone_coastStart ? 1f : -1f;      // the town/hill side of the road
        Launch(p, c, overtake ? _d - half : _d + half, overtake ? 1f : -1f, sp, inland * (overtake ? 9f : 12f), c.length + 0.5f, true, name == "veh_bus" ? 0.8f : 0.7f, 7f);
    }

    private void Launch(Passer p, AudioClip c, float pos, float dir, float speed, float lat, float life, bool vehicle, float vol, float minDist)
    {
        p.active = true; p.pos = pos; p.dir = dir; p.speed = speed; p.lat = lat; p.life = life; p.t = 0f; p.isVehicle = vehicle;
        var s = p.src;
        s.gameObject.SetActive(true);
        s.transform.position = _course.PositionAt(pos) + _course.SideAt(pos) * lat + Vector3.up * 0.8f;
        s.loop = false; s.clip = c; s.pitch = 1f; s.minDistance = minDist; s.volume = Mathf.Clamp01(vol * _gate * master);
        s.Play();
    }

    private void UpdatePassers(float dt)
    {
        int n = 0;
        for (int i = 0; i < _passers.Count; i++)
        {
            var p = _passers[i];
            if (!p.active) continue;
            n++;
            p.t += dt; p.pos += p.dir * p.speed * dt;
            p.src.transform.position = _course.PositionAt(p.pos) + _course.SideAt(p.pos) * p.lat + Vector3.up * 0.8f;
            if (p.t > p.life)
            {
                p.active = false; p.src.Stop(); p.src.gameObject.SetActive(false); n--;
            }
        }
        _activePassers = n;
    }

    // ------------------------------------------------------------------------------------- smoothing
    private void Smooth(float dt)
    {
        float g = _gate * master;
        float fade = dt / 1.2f;

        for (int i = 0; i < _loops.Count; i++)
        {
            var l = _loops[i];
            float target = l.want * g;
            if (l.src == null)
            {
                if (target < 0.002f) continue;
                var c = Clip(l.def.clip); if (c == null) { l.want = 0f; continue; }
                l.clip = c; l.src = NewSource("Loop " + l.def.clip, l.def.minDist, 0f);
                l.src.transform.position = l.pos;
            }
            if (!l.on && target >= 0.002f && l.clip != null) { StartLoop(l.src, l.clip, Random.Range(0.97f, 1.03f)); l.cur = 0f; l.on = true; }
            if (!l.on) continue;
            l.cur = Mathf.MoveTowards(l.cur, target, fade);
            l.src.volume = l.cur;
            if (l.cur <= 0.0005f && target <= 0.0005f) { StopLoop(l.src); l.on = false; }
        }

        // beds follow the rider along the road
        for (int i = 0; i < _beds.Count; i++)
        {
            var b = _beds[i];
            float target = BedWanted(b) * g;
            if (b.src == null)
            {
                if (target < 0.002f) continue;
                var c = Clip(b.clip); if (c == null) { b.vol = 0f; continue; }
                b.src = NewSource("Bed " + b.clip, b.minDist, 0f); b.src.clip = c;
            }
            if (!b.on && target >= 0.002f) { StartLoop(b.src, b.src.clip, b.pitch); b.cur = 0f; b.on = true; }
            if (!b.on) continue;
            b.cur = Mathf.MoveTowards(b.cur, target, fade);
            b.src.volume = b.cur;
            b.src.transform.position = Place(_d, b.lat, b.up);
            if (b.cur <= 0.0005f && target <= 0.0005f) { StopLoop(b.src); b.on = false; }
        }

        // shore sources glide along the coast instead of jumping between table points
        SmoothShore(_surf, ref _surfOn, ref _surfCur, ref _surfPos, _surfTarget, _surfWant * Mathf.Lerp(0.7f, 1.15f, _break) * 0.95f, "surf_loop", g, dt);
        SmoothShore(_far, ref _farOn, ref _farCur, ref _farPos, _farTarget, _farWant * 0.85f, "distant_breakers", g, dt);
    }

    private void SmoothShore(AudioSource s, ref bool on, ref float cur, ref Vector3 pos, Vector3 target, float want, string clip, float g, float dt)
    {
        float tv = want * g;
        if (!on && tv >= 0.002f)
        {
            var c = Clip(clip); if (c == null) return;
            s.transform.position = pos; StartLoop(s, c, 1f); cur = 0f; on = true;
        }
        if (!on) return;
        pos = Vector3.MoveTowards(pos, target, 90f * dt);
        s.transform.position = pos;
        cur = Mathf.MoveTowards(cur, tv, dt / 1.2f);
        s.volume = cur;
        if (cur <= 0.0005f && tv <= 0.0005f) { StopLoop(s); on = false; }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _loops.Count; i++) _loops[i].clip = null;
        _clips.Clear();
    }

    /// <summary>Which rider-hugging beds are wanted right now (for tests/logs).</summary>
    public string BedReport()
    {
        var sb = new StringBuilder();
        foreach (var b in _beds) { float w = BedWanted(b); if (w > 0.01f) sb.Append(b.clip).Append('=').Append(w.ToString("F2")).Append(' '); }
        return sb.ToString().TrimEnd();
    }

    public string Describe()
    {
        var sb = new StringBuilder("amb");
        sb.Append(" loops=").Append(_activeLoops).Append('/').Append(_loops.Count);
        sb.Append(" beds=").Append(_activeBeds).Append(" shots=").Append(_activeShots).Append(" pass=").Append(_activePassers);
        sb.Append(" surf=").Append(_surfCur.ToString("F2")).Append(" far=").Append(_farCur.ToString("F2"));
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------- test hooks
    /// <summary>Drives one decision tick without Unity's Update (editor self-test).</summary>
    public void TestTick(float dt)
    {
        _lp = _ctx.ListenerPos; _d = _ctx.RouteD; _gate = _ctx.Gate;
        Decide(dt);
    }

    /// <summary>Number of landmark loops currently wanted (score-ranked, capped at maxLoops).</summary>
    public int WantedLoops { get { int n = 0; for (int i = 0; i < _loops.Count; i++) if (_loops[i].want > 0f) n++; return n; } }
    public float SurfWant => _surfWant;
    public float FarWant => _farWant;
    public bool ShoreFound => _shoreOk;
    public static IEnumerable<string> AllClipNames()
    {
        var set = new HashSet<string>();
        foreach (var d in LoopDefs) set.Add(d.clip);
        foreach (var s in ShotDefs) foreach (var c in s.clips) set.Add(c);
        foreach (var n in new[] { "wind_loop", "wind_ridge", "wind_sea", "cicadas", "traffic_far", "surf_loop", "distant_breakers",
                                  "seawall_hit_0", "seawall_hit_1", "seawall_hit_2", "seawall_hit_3",
                                  "freehub_pass_a", "drivetrain_pass", "peloton_pass", "veh_car", "veh_luxury", "veh_scooter", "veh_bus" })
            set.Add(n);
        return set;
    }
}
