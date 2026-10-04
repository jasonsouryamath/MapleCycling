#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Headless self-test for the Nagisa Bay audio (worker H). Batchmode has no audio device, so this proves the parts
/// that break silently: every Resources clip imports with the right length/channel count, stems are the same length,
/// loops have no seam click in the IMPORTED data, the route-anchored emitters resolve onto the real Nagisa route,
/// the loop/voice budget holds along the whole 16 km, and the music zone weights tell the intended story.
/// Run: pwsh -NoProfile -File tools/unity/run_steps.ps1 "NagisaAudioSelfTest.Run|claude_h_audio.log|1"
/// Audibility and mix balance remain "not verified by ear".
/// </summary>
public static class NagisaAudioSelfTest
{
    private const string RoutePath = "Assets/Environment/NagisaBay/NagisaRoute.json";

    [Serializable] private class Sample { public float[] p; public float[] t; public float d; }
    [Serializable] private class RouteDto { public Sample[] samples; }

    private sealed class Stub : INagisaAudioContext
    {
        public RouteCourse Course { get; set; }
        public float RouteD { get; set; }
        public float SpeedMps { get; set; } = 9f;
        public Vector3 ListenerPos { get; set; }
        public float Gate { get; set; } = 1f;
        public float MusicLevel { get; set; } = 0.45f;
    }

    private static int _fails, _seamChecks;
    private static readonly StringBuilder Report = new StringBuilder();

    private static void Check(bool ok, string what)
    {
        if (!ok) _fails++;
        Report.AppendLine((ok ? "  PASS " : "  FAIL ") + what);
    }

    public static void Run()
    {
        _fails = 0; Report.Length = 0;
        try
        {
            ClipChecks();
            var course = BuildCourse();
            Check(course != null && course.Count > 1000 && course.Length > 16000f, $"route loaded ({course?.Count} samples, {course?.Length:F0} m)");
            if (course != null)
            {
                MusicChecks();
                AmbienceChecks(course);
            }
        }
        catch (Exception e)
        {
            _fails++; Report.AppendLine("EXCEPTION " + e);
        }
        Debug.Log("[nagisa-audio-selftest]\n" + Report);
        Debug.Log($"[nagisa-audio-selftest] {(_fails == 0 ? "PASS" : "FAIL")} ({_fails} failures)");
        if (Application.isBatchMode) EditorApplication.Exit(_fails == 0 ? 0 : 1);
    }

    // ------------------------------------------------------------------------------------------ clips
    private static void ClipChecks()
    {
        Report.AppendLine("== clips");
        var names = new List<string>(NagisaAmbienceDirector.AllClipNames());
        int ok = 0;
        foreach (var n in names)
        {
            var c = Resources.Load<AudioClip>(NagisaAmbienceDirector.Folder + n);
            if (c == null) { Check(false, "missing ambience clip " + n); continue; }
            bool good = c.channels == 1 && c.frequency == 32000 && c.length > 0.5f && c.length < 40f;
            if (!good) Check(false, $"{n}: ch={c.channels} sr={c.frequency} len={c.length:F2}");
            else ok++;
            // loops (long clips) must not click at the seam in the IMPORTED, decoded data
            if (c.length > 7.5f && !n.Contains("pass") && !n.Contains("hit"))
            {
                var d = new float[c.samples];
                if (c.GetData(d, 0))
                {
                    _seamChecks++;
                    float jump = Mathf.Abs(d[0] - d[d.Length - 1]);
                    float big = 0f;
                    for (int i = 1; i < 16000; i++) big = Mathf.Max(big, Mathf.Abs(d[i] - d[i - 1]));
                    if (jump > Mathf.Max(0.05f, big * 1.2f)) Check(false, $"{n}: seam jump {jump:F3} vs local max step {big:F3}");
                }
            }
        }
        Check(ok == names.Count, $"{ok}/{names.Count} ambience clips present, mono 32 kHz, sane length");

        float len0 = -1f; int stems = 0; bool same = true;
        for (int i = 0; i < NagisaMusicDirector.Count; i++)
        {
            var c = Resources.Load<AudioClip>(NagisaMusicDirector.ResourceFolder + NagisaMusicDirector.StemNames[i]);
            if (c == null) { Check(false, "missing stem " + NagisaMusicDirector.StemNames[i]); continue; }
            stems++;
            if (len0 < 0) len0 = c.length; else if (Mathf.Abs(c.length - len0) > 0.001f) same = false;
            Report.AppendLine($"  stem {NagisaMusicDirector.StemNames[i]}: {c.length:F3} s ch={c.channels} sr={c.frequency}");
            var data = new float[c.samples * c.channels];
            if (c.GetData(data, 0))
            {
                _seamChecks++;
                float jump = 0f, step = 0f;
                for (int ch = 0; ch < c.channels; ch++)
                {
                    jump = Mathf.Max(jump, Mathf.Abs(data[ch] - data[data.Length - c.channels + ch]));
                    for (int k = 1; k < 32000; k++) step = Mathf.Max(step, Mathf.Abs(data[k * c.channels + ch] - data[(k - 1) * c.channels + ch]));
                }
                Check(jump <= Mathf.Max(0.01f, step * 1.5f), $"stem {NagisaMusicDirector.StemNames[i]} loop seam: jump {jump:F4} vs local max step {step:F4}");
            }
        }
        Report.AppendLine($"  decoded-data seam checks that could run: {_seamChecks} (compressed-in-memory clips cannot be read back; the WAV sources are seam-checked by tools/audio/qa_nagisa_audio.py)");
        Check(stems == NagisaMusicDirector.Count && same && Mathf.Abs(len0 - 64f) < 0.05f, $"{stems} stems present, identical length {len0:F3} s (expect 64.000)");
    }

