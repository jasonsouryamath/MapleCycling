using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// AMBIENT PEDESTRIANS (copilot, 2026-09-26). Per-figure appearance for ambient pedestrians on
/// every map: civilian look (from <see cref="PedestrianWardrobe"/>), hair-cap style, a light
/// "persona" (adult / senior / young), a hat and a bag. Everything is chosen by
/// <see cref="PedestrianDirector"/> so that no two neighbours look identical, and applied here.
///
/// ACCESSORIES are tiny procedural cel-lit meshes (one shared mesh per kind, one shared material
/// per palette colour cloned from the figure's own CelLit body material so the lighting recipe
/// matches). They are fitted to each donor's measured head (hair-cap extents) and torso (live
/// skin vertices), parented to the Head / Spine02 / Hips / hand bones, and registered on the
/// figure's LOD0 so they cull with it. Budget: &lt;= 400 tris per figure, no extra bones.
///
/// Knobs are the PROVISIONAL constants below; see CLAUDE_HANDOFF.md "Ambient pedestrians".
/// </summary>
[DisallowMultipleComponent]
public sealed class PedestrianAppearance : MonoBehaviour
{
    public enum Hat { None, Cap, Bucket, Beanie }
    public enum Bag { None, Backpack, Shoulder, Shopping }
    public enum Persona { Adult, Senior, Young }

    public string donorKey = "";
    public string lookId = "";
    public int capStyle = -1;
    public Hat hat;
    public Bag bag;
    public int hatColour, bagColour;
    public Persona persona;
    public bool applied;

    /// <summary>What a player would read as "the same person": body look + hair style + hat + bag.</summary>
    public string BaseSignature => $"{donorKey}|{lookId}|{capStyle}";
    public string Signature => $"{BaseSignature}|{hat}{(hat != Hat.None ? hatColour : 0)}|{bag}{(bag != Bag.None ? bagColour : 0)}|{persona}";

    // ------------------------------------------------------------------ PROVISIONAL knobs
    public static readonly Color[] Palette =
    {
        new Color(0.16f, 0.20f, 0.30f), // navy
        new Color(0.78f, 0.74f, 0.64f), // stone
        new Color(0.55f, 0.18f, 0.20f), // brick
        new Color(0.22f, 0.38f, 0.28f), // forest
        new Color(0.86f, 0.62f, 0.22f), // mustard
        new Color(0.90f, 0.88f, 0.84f), // off-white
        new Color(0.12f, 0.12f, 0.13f), // charcoal
        new Color(0.46f, 0.30f, 0.20f), // tan leather
        new Color(0.36f, 0.52f, 0.72f), // denim
        new Color(0.80f, 0.46f, 0.52f), // dusty rose
        new Color(0.44f, 0.40f, 0.62f), // lavender
        new Color(0.30f, 0.60f, 0.62f), // teal
    };
    static readonly Color SeniorHair = new Color(0.72f, 0.72f, 0.74f);
    static readonly Color PaperBag = new Color(0.80f, 0.66f, 0.46f);

    // ------------------------------------------------------------------ runtime
    Transform _head, _spine, _hips, _rHand, _lHand;
    readonly List<Renderer> _extra = new List<Renderer>();
    Transform _shopping;           // bag that hangs from the right hand
    Transform _shopping2;          // optional second bag, left hand
    MapleCityLook _tag;

    public static string DonorKeyOf(GameObject go)
    {
        var tag = go.GetComponent<MapleCityLook>();
        if (tag != null && !string.IsNullOrEmpty(tag.donor)) return tag.donor;
        string n = go.name;
        return n.StartsWith("Crowd_", StringComparison.Ordinal) ? n.Substring(6) : "";
    }

    /// <summary>Applies the chosen look. Safe to call once; later calls are ignored.</summary>
    public void Apply(PedestrianWardrobe wardrobe, int lookIndex, MinatoCrowdActor actor)
    {
        if (applied || actor == null || !actor.IsReady) return;
        applied = true;
        _head = actor.Bone("Head");
        _spine = actor.Bone("Spine02");
        _hips = actor.Bone("Hips");
        _rHand = actor.Bone("RightHand");
        _lHand = actor.Bone("LeftHand");
        var high = transform.Find("LOD0 High Skinned");
        if (high == null) return;
        _tag = GetComponent<MapleCityLook>();

        // 1) body look + hair cap (the cap is what hides the donor's baked-in cycling helmet)
        var donor = wardrobe != null ? wardrobe.Find(donorKey) : null;
        Renderer cap = _tag != null ? _tag.hairCap : null;
        if (donor != null && donor.looks.Length > 0)
        {
            var look = donor.looks[Mathf.Clamp(lookIndex, 0, donor.looks.Length - 1)];
            lookId = look.id;
            SwapLookMaterials(look);
            EnsureHelmetless(high, donor, actor);
            if (capStyle < 0 || capStyle >= donor.caps.Length) capStyle = 0;
            cap = EnsureCap(cap, donor.caps[capStyle], look.hairCap);
            if (_tag == null) _tag = gameObject.AddComponent<MapleCityLook>();
            _tag.donor = donorKey;
            _tag.look = look.id;
            _tag.civilianAtlas = true;
            _tag.hairCap = cap;
            if (_tag.role == MapleCityLook.Role.Other && actor.motion == MinatoCrowdActor.MotionKind.Walk)
                _tag.role = MapleCityLook.Role.Walker;
        }
        if (persona == Persona.Senior && cap != null)
            cap.sharedMaterial = Tinted(cap.sharedMaterial, SeniorHair, "senior_hair");

        // 2) accessories, fitted to this donor
        var body = FirstBodyMaterial(high);
        _bodyLike = body;
        _rFore = actor.Bone("RightForeArm");
        _lFore = actor.Bone("LeftForeArm");
        if (body != null)
        {
            if (hat != Hat.None && cap != null) BuildHat(cap, body);
            if (bag != Bag.None) BuildBag(high, body);
        }
        RegisterOnLod0();
    }

