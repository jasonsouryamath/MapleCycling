using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// KURO-RIDER PEDESTRIANS (claude-peds, 2026-09-26, user direction): the primary look of the
/// model-swap pipeline. Runs AFTER the donor wardrobe (PedestrianAppearance.Apply) on a Maple
/// pedestrian when the model set is on and <see cref="PedestrianModelLibrary.KuroRiderSet"/> is
/// enabled. The figure keeps its Kuro rider chibi body (painted anime face, helmet sunk by the
/// helmetless mesh) and its procedural walk (the Kuro rig already uses the canonical bone names,
/// so <see cref="PedestrianBoneMap"/> resolves it 1:1). On top:
///   * street kit: the body slots get a PedKit_*.png atlas (same UV layout as KuroKit_*.png);
///   * hair: the hair cap is hidden and an authored hair mesh is parented to the Head bone;
///   * blink: RiderBlink.Attach (Assets/Ride/Race, called, not edited);
///   * variety: no identical (donor look, kit, hair) within VarietyRadius, same rule as the director.
/// </summary>
public static class PedestrianKuroLook
{
    public static float VarietyRadius = 9f;
    public static int Applied, Blinking, Kitted, Haired;

    sealed class Placed { public Vector3 pos; public string sig; }
    static readonly Dictionary<long, List<Placed>> _grid = new Dictionary<long, List<Placed>>();
    static readonly Dictionary<(Material, Texture), Material> _kitMats = new Dictionary<(Material, Texture), Material>();

    static long Key(Vector3 p) => ((long)Mathf.FloorToInt(p.x / VarietyRadius) << 32) ^ (uint)Mathf.FloorToInt(p.z / VarietyRadius);

    static bool Clash(Vector3 p, string sig)
    {
        long cx = Mathf.FloorToInt(p.x / VarietyRadius), cz = Mathf.FloorToInt(p.z / VarietyRadius);
        for (long dx = -1; dx <= 1; dx++)
            for (long dz = -1; dz <= 1; dz++)
                if (_grid.TryGetValue(((cx + dx) << 32) ^ (uint)(cz + dz), out var list))
                    foreach (var q in list)
                        if (q.sig == sig && (q.pos - p).sqrMagnitude < VarietyRadius * VarietyRadius) return true;
        return false;
    }