    // ------------------------------------------------------------------------------------------ route
    private static RouteCourse BuildCourse()
    {
        var dto = JsonUtility.FromJson<RouteDto>(File.ReadAllText(RoutePath));
        int n = dto.samples.Length;
        var c = new RouteCourse
        {
            Id = NagisaAudioHost.CourseId,
            Position = new Vector3[n], Tangent = new Vector3[n], Side = new Vector3[n], Up = new Vector3[n],
            Distance = new float[n], Bank = new float[n], SegmentOf = new string[n],
        };
        for (int i = 0; i < n; i++)
        {
            var s = dto.samples[i];
            c.Position[i] = new Vector3(s.p[0], s.p[1], s.p[2]);
            c.Tangent[i] = new Vector3(s.t[0], s.t[1], s.t[2]).normalized;
            c.Up[i] = Vector3.up;
            var side = Vector3.Cross(Vector3.up, new Vector3(c.Tangent[i].x, 0f, c.Tangent[i].z));
            c.Side[i] = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
            c.Distance[i] = s.d; c.SegmentOf[i] = "nagisa";
        }
        c.Length = c.Distance[n - 1];
        return c;
    }

    // ------------------------------------------------------------------------------------------ music
    private static void MusicChecks()
    {
        Report.AppendLine("== music zone weights (keys bass gtr perc drive lead)");
        var w = new float[NagisaMusicDirector.Count];
        var rows = new (string, float)[]
        {
            ("marina", 400f), ("promenade", 1500f), ("beachfront", NagisaAudioMap.Zone_hotelM), ("climb foot", NagisaAudioMap.Zone_climbStart + 100f),
            ("mid climb", (NagisaAudioMap.Zone_climbStart + NagisaAudioMap.Zone_komM) * 0.5f), ("pre-summit", NagisaAudioMap.Zone_komM - 250f),
            ("overlook", NagisaAudioMap.Zone_komM + 150f), ("descent", NagisaAudioMap.Zone_komM + 1800f), ("coast", NagisaAudioMap.Zone_coastStart + 500f),
            ("bridge", NagisaAudioMap.Zone_bridgeStart + 300f),
        };
        var at = new Dictionary<string, float[]>();
        foreach (var (name, d) in rows)
        {
            NagisaMusicDirector.WeightsAt(d, w);
            at[name] = (float[])w.Clone();
            Report.AppendLine($"  {name,-11} d={d,7:F0}  " + string.Join(" ", Array.ConvertAll(w, x => x.ToString("F2"))));
        }
        const int Perc = NagisaMusicDirector.PercI, Drive = NagisaMusicDirector.DriveI, Lead = NagisaMusicDirector.LeadI, Bass = NagisaMusicDirector.BassI;
        Check(at["marina"][Drive] < 0.05f && at["marina"][Perc] < at["beachfront"][Perc], "marina: relaxed (no drive layer, lighter percussion than the beachfront)");
        Check(at["beachfront"][Bass] > 0.95f && at["beachfront"][Perc] > 0.9f, "beachfront: bright/energetic (bass + percussion full)");
        Check(at["pre-summit"][Drive] > at["climb foot"][Drive] + 0.4f, "climb: intensity builds (drive layer ramps up toward the summit)");
        Check(at["overlook"][Lead] > 0.95f && at["overlook"][Lead] > at["pre-summit"][Lead] + 0.7f, "overlook: lead melody opens");
        Check(at["overlook"][Drive] < at["pre-summit"][Drive] - 0.4f, "overlook: drive layer drops away");
        Check(at["descent"][Perc] > at["overlook"][Perc] + 0.3f && at["descent"][Drive] > 0.6f, "descent: percussion and drive return");
        bool inRange = true;
        for (float d = 0f; d <= 16300f; d += 25f)
        {
            NagisaMusicDirector.WeightsAt(d, w);
            foreach (var v in w) if (v < 0f || v > 1.0001f) inRange = false;
        }
        Check(inRange, "all stem weights stay within 0..1 along the whole route");
    }

