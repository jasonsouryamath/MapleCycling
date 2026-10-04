using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

/// <summary>Comparable captures in the real boot -> streamed region -> gameplay camera path.
/// No authoring scene, material asset, or build registration writes.</summary>
[InitializeOnLoad]
public static class NagisaCoastalLookValidation
{
    private const string Key = "mapleride.nagisa.coastal.validation";
    private const string Output = "reference/good_graphics/nagisa_bay/coastal";
    private static int _stage, _frames;
    private static double _deadline;
    private static RideBootstrap _ride;
    private static readonly List<float> Samples = new List<float>();
    private static readonly List<string> Report = new List<string>();
    private static readonly Dictionary<Renderer, Material[]> Actors = new Dictionary<Renderer, Material[]>();
    private static bool _failed;
    private static RenderTexture _capture;
    private static Quaternion _forwardRotation;
    private static string _seaLabel;
    private static int _resumeStage;
    private static int _lastFrame;
    private static readonly Dictionary<Canvas, RenderMode> CanvasModes = new Dictionary<Canvas, RenderMode>();

    static NagisaCoastalLookValidation() { EditorApplication.playModeStateChanged += Changed; }

    public static void Run()
    {
        Directory.CreateDirectory(Output);
        EditorSceneManager.OpenScene(MapleRideBoot.ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(Key, true);
        SessionState.SetString(Key + ".boot", Environment.GetEnvironmentVariable("MR_BOOT_VALIDATION") ?? "");
        SessionState.SetString(Key + ".weather", Environment.GetEnvironmentVariable("MAPLERIDE_WEATHER_PRESET") ?? "");
        Environment.SetEnvironmentVariable("MR_BOOT_VALIDATION", "1");
        Environment.SetEnvironmentVariable("MAPLERIDE_WEATHER_PRESET", "ClearCoastalBreeze");
        EditorApplication.EnterPlaymode();
    }

    private static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            _stage = _frames = 0; _failed = false; Report.Clear(); Samples.Clear(); Actors.Clear();
            _deadline = EditorApplication.timeSinceStartup + 600;
            _lastFrame = -1;
            NagisaCoastalLook.CorrectionsEnabled = false;
            EditorApplication.update += Poll;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Poll;
            Environment.SetEnvironmentVariable("MR_BOOT_VALIDATION", SessionState.GetString(Key + ".boot", ""));
            Environment.SetEnvironmentVariable("MAPLERIDE_WEATHER_PRESET", SessionState.GetString(Key + ".weather", ""));
            NagisaCoastalLook.CorrectionsEnabled = true;
            if (_capture != null) { Object.DestroyImmediate(_capture); _capture = null; }
            Debug.Log("[nagisa-coastal-qa] " + (_failed ? "FAIL" : "PASS"));
            if (Application.isBatchMode) EditorApplication.Exit(_failed ? 1 : 0);
        }
    }

    private static void Poll()
    {
        try { Tick(); }
        catch (Exception e) { End(e.ToString()); }
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup > _deadline) { End("timeout at stage " + _stage); return; }
        if (_lastFrame == Time.frameCount) return;
        _lastFrame = Time.frameCount;
        if (_stage == 0)
        {
            if (MapleRideBoot.Active == null) return;
            Object.FindFirstObjectByType<MapleRideTitleScreen>().StartRide();
            _stage = 1; return;
        }
        if (_stage == 1)
        {
            var map = Object.FindFirstObjectByType<WorldMapHud>();
            if (map == null || !map.isOpen) return;
            map.Travel(RegionCatalog.Find(NagisaBayLook.RegionId));
            _stage = 2; return;
        }
        _ride = Object.FindFirstObjectByType<RideBootstrap>();
        if (_ride == null) return;
        var stream = _ride.GetComponent<RegionSceneStreamer>();
        if (stream != null && !string.IsNullOrEmpty(stream.Failure)) { End(stream.Failure); return; }
        if (_stage == 2)
        {
            if (MapleRideFlowDirector.Instance == null || MapleRideFlowDirector.Instance.phase != MapleRideFlowDirector.Phase.Riding) return;
            _ride.devices.HoldZeroPower = true; _ride.devices.acceptKeyboardEffort = false;
            float atM = 400f; float.TryParse(Environment.GetEnvironmentVariable("MR_NAGISA_AT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out atM);
            if (atM <= 0f) atM = 400f;
            _ride.JumpAlongRoute(atM / _ride.session.Course.Length);
            _stage = 3; _frames = 0; return;
        }
        if (_stage == 3)
        {
            if (stream != null && !stream.ReadyAt(_ride.session.WorldPosition)) return;
            if (++_frames < 90) return;
            var weather = WeatherDirector.Instance;
            if (weather != null) { weather.seed = 1234; weather.freeze = true; }
            foreach (var surf in Object.FindObjectsByType<NagisaSurf>(FindObjectsSortMode.None)) surf.driveShaderTime = false;
            Shader.SetGlobalFloat("_NagisaSurfTimeOn", 1f); Shader.SetGlobalFloat("_NagisaSurfTime", 12f);
            foreach (var r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) Actors[r] = r.sharedMaterials;
            Time.timeScale = 0f;
            _forwardRotation = _ride.rideCamera.transform.rotation;
            var follow = _ride.rideCamera.GetComponent<KuroFollowCamera>();
            if (follow != null) follow.enabled = false;
            // ScreenCapture.CaptureScreenshot produces no file in this batch editor. Render
            // the live gameplay camera normally into a target, including its overlay HUD.
            _capture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            _capture.Create();
            _ride.rideCamera.targetTexture = _capture;
            CanvasModes.Clear();
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                CanvasModes[canvas] = canvas.renderMode;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = _ride.rideCamera;
                canvas.planeDistance = 1f;
            }
            Audit("before"); _stage = 4; _frames = 0; return;
        }
        if (_stage == 4 || _stage == 6)
        {
            if (++_frames < 120) return;
            Samples.Clear(); _frames = 0; _stage++; return;
        }
        if (_stage == 5 || _stage == 7)
        {
            Samples.Add(Time.unscaledDeltaTime * 1000f);
            if (++_frames < 120) return;
            string label = _stage == 5 ? "before" : "after";
            Samples.Sort();
            Add($"{label}: static gameplay-camera frame interval median={Samples[60]:F2}ms p95={Samples[114]:F2}ms (120 warmup + 120 sample frames; editor/batch, simulation frozen)");
            SaveFrame(label);
            _seaLabel = label + "_sea";
            _resumeStage = _stage == 5 ? 8 : 9;
            float seaYaw = 65f; float.TryParse(Environment.GetEnvironmentVariable("MR_NAGISA_SEA_YAW"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seaYaw);
            _ride.rideCamera.transform.rotation = _forwardRotation * Quaternion.Euler(0f, seaYaw, 0f);
            _stage = 11; _frames = 0;
            return;
        }
        if (_stage == 11)
        {
            if (++_frames < 60) return;
            SaveFrame(_seaLabel);
            _ride.rideCamera.transform.rotation = _forwardRotation;
            _stage = _resumeStage; _frames = 0; return;
        }
        if (_stage == 8)
        {
            if (++_frames < 10) return;
            NagisaCoastalLook.CorrectionsEnabled = true;
            _stage = 6; _frames = 0; return;
        }
        if (_stage == 9)
        {
            if (++_frames < 10) return;
            Audit("after");
            if (NagisaCoastalLook.Instance == null || NagisaCoastalLook.Instance.MaterialCount == 0) { End("no coastal materials applied"); return; }
            var materials = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null).ToArray();
            if (!materials.Any(m => m.shader.name == "MapleRide/HDRP/NagisaOcean" && m.name.EndsWith("[Coastal]")) ||
                !materials.Any(m => m.name.Contains("BikeLane") && m.name.EndsWith("[Coastal]")))
            { End("ocean/lane copies not applied"); return; }
            foreach (var p in Actors)
                if (p.Key != null && !p.Value.SequenceEqual(p.Key.sharedMaterials)) { End("skinned actor material changed"); return; }
            var sun = _ride.regions.keyLight;
            if (sun == null || Mathf.Abs(Mathf.DeltaAngle(sun.transform.eulerAngles.x, 42f)) > 0.1f) { End("sun elevation did not persist"); return; }
            Add("PASS: actual streamed route, fixed camera/time/weather; skinned rider materials preserved; sun persists at 42deg.");
            NagisaCoastalLook.CorrectionsEnabled = false;
            _stage = 10; _frames = 0; return;
        }
        if (_stage == 10)
        {
            if (++_frames < 10) return;
            if (NagisaCoastalLook.Instance.MaterialCount != 0) { End("material copies not restored"); return; }
            Add("PASS: disabling restores original material slots and releases copies.");
            // The sky / pine / NPC close-up probes below are diagnostics (MR_NAGISA_PROBE=1); a normal run ends here.
            if (Environment.GetEnvironmentVariable("MR_NAGISA_SEA_CAM") == "1") { NagisaCoastalLook.CorrectionsEnabled = true; _variant = 0; _stage = 41; _frames = 0; return; }
            if (Environment.GetEnvironmentVariable("MR_NAGISA_CAFE_CAM") == "1") { NagisaCoastalLook.CorrectionsEnabled = true; _variant = 0; _stage = 40; _frames = 0; return; }
            if (Environment.GetEnvironmentVariable("MR_NAGISA_PROBE") != "1") { NagisaCoastalLook.CorrectionsEnabled = true; End(null); return; }
            NagisaCoastalLook.CorrectionsEnabled = true; _stage = 20; _frames = 0; return;
        }
        if (_stage == 41)
        {
            // sea QA cameras: from the road at the current distance, look out to either side and a long way off
            if (_variant == 0 && _frames == 0)
                foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    var m = r.sharedMaterial; if (m == null || m.shader == null || !m.shader.name.Contains("NagisaOcean")) continue;
                    NagisaRoadIndex.Project(r.bounds.center, out float rd, out float rl, out _, 4000f);
                    Add($"probe: OCEAN {r.name} shader={m.shader.name.Replace("MapleRide/HDRP/", "")} center={r.bounds.center} size={r.bounds.size} roadD={rd:F0} lateral={rl:F0} y={r.transform.position.y:F2} active={r.gameObject.activeInHierarchy}");
                }
            if (_variant == 0 && _frames == 0)
                foreach (float dd in new[] { 12450f, 13000f, 13600f, 14300f })
                {
                    var sd = NagisaRoadIndex.SideAt(dd); var pp = NagisaRoadIndex.PositionAt(dd);
                    var sb = new System.Text.StringBuilder($"probe: GROUND d={dd:F0} roadY={pp.y:F1}:");
                    foreach (float lat in new[] { -8f, -20f, -40f, -80f, -120f, -160f, -200f, -260f, -320f, -400f, -500f, -650f })
                    {
                        var q0 = pp + sd * lat;
                        sb.Append(Physics.Raycast(new Vector3(q0.x, 200f, q0.z), Vector3.down, out var hh, 400f, ~0, QueryTriggerInteraction.Ignore) ? $" {lat:F0}:{hh.point.y:F1}" : $" {lat:F0}:none");
                    }
                    Add(sb.ToString());
                }
            float d = _ride.session.DistanceM;
            var side = NagisaRoadIndex.SideAt(d); var pos = NagisaRoadIndex.PositionAt(d);
            string[] names = { "sea_r", "sea_l", "sea_r2", "sea_l2" };
            if (_variant >= names.Length) { End(null); return; }
            if (_frames == 0)
            {
                float sgn = (_variant % 2 == 0) ? 1f : -1f; bool high = _variant >= 2;
                var tr = _ride.rideCamera.transform;
                tr.position = pos + side * sgn * 6f + Vector3.up * (high ? 7f : 2.2f);
                tr.LookAt(pos + side * sgn * 700f + Vector3.up * (high ? -22f : 2.5f));
            }
            if (++_frames >= 45) { SaveFrame(names[_variant]); _variant++; _frames = 0; }
            return;
        }
        if (_stage == 40)
        {
            // cafe QA cameras: (along, lat, height) -> looking at (along, lat, height)
            var shots = new[]
            {
                (new Vector3(7f, 27.5f, 2.2f), new Vector3(0.5f, 17f, 1.0f), "cafe_a"),
                (new Vector3(-27f, 28f, 3.2f), new Vector3(-17f, 21f, 0.8f), "cafe_b"),
                (new Vector3(25f, 28f, 3.2f), new Vector3(8f, 19f, 0.8f), "cafe_c"),
                (new Vector3(-10f, 29f, 2.4f), new Vector3(-10f, 500f, -45f), "sea_a"),
                (new Vector3(-10f, 29f, 2.4f), new Vector3(260f, 520f, -20f), "sea_b"),
            };
            if (_variant >= shots.Length) { End(null); return; }
            if (_frames == 0)
            {
                var sh = shots[_variant];
                var tr = _ride.rideCamera.transform;
                tr.position = NagisaSummitCafe.Point(sh.Item1.x, sh.Item1.y, sh.Item1.z);
                tr.LookAt(NagisaSummitCafe.Point(sh.Item2.x, sh.Item2.y, sh.Item2.z));
            }
            if (++_frames >= 45) { SaveFrame(shots[_variant].Item3); _variant++; _frames = 0; }
            return;
        }
        if (_stage == 20)
        {
            if (++_frames < 20) return;
            _homePos = _ride.rideCamera.transform.position;
            _ride.rideCamera.transform.rotation = _forwardRotation * Quaternion.Euler(-24f, -35f, 0f);
            _stage = 23; _frames = 0; return;
        }
        if (_stage == 23)
        {
            if (++_frames < 90) return;
            SaveFrame("sky_up");
            _cv = 0; _stage = 26; _frames = 0; return;
        }
        if (_stage == 26)
        {
            if (_frames == 0)
            {
                if (_cv >= CloudVariants.Length) { _ride.rideCamera.transform.rotation = _forwardRotation * Quaternion.Euler(-12f, 40f, 0f); _stage = 24; return; }
                ApplyCloudVariant(_cv);
            }
            if (++_frames >= 40) { SaveFrame("sky_cv" + _cv); _cv++; _frames = 0; }
            return;
        }
        if (_stage == 27) { if (++_frames >= 70)
            {
                SaveFrame("npc_closeup" + _npcIdx);
                if (_npcIdx == 0 && HairVariants.Length > 0) { _hv = 0; _stage = 28; _frames = 0; return; }
                NextNpc();
            }
            return;
        }
        if (_stage == 28)
        {
            if (_frames == 0)
            {
                if (_hv >= HairVariants.Length) { NextNpc(); return; }
                ApplyHairVariant(_hv);
            }
            if (++_frames >= 40) { SaveFrame("hair_v" + _hv); _hv++; _frames = 0; }
            return;
        }
        if (_stage == 24)
        {
            if (++_frames < 60) return;
            SaveFrame("sky_up2");
            _ride.rideCamera.transform.rotation = _forwardRotation;
            ProbeTrees(); _stage = 21; _frames = 0; return;
        }
        if (_stage == 21) { if (++_frames >= 60) { SaveFrame("pine_asis"); _variant = 0; _stage = 25; _frames = 0; } return; }
        if (_stage == 25)
        {
            if (_frames == 0) { if (_variant >= Variants.Length) { ProbeNpc(); _stage = 27; _frames = 0; return; } ApplyVariant(_variant); }
            if (++_frames >= 45) { SaveFrame("pine_v" + _variant); _variant++; _frames = 0; }
            return;
        }
    }

    private static int _npcIdx;
    private static List<MinatoCrowdActor> _npcs = new List<MinatoCrowdActor>();
    private static void ProbeNpc()
    {
        var cam = _ride.rideCamera;
        _npcs = Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsSortMode.None).Where(a => a.isActiveAndEnabled)
            .OrderBy(a => (a.transform.position - cam.transform.position).sqrMagnitude).Take(3).ToList();
        Add($"probe: crowd actors in scene = {Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsSortMode.None).Length}");
        _npcIdx = -1; NextNpc(true);
    }
    private static void NextNpc(bool first = false)
    {
        _npcIdx++;
        if (_npcIdx >= _npcs.Count) { End(null); return; }
        var a = _npcs[_npcIdx];
        string path = a.name; for (var p = a.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
        Add($"probe: NPC {_npcIdx} {path} motion={a.motion} look={(a.GetComponent<PedestrianAppearance>() != null)} mapleLook={(a.GetComponent<MapleCityLook>() != null)} runner={(a.GetComponent<MapleCityRunner>() != null)}");
        foreach (var r in a.GetComponentsInChildren<Renderer>(true))
            Add($"probe:    {r.GetType().Name} {r.name} active={r.gameObject.activeInHierarchy} mats=[{string.Join(", ", r.sharedMaterials.Select(m => m == null ? "NULL" : m.name + ":" + m.shader.name.Replace("MapleRide/HDRP/", "")))}]");
        var headB = a.Bone("Head");
        Add($"probe: FIG root pos={a.transform.position} rot={a.transform.eulerAngles} scale={a.transform.lossyScale} head={(headB != null ? headB.position.ToString() : "null")} headRot={(headB != null ? headB.eulerAngles.ToString() : "-")}");
        var highT = a.transform.Find("LOD0 High Skinned"); var rig = highT != null ? highT.Find("Rigged Character") : null;
        var hairT = headB != null ? headB.Find("Ped Hair") : null;
        Add($"probe: ROT root={a.transform.eulerAngles} high={(highT != null ? highT.eulerAngles.ToString() : "-")} rig={(rig != null ? rig.eulerAngles.ToString() : "-")} hips={a.Bone("Hips")?.eulerAngles} head={(headB != null ? headB.eulerAngles.ToString() : "-")} hair={(hairT != null ? hairT.eulerAngles.ToString() : "-")} hairLocalPos={(hairT != null ? hairT.localPosition.ToString() : "-")} hairLocalRot={(hairT != null ? hairT.localEulerAngles.ToString() : "-")} headFwd={(headB != null ? headB.forward.ToString() : "-")} rootFwd={a.transform.forward} headUp={(headB != null ? headB.up.ToString() : "-")}");
        foreach (var r in a.GetComponentsInChildren<Renderer>(true))
            Add($"probe: FIGR {r.GetType().Name} '{r.name}' parent='{r.transform.parent?.name}' enabled={r.enabled} forceOff={r.forceRenderingOff} active={r.gameObject.activeInHierarchy} center={r.bounds.center} size={r.bounds.size}");
        foreach (var r in a.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.name != "Ped Hair" && !r.transform.parent.name.StartsWith("Ped Hair") && !r.name.StartsWith("PedHair")) continue;
            var mf = r.GetComponent<MeshFilter>(); var mesh = mf != null ? mf.sharedMesh : null;
            string meshInfo = mesh == null ? "NO MESH" : $"{mesh.name} v={mesh.vertexCount} colors={mesh.colors32.Length} tangents={mesh.tangents.Length} normals={mesh.normals.Length} uv={mesh.uv.Length} readable={mesh.isReadable}";
            Add($"probe:   HAIR {r.name} parent={r.transform.parent.name} {meshInfo} forceOff={r.forceRenderingOff} layer={r.gameObject.layer}");
            foreach (var m in r.sharedMaterials)
                Add($"probe:   HAIRMAT {m.name} shader={m.shader.name} color={(m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "-")} normalStr={(m.HasProperty("_NormalStrength") ? m.GetFloat("_NormalStrength") : -1)} tex={(m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null ? m.GetTexture("_MainTex").name : "NONE")} floor={(m.HasProperty("_MatteFloor") ? m.GetColor("_MatteFloor").ToString() : "-")} cull={(m.HasProperty("_Cull") ? m.GetFloat("_Cull") : -1)} shadeStr={(m.HasProperty("_ShadeStrength") ? m.GetFloat("_ShadeStrength") : -1)} charLight={(m.HasProperty("_CharacterLight") ? m.GetFloat("_CharacterLight") : -1)}");
        }
        var t = _ride.rideCamera.transform;
        var c = a.transform.position + Vector3.up * 1.0f;
        t.position = c + new Vector3(2.2f, 0.3f, 2.2f); t.LookAt(c);
        _stage = 27; _frames = 0;
    }
    private static int _hv;
    private static readonly string[] HairVariants = new string[0];   // hair bisect list; empty = skip
    private static Material[] _hairOrig;
    private static void ApplyHairVariant(int i)
    {
        var a = _npcs[0];
        MeshRenderer hair = null; Material body = null;
        foreach (var r in a.GetComponentsInChildren<MeshRenderer>(true)) if (r.name == "Ped Hair") hair = r;
        foreach (var r in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name == "Mesh_0" && r.enabled) foreach (var m in r.sharedMaterials) if (m != null && body == null) body = m;
        if (hair == null) { Add("probe: no hair for variant " + i); return; }
        if (_hairOrig == null) _hairOrig = hair.sharedMaterials;
        var slots = new Material[_hairOrig.Length];
        for (int k = 0; k < slots.Length; k++)
        {
            var m = new Material(_hairOrig[k]);
            switch (i)
            {
                case 0: m.SetTexture("_MainTex", Texture2D.whiteTexture); break;
                case 1: m = new Material(body); break;
                case 2: m = new Material(Shader.Find("HDRP/Unlit")); m.SetColor("_UnlitColor", Color.red); break;
                case 3: m.SetFloat("_Cull", 0f); break;
                case 4: m.SetColor("_ShadeColor", Color.white); m.SetFloat("_ShadeStrength", 1f); break;
            }
            slots[k] = m;
        }
        hair.sharedMaterials = slots;
        Add($"probe: hair variant {i} {HairVariants[i]}");
    }
    private static int _cv;
    private static Volume _cloudTest;
    private static readonly string[] CloudVariants = new string[0];   // runtime cloud bisect list; empty = capture the shipped sky
    private static void ApplyCloudVariant(int i)
    {
        if (_cloudTest == null)
        {
            var go = new GameObject("~cloud test");
            _cloudTest = go.AddComponent<Volume>(); _cloudTest.isGlobal = true; _cloudTest.priority = 200f;
            _cloudTest.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        }
        var prof = _cloudTest.sharedProfile;
        if (prof.TryGet<CloudLayer>(out var old)) prof.Remove<CloudLayer>();
        var c = prof.Add<CloudLayer>(true);
        c.opacity.Override(1f);
        var ours = AssetDatabase.LoadAssetAtPath<Texture2D>(NagisaBayEnvironment.CloudMapPath);
        c.layerA.cloudMap.Override(ours);
        switch (i)
        {
            case 1: c.upperHemisphereOnly.Override(false); break;
            case 2: c.layerA.opacityR.Override(1f); c.layerA.opacityG.Override(0f); c.layerA.opacityB.Override(0f); c.layerA.opacityA.Override(0f); c.layerA.lighting.Override(false); break;
            case 3: c.layers.Override(CloudMapMode.Double); c.layerB.cloudMap.Override(ours); c.layerA.exposure.Override(2.5f); break;
        }
        Add("probe: cloud variant " + i + " " + CloudVariants[i]);
    }

    private static int _variant;
    private static Material[] _pineOriginals;
    private static readonly string[] Variants = new string[0];   // bisect list; empty = just capture pine_asis

    private static void ApplyVariant(int i)
    {
        if (_pine == null) return;
        if (_pineOriginals == null) _pineOriginals = _pine.sharedMaterials;
        var slots = new Material[_pineOriginals.Length];
        for (int k = 0; k < slots.Length; k++)
        {
            var m = new Material(_pineOriginals[k]);
            switch (Variants[i])
            {
                case "matteFloor": m.SetColor("_MatteFloor", new Color(0.26f, 0.20f, 0.14f)); break;
                case "tintVariation=0.06": m.SetFloat("_TintVariation", 0.06f); break;
                case "color=white": m.SetColor("_Color", Color.white); break;
                case "normalStrength=0": m.SetFloat("_NormalStrength", 0f); break;
                case "gloss/rim/spec=palm": m.SetFloat("_Gloss", 0.08f); m.SetFloat("_RimStrength", 0.12f); m.SetFloat("_SpecStrength", 0.04f); break;
                case "foliageShaderLeaf": if (k == 1) m.shader = Shader.Find("MapleRide/HDRP/Foliage"); break;
                case "noShadowReceive": break;
            }
            slots[k] = m;
        }
        _pine.sharedMaterials = slots;
        _pine.receiveShadows = Variants[i] != "noShadowReceive";
        Add($"probe: variant {i} {Variants[i]}");
    }

    private static MeshRenderer _pine;
    private static Vector3 _homePos;
    // Diagnostic (2026-10-02): Japanese black pines render pure black in streamed play.
    private static void ProbeTrees()
    {
        var cam = _ride.rideCamera;
        var stack = HDCamera.GetOrCreate(cam).volumeStack;
        var ve = stack.GetComponent<UnityEngine.Rendering.HighDefinition.VisualEnvironment>();
        var cl = stack.GetComponent<UnityEngine.Rendering.HighDefinition.CloudLayer>();
        var gs = stack.GetComponent<UnityEngine.Rendering.HighDefinition.GradientSky>();
        Add($"probe: sky stack skyType={ve.skyType.value} cloudType={ve.cloudType.value} windSpeed={ve.windSpeed.value} | cloud opacity={cl.opacity.value} active={cl.active} layers={cl.layers.value} res={cl.resolution.value} A.map={(cl.layerA.cloudMap.value != null ? cl.layerA.cloudMap.value.name : "NULL")} A.R={cl.layerA.opacityR.value} A.alt={cl.layerA.altitude.value} A.light={cl.layerA.lighting.value} B.G={cl.layerB.opacityG.value} | gradient top={gs.top.value} mid={gs.middle.value} bottom={gs.bottom.value}");
        var hdc = cam.GetComponent<HDAdditionalCameraData>();
        Add($"probe: camera clearColorMode={hdc.clearColorMode} bg={cam.backgroundColor} clearFlags={cam.clearFlags} volumeMask={hdc.volumeLayerMask.value} camLayer={cam.gameObject.layer}");
        // Which renderers cover the black pixels seen in after.png? (screen px, y down, 1600x900)
        foreach (var px in new[] { new Vector2(572, 392), new Vector2(477, 399), new Vector2(754, 366), new Vector2(720, 371) })
        {
            var vp = new Vector2(px.x / 1600f, 1f - px.y / 900f);
            var hits = new System.Collections.Generic.List<(float d, string n)>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var bnd = r.bounds; Vector2 mn = new Vector2(9, 9), mx = new Vector2(-9, -9); bool front = true;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bnd.center + Vector3.Scale(bnd.extents, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1));
                    var v = cam.WorldToViewportPoint(corner); if (v.z <= 0) { front = false; break; }
                    mn = Vector2.Min(mn, v); mx = Vector2.Max(mx, v);
                }
                if (front && vp.x >= mn.x && vp.x <= mx.x && vp.y >= mn.y && vp.y <= mx.y)
                    hits.Add(((bnd.center - cam.transform.position).magnitude, r.name + " | " + string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m != null ? m.name + ":" + m.shader.name.Replace("MapleRide/HDRP/", "") : "NULL"))));
            }
            hits.Sort((x, y) => x.d.CompareTo(y.d));
            Add($"probe: at px {px} candidates (nearest first):");
            for (int i = 0; i < Mathf.Min(6, hits.Count); i++) Add($"probe:    {hits[i].d:F0} m  {hits[i].n}");
        }
        float bestD = 1e9f;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || !(r.name.Contains("BlackPine") || r.name == "Black Pine")) continue;
            float d = (r.bounds.center - cam.transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; _pine = r; }
        }
        var best = _pine;
        if (best == null) { Add("probe: no BlackPine renderer active"); return; }
        var mpb = new MaterialPropertyBlock(); best.GetPropertyBlock(mpb);
        Add($"probe: nearest {best.name} dist={Mathf.Sqrt(bestD):F1} propBlock={best.HasPropertyBlock()} empty={mpb.isEmpty} lodGroup={(best.GetComponentInParent<LODGroup>() != null)} scene={best.gameObject.scene.name} pos={best.transform.position}");
        foreach (var m in best.sharedMaterials)
        {
            if (m == null) { Add("probe: NULL material slot"); continue; }
            Add($"probe: mat {m.name} shader={m.shader.name} color={(m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "-")} floor={(m.HasProperty("_MatteFloor") ? m.GetColor("_MatteFloor").ToString() : "-")} tex={(m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null ? m.GetTexture("_MainTex").name : "NONE")} cull={(m.HasProperty("_Cull") ? m.GetFloat("_Cull") : -1)}");
        }
        var t = cam.transform;
        Vector3 c = best.bounds.center;
        t.position = c + new Vector3(9f, 1f, 9f); t.LookAt(c);
    }

    private static void SwapPineMaterials()
    {
        if (_pine == null) return;
        Material goodBark = null, goodLeaf = null;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.name.Contains("CoconutPalm") && !r.name.Contains("CanopyTree")) continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                if (goodBark == null && m.name.Contains("PalmBark")) goodBark = m;
                if (goodLeaf == null && m.name.Contains("Canopy")) goodLeaf = m;
            }
            if (goodBark != null && goodLeaf != null) break;
        }
        Add($"probe: swapping to bark={goodBark?.name} leaf={goodLeaf?.name}");
        var slots = _pine.sharedMaterials;
        if (slots.Length > 0 && goodBark != null) slots[0] = goodBark;
        if (slots.Length > 1 && goodLeaf != null) slots[1] = goodLeaf;
        _pine.sharedMaterials = slots;
    }

    private static void Audit(string label)
    {
        var camera = _ride.rideCamera;
        var hd = camera != null ? camera.GetComponent<HDAdditionalCameraData>() : null;
        Add($"{label}: pipeline={GraphicsSettings.currentRenderPipeline?.name} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} camera={camera?.name} hdrpCamera={hd != null} customFrameSettings={hd != null && hd.customRenderingSettings} route={_ride.session.courseId} metres={_ride.session.DistanceM:F1}");
        var sun = _ride.regions.keyLight;
        var hdl = sun != null ? sun.GetComponent<HDAdditionalLightData>() : null;
        Add($"{label}: sun={sun?.name} euler={sun?.transform.eulerAngles} intensity={sun?.intensity} shadows={sun?.shadows} hdrpDimmer={hdl?.shadowDimmer}");
        Add($"{label}: ambient={Shader.GetGlobalVector("_MR_AmbSky")} shaderSun={Shader.GetGlobalVector("_MR_SunColor")}");
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).Where(v => v.enabled && v.gameObject.activeInHierarchy))
            Add($"{label}: volume {v.name} priority={v.priority} weight={v.weight} profile={v.sharedProfile?.name}");
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
        var mats = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
        foreach (var group in mats.GroupBy(m => m.shader != null ? m.shader.name : "MISSING").OrderByDescending(g => g.Count()))
            Add($"{label}: shader {group.Key} materials={group.Count()}");
        int bad = mats.Count(m => m.shader == null || !m.shader.isSupported || m.shader.name.Contains("InternalError"));
        Add($"{label}: missing/unsupported/error materials={bad}, coastalCopies={NagisaCoastalLook.Instance?.MaterialCount}");
        if (bad > 0) _failed = true;
    }

    private static void Add(string text) { Report.Add(text); Debug.Log("[nagisa-coastal-qa] " + text); }
    private static void SaveFrame(string label)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = _capture;
        var texture = new Texture2D(_capture.width, _capture.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, _capture.width, _capture.height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Output + "/" + label + ".png", texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        RenderTexture.active = previous;
        Add(label + ": saved gameplay-camera PNG at 1600x900 with HUD.");
    }
    private static void End(string failure)
    {
        if (failure != null) { _failed = true; Add("FAIL: " + failure); }
        File.WriteAllLines(Output + "/audit.txt", Report);
        if (_ride != null && _ride.rideCamera != null) _ride.rideCamera.targetTexture = null;
        foreach (var pair in CanvasModes) if (pair.Key != null) pair.Key.renderMode = pair.Value;
        Time.timeScale = 1f;
        EditorApplication.update -= Poll;
        EditorApplication.ExitPlaymode();
    }
}
