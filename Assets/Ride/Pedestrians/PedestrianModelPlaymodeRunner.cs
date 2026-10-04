using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// PEDESTRIAN MODEL-SWAP play-mode proof (claude-peds, 2026-09-26). Driven by the editor entry
/// <c>PedestrianModelCapture.Run</c> (model set ON) / <c>PedestrianModelCapture.RunOff</c> (pref
/// cleared: the donor look). Same camera placements in both modes. Frames:
/// reference/good_graphics/pedestrians_v2/{on|off}_*.png.
/// Checks (ON): models swapped in Maple City, every swapped figure dressed, height within 6% of its
/// donor, walk bones resolved, walkers moving, rendered sole on the pavement, hats / bags / props
/// parented under the NEW rig. Checks (OFF): no swap component, no model objects anywhere.
/// </summary>
[DefaultExecutionOrder(1000)]
public class PedestrianModelPlaymodeRunner : MonoBehaviour
{
    public string outDir;
    public bool onMode = true;
    public static bool Finished, Failed;
    int _pass, _fail;
    RideBootstrap _boot;
    Camera _cam;
    KuroFollowCamera _follow;
    public string framePrefix = "";
    string Tag => framePrefix + (onMode ? "on_" : "off_");

    void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        Debug.Log((ok ? "[peds-model-play] PASS " : "[peds-model-play] FAIL ") + what);
    }
    void Info(string what) => Debug.Log("[peds-model-play] INFO " + what);

    IEnumerator Start()
    {
        Finished = Failed = false;
        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Debug.LogError("[peds-model-play] no RideBootstrap"); Failed = Finished = true; yield break; }
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        _follow = _cam != null ? _cam.GetComponent<KuroFollowCamera>() : null;
        _boot.devices.acceptKeyboardEffort = false;
        yield return null;

        if (_boot.regions == null || !_boot.regions.FastTravel(RegionCatalog.MapleCity))
            Debug.LogWarning("[peds-model-play] could not fast travel to Maple City");
        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse(RegionCatalog.Find(RegionCatalog.MapleCity).BuiltCourseId);
        yield return Seek(900f, 0f, 30);
        var dir = PedestrianDirector.Instance;
        if (dir == null) { Check(false, "PedestrianDirector exists"); Done(); yield break; }
        dir.Scan();
        var life = GameObject.Find("Maple City Life");
        var brains = dir.Brains.Where(b => b != null && b.gameObject.activeInHierarchy && (life == null || b.transform.IsChildOf(life.transform))).ToList();
        var swaps = FindObjectsByType<PedestrianModelSwap>(FindObjectsSortMode.None);
        Info($"model set pref='{PlayerPrefs.GetString(PedestrianModelLibrary.ModelSetPref, "")}', maple brains {brains.Count}, swapped {swaps.Length} ({PedestrianModelSwap.UsageSummary()})");

        bool kuro = onMode && FindObjectsByType<PedestrianKuroTag>(FindObjectsSortMode.None).Length > 0;
        if (kuro) KuroChecks(brains);
        else if (onMode) StaticChecks(brains, swaps);
        else
        {
            Check(swaps.Length == 0, $"flag OFF: {swaps.Length} PedestrianModelSwap components (want 0)");
            int modelObjs = FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith("LOD0 Model ("));
            Check(modelObjs == 0, $"flag OFF: {modelObjs} 'LOD0 Model (..)' objects (want 0)");
            Check(brains.All(b => b.look == null || string.IsNullOrEmpty(b.look.modelId)), "flag OFF: every figure dressed by the donor wardrobe path");
        }

        // ---- gameplay views
        foreach (float m in new[] { 900f, 1600f })
        {
            yield return Seek(m, 0.55f, 40);
            yield return CaptureNowCo($"{Tag}chase_{m:0000}m");
            yield return StreetView(m);
        }
        yield return Seek(900f, 0.55f, 20);
        // let the brains walk a little (sim + contact)
        for (int i = 0; i < 90; i++) { _boot.devices.EffortInput = 0f; yield return null; }
        if (onMode && !kuro) ContactChecks(brains);
        if (kuro) KuroContact(brains);

        // ---- close-ups 3-5 m: walkers and idlers (swapped figures in ON mode, donors in OFF mode)
        yield return CloseUps(brains);

        Debug.Log($"[peds-model-play] RESULT {_pass}/{_pass + _fail} checks passed ({(onMode ? "model set ON" : "flag OFF")})");
        Done();
    }

    void Done() { Failed = _fail > 0; Finished = true; }

    void StaticChecks(List<PedestrianBrain> brains, PedestrianModelSwap[] swaps)
    {
        Check(swaps.Length > 20, $"{swaps.Length} Maple pedestrians wear a library model ({PedestrianModelSwap.UsageSummary()}; failed {PedestrianModelSwap.Failed})");
        if (swaps.Length == 0) return;
        var swapped = brains.Where(b => b.GetComponent<PedestrianModelSwap>() != null).ToList();
        Check(swapped.All(b => b.look != null && b.look.applied && !string.IsNullOrEmpty(b.look.modelId)), $"{swapped.Count} swapped figures dressed through ApplyModel");
        foreach (var grp in swaps.GroupBy(s => s.modelId))
        {
            var s = grp.First();
            var smr = s.skins.FirstOrDefault(k => k != null);
            string fixes = "";
            if (smr != null)
            {
                var m = new Mesh(); smr.BakeMesh(m, true);
                fixes = $"bakeUnitFix {MinatoCrowdActor.BakeUnitFix(smr, m)}, reboundUnitFix {MinatoCrowdActor.ReboundUnitFix(smr, m)}";
                Destroy(m);
            }
            Info($"{grp.Key}: {grp.Count()} figures; e.g. {s.name}: donor {s.donorHeight:0.000} m, target {s.modelHeight:0.000} m, actual {ActualHeight(s):0.000} m; {fixes}; variant '{s.variantId}'");
        }
        float worst = swaps.Max(s => Mathf.Abs(ActualHeight(s) / Mathf.Max(0.01f, s.modelHeight) - 1f));
        Check(worst < 0.06f, $"model height matches target (donor x heightScale) within 6%: worst {worst * 100f:0.0}%; donor heights {swaps.Min(s => s.donorHeight):0.00}-{swaps.Max(s => s.donorHeight):0.00} m");
        var s0 = swaps[0];
        var a0 = s0.GetComponent<MinatoCrowdActor>();
        bool bonesOk = new[] { "Hips", "Spine02", "Head", "LeftHand", "RightHand", "LeftFoot", "RightFoot" }
            .All(n => a0.Bone(n) != null && a0.Bone(n).IsChildOf(s0.modelRoot));
        Check(bonesOk, $"alias map: walk bones resolve INTO the model rig (source {s0.boneSource}; Head='{a0.Bone("Head")?.name}', LeftFoot='{a0.Bone("LeftFoot")?.name}')");
        int hats = 0, hatsOk = 0, bags = 0, bagsOk = 0;
        foreach (var s in swaps)
        {
            foreach (Transform t in s.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Ped Hat") { hats++; if (t.IsChildOf(s.modelRoot)) hatsOk++; }
                if (t.name.StartsWith("Ped Backpack") || t.name.StartsWith("Ped Shoulder") || t.name.StartsWith("Ped Shopping Bag"))
                { if (t.parent != null && (t.parent.name.Contains("Hand") || t.parent.name.Contains("Spine") || t.parent.name.Contains("Hips") || t.parent.name.Contains("Chest"))) { bags++; if (t.IsChildOf(s.modelRoot)) bagsOk++; } }
            }
        }
        Check(hats > 0 && hatsOk == hats, $"hats on the model's head bone: {hatsOk}/{hats}");
        Check(bags > 0 && bagsOk == bags, $"bags on the model's spine/hips/hand bones: {bagsOk}/{bags}");
        var hidden = swaps.Sum(s => s.GetComponentsInChildren<Renderer>(true).Count(r => !r.transform.IsChildOf(s.modelRoot) && !r.forceRenderingOff && r.enabled && r.name.IndexOf("Shadow", System.StringComparison.OrdinalIgnoreCase) < 0 && !r.name.StartsWith("Ped ")));
        Check(hidden == 0, $"donor renderers still visible on swapped figures: {hidden}");
    }

    static float ActualHeight(PedestrianModelSwap s)
    {
        // world AABB of the skins (the runner forces updateWhenOffscreen so the bounds are live)
        float hi = float.MinValue;
        foreach (var smr in s.skins)
            if (smr != null) { bool was = smr.updateWhenOffscreen; smr.updateWhenOffscreen = true; hi = Mathf.Max(hi, smr.bounds.max.y); smr.updateWhenOffscreen = was; }
        return hi > float.MinValue ? hi - s.transform.position.y : 0f;
    }

    void ContactChecks(List<PedestrianBrain> brains)
    {
        var cam = _cam.transform.position;
        var near = brains.Where(b => b.GetComponent<PedestrianModelSwap>() != null && b.kind != PedestrianBrain.PathKind.Spot &&
                                     (b.transform.position - cam).sqrMagnitude < 60f * 60f).Take(12).ToList();
        if (near.Count == 0) { Info("no swapped walkers within 60 m for the contact check"); return; }
        int ok = 0; var reasons = new List<string>();
        foreach (var b in near)
        {
            if (b.actor.RuntimeRenderedContactPass(out var why)) ok++;
            else reasons.Add($"{b.name} [{b.GetComponent<PedestrianModelSwap>().modelId}]: {why}");
        }
        Info("contact detail: " + b0(near));
        Check(ok >= Mathf.CeilToInt(near.Count * 0.75f), $"rendered sole on the pavement for {ok}/{near.Count} near swapped walkers{(reasons.Count > 0 ? " (" + string.Join("; ", reasons.Take(3)) + ")" : "")}");
        float moved = 0f;
        foreach (var b in near) moved = Mathf.Max(moved, b.actor.gaitWeight);
        Check(moved > 0.3f, $"swapped walkers are walking (max gait weight {moved:0.00})");
    }

    static string b0(List<PedestrianBrain> near) => near[0].actor.RuntimeContactSummary();

    IEnumerator CloseUps(List<PedestrianBrain> brains)
    {
        if (_follow != null) _follow.enabled = false;
        var cam = _cam.transform.position;
        IEnumerable<PedestrianBrain> pool = brains.Where(b => b.look != null && b.leader == null && b.follower == null);
        if (onMode) pool = pool.Where(b => b.GetComponent<PedestrianModelSwap>() != null || b.GetComponent<PedestrianKuroTag>() != null);
        var ordered = pool.OrderBy(b => (b.transform.position - cam).sqrMagnitude).ToList();
        var walkers = ordered.Where(b => b.kind != PedestrianBrain.PathKind.Spot).Take(3).ToList();
        if (onMode)
        {
            // at least one close-up per library model (nearest walker wearing it)
            var perModel = ordered.Where(b => b.kind != PedestrianBrain.PathKind.Spot)
                                  .GroupBy(b => b.GetComponent<PedestrianModelSwap>() != null ? b.GetComponent<PedestrianModelSwap>().modelId : "kuro:" + b.GetComponent<PedestrianKuroTag>().hair)
                                  .Select(g => g.First()).ToList();
            walkers = perModel.Concat(walkers.Where(w => !perModel.Contains(w))).Take(Mathf.Max(3, perModel.Count)).ToList();
        }
        var idlers = ordered.Where(b => !walkers.Contains(b)).Take(2).ToList();
        int k = 0;
        foreach (var b in walkers)
        {
            float dist = 3.2f + 0.8f * k;
            for (int f = 0; f < 45; f++)
            {
                var fw = b.transform.forward;
                _cam.transform.position = b.transform.position + fw * dist * 0.8f + b.transform.right * dist * 0.6f + Vector3.up * 1.1f;
                _cam.transform.LookAt(b.transform.position + Vector3.up * 0.6f);
                yield return null;
            }
            yield return CaptureNowCo($"{Tag}close_walk_{k}{ModelTag(b)}");
            k++;
        }
        var idles = new[] { PedestrianBrain.Idle.Phone, PedestrianBrain.Idle.Wait };
        k = 0;
        foreach (var b in idlers.Concat(walkers.Take(Mathf.Max(0, 2 - idlers.Count))))
        {
            b.ForceIdle(idles[k % idles.Length], 6f);
            for (int f = 0; f < 50; f++)
            {
                var fw = b.transform.forward;
                _cam.transform.position = b.transform.position + fw * 3.0f + b.transform.right * 1.6f + Vector3.up * 1.15f;
                _cam.transform.LookAt(b.transform.position + Vector3.up * 0.6f);
                yield return null;
            }
            yield return CaptureNowCo($"{Tag}close_idle_{k}_{idles[k % idles.Length]}{ModelTag(b)}");
            k++;
        }
        if (_follow != null) _follow.enabled = true;
    }

    static string ModelTag(PedestrianBrain b)
    {
        var s = b.GetComponent<PedestrianModelSwap>();
        if (s != null) return "_" + s.modelId;
        var k = b.GetComponent<PedestrianKuroTag>();
        return k != null ? $"_kuro_{(k.kitName != "" ? k.kitName : "donorkit")}_{(k.hairName != "" ? k.hairName : "cap")}" : "";
    }

    void KuroChecks(List<PedestrianBrain> brains)
    {
        var tags = brains.Select(b => b.GetComponent<PedestrianKuroTag>()).Where(t => t != null).ToList();
        Info($"kuro-rider look: {tags.Count} figures; kits {tags.Count(t => t.kit >= 0)}, hair {tags.Count(t => t.hair >= 0)}, blinking {tags.Count(t => t.blinks)}; distinct looks {tags.Select(t => t.signature).Distinct().Count()}");
        Check(tags.Count > 200, $"{tags.Count} Maple pedestrians use the Kuro rider body look (fbx swaps {FindObjectsByType<PedestrianModelSwap>(FindObjectsSortMode.None).Length})");
        int blink = tags.Count(t => t.blinks);
        Check(blink > tags.Count / 2, $"RiderBlink eyelids attached on {blink}/{tags.Count}");
        int ident = 0, pairs = 0;
        for (int i = 0; i < tags.Count; i++)
            for (int j = i + 1; j < tags.Count; j++)
            {
                if ((tags[i].transform.position - tags[j].transform.position).sqrMagnitude > 4f * 4f) continue;
                pairs++;
                if (tags[i].signature == tags[j].signature) ident++;
            }
        Check(ident == 0, $"identical (body look + kit + hair) neighbours within 4 m: {ident} of {pairs} close pairs");
        int hair = 0, hairOk = 0;
        foreach (var t in tags)
            foreach (Transform h in t.GetComponentsInChildren<Transform>(true))
                if (h.name == "Ped Hair") { hair++; var a = t.GetComponent<MinatoCrowdActor>(); if (a != null && h.parent == a.Bone("Head")) hairOk++; }
        if (hair > 0) Check(hairOk == hair, $"hair meshes parented to the Head bone: {hairOk}/{hair}");
        else Info("no hair meshes in the library yet (PedHair_*.fbx not delivered): donor hair caps kept");
        var a0 = tags[0].GetComponent<MinatoCrowdActor>();
        var map = PedestrianBoneMap.Resolve(a0.rigRoot, out string src);
        Check(PedestrianBoneMap.MissingForWalk(map).Count == 0 && map["LeftFoot"] == a0.Bone("LeftFoot"),
              $"alias map resolves the Kuro rig 1:1 ({src}): Hips=\u0027{map["Hips"].name}\u0027 Spine02=\u0027{map["Spine02"].name}\u0027 LeftFoot=\u0027{map["LeftFoot"].name}\u0027");
    }

    void KuroContact(List<PedestrianBrain> brains)
    {
        var cam = _cam.transform.position;
        var near = brains.Where(b => b.GetComponent<PedestrianKuroTag>() != null && b.kind != PedestrianBrain.PathKind.Spot &&
                                     (b.transform.position - cam).sqrMagnitude < 60f * 60f).Take(12).ToList();
        if (near.Count == 0) { Info("no kuro walkers within 60 m"); return; }
        int ok = near.Count(b => b.actor.RuntimeRenderedContactPass(out _));
        Check(ok >= Mathf.CeilToInt(near.Count * 0.75f), $"rendered sole on the pavement for {ok}/{near.Count} near Kuro-rider walkers");
    }

    IEnumerator StreetView(float m)
    {
        if (_follow != null) _follow.enabled = false;
        var rider = _follow != null && _follow.target != null ? _follow.target.position : _cam.transform.position;
        var lanes = FindObjectsByType<MapleCityWalkLanes>(FindObjectsSortMode.None).FirstOrDefault();
        if (lanes != null)
        {
            int lane = lanes.Valid(1) ? 1 : 0;
            float bestS = 0f, best = float.MaxValue;
            var L = lanes.lanes[lane];
            for (int k = 0; k < L.points.Length; k++)
            {
                float d = (L.points[k] - rider).sqrMagnitude;
                if (d < best) { best = d; bestS = L.dist[k]; }
            }
            var p = lanes.Sample(lane, bestS, out var dir);
            for (int f = 0; f < 8; f++)
            {
                _cam.transform.position = p - dir * 4.2f + Vector3.up * 1.35f;
                _cam.transform.LookAt(p + dir * 5f + Vector3.up * 0.75f);
                yield return null;
            }
            yield return CaptureNowCo($"{Tag}street_{m:0000}m");
        }
        if (_follow != null) _follow.enabled = true;
    }

    IEnumerator Seek(float d, float effort, int frames)
    {
        _boot.session.SeekTo(d);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        for (int f = 0; f < frames; f++) { _boot.devices.EffortInput = effort; yield return null; }
        SnapCamera();
    }

    void SnapCamera()
    {
        if (_follow == null || _follow.target == null || !_follow.enabled) return;
        var t = _follow.target;
        _cam.transform.position = t.position + t.TransformDirection(_follow.offset);
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    // captures are taken in LateUpdate (WaitForEndOfFrame never resumes in batchmode)
    IEnumerator CaptureNowCo(string name)
    {
        _pendingCapture = name;
        while (_pendingCapture != null) yield return null;
    }
    string _pendingCapture;
    void LateUpdate()
    {
        if (_pendingCapture == null) return;
        var n = _pendingCapture;
        try { CaptureNow(n); } finally { _pendingCapture = null; }
    }

    void CaptureNow(string name)
    {
        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        var prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        rt.Release();
        Destroy(rt);
        Debug.Log($"[peds-model-play] frame {name}");
    }
}