    // ------------------------------------------------------------------ drop-in model (claude-peds)
    // Used ONLY for figures that PedestrianModelSwap retargeted onto a library model (opt-in, see
    // PedestrianModelLibrary). The donor path above is untouched. Hats, bags and handheld props
    // attach to the alias-resolved Head / Spine02 / Hips / hand bones of the new rig, and are fitted
    // from that model's own skinned vertices instead of the donor's hair cap / torso cache.

    bool _hasModelHead, _hasModelTorso;
    Vector3 _mhLo, _mhHi, _mtLo, _mtHi;
    /// <summary>Library model id when this figure wears a drop-in model (else empty).</summary>
    public string modelId = "";

    public void ApplyModel(MinatoCrowdActor actor, string model, string variant, Material like, SkinnedMeshRenderer[] skins)
    {
        if (applied || actor == null || !actor.IsReady) return;
        applied = true;
        modelId = model;
        donorKey = "model:" + model;
        lookId = variant;
        capStyle = 0;
        _head = actor.Bone("Head");
        _spine = actor.Bone("Spine02");
        _hips = actor.Bone("Hips");
        _rHand = actor.Bone("RightHand");
        _lHand = actor.Bone("LeftHand");
        _rFore = actor.Bone("RightForeArm");
        _lFore = actor.Bone("LeftForeArm");
        _bodyLike = like;
        MeasureModel(actor, skins);
        if (like != null)
        {
            if (hat != Hat.None && _hasModelHead) BuildHat(null, like);
            if (bag != Bag.None) BuildBag(null, like);
        }
        RegisterOnLod0();
    }