    public static PedestrianKuroTag Apply(PedestrianAppearance look, MinatoCrowdActor actor, PedestrianModelLibrary.KuroRiderSet set, uint hash,
                                          Texture2D[] kitOverride = null)
    {
        if (look == null || actor == null || set == null || !set.enabled) return null;
        var go = look.gameObject;
        var tag = go.GetComponent<PedestrianKuroTag>();
        if (tag != null) return tag;
        tag = go.AddComponent<PedestrianKuroTag>();
        var kitList = kitOverride != null && kitOverride.Length > 0 ? kitOverride : set.streetKits;
        int kits = kitList != null ? kitList.Length : 0;
        int hairs = set.hairStyles != null ? set.hairStyles.Length : 0;
        int combos = Mathf.Max(1, (kits + 1) * (hairs + 1));
        // choose (kit, hair): -1 = keep the donor's; walk combos from a stable start until no clash
        int start = (int)(hash % (uint)combos);
        int kit = -1, hair = -1;
        string sig = look.BaseSignature;
        for (int k = 0; k < combos; k++)
        {
            int c = (start + k) % combos;
            int kk = kits > 0 ? c % (kits + 1) - 1 : -1;
            int hh = hairs > 0 ? c / (kits + 1) % (hairs + 1) - 1 : -1;
            if (kits > 0 && kk < 0) continue;        // with kits available, always wear one
            if (hairs > 0 && hh < 0) continue;       // same for hair
            kit = kk; hair = hh;
            sig = $"{look.BaseSignature}|k{kit}|h{hair}";
            if (!Clash(go.transform.position, sig)) break;
        }
        if (!_grid.TryGetValue(Key(go.transform.position), out var cell)) _grid[Key(go.transform.position)] = cell = new List<Placed>();
        cell.Add(new Placed { pos = go.transform.position, sig = sig });
        tag.signature = sig;
        tag.kit = kit;
        tag.hair = hair;

        var high = go.transform.Find("LOD0 High Skinned");
        var extras = new List<Renderer>();
        if (kit >= 0 && high != null)
        {
            var tex = kitList[kit];
            foreach (var smr in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.enabled || smr.name != "Mesh_0") continue;
                var mats = smr.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) < 0 || !m.HasProperty("_MainTex")) continue;
                    if (!_kitMats.TryGetValue((m, tex), out var km) || km == null)
                    {
                        km = new Material(m) { name = m.name + "_" + tex.name };
                        km.SetTexture("_MainTex", tex);
                        _kitMats[(m, tex)] = km;
                    }
                    mats[i] = km; changed = true;
                }
                if (changed) { smr.sharedMaterials = mats; Kitted++; tag.kitName = tex.name; }
            }
        }
        var head = actor.Bone("Head");
        if (hair >= 0 && head != null && set.hairStyles[hair] != null)
        {
            var cap = look.GetComponent<MapleCityLook>() != null ? look.GetComponent<MapleCityLook>().hairCap : null;
            if (cap != null) cap.forceRenderingOff = true;
            // hats were sized to the (now hidden) hair cap; they float over the authored hair
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r.name == "Ped Hat" || r.name == "Hair Cap") r.forceRenderingOff = true;
            if (high != null && CollapseHelmet(high)) tag.collapsed = true;
            var h = UnityEngine.Object.Instantiate(set.hairStyles[hair]);
            h.name = "Ped Hair";
            // authored in the FIGURE frame (+Y up, +Z = face direction), origin at the Head bone,
            // metres at figure scale 1 (independent of the Head bone's own axes / 1/100 import scale)
            float s = go.transform.lossyScale.y * Mathf.Max(0.01f, set.hairScale);
            h.transform.SetPositionAndRotation(head.position + go.transform.rotation * (set.hairOffset * go.transform.lossyScale.y),
                                               go.transform.rotation);
            h.transform.localScale = Vector3.one * s;
            h.transform.SetParent(head, true);
            AlignToCap(h.transform, cap, go.transform.lossyScale.y);
            // Hair remains Head-bone authored, while the clearance key is driven from the
            // live helmet bone so animation/retargeting cannot push the crown through shell.
            var hairShape = h.GetComponent<HelmetDrivenHairShapeKey>();
            if (hairShape == null) hairShape = h.AddComponent<HelmetDrivenHairShapeKey>();
            hairShape.Bind(high, head);
            foreach (var r in h.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.layer = go.layer;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                var hm = HairMaterial(high, set, (int)((hash >> 8) % 997u));
                if (hm != null)
                {
                    var hms = r.sharedMaterials;
                    for (int i = 0; i < hms.Length; i++) hms[i] = hm;
                    r.sharedMaterials = hms;
                }
                extras.Add(r);
            }
            tag.hairName = set.hairStyles[hair].name;
            Haired++;
        }
        if (extras.Count > 0)
        {
            var group = go.GetComponent<LODGroup>();
            if (group != null)
            {
                var lods = group.GetLODs();
                if (lods.Length > 0)
                {
                    var list = new List<Renderer>(lods[0].renderers ?? Array.Empty<Renderer>());
                    list.AddRange(extras);
                    lods[0].renderers = list.ToArray();
                    group.SetLODs(lods);
                }
            }
        }
        if (set.blink)
        {
            // The eyelid data is keyed on Kuro's own body (Mesh_0#41496). The crowd donors are the
            // same head sculpt on a re-exported body (41497-41499 verts) and the collapsed copy has
            // a new name, so alias them; the lid points live in the Head bone's frame.
            if (high != null)
                foreach (var smr in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (smr.name == "Mesh_0" && smr.enabled) RiderBlink.Alias(RiderBlink.KeyFor(smr), BlinkSourceKey);
            var blink = RiderBlink.Attach(go);
            tag.blinks = blink != null;
            if (blink != null) Blinking++;
        }
        Applied++;
        return tag;
    }

    /// <summary>
    /// Hair only: hides the donor's helmet / hair cap and mounts one authored hair mesh on the Head bone with a tinted
    /// clone of the body's cel material. The body mesh keeps its vertices, UVs and skin weights (the helmet is collapsed
    /// onto the skull, same as <see cref="Apply"/>). Idempotent; returns the hair object or null. Used by Shunta's NPC styles.
    /// </summary>
    public static GameObject ApplyHair(GameObject go, PedestrianModelLibrary.KuroRiderSet set, int hair, int colour)
    {
        if (go == null || set == null || !set.enabled || set.hairStyles == null) return null;
        if (hair < 0 || hair >= set.hairStyles.Length || set.hairStyles[hair] == null) return null;
        var high = go.transform.Find("LOD0 High Skinned");
        Transform head = null;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Head") { head = t; break; }
        if (high == null || head == null) return null;
        foreach (Transform child in head) if (child.name == "Ped Hair") return child.gameObject;
        var tagLook = go.GetComponent<MapleCityLook>();
        var cap = tagLook != null ? tagLook.hairCap : null;
        if (cap != null) cap.forceRenderingOff = true;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r.name == "Ped Hat" || r.name == "Hair Cap") r.forceRenderingOff = true;
        CollapseHelmet(high);
        var h = UnityEngine.Object.Instantiate(set.hairStyles[hair]);
        h.name = "Ped Hair";
        float s = go.transform.lossyScale.y * Mathf.Max(0.01f, set.hairScale);
        h.transform.SetPositionAndRotation(head.position + go.transform.rotation * (set.hairOffset * go.transform.lossyScale.y), go.transform.rotation);
        h.transform.localScale = Vector3.one * s;
        h.transform.SetParent(head, true);
        AlignToCap(h.transform, cap, go.transform.lossyScale.y);
        var hairShape = h.GetComponent<HelmetDrivenHairShapeKey>();
        if (hairShape == null) hairShape = h.AddComponent<HelmetDrivenHairShapeKey>();
        hairShape.Bind(high, head);
        var extras = new List<Renderer>();
        var hm = HairMaterial(high, set, colour);
        foreach (var r in h.GetComponentsInChildren<Renderer>(true))
        {
            r.gameObject.layer = go.layer;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            if (hm != null)
            {
                var hms = r.sharedMaterials;
                for (int i = 0; i < hms.Length; i++) hms[i] = hm;
                r.sharedMaterials = hms;
            }
            extras.Add(r);
        }
        RegisterOnLod0(go, extras);
        Haired++;
        return h.gameObject;
    }

    /// <summary>Adds renderers to LOD0 of the figure's LODGroup so they cull with it.</summary>
    public static void RegisterOnLod0(GameObject go, List<Renderer> extras)
    {
        var group = go != null ? go.GetComponent<LODGroup>() : null;
        if (group == null || extras == null || extras.Count == 0) return;
        var lods = group.GetLODs();
        if (lods.Length == 0) return;
        var list = new List<Renderer>(lods[0].renderers ?? Array.Empty<Renderer>());
        list.AddRange(extras);
        lods[0].renderers = list.ToArray();
        group.SetLODs(lods);
    }

    /// <summary>
    /// The donor hair cap always sits on the skull. If the authored hair's centre is clearly off it (Nagisa
    /// Bay figures put the hair ~0.4 m low and behind, leaving the kit's near-black scalp showing as a black
    /// dome), slide the hair so its bounds centre matches the cap's. Within tolerance it is left untouched, so
    /// Maple City's verified placement does not move. 2026-10-02.
    /// </summary>
    static void AlignToCap(Transform hair, Renderer cap, float figureScale)
    {
        if (cap == null) return;
        var rs = hair.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return;
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        Vector3 delta = cap.bounds.center - b.center;
        if (delta.magnitude > 0.12f * Mathf.Max(0.5f, figureScale)) hair.position += delta;
    }

    static readonly Dictionary<(Shader, int), Material> _hairMats = new Dictionary<(Shader, int), Material>();

    /// <summary>The FBX's own "Hair" material is a pipeline default; hair uses a clone of the
    /// body's cel material (same lighting as the face) with the strand texture and a tint.</summary>
    static Material HairMaterial(Transform high, PedestrianModelLibrary.KuroRiderSet set, int pick)
    {
        if (high == null || set.hairColours == null || set.hairColours.Length == 0) return null;
        Material like = null;
        foreach (var smr in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.name != "Mesh_0" || !smr.enabled) continue;
            foreach (var m in smr.sharedMaterials) if (m != null && m.HasProperty("_MainTex")) { like = m; break; }
            if (like != null) break;
        }
        if (like == null) return null;
        int ci = pick % set.hairColours.Length;
        if (_hairMats.TryGetValue((like.shader, ci), out var hm) && hm != null) return hm;
        hm = new Material(like) { name = "PedKuro_Hair_" + ci };
        // The body material enables a tangent-space normal map; the hair FBX meshes carry no tangents, and a zero
        // tangent makes the shader output NaN, which HDRP clamps to pure black. Flat-shade the hair. 2026-10-02.
        if (hm.HasProperty("_NormalStrength")) hm.SetFloat("_NormalStrength", 0f);
        hm.SetTexture("_MainTex", set.hairStrands != null ? set.hairStrands : Texture2D.whiteTexture);
        hm.SetTextureScale("_MainTex", Vector2.one);
        hm.SetTextureOffset("_MainTex", Vector2.zero);
        if (hm.HasProperty("_Color")) hm.SetColor("_Color", set.hairColours[ci]);
        if (hm.HasProperty("_BaseColor")) hm.SetColor("_BaseColor", set.hairColours[ci]);
        // Dark hair tints rendered as pure black silhouettes under the daylight grade (same failure as the Nagisa
        // pines); lift to a readable dark brown/charcoal so the strands and shading show. 2026-10-02.
        var lifted = PedestrianAppearance.LiftDark(set.hairColours[ci], 0.46f);
        if (hm.HasProperty("_Color")) hm.SetColor("_Color", lifted);
        if (hm.HasProperty("_BaseColor")) hm.SetColor("_BaseColor", lifted);
        _hairMats[(like.shader, ci)] = hm;
        return hm;
    }

    // ------------------------------------------------------------------ helmet collapse
    // Same maths as design_assets/3d/pedestrians/build_ped_hair.py collapse() (the Blender proof
    // renders), done at run time on whatever Mesh_0 the donor wears (the MapleLife helmetless mesh
    // or the raw crowd body) so the vertex count, UVs, skin weights and face are untouched.
    // Parameters are in the Head bone's REST frame, metres, Unity mesh axes (+Y up, +Z = face,
    // +X = character's right); from the Blender skull (C = (0.035, 0.03, 0.96), Head bone at
    // (0, 0.0134, 0.7569)) via unity = (-bx, bz, -by). PROVISIONAL, tune with the renders.
    public const string BlinkSourceKey = "Mesh_0#41496";
    public static Vector3 SkullOffset = new Vector3(-0.035f, 0.2031f, -0.0166f);
    public static float SkullRx = 0.185f, SkullRyUp = 0.21f, SkullRFront = 0.16f, SkullRBack = 0.21f;
    public static float SkullMinY = 0.083f;          // Blender z 0.84 minus the Head bone height
    public static float BrowMinY = 0.178f, BrowHalfX = 0.13f, BrowFrontZ = 0.1314f, BrowFlatZ = 0.1234f;
    const float HeadBoneHeight = 0.7569f;            // Blender Kuro, metres above the soles
    public static int Collapsed, CollapseFailed;
    static readonly Dictionary<Mesh, Mesh> _collapsed = new Dictionary<Mesh, Mesh>();

    static bool IsHeadBone(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        n = n.ToLowerInvariant();
        return n.Contains("head") || n.Contains("neck");
    }

    /// <summary>Puts a collapsed copy of Mesh_0 on a fresh SMR (never re-assign sharedMesh on a
    /// renderer that may already have skinned). Idempotent: the copy's name marks it.</summary>
    public static bool CollapseHelmet(Transform high)
    {
        foreach (var smr in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.name != "Mesh_0" || !smr.enabled || smr.sharedMesh == null) continue;
            if (smr.sharedMesh.name.StartsWith("PedKuro_Collapsed_", StringComparison.Ordinal)) return true;
            var mesh = GetCollapsed(smr);
            if (mesh == null) { CollapseFailed++; return false; }
            var go = new GameObject("Mesh_0", typeof(SkinnedMeshRenderer));
            go.transform.SetParent(smr.transform.parent, false);
            go.transform.localPosition = smr.transform.localPosition;
            go.transform.localRotation = smr.transform.localRotation;
            go.transform.localScale = smr.transform.localScale;
            go.layer = smr.gameObject.layer;
            var fresh = go.GetComponent<SkinnedMeshRenderer>();
            fresh.sharedMesh = mesh;
            fresh.bones = smr.bones;
            fresh.rootBone = smr.rootBone;
            fresh.sharedMaterials = smr.sharedMaterials;
            fresh.quality = smr.quality;
            fresh.updateWhenOffscreen = false;
            fresh.localBounds = smr.localBounds;
            fresh.shadowCastingMode = smr.shadowCastingMode;
            fresh.receiveShadows = smr.receiveShadows;
            fresh.lightProbeUsage = smr.lightProbeUsage;
            fresh.reflectionProbeUsage = smr.reflectionProbeUsage;
            fresh.motionVectorGenerationMode = smr.motionVectorGenerationMode;
            fresh.skinnedMotionVectors = smr.skinnedMotionVectors;
            smr.gameObject.name = "Mesh_0 (helmet)";
            smr.enabled = false;
            // keep the LOD group pointing at what renders
            var group = high.GetComponentInParent<LODGroup>();
            if (group != null)
            {
                var lods = group.GetLODs();
                for (int li = 0; li < lods.Length; li++)
                {
                    var rs = lods[li].renderers;
                    if (rs == null) continue;
                    for (int ri = 0; ri < rs.Length; ri++) if (rs[ri] == smr) rs[ri] = fresh;
                    lods[li].renderers = rs;
                }
                group.SetLODs(lods);
            }
            Collapsed++;
            return true;
        }
        return false;
    }

    static Mesh GetCollapsed(SkinnedMeshRenderer smr)
    {
        var src = smr.sharedMesh;
        if (_collapsed.TryGetValue(src, out var done)) return done;
        _collapsed[src] = null;
        if (!src.isReadable) { Debug.LogWarning($"[peds-kuro] {src.name} is not readable; helmet not collapsed"); return null; }
        var bones = smr.bones;
        var bind = src.bindposes;
        int headIdx = -1;
        var headish = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            headish[i] = IsHeadBone(bones[i].name);
            if (bones[i].name == "Head") headIdx = i;
        }
        if (headIdx < 0 || headIdx >= bind.Length) { Debug.LogWarning($"[peds-kuro] {src.name}: no Head bone"); return null; }
        var v = src.vertices;
        var inv = bind[headIdx].inverse;             // Head bone rest -> mesh space
        Vector3 hp = inv.MultiplyPoint3x4(Vector3.zero);
        float minY = float.MaxValue;
        for (int i = 0; i < v.Length; i++) if (v[i].y < minY) minY = v[i].y;
        float s = (hp.y - minY) / HeadBoneHeight;    // mesh units per Blender metre
        if (s <= 0f || float.IsNaN(s)) return null;
        var perVert = src.GetBonesPerVertex();
        var weights = src.GetAllBoneWeights();
        Vector3 c = hp + SkullOffset * s;
        int moved = 0, flat = 0, wi = 0;
        for (int i = 0; i < v.Length; i++)
        {
            float hw = 0f;
            int n = perVert[i];
            for (int k = 0; k < n; k++, wi++)
            {
                var bw = weights[wi];
                if (bw.boneIndex < headish.Length && headish[bw.boneIndex]) hw += bw.weight;
            }
            if (hw <= 0.5f) continue;
            Vector3 rel = (v[i] - hp) / s;           // Blender metres, Head-bone relative
            if (rel.y < SkullMinY) continue;
            Vector3 d = (v[i] - c) / s;
            float len = d.magnitude;
            if (len > 1e-5f)
            {
                Vector3 u = d / len;
                float rz = u.z >= 0f ? SkullRFront : SkullRBack;
                float r = 1f / Mathf.Sqrt(u.x * u.x / (SkullRx * SkullRx) + u.y * u.y / (SkullRyUp * SkullRyUp) + u.z * u.z / (rz * rz));
                if (len > r) { v[i] = c + u * r * s; moved++; rel = (v[i] - hp) / s; }
            }
            // Kuro's fringe spikes in front of the forehead: flatten onto the face plane
            if (rel.y >= BrowMinY && Mathf.Abs(rel.x - SkullOffset.x) <= BrowHalfX && rel.z > BrowFrontZ)
            {
                rel.z = BrowFlatZ; v[i] = hp + rel * s; flat++;
            }
        }
        var m = UnityEngine.Object.Instantiate(src);
        m.name = "PedKuro_Collapsed_" + src.name;
        m.vertices = v;
        m.RecalculateBounds();
        _collapsed[src] = m;
        Debug.Log($"[peds-kuro] collapsed {src.name}: {moved} verts onto the skull, {flat} brow verts flattened, {v.Length} verts, scale {s:F3}");
        return m;
    }
}

/// <summary>Marks a Kuro-rider pedestrian and records its choice (harness / debugging).</summary>
public sealed class PedestrianKuroTag : MonoBehaviour
{
    public string signature = "";
    public int kit = -1, hair = -1;
    public string kitName = "", hairName = "";
    public bool blinks;
    public bool collapsed;
}
