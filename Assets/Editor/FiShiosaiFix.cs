using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// PLAYER/INSTANCE-SCOPED fix harness for the Shiosai test rider (the POC anime Kuro staged by
/// ShiosaiRealisticRiderPoc onto the SHARED SakuraPass.unity scene, shown while RegionDirector is
/// on the Shiosai region). Diagnoses and tunes ONLY the POC object: its hand IK seating and its
/// CLONED character/skin materials. Never edits the shared bike GLB, the rig GLB, or any NPC.
///
///   FiShiosaiFix.Dump        - report POC config + material inventory (read-only).
///   FiShiosaiFix.RenderCur   - render current POC state at the user's angle (read-only).
///   FiShiosaiFix.SweepHands  - render a grid of handTargetLocalOffset candidates (read-only).
///   FiShiosaiFix.CommitHands - MR_HOFF="x,y,z": set POC hand offset + SAVE.
///   FiShiosaiFix.SweepSkin   - render skin-tone candidates (read-only).
///   FiShiosaiFix.CommitSkin  - MR_SKIN="albedo,warm": retint cloned skin mat + SAVE.
/// </summary>
public static class FiShiosaiFix
{
    const string PocName = "Shiosai POC Realistic Rider";
    const string OutRoot = "../reference/good_graphics/fi_shiosai_fix/";
    static string OutRootOverride = null;

    static string ScenePath => ShiosaiCoastEnvironment.ScenePath;

    static void OpenScene()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    static RegionDirector ToShiosai()
    {
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = RegionCatalog.ShiosaiCoast;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
        return regions;
    }