    /// <summary>Head extents (Head space, along the figure axes) and torso extents (figure-local, scaled) from the live skin.</summary>
    void MeasureModel(MinatoCrowdActor actor, SkinnedMeshRenderer[] skins)
    {
        if (skins == null || _head == null) return;
        var torsoBones = new HashSet<Transform>();
        foreach (var n in new[] { "Spine", "Spine01", "Spine02" }) { var b = actor.Bone(n); if (b != null) torsoBones.Add(b); }
        Vector3 r = _head.InverseTransformDirection(transform.right).normalized;
        Vector3 u = _head.InverseTransformDirection(transform.up).normalized;
        Vector3 f = _head.InverseTransformDirection(transform.forward).normalized;
        Vector3 hLo = Vector3.one * float.MaxValue, hHi = Vector3.one * float.MinValue;
        Vector3 tLo = hLo, tHi = hHi;
        float S = transform.lossyScale.y;
        foreach (var smr in skins)
        {
            var mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) continue;
            var bones = smr.bones; var bind = mesh.bindposes;
            if (bones.Length == 0 || bind.Length != bones.Length) continue;
            var v = mesh.vertices; var bw = mesh.boneWeights;
            if (bw.Length != v.Length) continue;
            for (int i = 0; i < v.Length; i++)
            {
                if (bw[i].weight0 < 0.5f) continue;
                var bone = bones[bw[i].boneIndex0];
                if (bone == null) continue;
                bool head = bone == _head || bone.IsChildOf(_head);
                bool torso = torsoBones.Contains(bone);
                if (!head && !torso) continue;
                var w = (bone.localToWorldMatrix * bind[bw[i].boneIndex0]).MultiplyPoint3x4(v[i]);
                if (head)
                {
                    var p = _head.InverseTransformPoint(w);
                    var q = new Vector3(Vector3.Dot(p, r), Vector3.Dot(p, u), Vector3.Dot(p, f));
                    hLo = Vector3.Min(hLo, q); hHi = Vector3.Max(hHi, q);
                }
                else
                {
                    var p = transform.InverseTransformPoint(w) * S;
                    tLo = Vector3.Min(tLo, p); tHi = Vector3.Max(tHi, p);
                }
            }
        }
        _hasModelHead = hHi.x > hLo.x; _mhLo = hLo; _mhHi = hHi;
        _hasModelTorso = tHi.x > tLo.x; _mtLo = tLo; _mtHi = tHi;
    }

    // ------------------------------------------------------------------ looks

    static Texture MainTex(Material m)
    {
        if (m == null) return null;
        if (m.HasProperty("baseColorTexture")) { var t = m.GetTexture("baseColorTexture"); if (t != null) return t; }
        if (m.HasProperty("_MainTex")) return m.GetTexture("_MainTex");
        return null;
    }

    enum Slot { Other, Body, Hair, Dist }

    static Slot Classify(Material m)
    {
        if (m == null) return Slot.Other;
        string n = m.name;
        if (n.StartsWith("MapleLife_Crowd_", StringComparison.Ordinal))
        {
            if (n.EndsWith("_Body", StringComparison.Ordinal)) return Slot.Body;
            if (n.EndsWith("_Hair", StringComparison.Ordinal)) return Slot.Hair;
            if (n.EndsWith("_Dist", StringComparison.Ordinal)) return Slot.Dist;
            return Slot.Other;
        }
        var t = MainTex(m);
        if (t == null) return Slot.Other;
        if (t.name.StartsWith("KuroKit_", StringComparison.Ordinal)) return Slot.Body;
        if (t.name.StartsWith("NpcHair_", StringComparison.Ordinal)) return Slot.Hair;
        if (t.name.EndsWith("_Atlas_Distance", StringComparison.Ordinal)) return Slot.Dist;
        return Slot.Other;
    }

    static bool IsRawDonorBody(Renderer r, Material m) =>
        m != null && r is SkinnedMeshRenderer && r.name.StartsWith("Mesh_0", StringComparison.Ordinal) &&
        m.name.StartsWith("Shared_Material_0", StringComparison.Ordinal);

    void SwapLookMaterials(PedestrianWardrobe.Look look)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.name == "Hair Cap" || r.name.StartsWith("Ped ", StringComparison.Ordinal)) continue;
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material want = null;
                var slot = Classify(mats[i]);
                // Nagisa Bay figures keep the donor's RAW glTF body material (never renamed to
                // MapleLife_Crowd_*_Body, so Classify says Other) and showed the camouflage atlas.
                if (slot == Slot.Other && IsRawDonorBody(r, mats[i])) slot = Slot.Body;
                switch (slot)
                {
                    case Slot.Body: want = look.body; break;
                    case Slot.Hair: want = look.hair; break;
                    case Slot.Dist: want = look.dist; break;
                }
                if (want != null && want != mats[i]) { mats[i] = want; changed = true; }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    /// <summary>
    /// The helmet-sunk skin goes on a FRESH SkinnedMeshRenderer (never re-assign sharedMesh on a
    /// skin that may already have been CPU-skinned this session - that can render nothing).
    /// </summary>
    void EnsureHelmetless(Transform high, PedestrianWardrobe.Donor donor, MinatoCrowdActor actor)
    {
        if (donor.helmetless == null) return;
        foreach (var smr in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.name != "Mesh_0" || smr.sharedMesh == null) continue;
            if (smr.sharedMesh.name.StartsWith("MapleLife_Helmetless_", StringComparison.Ordinal)) return;
            if (smr.sharedMesh.vertexCount != donor.helmetless.vertexCount) return;   // a different body
            var go = new GameObject("Mesh_0", typeof(SkinnedMeshRenderer));
            go.transform.SetParent(smr.transform.parent, false);
            go.transform.localPosition = smr.transform.localPosition;
            go.transform.localRotation = smr.transform.localRotation;
            go.transform.localScale = smr.transform.localScale;
            go.layer = smr.gameObject.layer;
            var fresh = go.GetComponent<SkinnedMeshRenderer>();
            fresh.sharedMesh = donor.helmetless;
            fresh.bones = smr.bones;
            fresh.rootBone = smr.rootBone;
            fresh.sharedMaterials = smr.sharedMaterials;
            fresh.quality = smr.quality;
            // NEVER true: 1000 always-skinned figures cost ~125 ms/frame at Minato (perf probe)
            fresh.updateWhenOffscreen = false;
            fresh.localBounds = smr.localBounds;
            fresh.shadowCastingMode = smr.shadowCastingMode;
            fresh.receiveShadows = smr.receiveShadows;
            fresh.lightProbeUsage = smr.lightProbeUsage;
            fresh.reflectionProbeUsage = smr.reflectionProbeUsage;
            fresh.motionVectorGenerationMode = smr.motionVectorGenerationMode;
            fresh.skinnedMotionVectors = smr.skinnedMotionVectors;
            smr.gameObject.name = "Mesh_0 (kit)";
            smr.enabled = false;
            _swappedOut = smr;
            _swappedIn = fresh;
            return;
        }
    }
    SkinnedMeshRenderer _swappedOut, _swappedIn;
    /// <summary>Perf probes (harness only).</summary>
    public void ProbeOffscreen(bool on) { if (_swappedIn != null) _swappedIn.updateWhenOffscreen = on; }
    public void ProbeExtras(bool on) { foreach (var r in _extra) if (r != null) r.forceRenderingOff = !on; }

    Renderer EnsureCap(Renderer cap, Mesh mesh, Material mat)
    {
        if (_head == null || mesh == null || mat == null) return cap;
        if (cap == null)
        {
            var go = new GameObject("Hair Cap", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_head, false);
            go.layer = gameObject.layer;
            cap = go.GetComponent<MeshRenderer>();
            cap.shadowCastingMode = ShadowCastingMode.On;
            cap.receiveShadows = true;
            cap.lightProbeUsage = LightProbeUsage.BlendProbes;
            _extra.Add(cap);
        }
        var mf = cap.GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = mesh;
        cap.sharedMaterial = mat;
        return cap;
    }

    static Material FirstBodyMaterial(Transform high)
    {
        foreach (var r in high.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && m.HasProperty("_Color") && m.HasProperty("_MainTex") && Classify(m) == Slot.Body)
                    return m;
        foreach (var r in high.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && m.HasProperty("_Color") && m.HasProperty("_MainTex")) return m;
        return null;
    }

    static readonly Dictionary<string, Material> Tints = new Dictionary<string, Material>();

    /// <summary>A shared flat-colour clone of a CelLit material (one per colour key).</summary>
    /// <summary>
    /// Scales a too-dark colour up (hue kept) so its brightest channel reaches <paramref name="minMax"/>.
    /// CelLit reads _Color as authored-gamma albedo and the daylight grade is dark, so charcoal/navy/black
    /// tints rendered as pure black silhouettes (black backpack flaps, black hair on Nagisa Bay). 2026-10-02.
    /// </summary>
    public static Color LiftDark(Color c, float minMax)
    {
        float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        if (m >= minMax) return c;
        float k = minMax / Mathf.Max(m, 0.04f);
        return new Color(Mathf.Min(1f, c.r * k), Mathf.Min(1f, c.g * k), Mathf.Min(1f, c.b * k), c.a);
    }

    public static Material Tinted(Material like, Color c, string key)
    {
        if (like == null) return null;
        c = LiftDark(c, 0.46f);
        if (Tints.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(like) { name = "Ped_" + key, enableInstancing = true };
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
        if (m.HasProperty("baseColorTexture")) m.SetTexture("baseColorTexture", null);
        if (m.HasProperty("_NormalStrength")) m.SetFloat("_NormalStrength", 0f);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", c);
        Tints[key] = m;
        return m;
    }

    // ------------------------------------------------------------------ fitting

    /// <summary>Extents of a Head-space mesh along the figure's axes (in Head-local units).</summary>
    bool HeadFrame(Renderer cap, out Vector3 r, out Vector3 u, out Vector3 f, out Vector3 lo, out Vector3 hi)
    {
        r = _head.InverseTransformDirection(transform.right).normalized;
        u = _head.InverseTransformDirection(transform.up).normalized;
        f = _head.InverseTransformDirection(transform.forward).normalized;
        lo = Vector3.one * float.MaxValue; hi = Vector3.one * float.MinValue;
        if (_hasModelHead) { lo = _mhLo; hi = _mhHi; return true; }   // drop-in model (claude-peds)
        var mf = cap.GetComponent<MeshFilter>();
        var mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null) return false;
        // cap may be parented with a local offset; bring its vertices into Head space
        var toHead = _head.worldToLocalMatrix * cap.transform.localToWorldMatrix;
        if (mesh.isReadable)
        {
            foreach (var v in mesh.vertices)
            {
                var p = toHead.MultiplyPoint3x4(v);
                var q = new Vector3(Vector3.Dot(p, r), Vector3.Dot(p, u), Vector3.Dot(p, f));
                lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q);
            }
        }
        else
        {
            var b = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = toHead.MultiplyPoint3x4(c);
                var q = new Vector3(Vector3.Dot(p, r), Vector3.Dot(p, u), Vector3.Dot(p, f));
                lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q);
            }
        }
        return hi.x > lo.x;
    }

    void BuildHat(Renderer cap, Material like)
    {
        if (_head == null || !HeadFrame(cap, out var r, out var u, out var f, out var lo, out var hi)) return;
        var size = hi - lo;
        var c = (lo + hi) * 0.5f;
        var mesh = HatMesh(hat);
        var go = new GameObject("Ped Hat", typeof(MeshFilter), typeof(MeshRenderer));
        go.layer = gameObject.layer;
        go.transform.SetParent(_head, false);
        // Hat meshes are authored for a unit head (x/z radius 1, crown at y = 1, origin at the
        // head's widest ring). Local basis = the figure's right/up/forward in Head space.
        // Sit on the CROWN only (a hat that reaches the ears reads as a cycling helmet on these
        // big chibi heads), slightly wider than the hair so no strands poke through the band.
        float rx = size.x * 0.5f * 1.10f, rz = size.z * 0.5f * 1.10f;
        float ry = size.y * (hat == Hat.Beanie ? 0.40f : hat == Hat.Cap ? 0.30f : 0.32f);
        var origin = new Vector3(c.x, hi.y - ry * 0.92f, c.z + size.z * (hat == Hat.Cap ? 0.02f : 0f));
        go.transform.localPosition = r * origin.x + u * origin.y + f * origin.z;
        go.transform.localRotation = Quaternion.LookRotation(f, u);
        go.transform.localScale = new Vector3(rx, ry, rz);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        int hc = HatColours[hatColour % HatColours.Length];
        mr.sharedMaterial = Tinted(like, Palette[hc], "c" + hc);
        Setup(mr);
    }

    /// <summary>Hat colours: saturated/dark only - no white/stone/grey domes (helmet look).</summary>
    static readonly int[] HatColours = { 0, 2, 3, 4, 6, 8, 9, 10, 11 };

    void BuildBag(Transform high, Material like)
    {
        if (!TorsoFrame(high, out Vector3 tLo, out Vector3 tHi)) return;
        var col = Palette[bagColour % Palette.Length];
        var mat = Tinted(like, col, "c" + (bagColour % Palette.Length));
        float w = tHi.x - tLo.x, h = tHi.y - tLo.y;
        float S = transform.lossyScale.y;
        switch (bag)
        {
            case Bag.Backpack:
            {
                if (_spine == null) return;
                // absolute chibi-readable size (big enough to show under long hair), on the upper back
                var size = BackpackSize * S;
                // top of the pack just under the shoulder line (the arm roots), not the torso-vert top
                float top = tHi.y;
                var arm = _hasModelTorso ? null : FindDeep(transform, "LeftArm") ?? FindDeep(transform, "RightArm");
                if (arm != null) top = Mathf.Max(top, transform.InverseTransformPoint(arm.position).y * S - 0.015f * S);
                var centre = new Vector3((tLo.x + tHi.x) * 0.5f, top - size.y * 0.5f, tLo.z - size.z * 0.45f);
                Part("Ped Backpack", BoxMesh(), mat, _spine, centre, size, Quaternion.identity);
                // a lid flap in a darker shade reads as a pack rather than a box
                Part("Ped Backpack Flap", BoxMesh(), Tinted(like, Palette[6], "c6"), _spine,
                     centre + new Vector3(0f, size.y * 0.36f, -size.z * 0.06f),
                     new Vector3(size.x * 1.03f, size.y * 0.30f, size.z * 1.04f), Quaternion.identity);
                // two short straps over the shoulders (front-to-back over the torso depth only)
                var strapMat = Tinted(like, Palette[6], "c6");
                float depth = Mathf.Min(tHi.z - tLo.z, 0.22f * S);
                foreach (int sgn in new[] { -1, 1 })
                    Part("Ped Strap", BoxMesh(), strapMat, _spine,
                         new Vector3(centre.x + sgn * size.x * 0.32f, top - 0.005f * S, (tLo.z + tHi.z) * 0.5f),
                         new Vector3(0.035f * S, 0.05f * S, depth * 1.08f), Quaternion.identity);
                break;
            }
            case Bag.Shoulder:
            {
                if (_hips == null || _spine == null) return;
                var size = ShoulderBagSize * S;
                var centre = new Vector3(tLo.x - size.x * 0.45f, tLo.y + h * 0.02f, (tLo.z + tHi.z) * 0.5f);
                Part("Ped Shoulder Bag", BoxMesh(), mat, _hips, centre, size, Quaternion.identity);
                // strap diagonally across the chest, just outside the jersey
                var a = new Vector3(tHi.x - w * 0.18f, tHi.y - h * 0.02f, tHi.z + 0.004f);
                var b = new Vector3(tLo.x + w * 0.02f, tLo.y + h * 0.30f, tHi.z - (tHi.z - tLo.z) * 0.1f);
                var mid = (a + b) * 0.5f;
                var dir = b - a;
                var rot = Quaternion.LookRotation(Vector3.forward, dir.normalized);
                Part("Ped Strap", BoxMesh(), Tinted(like, Palette[7], "c7"), _spine, mid,
                     new Vector3(0.03f * S, dir.magnitude, 0.01f * S), rot);
                break;
            }
            case Bag.Shopping:
            {
                if (_rHand == null) return;
                // absolute, chibi-readable size (torso-relative sizing came out ~5 cm on screen)
                var size = ShoppingBagSize * transform.lossyScale.y;
                var paper = Tinted(like, (bagColour & 1) == 0 ? PaperBag : col, (bagColour & 1) == 0 ? "paper" : "c" + (bagColour % Palette.Length));
                _shopping = HangingBag("Ped Shopping Bag", paper, _rHand, size);
                if (_lHand != null && (bagColour % 3) == 0)
                    _shopping2 = HangingBag("Ped Shopping Bag L", Tinted(like, Palette[5], "c5"), _lHand, size * 0.85f);
                break;
            }
        }
    }

    public static Vector3 ShoppingBagSize = new Vector3(0.24f, 0.27f, 0.10f);   // PROVISIONAL, metres at figure scale 1

    public static Vector3 BackpackSize = new Vector3(0.26f, 0.28f, 0.12f);       // PROVISIONAL
    public static Vector3 ShoulderBagSize = new Vector3(0.07f, 0.17f, 0.22f);    // PROVISIONAL

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
        return null;
    }

    Transform HangingBag(string name, Material mat, Transform hand, Vector3 size)
    {
        var root = new GameObject(name).transform;
        root.gameObject.layer = gameObject.layer;
        root.SetParent(hand, false);
        root.localScale = Vector3.one / Mathf.Max(1e-5f, hand.lossyScale.x);
        // body hangs below the hand; the handle is a thin inverted U
        var body = Part(name + " Body", BoxMesh(), mat, root, Vector3.zero, Vector3.one, Quaternion.identity, local: true);
        body.localPosition = Vector3.down * (size.y * 0.5f + size.y * 0.28f);
        body.localScale = size;
        var handle = Part(name + " Handle", BoxMesh(), mat, root, Vector3.zero, Vector3.one, Quaternion.identity, local: true);
        handle.localPosition = Vector3.down * size.y * 0.14f;
        handle.localScale = new Vector3(size.x * 0.5f, size.y * 0.28f, size.z * 0.18f);
        return root;
    }

    /// <summary>Figure-space extents of the live skin's torso (Spine / Spine01 / Spine02 verts).</summary>
    bool TorsoFrame(Transform high, out Vector3 lo, out Vector3 hi)
    {
        lo = Vector3.one * float.MaxValue; hi = Vector3.one * float.MinValue;
        if (_hasModelTorso) { lo = _mtLo; hi = _mtHi; return true; }  // drop-in model (claude-peds)
        string key = donorKey;
        if (TorsoCache.TryGetValue(key, out var cached))
        {
            float s = transform.lossyScale.y;
            lo = cached.lo * s; hi = cached.hi * s;
            return hi.x > lo.x;
        }
        foreach (var smr in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            // body only: hair caps / ponytails skinned to the spine stretched the depth (spike straps)
            if (_swappedIn != null ? smr != _swappedIn : smr.name != "Mesh_0") continue;
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            var bones = smr.bones;
            var bind = mesh.bindposes;
            var torso = new HashSet<int>();
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && (bones[i].name == "Spine" || bones[i].name == "Spine01" || bones[i].name == "Spine02"))
                    torso.Add(i);
            if (torso.Count == 0 || bind.Length != bones.Length) continue;
            var v = mesh.vertices;
            var bw = mesh.boneWeights;
            for (int i = 0; i < v.Length && i < bw.Length; i++)
            {
                if (bw[i].weight0 < 0.5f || !torso.Contains(bw[i].boneIndex0)) continue;
                var w = bones[bw[i].boneIndex0].localToWorldMatrix * bind[bw[i].boneIndex0];
                var p = transform.InverseTransformPoint(w.MultiplyPoint3x4(v[i]));
                lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
            }
        }
        if (!(hi.x > lo.x)) return false;
        // store in figure-local units (independent of this figure's scale)
        var ls = transform.lossyScale.y;
        TorsoCache[key] = (lo, hi);
        lo *= ls; hi *= ls;
        return true;
    }
    static readonly Dictionary<string, (Vector3 lo, Vector3 hi)> TorsoCache = new Dictionary<string, (Vector3, Vector3)>();

    /// <summary>A box part. Centre/size are in FIGURE-LOCAL metres unless <paramref name="local"/>.</summary>
    Transform Part(string name, Mesh mesh, Material mat, Transform bone, Vector3 centre, Vector3 size,
                   Quaternion figureRot, bool local = false)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.layer = gameObject.layer;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        Setup(mr);
        if (local)
        {
            go.transform.SetParent(bone, false);
            return go.transform;
        }
        // centre is figure-local in scaled units -> world, then parent with world pose kept
        float s = transform.lossyScale.y;
        go.transform.position = transform.TransformPoint(centre / Mathf.Max(1e-4f, s));
        go.transform.rotation = transform.rotation * figureRot;
        go.transform.localScale = size;
        go.transform.SetParent(bone, true);
        return go.transform;
    }

    void Setup(MeshRenderer mr)
    {
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveShadows = true;
        mr.lightProbeUsage = LightProbeUsage.BlendProbes;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _extra.Add(mr);
    }

    void RegisterOnLod0()
    {
        var group = GetComponent<LODGroup>();
        if (group == null) return;
        var lods = group.GetLODs();
        if (lods.Length == 0) return;
        var list = new List<Renderer>(lods[0].renderers ?? Array.Empty<Renderer>());
        if (_swappedOut != null) list.Remove(_swappedOut);
        if (_swappedIn != null && !list.Contains(_swappedIn)) list.Add(_swappedIn);
        foreach (var r in _extra) if (r != null && !list.Contains(r)) list.Add(r);
        lods[0].renderers = list.ToArray();
        group.SetLODs(lods);
    }

    /// <summary>Hanging shopping bags keep hanging under gravity whatever the arm does.</summary>
    public void LateTick(Vector3 figureForward)
    {
        if (_shopping != null) Hang(_shopping, figureForward);
        if (_shopping2 != null) Hang(_shopping2, figureForward);
    }

    // ------------------------------------------------------------------ handheld idle props
    // QA 2026-09-26 (after_maple_idle_phone): the Phone / Photo / Map idles raised the hands with
    // nothing in them. One tiny prop per figure, created lazily the first time a NEAR-tier brain
    // plays such an idle (LateTick only runs for the director's full tier, <= FullRadius), from ONE
    // shared mesh per kind and shared tinted CelLit materials. It is parented to the holding hand
    // (so it follows the arm and culls with the figure's LOD0), re-aimed every animated frame from
    // the live hand/head positions (independent of each donor's bone axes), scaled in/out with the
    // idle's pose blend and deactivated when the idle ends or the figure leaves the near tier.

    public enum Prop { None, Phone, Camera, Map }

    public static Vector3 PhoneSize = new Vector3(0.07f, 0.13f, 0.013f);   // PROVISIONAL, m at figure scale 1 (chibi-readable)
    public static Vector3 CameraSize = new Vector3(0.12f, 0.075f, 0.05f);  // PROVISIONAL
    public static Vector2 MapSize = new Vector2(0.36f, 0.25f);             // PROVISIONAL, open folded map

    Transform _prop;
    Prop _propKind;
    Material _bodyLike;
    Transform _rFore, _lFore;

    /// <summary>True while a handheld prop is visible (harness).</summary>
    public bool PropVisible => _prop != null && _prop.gameObject.activeSelf && _prop.localScale.x > 1e-4f;
    public Prop PropKind => PropVisible ? _propKind : Prop.None;
    public Transform PropTransform => _prop;

    public void HideProp()
    {
        if (_prop != null && _prop.gameObject.activeSelf) _prop.gameObject.SetActive(false);
    }

    /// <summary>Called by the brain on animated frames AFTER its arm overlays. weight = idle pose blend 0..1.</summary>
    public void ShowProp(Prop kind, float weight, Transform headBone)
    {
        if (kind == Prop.None || weight <= 0.02f || _rHand == null || headBone == null) { HideProp(); return; }
        if (kind != Prop.Phone && _lHand == null) { HideProp(); return; }
        if (_bodyLike == null) return;
        if (_prop == null || _propKind != kind) BuildProp(kind);
        if (_prop == null) return;
        if (!_prop.gameObject.activeSelf) _prop.gameObject.SetActive(true);

        float S = transform.lossyScale.y;
        Vector3 rPalm = Palm(_rHand, _rFore, S), lPalm = _lHand != null ? Palm(_lHand, _lFore, S) : rPalm;
        Vector3 eye = headBone.position;
        Vector3 pos; Quaternion rot;
        switch (kind)
        {
            case Prop.Phone:
            {
                // screen (+z) toward the eyes, long axis along the fingers, resting on the palm
                var n = (eye - rPalm).normalized;
                var along = Vector3.ProjectOnPlane(rPalm - (_rFore != null ? _rFore.position : _rHand.position), n);
                if (along.sqrMagnitude < 1e-6f) along = Vector3.ProjectOnPlane(Vector3.up, n);
                pos = rPalm + n * 0.018f * S;
                rot = Quaternion.LookRotation(n, along.normalized);
                break;
            }
            case Prop.Camera:
            {
                // held in front of the face between both hands, lens (+z) pointing away from the eyes
                var mid = (rPalm + lPalm) * 0.5f;
                var fwd = Vector3.ProjectOnPlane(mid - eye, Vector3.up);
                if (fwd.sqrMagnitude < 1e-6f) fwd = transform.forward;
                fwd = Vector3.Slerp(fwd.normalized, transform.forward, 0.35f);
                pos = mid - Vector3.up * 0.012f * S;
                rot = Quaternion.LookRotation(fwd, Vector3.up);
                break;
            }
            default:
            {
                // open map between the hands, face (+z) toward the eyes, top edge up
                var mid = (rPalm + lPalm) * 0.5f;
                var n = (eye - mid).normalized;
                var up = Vector3.ProjectOnPlane(Vector3.up, n);
                if (up.sqrMagnitude < 1e-6f) up = transform.forward;
                pos = mid + up.normalized * (MapSize.y * 0.18f * S) + n * 0.02f * S;
                rot = Quaternion.LookRotation(n, up.normalized);
                break;
            }
        }
        // grow in over the middle of the arm raise / shrink out as it lowers (hidden by the hand motion)
        float g = Mathf.Clamp01((weight - 0.35f) / 0.4f);
        g = g * g * (3f - 2f * g);
        _prop.SetPositionAndRotation(pos, rot);
        _prop.localScale = Vector3.one * (Mathf.Max(1e-4f, g) * S / Mathf.Max(1e-5f, _prop.parent.lossyScale.x));
    }

    /// <summary>Centre of the palm: from the wrist toward the finger roots (bone children), or a fixed reach.</summary>
    static Vector3 Palm(Transform hand, Transform fore, float S)
    {
        Vector3 sum = Vector3.zero; int k = 0;
        for (int i = 0; i < hand.childCount; i++)
        {
            var c = hand.GetChild(i);
            if (c.name.StartsWith("Ped ", StringComparison.Ordinal)) continue;
            sum += c.position; k++;
        }
        if (k > 0) return Vector3.Lerp(hand.position, sum / k, 0.75f);
        var d = fore != null ? (hand.position - fore.position).normalized : Vector3.down;
        return hand.position + d * 0.055f * S;
    }

    void BuildProp(Prop kind)
    {
        if (_prop == null)
        {
            var go = new GameObject("Ped Prop", typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = gameObject.layer;
            go.transform.SetParent(_rHand, false);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;   // a phone's shadow is invisible; keep it cheap
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _extra.Add(r);
            _prop = go.transform;
            RegisterOnLod0();
            go.SetActive(false);
        }
        // one object per figure; switching kind swaps the shared mesh + shared materials
        _prop.GetComponent<MeshFilter>().sharedMesh = PropMesh(kind);
        _prop.GetComponent<MeshRenderer>().sharedMaterials = PropMaterials(kind, _bodyLike);
        _prop.name = "Ped Prop " + kind;
        _propKind = kind;
    }

    static readonly Dictionary<Prop, Mesh> PropMeshes = new Dictionary<Prop, Mesh>();

    static Material[] PropMaterials(Prop kind, Material like)
    {
        switch (kind)
        {
            case Prop.Phone:
                return new[] { Tinted(like, new Color(0.10f, 0.10f, 0.11f), "prop_black"), Tinted(like, new Color(0.36f, 0.62f, 0.86f), "prop_screen") };
            case Prop.Camera:
                return new[] { Tinted(like, new Color(0.13f, 0.13f, 0.14f), "prop_camera"), Tinted(like, new Color(0.72f, 0.72f, 0.70f), "prop_silver") };
            default:
                return new[] { Tinted(like, new Color(0.93f, 0.90f, 0.80f), "prop_paper"), Tinted(like, new Color(0.46f, 0.72f, 0.56f), "prop_mapink") };
        }
    }

    /// <summary>Unit-free prop meshes in metres at figure scale 1; submesh 0 = body, 1 = detail.</summary>
    static Mesh PropMesh(Prop kind)
    {
        if (PropMeshes.TryGetValue(kind, out var m) && m != null) return m;
        var v = new List<Vector3>(); var n = new List<Vector3>();
        var t0 = new List<int>(); var t1 = new List<int>();
        void Box(List<int> tris, Vector3 c, Vector3 size, Quaternion r)
        {
            void Face(Vector3 normal, Vector3 a, Vector3 b)
            {
                int o = v.Count;
                Vector3 h = Vector3.Scale(normal, size) * 0.5f, ha = Vector3.Scale(a, size) * 0.5f, hb = Vector3.Scale(b, size) * 0.5f;
                v.Add(c + r * (h - ha - hb)); v.Add(c + r * (h + ha - hb)); v.Add(c + r * (h + ha + hb)); v.Add(c + r * (h - ha + hb));
                for (int i = 0; i < 4; i++) n.Add(r * normal);
                tris.AddRange(new[] { o, o + 2, o + 1, o, o + 3, o + 2 });
            }
            Face(Vector3.up, Vector3.right, Vector3.forward);
            Face(Vector3.down, Vector3.forward, Vector3.right);
            Face(Vector3.right, Vector3.forward, Vector3.up);
            Face(Vector3.left, Vector3.up, Vector3.forward);
            Face(Vector3.forward, Vector3.up, Vector3.right);
            Face(Vector3.back, Vector3.right, Vector3.up);
        }
        switch (kind)
        {
            case Prop.Phone:
                Box(t0, Vector3.zero, PhoneSize, Quaternion.identity);
                // screen inset on the +z face (toward the eyes)
                Box(t1, new Vector3(0f, PhoneSize.y * 0.02f, PhoneSize.z * 0.5f + 0.0012f),
                    new Vector3(PhoneSize.x * 0.84f, PhoneSize.y * 0.84f, 0.002f), Quaternion.identity);
                break;
            case Prop.Camera:
                Box(t0, Vector3.zero, CameraSize, Quaternion.identity);
                // lens barrel (+z, away from the eyes) and a small top plate
                Box(t1, new Vector3(CameraSize.x * 0.12f, -CameraSize.y * 0.04f, CameraSize.z * 0.5f + 0.016f),
                    new Vector3(CameraSize.y * 0.62f, CameraSize.y * 0.62f, 0.032f), Quaternion.identity);
                Box(t1, new Vector3(-CameraSize.x * 0.28f, CameraSize.y * 0.5f + 0.005f, 0f),
                    new Vector3(CameraSize.x * 0.26f, 0.010f, CameraSize.z * 0.6f), Quaternion.identity);
                break;
            default:
            {
                // two panels folded ~20 deg toward the reader (a folded map, not a flat card)
                float w = MapSize.x * 0.5f, h = MapSize.y;
                var left = Quaternion.AngleAxis(10f, Vector3.up);   // edges toward the reader (+z)
                var right = Quaternion.AngleAxis(-10f, Vector3.up);
                Box(t0, left * new Vector3(-w * 0.5f, 0f, 0f), new Vector3(w, h, 0.004f), left);
                Box(t0, right * new Vector3(w * 0.5f, 0f, 0f), new Vector3(w, h, 0.004f), right);
                // printed patches (park / water) on both sides of each panel
                foreach (var (rq, sx) in new[] { (left, -1f), (right, 1f) })
                    foreach (float side in new[] { 1f, -1f })
                    {
                        var c = rq * new Vector3(sx * w * 0.45f, h * 0.08f, side * 0.0032f);
                        Box(t1, c, new Vector3(w * 0.42f, h * 0.36f, 0.001f), rq);
                    }
                break;
            }
        }
        m = new Mesh { name = "Ped_Prop_" + kind };
        m.SetVertices(v); m.SetNormals(n);
        m.SetUVs(0, new List<Vector2>(new Vector2[v.Count]));
        m.subMeshCount = 2;
        m.SetTriangles(TwoSided(t0), 0);
        m.SetTriangles(TwoSided(t1), 1);
        m.RecalculateBounds();
        PropMeshes[kind] = m;
        return m;
    }

    static void Hang(Transform bag, Vector3 fwd)
    {
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        bag.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
    }

    public int ExtraTriangles
    {
        get
        {
            int n = 0;
            foreach (var r in _extra)
                if (r != null && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                    n += (int)(mf.sharedMesh.GetIndexCount(0) / 3);
            return n;
        }
    }

    // ------------------------------------------------------------------ procedural meshes

    /// <summary>Both windings, so the shells read correctly whatever the cull mode.</summary>
    static List<int> TwoSided(List<int> t)
    {
        int n = t.Count;
        for (int i = 0; i < n; i += 3) { t.Add(t[i]); t.Add(t[i + 2]); t.Add(t[i + 1]); }
        return t;
    }

    static Mesh _box;
    static readonly Dictionary<Hat, Mesh> HatMeshes = new Dictionary<Hat, Mesh>();

    /// <summary>Unit cube with bevel-free flat faces (24 verts, 12 tris).</summary>
    static Mesh BoxMesh()
    {
        if (_box != null) return _box;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
        void Face(Vector3 normal, Vector3 a, Vector3 b)
        {
            int o = v.Count;
            Vector3 c = normal * 0.5f;
            v.Add(c - a * 0.5f - b * 0.5f); v.Add(c + a * 0.5f - b * 0.5f);
            v.Add(c + a * 0.5f + b * 0.5f); v.Add(c - a * 0.5f + b * 0.5f);
            for (int i = 0; i < 4; i++) n.Add(normal);
            t.AddRange(new[] { o, o + 2, o + 1, o, o + 3, o + 2 });
        }
        Face(Vector3.up, Vector3.right, Vector3.forward);
        Face(Vector3.down, Vector3.forward, Vector3.right);
        Face(Vector3.right, Vector3.forward, Vector3.up);
        Face(Vector3.left, Vector3.up, Vector3.forward);
        Face(Vector3.forward, Vector3.up, Vector3.right);
        Face(Vector3.back, Vector3.right, Vector3.up);
        _box = new Mesh { name = "Ped_Box" };
        _box.SetVertices(v); _box.SetNormals(n); _box.SetTriangles(TwoSided(t), 0);
        _box.SetUVs(0, new List<Vector2>(new Vector2[v.Count]));
        _box.RecalculateBounds();
        return _box;
    }

    /// <summary>
    /// Hat shells for a unit head: x/z radius 1 at y = 0, crown top at y = 1 (+ brim). Smooth
    /// normals so the cel ramp reads them as soft forms. ~150-300 tris each.
    /// </summary>
    static Mesh HatMesh(Hat kind)
    {
        if (HatMeshes.TryGetValue(kind, out var m) && m != null) return m;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
        const int Sides = 20;
        // dome: rings from y=0 (radius 1) to the crown
        int rings = 6;
        float domeBottom = kind == Hat.Bucket ? 0.02f : 0.0f;
        for (int r = 0; r <= rings; r++)
        {
            float a = r / (float)rings * Mathf.PI * 0.5f;
            float rad = Mathf.Cos(a), y = Mathf.Sin(a);
            if (kind == Hat.Bucket) { rad = Mathf.Lerp(1.0f, 0.82f, r / (float)rings); y = r / (float)rings * 0.95f; if (r == rings) rad = 0f; }
            if (kind == Hat.Beanie) y *= 1.12f;
            for (int s = 0; s < Sides; s++)
            {
                float ang = s / (float)Sides * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(ang) * rad, domeBottom + y, Mathf.Sin(ang) * rad);
                v.Add(p);
                n.Add(new Vector3(p.x, (kind == Hat.Bucket ? 0.6f : 1f) * (p.y + 0.15f), p.z).normalized);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < Sides; s++)
            {
                int a0 = r * Sides + s, a1 = r * Sides + (s + 1) % Sides;
                int b0 = a0 + Sides, b1 = a1 + Sides;
                t.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
            }
        // brims / bands
        if (kind == Hat.Cap)
        {
            // front brim: a flattened half-ellipse sticking out along +z, slightly drooped
            int o = v.Count;
            const int B = 10;
            for (int i = 0; i <= B; i++)
            {
                float ang = Mathf.Lerp(Mathf.PI * 0.15f, Mathf.PI * 0.85f, i / (float)B);
                var inner = new Vector3(Mathf.Cos(ang) * 0.98f, 0.04f, Mathf.Sin(ang) * 0.98f);
                var outer = new Vector3(Mathf.Cos(ang) * 1.08f, -0.10f, Mathf.Sin(ang) * 1.95f);
                v.Add(inner); n.Add(Vector3.up);
                v.Add(outer); n.Add(Vector3.up);
            }
            for (int i = 0; i < B; i++)
            {
                int a = o + i * 2;
                t.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });   // top
                t.AddRange(new[] { a, a + 2, a + 1, a + 2, a + 3, a + 1 });   // underside
            }
        }
        else if (kind == Hat.Bucket || kind == Hat.Beanie)
        {
            float inR = kind == Hat.Bucket ? 1.0f : 1.01f;
            float outR = kind == Hat.Bucket ? 1.42f : 1.06f;
            float outY = kind == Hat.Bucket ? -0.16f : -0.02f;
            float topY = kind == Hat.Bucket ? 0.02f : 0.26f;
            int o = v.Count;
            for (int s = 0; s < Sides; s++)
            {
                float ang = s / (float)Sides * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                v.Add(d * inR + Vector3.up * topY); n.Add((d + Vector3.up * 0.8f).normalized);
                v.Add(d * outR + Vector3.up * outY); n.Add((d + Vector3.up * 0.4f).normalized);
            }
            for (int s = 0; s < Sides; s++)
            {
                int a = o + s * 2, b = o + ((s + 1) % Sides) * 2;
                t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                t.AddRange(new[] { a, b, a + 1, b, b + 1, a + 1 });
            }
        }
        m = new Mesh { name = "Ped_Hat_" + kind };
        m.SetVertices(v); m.SetNormals(n); m.SetTriangles(TwoSided(t), 0);
        m.SetUVs(0, new List<Vector2>(new Vector2[v.Count]));
        m.RecalculateBounds();
        HatMeshes[kind] = m;
        return m;
    }
}
