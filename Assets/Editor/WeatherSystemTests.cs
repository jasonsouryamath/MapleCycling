using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Headless weather-system tests (same style as <see cref="WeatherMathValidation"/>).
///   -executeMethod WeatherSystemTests.Run                 pure / edit-mode suite (C4, no scene)
///   -executeMethod WeatherSystemTests.RoadWetnessScene    scene check for wet roads (C1)
///   -executeMethod WeatherSystemTests.CurtainRenderProbe  HDRP render probe for rain curtains (C2)
/// Prints "[weather-sys] &lt;suite&gt; PASS n/n" or each FAIL, and exits non-zero on failure.
/// </summary>
public static class WeatherSystemTests
{
    private static int _pass, _fail;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Debug.Log($"[weather-sys] ok   {name} {detail}"); }
        else { _fail++; Debug.LogError($"[weather-sys] FAIL {name} {detail}"); }
    }

    private static void Finish(string suite)
    {
        Debug.Log($"[weather-sys] {suite} {(_fail == 0 ? "PASS" : "FAIL")} {_pass}/{_pass + _fail}");
        if (Application.isBatchMode) EditorApplication.Exit(_fail == 0 ? 0 : 1);
    }

    // Reflection helpers: the runtime classes keep their state private-set and their Unity
    // messages private, which is right for gameplay; tests drive them explicitly.
    private static void SetProp(object target, string name, object value, System.Type type = null)
    {
        var t = type ?? target.GetType();
        var p = t.GetProperty(name, Any);
        if (p == null) throw new MissingMemberException(t.Name, name);
        p.SetValue(target, value);
    }

    private static object Call(object target, string name, params object[] args)
    {
        var t = target as System.Type ?? target.GetType();
        var m = t.GetMethod(name, Any);
        if (m == null) throw new MissingMethodException(t.Name, name);
        return m.Invoke(target is System.Type ? null : target, args);
    }

    /// <summary>Pure / edit-mode suite. No scene is opened.</summary>
    public static void Run()
    {
        _pass = _fail = 0;
        var temp = new List<Object>();
        try
        {
            EchelonShift(temp);
            TerrainExposure(temp);
            WindVolumeBlending(temp);
            PresetTransitions(temp);
            WetnessDynamics();
            AtmosphereMaths();
        }
        catch (System.Exception e)
        {
            Check("suite ran without exception", false, e.ToString());
        }
        finally
        {
            SetProp(null, "Instance", null, typeof(WeatherDirector));
            foreach (var o in temp) if (o != null) Object.DestroyImmediate(o);
        }
        Finish("system");
    }

    // ------------------------------------------------------------------ echelon

    private static void EchelonShift(List<Object> temp)
    {
        var go = new GameObject("~wx echelon"); temp.Add(go);
        var wd = go.AddComponent<WeatherDirector>();
        SetProp(null, "Instance", wd, typeof(WeatherDirector));
        float Shift(float gap) => (float)Call(typeof(ShiosaiTrafficDirector), "EchelonShift", gap);
        void Apparent(Vector2 wind, float speed) =>
            SetProp(wd, "Apparent", WindMath.Resolve(Vector2.up, speed, wind));

        Apparent(new Vector2(0f, -4f), 10f);                     // pure headwind
        Check("echelon: headwind no shift", Mathf.Abs(Shift(2f)) < 1e-4f);

        Apparent(new Vector2(-4f, 0f), 10f);                     // crosswind from the right
        float ang = WindMath.Resolve(Vector2.up, 10f, new Vector2(-4f, 0f)).AngleDeg;
        float s1 = Shift(1f), s2 = Shift(2f);
        Check("echelon: from-right shifts +", s1 > 0f && Mathf.Abs(s1 - Mathf.Tan(ang * Mathf.Deg2Rad)) < 1e-3f,
              $"angle {ang:0.0} shift {s1:0.000} m @1 m gap");
        Check("echelon: grows with gap", s2 > s1 && Mathf.Abs(s2 - 2f * s1) < 1e-3f);
        Check("echelon: clamped to 1.2 m", Mathf.Abs(Shift(50f) - 1.2f) < 1e-4f);

        Apparent(new Vector2(4f, 0f), 10f);                      // crosswind from the left
        Check("echelon: from-left mirrors", Mathf.Abs(Shift(1f) + s1) < 1e-4f);

        Apparent(new Vector2(-20f, 0f), 0.5f);                   // near-beam gale at walking pace
        Check("echelon: angle clamped at 60 deg", Mathf.Abs(Shift(0.5f) - 0.5f * Mathf.Tan(60f * Mathf.Deg2Rad)) < 1e-3f);

        // The pocket, not the leader's wheel, is the best place to sit in a crosswind.
        Apparent(new Vector2(-4f, 0f), 10f);
        float gap = 1f, pocket = Shift(gap);
        float onPocket = WindMath.DraftMultiplier(gap, 0f), onWheel = WindMath.DraftMultiplier(gap, 0f - pocket);
        Check("echelon: pocket beats the wheel", onPocket < onWheel, $"{onPocket:0.000} vs {onWheel:0.000}");

        SetProp(null, "Instance", null, typeof(WeatherDirector));
        Check("echelon: no director = no shift", Mathf.Abs(Shift(3f)) < 1e-6f);
    }

    // ------------------------------------------------------------------ terrain exposure

    /// <summary>Straight 4 km course along +Z; elevation(d) supplied by the caller.</summary>
    private static RouteCourse Course(System.Func<float, float> elevation)
    {
        const int n = 401;
        var c = new RouteCourse { Id = "~test", Position = new Vector3[n], Distance = new float[n] };
        for (int i = 0; i < n; i++)
        {
            float d = i * 10f;
            c.Position[i] = new Vector3(0f, elevation(d), d);
            c.Distance[i] = d;            // horizontal arc length; fine for a test profile
        }
        c.Length = c.Distance[n - 1];
        return c;
    }

    private static void TerrainExposure(List<Object> temp)
    {
        var go = new GameObject("~wx terrain"); temp.Add(go);
        var session = go.AddComponent<RideSession>();
        var wd = go.AddComponent<WeatherDirector>();
        wd.session = session;

        float Bump(float d, float centre, float h, float w) => h * Mathf.Exp(-(d - centre) * (d - centre) / (2f * w * w));
        // Ridge at 1000 m (+30 m), valley at 2000 m (-30 m), flat at 3200 m.
        SetProp(session, "Course", Course(d => 100f + Bump(d, 1000f, 30f, 120f) - Bump(d, 2000f, 30f, 120f)));
        float At(float d) { SetProp(session, "DistanceM", d); return wd.TerrainExposure(); }

        float flat = At(3200f), ridge = At(1000f), valley = At(2000f);
        Check("terrain: flat = open", Mathf.Abs(flat - 1f) < 0.02f, $"{flat:0.000}");
        Check("terrain: ridge exposed", ridge > 1.1f && ridge <= 1.35f, $"{ridge:0.000}");
        Check("terrain: valley sheltered", valley < 0.9f && valley >= 0.6f, $"{valley:0.000}");
        Check("terrain: ridge/valley symmetric", Mathf.Abs((ridge - 1f) + (valley - 1f)) < 0.02f);

        SetProp(session, "Course", Course(d => Bump(d, 1500f, 400f, 100f) - Bump(d, 500f, 400f, 100f)));
        Check("terrain: clamped high", Mathf.Approximately(At(1500f), 1.35f));
        Check("terrain: clamped low", Mathf.Approximately(At(500f), 0.6f));

        SetProp(session, "Course", Course(d => 0.05f * d));      // steady 5 % climb: no ridge, no valley
        Check("terrain: steady grade = open", Mathf.Abs(At(1500f) - 1f) < 0.01f, $"{At(1500f):0.000}");

        SetProp(session, "Course", null);
        Check("terrain: no course = open", Mathf.Approximately(wd.TerrainExposure(), 1f));
        wd.session = null;
        Check("terrain: no session = open", Mathf.Approximately(wd.TerrainExposure(), 1f));
    }

    // ------------------------------------------------------------------ wind volumes

    private static WeatherWindVolume Volume(List<Object> temp, Vector3 pos, Vector3 size, float mult,
                                            float edge = 25f, float yawDeg = 0f, float scale = 1f)
    {
        var go = new GameObject("~wx volume"); temp.Add(go);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<BoxCollider>().size = size;
        var v = go.AddComponent<WeatherWindVolume>();
        v.windMultiplier = mult;
        v.edgeBlendM = edge;
        Call(v, "OnEnable");                // edit mode: register with the static list
        return v;
    }

    private static void Unregister(params WeatherWindVolume[] vs) { foreach (var v in vs) Call(v, "OnDisable"); }

    private static void WindVolumeBlending(List<Object> temp)
    {
        var box = new Vector3(200f, 20f, 200f);
        Check("volume: none = open, not found",
              !WeatherWindVolume.TryExposureAt(Vector3.zero, out float e0) && Mathf.Approximately(e0, 1f));

        var a = Volume(temp, Vector3.zero, box, 0.6f);
        Check("volume: centre = multiplier", Mathf.Abs(WeatherWindVolume.ExposureAt(Vector3.zero) - 0.6f) < 1e-4f);
        // 10 m inside the x edge (x = 90): weight 10/25 = 0.4 -> lerp(1, 0.6, 0.4) = 0.84.
        Check("volume: edge blends in", Mathf.Abs(WeatherWindVolume.ExposureAt(new Vector3(90f, 0f, 0f)) - 0.84f) < 1e-3f,
              $"{WeatherWindVolume.ExposureAt(new Vector3(90f, 0f, 0f)):0.000}");
        Check("volume: outside = open", !WeatherWindVolume.TryExposureAt(new Vector3(150f, 0f, 0f), out _));
        Check("volume: above box excluded", !WeatherWindVolume.TryExposureAt(new Vector3(0f, 15f, 0f), out _));

        // Same-zone overlap (the Minato builder lays 100 m boxes that overlap): must NOT compound.
        var b = Volume(temp, new Vector3(50f, 0f, 0f), box, 0.6f);
        float overlap = WeatherWindVolume.ExposureAt(new Vector3(25f, 0f, 0f));
        Check("volume: overlap does not compound", Mathf.Abs(overlap - 0.6f) < 1e-4f, $"{overlap:0.000} (0.36 would be compounding)");
        Unregister(b);

        // Two zones overlapping: weighted average, never outside [min, max].
        var c = Volume(temp, new Vector3(50f, 0f, 0f), box, 1.4f);
        Check("volume: zone mix averages", Mathf.Abs(WeatherWindVolume.ExposureAt(new Vector3(25f, 0f, 0f)) - 1.0f) < 1e-3f);
        bool bounded = true, continuous = true;
        float prev = WeatherWindVolume.ExposureAt(new Vector3(-120f, 0f, 0f));
        for (float x = -120f; x <= 180f; x += 0.5f)
        {
            float e = WeatherWindVolume.ExposureAt(new Vector3(x, 0f, 3f));
            bounded &= e >= 0.6f - 1e-4f && e <= 1.4f + 1e-4f;
            continuous &= Mathf.Abs(e - prev) < 0.05f;        // cross-fades, no pops at borders
            prev = e;
        }
        Check("volume: bounded by its zones", bounded);
        Check("volume: continuous across borders", continuous);
        Unregister(a, c);

        // Rotation and scale are honoured (Minato boxes follow the road heading).
        var r = Volume(temp, Vector3.zero, new Vector3(100f, 20f, 10f), 1.5f, 2f, 90f);
        Check("volume: rotated box", WeatherWindVolume.TryExposureAt(new Vector3(0f, 0f, 40f), out _) &&
                                     !WeatherWindVolume.TryExposureAt(new Vector3(40f, 0f, 0f), out _));
        Unregister(r);
        var s = Volume(temp, Vector3.zero, new Vector3(100f, 20f, 100f), 1.5f, 5f, 0f, 2f);
        Check("volume: scaled box", Mathf.Abs(WeatherWindVolume.ExposureAt(new Vector3(90f, 0f, 0f)) - 1.5f) < 1e-4f);
        Unregister(s);

        // WindAt: volume wins over terrain; exposure multiplies the sustained wind (gust off).
        var go = new GameObject("~wx windat"); temp.Add(go);
        var wd = go.AddComponent<WeatherDirector>();
        SetProp(wd, "State", new WeatherDirector.WeatherState { WindToXZ = new Vector2(0f, 10f) });
        var v = Volume(temp, Vector3.zero, box, 0.5f);
        var w = wd.WindAt(Vector3.zero, 0.0, out float exp);
        Check("volume: WindAt uses volume", Mathf.Abs(exp - 0.5f) < 1e-4f && Mathf.Abs(w.y - 5f) < 1e-3f, $"exp {exp:0.00} wind {w}");
        Unregister(v);
        w = wd.WindAt(Vector3.zero, 0.0, out exp);
        Check("volume: WindAt falls back to terrain", Mathf.Approximately(exp, 1f) && Mathf.Abs(w.y - 10f) < 1e-3f);
    }

    // ------------------------------------------------------------------ preset transitions

    private static WeatherPreset Preset(List<Object> temp, float windToDeg, float mps, float cover, float precip,
                                        float wet, float fog, float tempC, float seconds)
    {
        var p = ScriptableObject.CreateInstance<WeatherPreset>(); temp.Add(p);
        p.windToDeg = windToDeg; p.sustainedMps = mps; p.gustMps = 0f; p.cloudCoverage = cover;
        p.precipitation = precip; p.wetness = wet; p.fog = fog; p.airTempC = tempC; p.transitionSeconds = seconds;
        return p;
    }

    private static void PresetTransitions(List<Object> temp)
    {
        var go = new GameObject("~wx transitions"); temp.Add(go);
        var wd = go.AddComponent<WeatherDirector>();
        var clear = Preset(temp, 0f, 3f, 0.2f, 0f, 0f, 0.05f, 25f, 60f);
        var storm = Preset(temp, 90f, 9f, 0.9f, 0.8f, 0.9f, 0.3f, 12f, 40f);
        SetProp(wd, "State", WeatherDirector.FromPreset(clear));
        float dt = Time.fixedDeltaTime;
        void Tick(float seconds) { int n = Mathf.RoundToInt(seconds / dt); for (int i = 0; i < n; i++) Call(wd, "FixedUpdate"); }

        wd.SetPreset(storm);                                     // uses the preset's own 40 s
        Check("transition: starts at the old state", Mathf.Approximately(wd.State.Coverage, 0.2f));
        Tick(1f);
        Check("transition: no pop on the first second", wd.State.Coverage < 0.21f, $"{wd.State.Coverage:0.0000}");

        bool monotonic = true; float last = wd.State.Coverage;
        for (int k = 0; k < 19; k++) { Tick(1f); monotonic &= wd.State.Coverage >= last - 1e-6f; last = wd.State.Coverage; }
        Check("transition: halfway at half time", Mathf.Abs(wd.TransitionProgress - 0.5f) < 0.02f && Mathf.Abs(wd.State.Coverage - 0.55f) < 0.02f,
              $"progress {wd.TransitionProgress:0.00} coverage {wd.State.Coverage:0.000}");
        float midMag = wd.State.WindToXZ.magnitude;
        Check("transition: wind vector blends (no NaN)", !float.IsNaN(midMag) && midMag > 0.5f && midMag < 9f, $"{midMag:0.00} m/s");

        wd.freeze = true;
        float frozenAt = wd.State.Coverage; Tick(5f);
        Check("transition: freeze holds", Mathf.Approximately(wd.State.Coverage, frozenAt));
        wd.freeze = false;

        for (int k = 0; k < 25; k++) { Tick(1f); monotonic &= wd.State.Coverage >= last - 1e-6f; last = wd.State.Coverage; }
        Check("transition: monotonic", monotonic);
        var end = WeatherDirector.FromPreset(storm);
        Check("transition: lands exactly", Mathf.Approximately(wd.State.Coverage, end.Coverage) && wd.State.WindToXZ == end.WindToXZ
                                           && Mathf.Approximately(wd.State.Precipitation, 0.8f) && Mathf.Approximately(wd.TransitionProgress, 1f));
        Check("transition: air density follows temperature", wd.State.AirDensity > WeatherDirector.FromPreset(clear).AirDensity);

        // Retarget mid-blend: continues from where it is, no jump.
        wd.SetPreset(clear, 10f);
        Tick(5f);
        float before = wd.State.Coverage;
        wd.SetPreset(storm, 10f);
        Check("transition: retarget mid-blend is continuous", Mathf.Approximately(wd.State.Coverage, before));
        Tick(10f);
        Check("transition: retarget lands", Mathf.Approximately(wd.State.Coverage, 0.9f));

        // The shader globals carry the same state (visuals agree with physics).
        var g = Shader.GetGlobalVector("_MapleWeather");
        Check("transition: _MapleWeather global", Mathf.Approximately(g.x, 0.9f) && Mathf.Approximately(g.y, 0.8f)
                                                  && Mathf.Approximately(g.z, 0.9f) && Mathf.Approximately(g.w, 0.3f), $"{g}");
        var gw = Shader.GetGlobalVector("_MapleWind");
        Check("transition: _MapleWind global", Mathf.Abs(gw.z - 9f) < 1e-3f && Mathf.Abs(gw.x - 9f) < 1e-3f, $"{gw}");

        // Every shipped preset loads, is in range and blends to itself cleanly.
        var shipped = Resources.LoadAll<WeatherPreset>("Weather");
        bool sane = shipped.Length >= 8;
        foreach (var p in shipped)
        {
            var st = WeatherDirector.FromPreset(p);
            sane &= p.transitionSeconds > 0f && st.AirDensity > 1.1f && st.AirDensity < 1.35f
                    && st.WindToXZ.magnitude <= 25f && WeatherDirector.Lerp(st, st, 0.5f).Coverage == st.Coverage;
        }
        Check("transition: shipped presets sane", sane, $"{shipped.Length} presets");
    }

    // ------------------------------------------------------------------ C1 / C2 pure maths

    private static void WetnessDynamics()
    {
        // Rain soaks quickly, drying after rain is slow, still raining never dries.
        var rain = Weather(0.45f, 0.7f);
        float w = 0f; int sec = 0;
        while (w < 0.99f * WeatherWetness.TargetWetness(rain) && sec < 3600) { w = WeatherWetness.Step(w, rain, 1f); sec++; }
        Check("wet: soaks in rain", sec < 60, $"{sec} s to soak");
        var clear = Weather(0f, 0f);
        float d = 1f; int dsec = 0;
        while (d > 0.05f && dsec < 20000) { d = WeatherWetness.Step(d, clear, 1f); dsec++; }
        Check("wet: dries slowly", dsec > 300 && dsec < 3600, $"{dsec} s to dry");
        var hot = Weather(0f, 0f, 8f, 30f, 0.3f);
        float dh = 1f; int hsec = 0;
        while (dh > 0.05f && hsec < 20000) { dh = WeatherWetness.Step(dh, hot, 1f); hsec++; }
        Check("wet: warm windy dries faster", hsec < dsec, $"{hsec} s vs {dsec} s");
        Check("wet: no drying while raining", WeatherWetness.Step(1f, Weather(0.3f, 0f), 60f) == 1f);
        Check("wet: post-rain floor", Mathf.Approximately(WeatherWetness.Step(0.9f, Weather(0f, 0.8f), 5000f), 0.8f));
        float w30 = Integrate(clear, 1f, 600f, 1f / 30f), w144 = Integrate(clear, 1f, 600f, 1f / 144f);
        Check("wet: frame-rate independent", Mathf.Abs(w30 - w144) < 0.01f, $"{w30:0.0000} vs {w144:0.0000}");
    }

    private static float Integrate(WeatherDirector.WeatherState s, float start, float seconds, float dt)
    {
        float w = start; for (float t = 0f; t < seconds; t += dt) w = WeatherWetness.Step(w, s, dt); return w;
    }

    private static void AtmosphereMaths()
    {
        var def = Resources.Load<WeatherPreset>("Weather/ClearCoastalBreeze");
        if (def != null)
            Check("fog: default preset = region look", Mathf.Abs(WeatherAtmosphere.ComputeFogScale(def.fog, def.humidity, def.precipitation) - 1f) < 0.01f);
        Check("fog: thicker with fog", WeatherAtmosphere.ComputeFogScale(0.3f, 0.5f, 0f) > WeatherAtmosphere.ComputeFogScale(0.1f, 0.5f, 0f));
        Check("fog: thicker with humidity", WeatherAtmosphere.ComputeFogScale(0.1f, 0.9f, 0f) > WeatherAtmosphere.ComputeFogScale(0.1f, 0.3f, 0f));
        Check("fog: thicker in rain", WeatherAtmosphere.ComputeFogScale(0.1f, 0.5f, 0.6f) > WeatherAtmosphere.ComputeFogScale(0.1f, 0.5f, 0f));
        Check("fog: clamped", Mathf.Approximately(WeatherAtmosphere.ComputeFogScale(1f, 1f, 1f), 10f) &&
                              Mathf.Approximately(WeatherAtmosphere.ComputeFogScale(0f, 0f, 0f), 0.6f));

        Check("curtain: off at threshold", WeatherAtmosphere.CurtainStrength(0.3f) == 0f && WeatherAtmosphere.CurtainStrength(0.1f) == 0f);
        Check("curtain: full by 0.5", Mathf.Approximately(WeatherAtmosphere.CurtainStrength(0.5f), 1f));

        bool inRange = true, arrival = true, det = true, fadesAtEnds = true;
        const float frontDeg = 60f;
        var travel = new Vector2(Mathf.Sin(frontDeg * Mathf.Deg2Rad), Mathf.Cos(frontDeg * Mathf.Deg2Rad));
        for (int i = 0; i < 6; i++)
            for (double t = 0; t < 1200; t += 7.3)
            {
                var o = WeatherAtmosphere.CellOffset(i, 6, frontDeg, 8f, t, 1234, 1000f, 3000f, out float fade);
                var o2 = WeatherAtmosphere.CellOffset(i, 6, frontDeg, 8f, t, 1234, 1000f, 3000f, out float fade2);
                inRange &= o.magnitude >= 1000f - 0.5f && o.magnitude <= 3000f + 0.5f;
                arrival &= Vector2.Dot(o.normalized, travel) < -0.5f;          // within 60 deg of the arrival bearing
                det &= o == o2 && fade == fade2;
                if (o.magnitude < 1010f || o.magnitude > 2990f) fadesAtEnds &= fade < 0.05f;
            }
        Check("curtain: 1-3 km from the rider", inRange);
        Check("curtain: on the side the front arrives from", arrival);
        Check("curtain: deterministic (seed, time)", det);
        Check("curtain: fades at wrap-around", fadesAtEnds);
        // Each cell closes in at the front speed (8 m/s -> 80 m per 10 s) unless it wrapped far.
        bool drifts = true; int drifted = 0;
        for (int i = 0; i < 6; i++)
        {
            float m0 = WeatherAtmosphere.CellOffset(i, 6, frontDeg, 8f, 100.0, 1234, 1000f, 3000f, out _).magnitude;
            float m1 = WeatherAtmosphere.CellOffset(i, 6, frontDeg, 8f, 110.0, 1234, 1000f, 3000f, out _).magnitude;
            if (m1 > m0) continue;                                   // wrapped to the far end
            drifted++; drifts &= Mathf.Abs(m0 - m1 - 80f) < 1f;
        }
        Check("curtain: drifts in at the front speed", drifts && drifted >= 4, $"{drifted}/6 cells checked");

        var line = new List<Vector2> { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(100f, 100f) };
        Check("curtain: polyline distance", Mathf.Abs(WeatherAtmosphere.DistanceToPolyline(new Vector2(50f, 30f), line) - 30f) < 1e-4f &&
                                            Mathf.Abs(WeatherAtmosphere.DistanceToPolyline(new Vector2(130f, 50f), line) - 30f) < 1e-4f);
    }

    private static WeatherDirector.WeatherState Weather(float precip, float wet, float windMps = 3f,
                                                        float tempC = 18f, float humidity = 0.5f) =>
        new WeatherDirector.WeatherState
        {
            Precipitation = precip, Wetness = wet, WindToXZ = new Vector2(0f, windMps),
            TempC = tempC, Humidity = humidity, AirDensity = 1.2f,
        };

    /// <summary>C1: wet-road dynamics, plus the property-block path on the real road renderers
    /// in SakuraPass.unity (material assets must stay untouched).</summary>
    public static void RoadWetnessScene()
    {
        _pass = _fail = 0;
        WetnessDynamics();

        // Scene: real road renderers get the block, assets keep their authored value.
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity", OpenSceneMode.Single);
        var go = new GameObject("~wetness test");
        var ww = go.AddComponent<WeatherWetness>();
        ww.Rescan();
        Check("found road renderers", ww.RoadRendererCount > 0, $"{ww.RoadRendererCount} slots");

        var roads = new System.Collections.Generic.List<(Renderer r, int i, Material m, float before)>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].shader != null && mats[i].shader.name == WeatherWetness.RoadShaderName)
                    roads.Add((r, i, mats[i], mats[i].GetFloat("_WetAmount")));
        }
        ww.Apply(1f);
        var mpb = new MaterialPropertyBlock();
        bool allWet = true, assetsSame = true;
        foreach (var (r, i, m, before) in roads)
        {
            r.GetPropertyBlock(mpb, i);
            allWet &= Mathf.Abs(mpb.GetFloat("_WetAmount") - 1f) < 1e-4f;
            assetsSame &= Mathf.Approximately(m.GetFloat("_WetAmount"), before);
        }
        Check("block = full wet", allWet);
        Check("assets untouched", assetsSame);

        ww.Apply(0f);
        bool authored = true;
        foreach (var (r, i, m, before) in roads)
        {
            r.GetPropertyBlock(mpb, i);
            authored &= Mathf.Approximately(mpb.GetFloat("_WetAmount"), before);
        }
        Check("dry = authored value", authored);
        for (int k = 0; k < Mathf.Min(8, roads.Count); k++)
            Debug.Log($"[weather-sys] road '{roads[k].r.name}' [{roads[k].i}] {roads[k].m.name} authored {roads[k].before:0.00}");
        Object.DestroyImmediate(go);
        Finish("wetness");
    }

    /// <summary>C2 diagnosis: does a rain-curtain card render? Edit-mode HDRP render of the card
    /// in an empty scene, in three variants (material colour / property block / no texture).</summary>
    public static void CurtainRenderProbe()
    {
        _pass = _fail = 0;
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(new Vector3(0f, 5f, 0f), Quaternion.identity);
        cam.farClipPlane = 9000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        var hd = cam.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>()
                 ?? cam.gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;
        hd.backgroundColorHDR = Color.black;

        var mesh = WeatherAtmosphere.CurtainMesh();
        string[] variants = { "material", "mpb", "notex" };
        foreach (var variant in variants)
        {
            var mat = WeatherAtmosphere.CurtainMaterial();
            if (variant == "notex") mat.SetTexture("_UnlitColorMap", null);
            var go = new GameObject("curtain " + variant);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            go.transform.position = new Vector3(0f, -150f, 1200f);
            go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            go.transform.localScale = new Vector3(600f, 1950f, 1f);
            var col = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            if (variant == "mpb") { var b = new MaterialPropertyBlock(); b.SetColor("_UnlitColor", col); r.SetPropertyBlock(b); }
            else mat.SetColor("_UnlitColor", col);

            var rt = new RenderTexture(320, 180, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(320, 180, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 320, 180), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            var c = tex.GetPixel(160, 100);
            var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../reference/copilot/weather"));
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"probe_{variant}.png"), tex.EncodeToPNG());
            Check($"curtain renders ({variant})", c.maxColorComponent > 0.05f,
                  $"centre {c}, shader {mat.shader.name} q{mat.renderQueue} kw [{string.Join(",", mat.shaderKeywords)}]");
            Object.DestroyImmediate(go);
            rt.Release();
        }
        Finish("curtain-probe");
    }
}
