// Nagisa Bay PEOPLE OVERHAUL (Claude, 2026-10-01; user: "the NPCs need a total overhaul ... should be
// runners, people laying out on the beach, very lively high quality character models").
//
// Why: the NB recoloured donor atlases (nagisa_looks.py -> MapleLife_Crowd_*_NB<n>.png) render as pink /
// black camouflage on the Kuro-rig donors, the "runners" were slow walkers and the "sunbathers" were
// people standing or sitting. This stage runs LAST (order 119) and:
//   1. dresses EVERY Nagisa person (MinatoCrowdActor under the environment root, cyclists excepted) with the
//      proven Kuro-rider pedestrian look (PedestrianKuroLook's recipe, baked in the editor): a PedKit_* atlas
//      in the Kuro UV layout (street kits + the new beach kits from design_assets/3d/pedestrians/
//      make_beach_kits.py), the helmet collapsed, an authored PedHair_* mesh with a tinted strand material,
//      and RiderBlink eyelids;
//   2. gives every "Runner" Kuro's retargeted run cycle (MapleCityRunner) at a real jogging speed;
//   3. replaces every standing/sitting "Sunbather" by a figure LYING on its towel / lounger
//      (NagisaSunbather: breathing, head turns, arms and knees shifting) and lays more people out on the
//      unoccupied towels.
// Idempotent: a figure that already has "Ped Hair" is skipped; this stage's own group is rebuilt each run.
// Single-stage run:  MR_NB_STAGES=PeopleOverhaul  +  NagisaBayEnvironment.ApplyOverhaul.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MK = MinatoCrowdActor.MotionKind;

public static partial class NagisaBayEnvironment
{
    private const string PplMatDir = "Assets/Environment/NagisaBay/Materials/People";
    private const string PplMeshDir = "Assets/Environment/NagisaBay/Meshes/People";
    private const string PplBeachKitDir = "Assets/Characters/Pedestrians/Beach";
    private const string PplStreetKitDir = "Assets/Characters/Pedestrians/Import";
    private const string PplRunCycleDir = "Assets/Environment/MapleCity/Life/Anim";

    private static readonly string[] PplRunKits = { "RunOrange", "RunPink", "RunLime", "RunBlue", "RunTeal", "RunRed" };
    private static readonly string[] PplSwimM = { "TrunksNavy", "TrunksOrange", "TrunksGreen", "TrunksFloral" };
    private static readonly string[] PplSwimF = { "BikiniCoral", "BikiniAqua", "BikiniYellow", "BikiniWhite", "BikiniNavy" };
    private static readonly string[] PplBeachN = { "TankTeal", "TankYellow", "TankWhite", "TankCoral", "AlohaTeal", "AlohaRed", "AlohaNavy" };
    private static readonly string[] PplDressF = { "SundressAqua", "SundressWhite", "SundressLavender", "SundressMint", "SundressRose", "Sundress", "CoralDress" };
    private static readonly string[] PplStreetN = { "TeeJeans", "PoloChinos", "TouristJacket", "BretonDenimSkirt", "KnitSkirt" };
    private static readonly string[] PplHairF = { "Long", "Ponytail", "Bun", "Bob" };
    private static readonly string[] PplHairM = { "Spiky", "Neat", "Swept", "Curly" };

    private static int _pplDressed, _pplRunners, _pplLying, _pplSkipped;
    private static readonly Dictionary<string, Material> _pplMats = new Dictionary<string, Material>();

    [NagisaStage(119, "PeopleOverhaul")]
    private static void BuildPeopleOverhaul(Transform group)
    {
        EnsureRouteGround();
        _pplDressed = _pplRunners = _pplLying = _pplSkipped = 0;
        _pplMats.Clear();
        foreach (var d in new[] { PplMatDir, PplMeshDir })
            if (!AssetDatabase.IsValidFolder(d))
            {
                var parent = Path.GetDirectoryName(d).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(d));
            }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var lib = Resources.Load<PedestrianModelLibrary>(PedestrianModelLibrary.ResourcePath);
        if (lib == null || lib.kuroRider == null || lib.kuroRider.hairStyles == null || lib.kuroRider.hairStyles.Length == 0)
        {
            Debug.LogError("[nagisa-ppl] PedestrianModelLibrary / kuroRider hair set missing - run PedestrianModelLibraryBuilder.Build.");
            return;
        }
        var set = lib.kuroRider;
        var root = group.root;
        var cast = _lastCast ?? NbCrowdCast(root);
        var rng = new System.Random(11901);