    // ------------------------------------------------------------------------------------------ ambience
    private static void AmbienceChecks(RouteCourse course)
    {
        Report.AppendLine("== positional ambience");
        var go = new GameObject("~NagisaAmbienceSelfTest");
        try
        {
            var stub = new Stub { Course = course };
            var amb = go.AddComponent<NagisaAmbienceDirector>();
            stub.ListenerPos = course.PositionAt(0f); stub.RouteD = 0f;
            amb.Init(stub);
            Check(amb.LoopCount >= 34, $"landmark loops placed on the route: {amb.LoopCount}");

            int maxWanted = 0, shoreMissing = 0;
            var landmarks = new[] { 100f, 500f, 1100f, 1700f, 2400f, 3200f, 3992f, 4640f, 6500f, NagisaAudioMap.Zone_komM, 10000f, 13000f, 15600f };
            var table = new StringBuilder();
            for (float d = 0f; d <= course.Length; d += 100f)
            {
                stub.RouteD = d;
                var side = course.SideAt(d);
                stub.ListenerPos = course.PositionAt(d) + side * 1.5f + Vector3.up * 1.6f;
                amb.TestTick(0.2f);
                maxWanted = Mathf.Max(maxWanted, amb.WantedLoops);
                if (!amb.ShoreFound) shoreMissing++;
                foreach (var lm in landmarks)
                    if (Mathf.Abs(d - lm) < 50f)
                        table.AppendLine($"  d={d,6:F0}  loops wanted={amb.WantedLoops,2}  surf={amb.SurfWant:F2}  distant={amb.FarWant:F2}  beds: {amb.BedReport()}");
            }
            Report.Append(table);
            Check(maxWanted <= amb.maxLoops, $"loop budget holds along the route (max wanted {maxWanted} <= {amb.maxLoops})");
            Check(shoreMissing == 0, "shoreline table resolves everywhere");

            // spot checks of zone behaviour
            Probe(amb, stub, course, 450f, out int marinaLoops, out float marinaSurf, out _);
            Probe(amb, stub, course, 3200f, out int beachLoops, out float beachSurf, out _);
            Probe(amb, stub, course, 6500f, out int climbLoops, out float climbSurf, out float climbFar);
            Probe(amb, stub, course, 13000f, out _, out float eastSurf, out _);
            Check(marinaLoops >= 4, $"marina has a dense local soundscape ({marinaLoops} loops)");
            Check(marinaSurf < 0.5f, $"marina is calm (surf want {marinaSurf:F2})");
            Check(beachSurf > 0.35f && beachLoops >= 2, $"beachfront: surf {beachSurf:F2}, {beachLoops} local loops");
            Check(climbSurf < 0.05f && climbFar > 0.01f, $"climb: close surf gone ({climbSurf:F2}), only distant breakers ({climbFar:F2})");
            Check(eastSurf > 0.1f, $"east coast road hears the sea (surf {eastSurf:F2})");

            SimulatedRide(amb, stub, course);

            // silence while the title / World Map gate is closed
            stub.Gate = 0f; stub.RouteD = 3200f; stub.ListenerPos = course.PositionAt(3200f);
            amb.TestTick(0.2f);
            Check(amb.WantedLoops == 0 && amb.SurfWant == 0f, "gate closed: nothing is wanted (title / world map stays silent)");
            stub.Gate = 1f;

            // emitter placement sanity: every landmark loop within 500 m of the route
            var f = typeof(NagisaAmbienceDirector).GetField("_loops", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var loops = (System.Collections.IList)f.GetValue(amb);
            int far = 0;
            foreach (var l in loops)
            {
                var pos = (Vector3)l.GetType().GetField("pos").GetValue(l);
                float best = float.MaxValue;
                for (int i = 0; i < course.Count; i += 2)
                { float dx = course.Position[i].x - pos.x, dz = course.Position[i].z - pos.z; best = Mathf.Min(best, dx * dx + dz * dz); }
                if (Mathf.Sqrt(best) > 500f) far++;
            }
            Check(far == 0, $"every landmark emitter sits within 500 m of the route ({far} outliers)");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    /// <summary>Rides the whole 16 km at 10 m/s through the real Frame() path and measures the voice budget.</summary>
    private static void SimulatedRide(NagisaAmbienceDirector amb, Stub stub, RouteCourse course)
    {
        const float dt = 0.2f;
        int maxVoices = 0; long sum = 0; int ticks = 0, maxPass = 0, maxShots = 0; int passSeen = 0;
        var voices = new List<AudioSource>();
        var seenNames = new HashSet<string>();
        stub.Gate = 1f; stub.SpeedMps = 10f;
        for (float d = 0f; d < course.Length; d += 10f * dt)
        {
            stub.RouteD = d;
            stub.ListenerPos = course.PositionAt(d) + course.SideAt(d) * 1.5f + Vector3.up * 1.6f;
            amb.Frame(dt);
            amb.GetComponentsInChildren(false, voices);
            int v = 0;
            foreach (var s in voices)
            {
                if (s.volume > 0.003f) { v++; string n = s.gameObject.name; seenNames.Add(n.StartsWith("Pass") ? "Pass" : n.StartsWith("Shot") ? "Shot" : n); }
            }
            maxVoices = Mathf.Max(maxVoices, v); sum += v; ticks++;
            maxPass = Mathf.Max(maxPass, amb.ActivePassers);
            if (amb.ActivePassers > 0) passSeen++;
        }
        Report.AppendLine($"  simulated ride: {ticks} frames, voices audible max {maxVoices}, mean {(float)sum / ticks:F1}; frames with a pass-by active {passSeen}; distinct voice kinds {seenNames.Count}");
        Report.AppendLine("  voice kinds: " + string.Join(", ", new SortedSet<string>(seenNames)));
        Check(maxVoices <= 12 + 6 + 6 + 2 + 4, $"audible ambience voices stay within budget (max {maxVoices} <= 30)");
        Check(passSeen > 20, $"pass-bys occur along the ride ({passSeen} frames)");
        Check(seenNames.Count >= 12, $"a wide layer variety is heard along the route ({seenNames.Count} kinds)");
    }

    private static void Probe(NagisaAmbienceDirector amb, Stub stub, RouteCourse course, float d, out int loops, out float surf, out float far)
    {
        stub.RouteD = d; stub.Gate = 1f;
        stub.ListenerPos = course.PositionAt(d) + course.SideAt(d) * 1.5f + Vector3.up * 1.6f;
        amb.TestTick(0.2f);
        loops = amb.WantedLoops; surf = amb.SurfWant; far = amb.FarWant;
    }
}
#endif