    static Transform FindPoc()
    {
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == PocName) return t;
        return null;
    }

    static Transform Find(Transform p, string n) =>
        p.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);

    static void SolvePoc(Transform poc)
    {
        var rig = poc.GetComponent<KuroBikeRig>();
        if (rig != null)
        {
            rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
            rig.ForceSolveOnce();
        }
        foreach (var s in poc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        { s.forceMatrixRecalculationPerRender = true; s.updateWhenOffscreen = true; }
    }

    static string Metrics(Transform poc)
    {
        string one(string side)
        {
            var arm = Find(poc, side + "Arm");
            var fore = Find(poc, side + "ForeArm");
            var hand = Find(poc, side + "Hand");
            var hood = Find(poc, side == "Left" ? "Hood_L" : "Hood_R");
            if (!arm || !fore || !hand || !hood) return side + ":missing";
            float elbow = 180f - Vector3.Angle(fore.position - arm.position, hand.position - fore.position);
            return $"{side[0]} elbow={elbow:F1} h2hood={Vector3.Distance(hand.position, hood.position) * 1000f:F0}mm x={hand.position.x:F3}";
        }
        return one("Left") + "  " + one("Right");
    }

    // Ground-truth: measure where the VISIBLE glove mesh actually sits (baked skinned verts
    // dominated by the hand bones) vs the Hood sockets, in world X. The hand-BONE metric can read
    // "on the hood" while the skinned glove renders inboard, so this is what the eye actually sees.
    public static void DumpGlove()
    {
        OpenScene();
        var player = GameObject.Find("Kuro on Sakura Pass");
        var rig = player.GetComponent<KuroBikeRig>();
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null) { regions.Resolve(); regions.currentRegionId = RegionCatalog.SakuraPass; regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience(); }
        SolvePoc(player.transform);
        var hoodL = Find(player.transform, "Hood_L"); var hoodR = Find(player.transform, "Hood_R");
        var handLb = Find(player.transform, "LeftHand"); var handRb = Find(player.transform, "RightHand");
        Debug.Log($"[shfix] HOOD_L x={hoodL.position.x:F4}  HOOD_R x={hoodR.position.x:F4}  (span={Mathf.Abs(hoodL.position.x-hoodR.position.x):F4})");
        Debug.Log($"[shfix] BONE LeftHand x={handLb.position.x:F4}  RightHand x={handRb.position.x:F4}");
        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            int li = System.Array.FindIndex(smr.bones, b => b && b.name == "LeftHand");
            int ri = System.Array.FindIndex(smr.bones, b => b && b.name == "RightHand");
            if (li < 0 && ri < 0) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var verts = baked.vertices;
            var bw = smr.sharedMesh.boneWeights;
            var l2w = smr.transform.localToWorldMatrix;
            GloveStat(smr.name, "L", verts, bw, li, l2w);
            GloveStat(smr.name, "R", verts, bw, ri, l2w);
            Object.DestroyImmediate(baked);
        }
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void GloveStat(string mesh, string side, Vector3[] verts, BoneWeight[] bw, int bone, Matrix4x4 l2w)
    {
        if (bone < 0) return;
        int n = 0; float sx = 0, minx = 9999, maxx = -9999, sz = 0, sy = 0;
        for (int i = 0; i < verts.Length && i < bw.Length; i++)
        {
            var w = bw[i];
            float wt = (w.boneIndex0 == bone ? w.weight0 : 0) + (w.boneIndex1 == bone ? w.weight1 : 0)
                     + (w.boneIndex2 == bone ? w.weight2 : 0) + (w.boneIndex3 == bone ? w.weight3 : 0);
            if (wt < 0.5f) continue;
            var p = l2w.MultiplyPoint3x4(verts[i]);
            n++; sx += p.x; sy += p.y; sz += p.z; if (p.x < minx) minx = p.x; if (p.x > maxx) maxx = p.x;
        }
        if (n == 0) { Debug.Log($"[shfix]   {mesh}/{side}: no verts>0.5 on bone {bone}"); return; }
        Debug.Log($"[shfix]   GLOVE {mesh}/{side}: n={n} centroid x={sx/n:F4} y={sy/n:F4} z={sz/n:F4}  xrange[{minx:F4}..{maxx:F4}]");
    }

    static float GloveCentroidX(GameObject player, string boneName)
    {
        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            int bi = System.Array.FindIndex(smr.bones, b => b && b.name == boneName);
            if (bi < 0) continue;
            var baked = new Mesh(); smr.BakeMesh(baked, true);
            var verts = baked.vertices; var bw = smr.sharedMesh.boneWeights;
            var l2w = smr.transform.localToWorldMatrix;
            int n = 0; float sx = 0;
            for (int i = 0; i < verts.Length && i < bw.Length; i++)
            {
                var w = bw[i];
                float wt = (w.boneIndex0 == bi ? w.weight0 : 0) + (w.boneIndex1 == bi ? w.weight1 : 0)
                         + (w.boneIndex2 == bi ? w.weight2 : 0) + (w.boneIndex3 == bi ? w.weight3 : 0);
                if (wt < 0.5f) continue;
                sx += l2w.MultiplyPoint3x4(verts[i]).x; n++;
            }
            Object.DestroyImmediate(baked);
            if (n > 0) return sx / n;
        }
        return float.NaN;
    }

    static Vector3 GloveCentroid(GameObject player, string boneName)
    {
        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            int bi = System.Array.FindIndex(smr.bones, b => b && b.name == boneName);
            if (bi < 0) continue;
            var baked = new Mesh(); smr.BakeMesh(baked, true);
            var verts = baked.vertices; var bw = smr.sharedMesh.boneWeights;
            var l2w = smr.transform.localToWorldMatrix;
            int n = 0; Vector3 s = Vector3.zero;
            for (int i = 0; i < verts.Length && i < bw.Length; i++)
            {
                var w = bw[i];
                float wt = (w.boneIndex0 == bi ? w.weight0 : 0) + (w.boneIndex1 == bi ? w.weight1 : 0)
                         + (w.boneIndex2 == bi ? w.weight2 : 0) + (w.boneIndex3 == bi ? w.weight3 : 0);
                if (wt < 0.5f) continue;
                s += l2w.MultiplyPoint3x4(verts[i]); n++;
            }
            Object.DestroyImmediate(baked);
            if (n > 0) return s / n;
        }
        return new Vector3(float.NaN, float.NaN, float.NaN);
    }

    // Measure where the naturally-bent arm places the GLOVE vs the hood socket, in world + steer-local,
    // across a few grip values. Prints the inboard delta needed to bring the hoods to the gloves.
    public static void MeasureNatural()
    {
        OpenScene();
        var player = GameObject.Find("Kuro on Sakura Pass");
        var rig = player.GetComponent<KuroBikeRig>();
        rig.handTargetLocalOffset = new Vector3(0f, -0.035f, -0.05f);
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null) { regions.Resolve(); regions.currentRegionId = RegionCatalog.SakuraPass; regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience(); }
        var steer = Find(player.transform, "SteerPivot");
        foreach (var g in new[] { 0.00f, 0.03f, 0.05f })
        {
            rig.gripSpreadMetres = g;
            SolvePoc(player.transform);
            var hoodL = Find(player.transform, "Hood_L"); var hoodR = Find(player.transform, "Hood_R");
            Vector3 gl = GloveCentroid(player, "LeftHand"), gr = GloveCentroid(player, "RightHand");
            Vector3 lHoodLocal = steer.InverseTransformPoint(hoodL.position);
            Vector3 lGloveL = steer.InverseTransformPoint(gl), lGloveR = steer.InverseTransformPoint(gr);
            float dL = Vector3.Distance(gl, hoodL.position) * 1000f, dR = Vector3.Distance(gr, hoodR.position) * 1000f;
            Debug.Log($"[shfix] grip={g:F2} | hoodLocalX=±{lHoodLocal.x:F4} | gloveLocal L.x={lGloveL.x:F4} R.x={lGloveR.x:F4} | 3Dgap L={dL:F0}mm R={dR:F0}mm | {Metrics(player.transform)}");
        }
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // Sweep the player's mirrored grip spread; report where the GLOVE centroid lands vs the hoods
    // and render the frontal so the value that seats both gloves ON the hoods can be picked by eye.
    public static void SweepGrip()
    {
        OpenScene();
        var player = GameObject.Find("Kuro on Sakura Pass");
        var rig = player.GetComponent<KuroBikeRig>();
        rig.handTargetLocalOffset = new Vector3(0f, -0.035f, -0.05f);
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null) { regions.Resolve(); regions.currentRegionId = RegionCatalog.SakuraPass; regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience(); }
        float[] cands = { 0.08f, 0.09f };
        foreach (var g in cands)
        {
            rig.gripSpreadMetres = g;
            SolvePoc(player.transform);
            var hoodL = Find(player.transform, "Hood_L"); var hoodR = Find(player.transform, "Hood_R");
            float gl = GloveCentroidX(player, "LeftHand"), gr = GloveCentroidX(player, "RightHand");
            Debug.Log($"[shfix] grip={g:F2}  hoodL={hoodL.position.x:F3} gloveL={gl:F3} gapL={(gl-hoodL.position.x)*1000:F0}mm | hoodR={hoodR.position.x:F3} gloveR={gr:F3} gapR={(gr-hoodR.position.x)*1000:F0}mm | {Metrics(player.transform)}");
            RenderSet(player.transform, "grip_" + g.ToString("F2").Replace(".", ""));
        }
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    public static void Dump()
    {
        OpenScene();
        var regions = ToShiosai();
        Debug.Log($"[shfix] region={(regions != null ? regions.currentRegionId : "<none>")}");
        var poc = FindPoc();
        if (poc == null) { Debug.LogError("[shfix] POC not staged. Run MapleRide/Shiosai/Stage Realistic Rider (POC) first."); MapleRideSceneBootstrap.DiscardChanges(); if (Application.isBatchMode) EditorApplication.Exit(0); return; }
        var rig = poc.GetComponent<KuroBikeRig>();
        var bikeAnchor = Find(poc, "Bike");
        Debug.Log($"[shfix] POC pos={poc.position:F2} active={poc.gameObject.activeInHierarchy} bikeScale={(bikeAnchor ? bikeAnchor.localScale.x : -1):F3}");
        if (rig != null)
        {
            string bp = rig.bikePrefab != null ? AssetDatabase.GetAssetPath(rig.bikePrefab) : "<null>";
            Debug.Log($"[shfix] rig.bikePrefab={bp}");
            Debug.Log($"[shfix] rig.handTargetLocalOffset={rig.handTargetLocalOffset:F4} hipTilt={rig.hipTiltDegrees} spineLean={rig.spineLeanDegrees}");
        }
        SolvePoc(poc);
        Debug.Log($"[shfix] metrics: {Metrics(poc)}");
        // Material inventory (character body renderers only, not the bike).
        var body = Find(poc, "KuroRealArmatureAndMesh");
        if (body != null)
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string col = m.HasProperty("_Color") ? m.GetColor("_Color").ToString("F3") : "n/a";
                    string tex = (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) ? m.GetTexture("_MainTex").name : "-";
                    Debug.Log($"[shfix]   rend='{r.name}' mat='{m.name}' shader='{m.shader.name}' _Color={col} tex='{tex}'");
                }
            }
        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log("[shfix] Dump done (scene NOT saved).");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static string RegionName(string id) => id;

    public static void DumpPlayerMats()
    {
        OpenScene();
        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[shfix] player not found"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) { Debug.Log($"[shfix] rend='{r.name}' mat=<null>"); continue; }
                string sh = m.shader != null ? m.shader.name : "<noshader>";
                string col = m.HasProperty("_Color") ? m.GetColor("_Color").ToString("F3") : "-";
                string tex = (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) ? m.GetTexture("_MainTex").name : "-";
                string hw = m.HasProperty("_HighlightWhite") ? m.GetFloat("_HighlightWhite").ToString("F2") : "NO";
                string kn = m.HasProperty("_HighlightKnee") ? m.GetFloat("_HighlightKnee").ToString("F2") : "NO";
                string sa = m.HasProperty("_ShadowAmbient") ? m.GetFloat("_ShadowAmbient").ToString("F2") : "NO";
                string cl = m.HasProperty("_CharacterLight") ? m.GetFloat("_CharacterLight").ToString("F2") : "NO";
                Debug.Log($"[shfix] rend='{r.name}' mat='{m.name}' shader='{sh}' tex='{tex}' _Color={col} HiWhite={hw} Knee={kn} ShAmb={sa} CharLt={cl}");
            }
        }
        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log("[shfix] DumpPlayerMats done.");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    public static void DumpRiders()
    {
        OpenScene();
        var rigs = Object.FindObjectsByType<KuroBikeRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[shfix] found {rigs.Length} KuroBikeRig instance(s)");
        foreach (var rig in rigs)
        {
            string path = PathOf(rig.transform);
            string bp = rig.bikePrefab != null ? AssetDatabase.GetAssetPath(rig.bikePrefab) : "<null>";
            var bike = Find(rig.transform, "Bike");
            Debug.Log($"[shfix] RIDER '{path}' drop={rig.shoulderDropDegrees} hoff={rig.handTargetLocalOffset:F3} bikeScale={(bike ? bike.localScale.x : -1):F3} bikePrefab={bp}");
        }
        // Player body materials
        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player != null)
            foreach (var r in player.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name != "Mesh_0.001" && r.name != "SmileDecal") continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string col = m.HasProperty("_Color") ? m.GetColor("_Color").ToString("F3") : "n/a";
                    string tex = (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) ? m.GetTexture("_MainTex").name : "-";
                    string hw = m.HasProperty("_HighlightWhite") ? m.GetFloat("_HighlightWhite").ToString("F2") : "-";
                    Debug.Log($"[shfix]   PLAYER rend='{r.name}' mat='{m.name}' shader='{m.shader.name}' _Color={col} tex='{tex}' HiWhite={hw}");
                }
            }
        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log("[shfix] DumpRiders done.");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static string PathOf(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    // Render an arbitrary rider (player or POC) under a given region.
    static void RenderRider(string regionId, string riderName, string sub)
    {
        OpenScene();
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null)
        {
            regions.Resolve();
            regions.currentRegionId = regionId;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }
        Transform rider = null;
        var go = GameObject.Find(riderName);
        if (go != null) rider = go.transform;
        if (rider == null)
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == riderName) { rider = t; break; }
        }
        if (rider == null) { Debug.LogError($"[shfix] rider '{riderName}' not found"); return; }
        SolvePoc(rider);
        Debug.Log($"[shfix] RenderRider region={regionId} rider='{riderName}' metrics: {Metrics(rider)}");
        RenderSet(rider, sub);
    }

    // Final verification: render the committed player (grip=0.10, offset, skin) on BOTH maps
    // into reference/good_graphics/fi_model_fix/{shiosai,sakura}. Reads the SAVED scene.
    public static void RenderModelFix()
    {
        AssetDatabase.Refresh();
        OutRootOverride = "../reference/good_graphics/fi_model_fix/";
        RenderRider(RegionCatalog.ShiosaiCoast, "Kuro on Sakura Pass", "shiosai");
        RenderRider(RegionCatalog.SakuraPass, "Kuro on Sakura Pass", "sakura");
        OutRootOverride = null;
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // ------------------------------------------------------------------------------------------
    // USER-CAMERA iteration harness. Reproduces USER_TARGET_VIEW.png (front, ~15 deg high, CLOSE,
    // torso-to-front-wheel) so the honest defect (a glove dangling by the hip) is visible, then
    // sweeps player-only levers and renders that exact camera per candidate. Player/instance-scoped
    // only: never edits the shared bike GLB, the rig GLB, or any NPC.
    // ------------------------------------------------------------------------------------------

    // Close, slightly-high, front camera matching the user's live in-game screenshot.
    static void ShotUser(string dir, string name, Transform poc, float dist, float height, float lookY, float fov)
    {
        Vector3 p = poc.position, f = poc.forward, up = Vector3.up;
        Shot(dir, name, p + f * dist + up * height, p + up * lookY, fov);
    }

    static string IterDir()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/fi_model_fix/iter"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void LogRiders()
    {
        var rigs = Object.FindObjectsByType<KuroBikeRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Debug.Log($"[shfix] found {rigs.Length} KuroBikeRig instance(s)");
        foreach (var rig in rigs)
        {
            string path = PathOf(rig.transform);
            bool active = rig.gameObject.activeInHierarchy;
            string bp = rig.bikePrefab != null ? Path.GetFileName(AssetDatabase.GetAssetPath(rig.bikePrefab)) : "<null>";
            var bike = Find(rig.transform, "Bike");
            Debug.Log($"[shfix] RIDER active={active} '{path}' drop={rig.shoulderDropDegrees} hoff={rig.handTargetLocalOffset:F3} grip={rig.gripSpreadMetres:F3} scale={(bike ? bike.localScale.x : -1):F3} bike={bp}");
        }
    }

    // Diagnostic: dump the rider inventory + render the CURRENT committed player from three
    // candidate close/high framings on BOTH regions, so the framing that matches
    // USER_TARGET_VIEW can be picked and the defect confirmed honestly. Read-only.
    public static void IterDiag()
    {
        OpenScene();
        LogRiders();
        string dir = IterDir();
        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[shfix] player not found"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        foreach (var reg in new[] { RegionCatalog.ShiosaiCoast, RegionCatalog.SakuraPass })
        {
            string tag = reg == RegionCatalog.ShiosaiCoast ? "shiosai" : "sakura";
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            if (regions != null) { regions.Resolve(); regions.currentRegionId = reg; regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience(); }
            SolvePoc(player.transform);
            Vector3 gl = GloveCentroid(player, "LeftHand"), gr = GloveCentroid(player, "RightHand");
            var hoodL = Find(player.transform, "Hood_L"); var hoodR = Find(player.transform, "Hood_R");
            Debug.Log($"[shfix] DIAG {tag}: gloveL={gl:F3} hoodL={hoodL.position:F3} gloveR={gr:F3} hoodR={hoodR.position:F3} | {Metrics(player.transform)}");
            ShotUser(dir, $"00_{tag}_A", player.transform, 1.55f, 1.15f, 0.72f, 44f);
            ShotUser(dir, $"00_{tag}_B", player.transform, 1.35f, 1.28f, 0.66f, 47f);
            ShotUser(dir, $"00_{tag}_C", player.transform, 1.75f, 1.05f, 0.80f, 40f);
        }
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // Iteration sweep: apply each candidate lever set to the PLAYER rig, solve, and render the
    // user camera on BOTH regions into iter/<NN>_<label>_{shiosai,sakura}.png. Edit `cands` between
    // runs. Read-only (never saves) unless MR_COMMIT names the label to persist.
    public static void IterSweep()
    {
        OpenScene();
        string dir = IterDir();
        var player = GameObject.Find("Kuro on Sakura Pass");
        var rig = player.GetComponent<KuroBikeRig>();

        // (label, handTargetLocalOffset, gripSpreadMetres, shoulderDropDegrees)
        // Gloves currently hang ~13cm BELOW the hoods (glove Y 0.45 vs hood Y 0.58); lift Y to seat them.
        var cands = new (string label, Vector3 hoff, float grip, float drop)[]
        {
            ("01_y06",  new Vector3(0f, 0.060f, -0.05f), 0.08f, 14f),
            ("02_y10",  new Vector3(0f, 0.100f, -0.05f), 0.09f, 14f),
            ("03_y13",  new Vector3(0f, 0.130f, -0.04f), 0.09f, 14f),
            ("04_y16",  new Vector3(0f, 0.160f, -0.03f), 0.10f, 14f),
        };

        foreach (var c in cands)
        {
            rig.handTargetLocalOffset = c.hoff;
            rig.gripSpreadMetres = c.grip;
            rig.shoulderDropDegrees = c.drop;
            foreach (var reg in new[] { RegionCatalog.ShiosaiCoast, RegionCatalog.SakuraPass })
            {
                string tag = reg == RegionCatalog.ShiosaiCoast ? "shiosai" : "sakura";
                var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
                if (regions != null) { regions.Resolve(); regions.currentRegionId = reg; regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience(); }
                SolvePoc(player.transform);
                if (tag == "shiosai")
                {
                    Vector3 gl = GloveCentroid(player, "LeftHand"), gr = GloveCentroid(player, "RightHand");
                    var hoodL = Find(player.transform, "Hood_L"); var hoodR = Find(player.transform, "Hood_R");
                    float dL = Vector3.Distance(gl, hoodL.position) * 1000f, dR = Vector3.Distance(gr, hoodR.position) * 1000f;
                    Debug.Log($"[shfix] {c.label}: hoff={c.hoff:F3} grip={c.grip:F2} drop={c.drop} | gloveGap L={dL:F0}mm R={dR:F0}mm | {Metrics(player.transform)}");
                }
                ShotUser(dir, $"{c.label}_{tag}", player.transform, 1.55f, 1.15f, 0.72f, 44f);
            }
        }
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // Iteratively solve the per-hand seat offsets so the visible GLOVE centroid lands on the hood
    // socket (the wrist-vs-glove offset is non-axis-aligned, so a fixed nudge can't do it). Logs
    // convergence + elbow per iteration, then renders the user camera on both maps. If MR_COMMIT=1,
    // persists the converged offsets to the player instance and SAVES the scene clean.
    static Vector3[] ConvergeSeat(GameObject player, KuroBikeRig rig, Transform steer,
                                  Transform hoodL, Transform hoodR, int iters, float damp)
    {
        rig.handSeatOffsetL = Vector3.zero;
        rig.handSeatOffsetR = Vector3.zero;
        for (int it = 0; it < iters; it++)
        {
            SolvePoc(player.transform);
            Vector3 gl = GloveCentroid(player, "LeftHand"), gr = GloveCentroid(player, "RightHand");
            float dL = Vector3.Distance(gl, hoodL.position) * 1000f, dR = Vector3.Distance(gr, hoodR.position) * 1000f;
            Debug.Log($"[shfix] seat it{it}: gap L={dL:F0}mm R={dR:F0}mm | seatL={rig.handSeatOffsetL:F3} seatR={rig.handSeatOffsetR:F3} | {Metrics(player.transform)}");
            rig.handSeatOffsetL += steer.InverseTransformVector(hoodL.position - gl) * damp;
            rig.handSeatOffsetR += steer.InverseTransformVector(hoodR.position - gr) * damp;
        }
        SolvePoc(player.transform);
        return new[] { rig.handSeatOffsetL, rig.handSeatOffsetR };
    }

    public static void IterSeat()
    {
        OpenScene();
        string dir = IterDir();
        var player = GameObject.Find("Kuro on Sakura Pass");
        var rig = player.GetComponent<KuroBikeRig>();
        var steer = Find(player.transform, "SteerPivot");
        var hoodL = Find(player.transform, "Hood_L"); var hoodR = Find(player.transform, "Hood_R");

        rig.handTargetLocalOffset = Vector3.zero;
        rig.gripSpreadMetres = 0f;

        // Sweep shoulder height (moving the shoulder away from the seated wrist opens the elbow),
        // re-converging the glove-on-hood seat for each, and render the honest user camera per drop.
        ToShiosai();
        foreach (var drop in new[] { 14f, 6f, 0f, -8f })
        {
            rig.shoulderDropDegrees = drop;
            var seats = ConvergeSeat(player, rig, steer, hoodL, hoodR, 8, 0.8f);
            Vector3 gl = GloveCentroid(player, "LeftHand"), gr = GloveCentroid(player, "RightHand");
            float dL = Vector3.Distance(gl, hoodL.position) * 1000f, dR = Vector3.Distance(gr, hoodR.position) * 1000f;
            Debug.Log($"[shfix] SEAT drop={drop}: gap L={dL:F0}mm R={dR:F0}mm seatL={seats[0]:F3} seatR={seats[1]:F3} | {Metrics(player.transform)}");
            ShotUser(dir, $"drop{drop}_shiosai", player.transform, 1.55f, 1.15f, 0.72f, 44f);
        }
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // Render an EXT candidate: after the player bike GLB has been edited on disk, reimport it,
    // re-converge the glove-on-hood seat (geometry changed), and render usercam+side+front on both
    // maps into iter/ext/<MR_EXT>/. Does NOT save the scene (read-only trial).
    public static void RenderExtCandidate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        string label = System.Environment.GetEnvironmentVariable("MR_EXT") ?? "ext";
        OpenScene();
        var player = GameObject.Find("Kuro on Sakura Pass");
        var rig = player.GetComponent<KuroBikeRig>();
        var steer = Find(player.transform, "SteerPivot");
        var hoodL = Find(player.transform, "Hood_L"); var hoodR = Find(player.transform, "Hood_R");
        rig.handTargetLocalOffset = Vector3.zero;
        rig.gripSpreadMetres = 0f;
        rig.shoulderDropDegrees = 14f;

        foreach (var reg in new[] { RegionCatalog.ShiosaiCoast, RegionCatalog.SakuraPass })
        {
            string tag = reg == RegionCatalog.ShiosaiCoast ? "shiosai" : "sakura";
            var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
            if (regions != null) { regions.Resolve(); regions.currentRegionId = reg; regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience(); }
            ConvergeSeat(player, rig, steer, hoodL, hoodR, 8, 0.8f);
            Vector3 gl = GloveCentroid(player, "LeftHand"), gr = GloveCentroid(player, "RightHand");
            float dL = Vector3.Distance(gl, hoodL.position) * 1000f, dR = Vector3.Distance(gr, hoodR.position) * 1000f;
            Debug.Log($"[shfix] EXT {label} {tag}: gap L={dL:F0}mm R={dR:F0}mm seatL={rig.handSeatOffsetL:F3} seatR={rig.handSeatOffsetR:F3} | {Metrics(player.transform)}");
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/fi_model_fix/iter/ext/" + label));
            Directory.CreateDirectory(dir);
            Vector3 p = player.transform.position, f = player.transform.forward, r = player.transform.right, up = Vector3.up;
            Shot(dir, $"{tag}_usercam", p + f * 1.55f + up * 1.15f, p + up * 0.72f, 44f);
            Shot(dir, $"{tag}_front", p + f * 2.7f + up * 0.95f, p + up * 0.92f, 38f);
            Shot(dir, $"{tag}_side", p + r * 3.2f + up * 1.05f, p + up * 0.90f, 40f);
        }
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // Render both maps into a sub named by MR_SUB (e.g. "wide"), for A/B compares.
    public static void RenderBoth()
    {
        AssetDatabase.Refresh();
        string sub = System.Environment.GetEnvironmentVariable("MR_SUB") ?? "cmp";
        RenderRider(RegionCatalog.ShiosaiCoast, "Kuro on Sakura Pass", "player_shiosai_" + sub);
        RenderRider(RegionCatalog.SakuraPass, "Kuro on Sakura Pass", "player_sakura_" + sub);
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // Sweep player handTargetLocalOffset on the CURRENT bike (no reimport). Reports elbow angles
    // and renders front/side/closeup per candidate so a value that un-locks the elbow while
    // keeping the hands on the widened hoods can be chosen by eye.
    public static void SweepPlayerOffset()
    {
        OpenScene();
        var player = GameObject.Find("Kuro on Sakura Pass");
        var rig = player.GetComponent<KuroBikeRig>();
        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        if (regions != null) { regions.Resolve(); regions.currentRegionId = RegionCatalog.SakuraPass; regions.ApplyEnvironmentVisibility(); regions.ApplyAmbience(); }
        var cands = new (string name, Vector3 off)[]
        {
            ("z03", new Vector3(0f, -0.035f, -0.03f)),
            ("z05", new Vector3(0f, -0.035f, -0.05f)),
            ("z07", new Vector3(0f, -0.035f, -0.07f)),
            ("y5z6", new Vector3(0f, -0.05f, -0.06f)),
        };
        foreach (var c in cands)
        {
            rig.handTargetLocalOffset = c.off;
            SolvePoc(player.transform);
            Debug.Log($"[shfix] off {c.name}={c.off:F3}  {Metrics(player.transform)}");
            RenderSet(player.transform, "off_" + c.name);
        }
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    public static void RenderPlayerShiosai()
    {
        RenderRider(RegionCatalog.ShiosaiCoast, "Kuro on Sakura Pass", "player_shiosai_before");
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    public static void RenderPlayerSakura()
    {
        RenderRider(RegionCatalog.SakuraPass, "Kuro on Sakura Pass", "player_sakura_before");
        MapleRideSceneBootstrap.DiscardChanges();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void RenderSet(Transform poc, string sub)
    {
        string root = OutRootOverride ?? OutRoot;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, root + sub));
        Directory.CreateDirectory(dir);
        var hoodL = Find(poc, "Hood_L"); var hoodR = Find(poc, "Hood_R");
        Vector3 p = poc.position, f = poc.forward, r = poc.right, up = Vector3.up;
        // User's LIVE angle: close, front, ~15 deg high, torso-to-front-wheel (matches USER_TARGET_VIEW.png).
        // This is the honest camera that exposes a dangling hand; the wider shots below can hide it.
        Shot(dir, "usercam", p + f * 1.55f + up * 1.15f, p + up * 0.72f, 44f);
        // User's angle: near-frontal, slightly high, framing upper body + bar.
        Shot(dir, "user_frontal_high", p + f * 2.4f + up * 1.35f, p + up * 0.98f, 40f);
        // Front (more level) and side for pose read.
        Shot(dir, "front", p + f * 2.7f + up * 0.95f, p + up * 0.92f, 38f);
        Shot(dir, "side", p + r * 3.2f + up * 1.05f, p + up * 0.90f, 40f);
        if (hoodL && hoodR)
        {
            Vector3 hc = (hoodL.position + hoodR.position) * 0.5f;
            Shot(dir, "hands_closeup", hc + f * 0.80f + r * 0.28f + up * 0.14f, hc, 28f);
        }
    }

    public static void RenderCur()
    {
        OpenScene();
        ToShiosai();
        var poc = FindPoc();
        if (poc == null) { Debug.LogError("[shfix] POC not staged."); if (Application.isBatchMode) EditorApplication.Exit(0); return; }
        SolvePoc(poc);
        Debug.Log($"[shfix] RenderCur metrics: {Metrics(poc)}");
        RenderSet(poc, "current");
        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log("[shfix] RenderCur done (scene NOT saved).");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    public static void SweepHands()
    {
        OpenScene();
        ToShiosai();
        var poc = FindPoc();
        if (poc == null) { Debug.LogError("[shfix] POC not staged."); if (Application.isBatchMode) EditorApplication.Exit(0); return; }
        var rig = poc.GetComponent<KuroBikeRig>();
        var cands = new (string name, Vector3 off)[]
        {
            ("drops",   new Vector3(0f, -0.144f, 0.164f)),   // current design (hands on drops)
            ("hood0",   new Vector3(0f, 0f, 0f)),            // hood socket direct
            ("hoodDn2", new Vector3(0f, -0.02f, 0f)),
            ("hoodDn4", new Vector3(0f, -0.04f, 0f)),
            ("hoodDn3Bk3", new Vector3(0f, -0.03f, -0.03f)),
            ("hoodDn5Bk4", new Vector3(0f, -0.05f, -0.04f)),
        };
        foreach (var c in cands)
        {
            rig.handTargetLocalOffset = c.off;
            SolvePoc(poc);
            Debug.Log($"[shfix] hand {c.name} off={c.off:F3}  {Metrics(poc)}");
            RenderSet(poc, "hand_" + c.name);
        }
        MapleRideSceneBootstrap.DiscardChanges();
        Debug.Log("[shfix] SweepHands done (scene NOT saved).");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    public static void CommitHands()
    {
        OpenScene();
        var poc = FindPoc();
        if (poc == null) { Debug.LogError("[shfix] POC not staged."); if (Application.isBatchMode) EditorApplication.Exit(0); return; }
        string s = System.Environment.GetEnvironmentVariable("MR_HOFF");
        if (string.IsNullOrEmpty(s)) { Debug.LogError("[shfix] MR_HOFF not set"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        var pt = s.Split(',');
        Vector3 off = new Vector3(
            float.Parse(pt[0], CultureInfo.InvariantCulture),
            float.Parse(pt[1], CultureInfo.InvariantCulture),
            float.Parse(pt[2], CultureInfo.InvariantCulture));
        var rig = poc.GetComponent<KuroBikeRig>();
        rig.handTargetLocalOffset = off;
        EditorUtility.SetDirty(rig);
        EditorSceneManager.MarkSceneDirty(poc.gameObject.scene);
        EditorSceneManager.SaveScene(poc.gameObject.scene);
        Debug.Log($"[shfix] COMMIT hands: saved handTargetLocalOffset={off:F4}");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // ---- SKIN FIX: player-exclusive body material with corrected highlight/white-point --------
    // The player body atlas (Image_0) is SHARED by several Kuro-visual NPCs, so the persistent fix
    // must live on a player-EXCLUSIVE clone. The whiteness is a highlight soft-clip blowout (white
    // point 1.35 pushed past 1.0 by the scene ACES+exposure), NOT albedo - so lowering the white
    // point restores the atlas's own warm skin tone while the near-black kit (no highlight) stays
    // black. Adds the region-independent character self-light rig so it reads on ANY map.
    internal const string PlayerOnlyBodyMat = "Assets/Kuro/Materials/PlayerOnly_Body_Image_0_CelLit.mat";
    const string SharedBodyMat = "Assets/Kuro/Materials/PlayerBody_Image_0_CelLit.mat";

    internal static void ApplySkinRecipe(Material m)
    {
        if (m.HasProperty("_HighlightRolloff")) m.SetFloat("_HighlightRolloff", 0.80f);
        if (m.HasProperty("_HighlightKnee"))    m.SetFloat("_HighlightKnee", 0.52f);
        if (m.HasProperty("_HighlightWhite"))   m.SetFloat("_HighlightWhite", 1.06f);
        if (m.HasProperty("_ShadowAmbient"))    m.SetFloat("_ShadowAmbient", 0.55f);
        if (m.HasProperty("_MatteFloor"))       m.SetColor("_MatteFloor", new Color(0.165f, 0.165f, 0.177f, 1f));
        // Region-independent character self-light rig (warm key) so skin has form + warmth on any map.
        if (m.HasProperty("_CharacterLight"))    m.SetFloat("_CharacterLight", 0.85f);
        if (m.HasProperty("_CharKeyDir"))        m.SetVector("_CharKeyDir", new Vector4(0.35f, 0.60f, 0.55f, 0f));
        if (m.HasProperty("_CharKeyColor"))      m.SetColor("_CharKeyColor", new Color(1f, 0.98f, 0.94f, 1f));
        if (m.HasProperty("_CharKeyIntensity"))  m.SetFloat("_CharKeyIntensity", 1.25f);
        if (m.HasProperty("_CharFillColor"))     m.SetColor("_CharFillColor", new Color(0.62f, 0.72f, 0.95f, 1f));
        if (m.HasProperty("_CharFillIntensity")) m.SetFloat("_CharFillIntensity", 0.45f);
        if (m.HasProperty("_CharAmbient"))       m.SetFloat("_CharAmbient", 0.34f);
        if (m.HasProperty("_EdgeRimColor"))      m.SetColor("_EdgeRimColor", new Color(0.72f, 0.80f, 1.0f, 1f));
        if (m.HasProperty("_EdgeRimStrength"))   m.SetFloat("_EdgeRimStrength", 0.18f);
        if (m.HasProperty("_EdgeRimPower"))      m.SetFloat("_EdgeRimPower", 3.0f);
    }

    /// <summary>Create (or refresh) the player-exclusive body material with the skin recipe.</summary>
    internal static Material EnsurePlayerOnlyBodyMat()
    {
        var shared = AssetDatabase.LoadAssetAtPath<Material>(SharedBodyMat);
        if (shared == null) { Debug.LogError("[shfix] shared body mat missing: " + SharedBodyMat); return null; }
        var existing = AssetDatabase.LoadAssetAtPath<Material>(PlayerOnlyBodyMat);
        if (existing == null)
        {
            var clone = new Material(shared);
            clone.CopyPropertiesFromMaterial(shared);
            clone.name = "PlayerOnly_Body_Image_0_CelLit";
            ApplySkinRecipe(clone);
            AssetDatabase.CreateAsset(clone, PlayerOnlyBodyMat);
            Debug.Log("[shfix] created " + PlayerOnlyBodyMat);
            return clone;
        }
        existing.shader = shared.shader;
        existing.CopyPropertiesFromMaterial(shared);
        existing.name = "PlayerOnly_Body_Image_0_CelLit";
        ApplySkinRecipe(existing);
        EditorUtility.SetDirty(existing);
        Debug.Log("[shfix] refreshed " + PlayerOnlyBodyMat);
        return existing;
    }

    // Final commit: set the chosen player hand offset + re-assert the skin-corrected body
    // material, then SAVE the scene clean. Leaves SakuraPass as the saved scene.
    public static void CommitFinal()
    {
        OpenScene();
        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[shfix] player not found"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        var rig = player.GetComponent<KuroBikeRig>();
        // Hands-on-hoods = seat the visible GLOVE centroid onto the hood via the iterative per-hand
        // correction (the glove hangs ~13cm below the wrist, so a fixed offset can't do it). Let the
        // seat solver own the hand placement: no base offset / no grip spread.
        rig.handTargetLocalOffset = Vector3.zero;
        rig.gripSpreadMetres = 0f;
        rig.shoulderDropDegrees = 14f;
        var steer = Find(player.transform, "SteerPivot");
        var hoodL = Find(player.transform, "Hood_L"); var hoodR = Find(player.transform, "Hood_R");
        ConvergeSeat(player, rig, steer, hoodL, hoodR, 8, 0.8f);
        Debug.Log($"[shfix] CommitFinal seat converged: seatL={rig.handSeatOffsetL:F4} seatR={rig.handSeatOffsetR:F4} | {Metrics(player.transform)}");
        EditorUtility.SetDirty(rig);

        var mat = EnsurePlayerOnlyBodyMat();
        AssetDatabase.SaveAssets();
        int assigned = 0;
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials; bool t = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name.StartsWith("PlayerBody_Image_0_CelLit")) { mats[i] = mat; t = true; assigned++; }
            if (t) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
        }
        int leaks = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var m in r.sharedMaterials)
                if (m == mat && !PathOf(r.transform).StartsWith("Kuro on Sakura Pass")) leaks++;
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[shfix] CommitFinal: seatL={rig.handSeatOffsetL:F4} seatR={rig.handSeatOffsetR:F4} skinReassigned={assigned} playerOnlyLeaks={leaks}");
        if (Application.isBatchMode) EditorApplication.Exit(leaks == 0 ? 0 : 1);
    }

    public static void ApplySkinFix()
    {        OpenScene();
        var player = GameObject.Find("Kuro on Sakura Pass");
        if (player == null) { Debug.LogError("[shfix] player not found"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        var mat = EnsurePlayerOnlyBodyMat();
        if (mat == null) { if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        AssetDatabase.SaveAssets();

        int assigned = 0;
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool touched = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name.StartsWith("PlayerBody_Image_0_CelLit"))
                { mats[i] = mat; touched = true; assigned++; }
            if (touched) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
        }
        Debug.Log($"[shfix] assigned PlayerOnly body mat to {assigned} slot(s) on the player.");

        // Leak guard: PlayerOnly mat must be used ONLY by the player root.
        int leaks = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var m in r.sharedMaterials)
                if (m == mat && !PathOf(r.transform).StartsWith("Kuro on Sakura Pass"))
                { Debug.LogError($"[shfix] LEAK: PlayerOnly body mat used by '{PathOf(r.transform)}'"); leaks++; }
        Debug.Log($"[shfix] PlayerOnly leaks into non-player: {leaks}");

        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[shfix] ApplySkinFix saved.");
        if (Application.isBatchMode) EditorApplication.Exit(assigned > 0 && leaks == 0 ? 0 : 1);
    }

    static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~ShFixCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        var fx = go.AddComponent<SakuraPostFX>();
        var a = RegionDirector.ShiosaiAmbience;
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        fx.dofStrength = 0f;
        if (a.aerialRange > 0f)
        {
            fx.aerialStart = a.aerialStart; fx.aerialRange = a.aerialRange;
            fx.aerialDesaturation = a.aerialDesaturation; fx.aerialFlatten = a.aerialFlatten;
            fx.aerialTint = a.aerialTint; fx.aerialTintAmount = a.aerialTintAmount;
        }
        const int w = 1000, h = 1000;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev;
        cam.targetTexture = null;
        Object.DestroyImmediate(go);
        rt.Release();
    }
}