        // ---- 1. sunbathers: replace the standing / sitting ones by figures lying on towels and loungers
        var towels = new List<Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == "Nagisa_S_Towel") towels.Add(t);
        var used = new List<Vector3>();
        var olds = new List<GameObject>();
        foreach (var a in root.GetComponentsInChildren<MinatoCrowdActor>(true))
            // seated sunbathers keep their lounger (a flat figure would cut through the tilted backrest) and are
            // re-dressed below; only the standing ones beside towels are laid down.
            if (a != null && a.name.Contains("Sunbather") && a.motion != MK.Sit && !a.transform.IsChildOf(group) &&
                a.GetComponentInParent<NagisaSunbather>() == null)
                olds.Add(a.gameObject);
        foreach (var old in olds)
        {
            var actor = old.GetComponent<MinatoCrowdActor>();
            bool lounger = actor != null && actor.motion == MK.Sit;
            Vector3 head = old.transform.forward; head.y = 0f; head.Normalize();
            Vector3 centre; float y;
            var towel = NearestTowel(towels, old.transform.position + head * 1.3f, 3.0f);
            if (!lounger && towel != null)
            {
                centre = towel.position; TowelAxis(towel, out head);
                y = _ground.Height(centre.x, centre.z) + 0.04f;
            }
            else if (lounger) { centre = old.transform.position + head * 0.12f; y = old.transform.position.y - 0.02f; }
            else { centre = old.transform.position + head * 1.0f; y = _ground.Height(centre.x, centre.z) + 0.02f; }
            Object.DestroyImmediate(old);
            if (PplLieDown(cast, group, centre, y, head, set, rng, lounger ? "Sunbather" : "Sunbather")) used.Add(centre);
        }
        // more people laid out on the unoccupied towels
        foreach (var tw in towels)
        {
            if (tw == null) continue;
            if (used.Any(u => (u - tw.position).sqrMagnitude < 1.4f * 1.4f)) continue;
            if (rng.NextDouble() > 0.62) continue;
            TowelAxis(tw, out var head);
            var c = tw.position;
            float y = _ground.Height(c.x, c.z) + 0.04f;
            if (PplLieDown(cast, group, c, y, head, set, rng, "Sunbather")) used.Add(c);
        }

        // ---- 2. everyone else: the Kuro look; runners also get the run cycle
        var people = new List<GameObject>();
        foreach (var a in root.GetComponentsInChildren<MinatoCrowdActor>(true))
        {
            if (a == null || a.motion == MK.Cyclist) continue;
            if (a.transform.Find("LOD0 High Skinned") == null) continue;
            if (a.GetComponentInParent<NagisaSunbather>() != null) continue;
            if (people.Contains(a.gameObject)) continue;
            people.Add(a.gameObject);
        }
        foreach (var go in people)
        {
            // already dressed = the close-up body skin wears a NagisaPpl kit material (an earlier run only fixed the hair)
            if (go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(sm => sm.name == "Mesh_0" && sm.enabled &&
                    sm.sharedMaterials.Any(mm => mm != null && mm.name.StartsWith("NagisaPpl_")))) { _pplSkipped++; continue; }
            foreach (var oldHair in go.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Ped Hair").ToList())
                if (oldHair != null) Object.DestroyImmediate(oldHair.gameObject);
            string role = go.name.ToLowerInvariant();
            PplPick(role, go.transform.lossyScale.x, rng, out string kit, out bool female);
            var hair = PplHair(set, female, rng);
            PplDress(go, kit, hair, rng.Next(set.hairColours.Length), set);
            if (role.Contains("runner")) PplMakeRunner(go, rng);
            FinishPerson(go);
            _pplDressed++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[nagisa-ppl] dressed {_pplDressed} people ({_pplSkipped} already done), {_pplRunners} runners with the run cycle, " +
                  $"{_pplLying} sunbathers lying down (from {olds.Count} old ones + towels).");
    }

    // ------------------------------------------------------------------ choice
    private static void PplPick(string role, float scale, System.Random rng, out string kit, out bool female)
    {
        double r = rng.NextDouble();
        female = rng.NextDouble() < 0.5;
        if (role.Contains("runner"))
            kit = PplRunKits[rng.Next(PplRunKits.Length)];
        else if (role.Contains("kid") || role.Contains("child") || scale < 0.8f)
            kit = "KidTee";
        else if (role.Contains("sunbather") || role.Contains("swim") || role.Contains("surf") || role.Contains("shore") ||
                 role.Contains("volley") || role.Contains("beach"))
        {
            if (r < 0.34) { kit = PplSwimM[rng.Next(PplSwimM.Length)]; female = false; }
            else if (r < 0.68) { kit = PplSwimF[rng.Next(PplSwimF.Length)]; female = true; }
            else kit = PplBeachN[rng.Next(PplBeachN.Length)];
        }
        else if (role.Contains("skater") || role.Contains("blader"))
            kit = rng.NextDouble() < 0.5 ? PplRunKits[rng.Next(PplRunKits.Length)] : PplBeachN[rng.Next(PplBeachN.Length)];
        else
        {
            if (r < 0.28) kit = PplBeachN[rng.Next(PplBeachN.Length)];
            else if (r < 0.55) { kit = PplDressF[rng.Next(PplDressF.Length)]; female = true; }
            else kit = PplStreetN[rng.Next(PplStreetN.Length)];
        }
        if (kit.StartsWith("Bikini") || kit.Contains("Dress") || kit.StartsWith("Sundress") || kit == "KnitSkirt" || kit == "BretonDenimSkirt" || kit == "RunPink")
            female = true;
        else if (kit.StartsWith("Trunks") || kit == "PoloChinos") female = false;
    }

    private static GameObject PplHair(PedestrianModelLibrary.KuroRiderSet set, bool female, System.Random rng)
    {
        var names = female ? PplHairF : PplHairM;
        var pool = set.hairStyles.Where(h => h != null && names.Any(n => h.name.Contains(n))).ToList();
        if (pool.Count == 0) pool = set.hairStyles.Where(h => h != null).ToList();
        return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
    }

    private static Texture2D PplKit(string kit)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PplBeachKitDir}/PedKit_{kit}.png");
        return t != null ? t : AssetDatabase.LoadAssetAtPath<Texture2D>($"{PplStreetKitDir}/PedKit_{kit}.png");
    }

    // ------------------------------------------------------------------ dressing
    private static void PplDress(GameObject person, string kit, GameObject hairPrefab, int colour,
                                 PedestrianModelLibrary.KuroRiderSet set)
    {
        var kitTex = PplKit(kit);
        var high = person.transform.Find("LOD0 High Skinned");
        if (kitTex == null) { Debug.LogWarning($"[nagisa-ppl] kit '{kit}' not found; {person.name} left as is."); return; }

        // 1. body atlas -> the kit (every LOD that carries a donor atlas)
        foreach (var r in person.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || !m.HasProperty("_MainTex")) continue;
                var t = m.GetTexture("_MainTex");
                if (t == null) continue;
                string tn = t.name;
                bool crowdAtlas = tn.StartsWith("MapleLife_Crowd_") && !tn.EndsWith("_Hair") && !tn.EndsWith("_HairCap");
                // LOD0: the clone still carries the donor's raw glTF material (Shared_Material_0_CelLit / "Image_0", the
                // original Kuro atlas, which does NOT match the donor mesh's UV layout -> pink/black camouflage).
                bool rawBody = r is SkinnedMeshRenderer && r.name.StartsWith("Mesh_0") && m.name.StartsWith("Shared_Material_0");
                if (!crowdAtlas && !rawBody) continue;
                bool dist = m.name.EndsWith("_Dist");
                string key = $"{kit}|{(dist ? "D" : "B")}|{m.shader.name}";
                if (!_pplMats.TryGetValue(key, out var km) || km == null)
                {
                    string path = $"{PplMatDir}/NagisaPpl_{kit}_{(dist ? "Dist" : "Body")}.mat";
                    km = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (km == null) { km = new Material(m) { name = $"NagisaPpl_{kit}_{(dist ? "Dist" : "Body")}" }; AssetDatabase.CreateAsset(km, path); }
                    else { km.shader = m.shader; km.CopyPropertiesFromMaterial(m); }
                    km.SetTexture("_MainTex", kitTex);
                    if (km.HasProperty("_Color")) km.SetColor("_Color", Color.white);
                    km.enableInstancing = true;
                    EditorUtility.SetDirty(km);
                    _pplMats[key] = km;
                }
                mats[i] = km; changed = true;
            }
            if (changed) { r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
        }

        if (high == null) return;
        // 2. hide the donor hair cap / hat, collapse the helmet shell
        foreach (var r in person.GetComponentsInChildren<Renderer>(true))
            if (r.name == "Hair Cap" || r.name == "Ped Hat" || (hairPrefab != null && r.name == "NpcHair")) r.enabled = false;
        var look = person.GetComponent<MapleCityLook>();
        if (look != null && look.hairCap != null) look.hairCap.enabled = false;
        PedestrianKuroLook.CollapseHelmet(high);
        foreach (var smr in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.name.StartsWith("PedKuro_Collapsed_") || AssetDatabase.Contains(mesh)) continue;
            string mp = $"{PplMeshDir}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
            if (existing != null) smr.sharedMesh = existing;
            else AssetDatabase.CreateAsset(mesh, mp);
        }

        // 3. authored hair on the Head bone
        Transform head = null;
        foreach (var t in high.GetComponentsInChildren<Transform>(true)) if (t.name == "Head") { head = t; break; }
        var extras = new List<Renderer>();
        if (head != null && hairPrefab != null)
        {
            var h = Object.Instantiate(hairPrefab);
            h.name = "Ped Hair";
            float s = person.transform.lossyScale.y * Mathf.Max(0.01f, set.hairScale);
            h.transform.SetPositionAndRotation(head.position + person.transform.rotation * (set.hairOffset * person.transform.lossyScale.y),
                                               person.transform.rotation);
            h.transform.localScale = Vector3.one * s;
            h.transform.SetParent(head, true);
            var shape = h.GetComponent<HelmetDrivenHairShapeKey>() ?? h.AddComponent<HelmetDrivenHairShapeKey>();
            shape.Bind(high, head);
            var hm = PplHairMaterial(high, set, colour);
            foreach (var r in h.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.layer = person.layer;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                if (hm != null) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = hm; r.sharedMaterials = ms; }
                extras.Add(r);
            }
        }
        var lodg = person.GetComponent<LODGroup>();
        if (lodg != null && extras.Count > 0)
        {
            var lods = lodg.GetLODs();
            if (lods.Length > 0)
            {
                var list = new List<Renderer>((lods[0].renderers ?? new Renderer[0]).Where(rr => rr != null));
                list.AddRange(extras);
                lods[0].renderers = list.ToArray();
                lodg.SetLODs(lods);
            }
        }
        // 4. blinking
        if (set.blink)
        {
            foreach (var smr in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name == "Mesh_0" && smr.enabled) RiderBlink.Alias(RiderBlink.KeyFor(smr), PedestrianKuroLook.BlinkSourceKey);
            RiderBlink.Attach(person);
        }
        EditorUtility.SetDirty(person);
    }

    private static Material PplHairMaterial(Transform high, PedestrianModelLibrary.KuroRiderSet set, int pick)
    {
        Material like = null;
        foreach (var smr in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.name != "Mesh_0" || !smr.enabled) continue;
            foreach (var m in smr.sharedMaterials) if (m != null && m.HasProperty("_MainTex")) { like = m; break; }
            if (like != null) break;
        }
        if (like == null || set.hairColours == null || set.hairColours.Length == 0) return null;
        int ci = pick % set.hairColours.Length;
        string key = $"hair|{ci}|{like.shader.name}";
        if (_pplMats.TryGetValue(key, out var hm) && hm != null) return hm;
        string path = $"{PplMatDir}/NagisaPpl_Hair_{ci}.mat";
        hm = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (hm == null) { hm = new Material(like) { name = $"NagisaPpl_Hair_{ci}" }; AssetDatabase.CreateAsset(hm, path); }
        else { hm.shader = like.shader; hm.CopyPropertiesFromMaterial(like); }
        hm.SetTexture("_MainTex", set.hairStrands != null ? set.hairStrands : Texture2D.whiteTexture);
        hm.SetTextureScale("_MainTex", Vector2.one);
        hm.SetTextureOffset("_MainTex", Vector2.zero);
        if (hm.HasProperty("_Color")) hm.SetColor("_Color", set.hairColours[ci]);
        if (hm.HasProperty("_BaseColor")) hm.SetColor("_BaseColor", set.hairColours[ci]);
        hm.enableInstancing = true;
        EditorUtility.SetDirty(hm);
        _pplMats[key] = hm;
        return hm;
    }

    // ------------------------------------------------------------------ runners
    [System.Serializable] private sealed class PplRunHeader { public float naturalSpeed; }

    private static void PplMakeRunner(GameObject go, System.Random rng)
    {
        var look = go.GetComponent<MapleCityLook>();
        var actor = go.GetComponent<MinatoCrowdActor>();
        if (look == null || actor == null || string.IsNullOrEmpty(look.donor)) return;
        var cycle = AssetDatabase.LoadAssetAtPath<TextAsset>($"{PplRunCycleDir}/MapleLife_RunCycle_{look.donor}.json");
        if (cycle == null) { Debug.LogWarning($"[nagisa-ppl] no run cycle for donor '{look.donor}' ({go.name}); keeps the walk."); return; }
        var rig = actor.rigRoot != null ? actor.rigRoot : go.transform.Find("LOD0 High Skinned/Rigged Character");
        if (rig == null) return;
        float speed = Mathf.Lerp(2.6f, 3.3f, (float)rng.NextDouble());
        if (actor.motion == MK.Walk && (actor.pathEnd - actor.pathStart).sqrMagnitude > 1f) actor.moveSpeed = speed;
        float natural = 2.82f;
        try { natural = Mathf.Max(0.5f, JsonUtility.FromJson<PplRunHeader>(cycle.text).naturalSpeed); } catch (System.Exception) { }
        var run = go.GetComponent<MapleCityRunner>() ?? go.AddComponent<MapleCityRunner>();
        run.cycle = cycle;
        run.rigRoot = rig;
        run.phase = (float)rng.NextDouble();
        run.playback = Mathf.Clamp(speed / (natural * Mathf.Max(0.5f, go.transform.lossyScale.x)), 0.8f, 1.4f);
        EditorUtility.SetDirty(run);
        _pplRunners++;
    }

    // ------------------------------------------------------------------ sunbathers
    private static Transform NearestTowel(List<Transform> towels, Vector3 p, float within)
    {
        Transform best = null; float bd = within * within;
        foreach (var t in towels)
        {
            if (t == null) continue;
            float d = (new Vector2(t.position.x - p.x, t.position.z - p.z)).sqrMagnitude;
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }

    /// <summary>The towel's long axis, signed so the head end is the one further from the water.</summary>
    private static void TowelAxis(Transform towel, out Vector3 headDir)
    {
        var mf = towel.GetComponentInChildren<MeshFilter>(true);
        Vector3 axis = towel.forward;
        if (mf != null && mf.sharedMesh != null)
        {
            var s = mf.sharedMesh.bounds.size;
            axis = s.x >= s.z ? mf.transform.right : mf.transform.forward;
        }
        axis.y = 0f; axis.Normalize();
        var p = towel.position;
        float a = _ground.Coast(p.x + axis.x, p.z + axis.z), b = _ground.Coast(p.x - axis.x, p.z - axis.z);
        headDir = a >= b ? axis : -axis;
    }

    private static bool PplLieDown(NbCast cast, Transform group, Vector3 centre, float groundY, Vector3 headDir,
                                   PedestrianModelLibrary.KuroRiderSet set, System.Random rng, string role)
    {
        var p = new Vector3(centre.x, groundY, centre.z);
        var person = NbPerson(cast.stand, group, role, p, headDir, MK.Idle, (float)rng.NextDouble());
        if (person == null) return false;
        var actor = person.GetComponent<MinatoCrowdActor>();
        float height = 1.05f;
        var smr0 = person.GetComponentsInChildren<SkinnedMeshRenderer>(false).FirstOrDefault();
        if (smr0 != null) height = Mathf.Clamp(smr0.bounds.size.y, 0.7f, 1.6f);
        if (actor != null) actor.enabled = false;
        foreach (var r in person.GetComponentsInChildren<Renderer>(true))
            if (r.name == "Contact Shadow") Object.DestroyImmediate(r.gameObject);

        PplPick(role.ToLowerInvariant(), 1f, rng, out string kit, out bool female);
        if (kit.StartsWith("Run") || kit == "KidTee") { kit = (rng.NextDouble() < 0.5 ? PplSwimM : PplSwimF)[rng.Next(4)]; female = kit.StartsWith("Bikini"); }
        PplDress(person, kit, PplHair(set, female, rng), rng.Next(set.hairColours.Length), set);

        // lay it on its back: figure up (head) -> headDir, figure front -> skyward; feet half a body below the centre
        var pose = new GameObject("Sunbather Pose").transform;
        pose.SetParent(group, false);
        float thickness = 0.11f * person.transform.lossyScale.y;
        pose.position = new Vector3(centre.x - headDir.x * height * 0.5f, groundY + thickness, centre.z - headDir.z * height * 0.5f);
        pose.rotation = Quaternion.LookRotation(Vector3.up, headDir);
        person.transform.SetParent(pose, false);
        person.transform.localPosition = Vector3.zero;
        person.transform.localRotation = Quaternion.identity;
        person.name = $"Nagisa Sunbather {_pplLying:D3}";

        var sb = pose.gameObject.AddComponent<NagisaSunbather>();
        sb.figure = person.transform;
        sb.rigRoot = actor != null && actor.rigRoot != null ? actor.rigRoot : person.transform.Find("LOD0 High Skinned/Rigged Character");
        sb.pose = rng.Next(3);
        sb.legPose = rng.Next(3);
        sb.phase = (float)rng.NextDouble();
        _pplLying++;
        return true;
    }
}
