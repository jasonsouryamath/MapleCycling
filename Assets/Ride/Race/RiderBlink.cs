using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Eyelids and blinking for the riders' painted anime faces.
///
/// The faces are painted into the body atlas and have no lid geometry or blendshapes, so a blink
/// is added as geometry. Per eye, <c>EyelidBake</c> (editor) stores a small grid of points lying ON
/// the face surface over the painted eye, in the Head bone's space (its pose at scale 1: the head is rigid, so this is pose-independent), plus two atlas texels: one of the rider's own skin just above the eye and
/// one of the dark lash line. At run time the lid is a mesh that follows the Head bone and is drawn
/// with the rider's OWN body material, UV-pinned to that skin texel, so it lights and tones
/// exactly like the face around it. Blinking slides the lid's lower edge down the stored surface
/// (never through the face) with a thin dark lash strip on its edge; fully open, the lid is
/// collapsed onto its top row and switched off.
///
/// Timing follows human blinking: a blink every 2-6 s (mean ~3.8 s, i.e. ~16 a minute), closing
/// in ~75 ms, a ~35 ms hold, opening in ~130 ms (opening is slower than closing), and about one
/// blink in six is a quick double. Only riders within <see cref="activeRadiusM"/> of the camera
/// and actually on screen blink, so a full region of riders costs nothing.
/// </summary>
[DefaultExecutionOrder(200)]   // after the rig solve (100) and the racers' head turn (150)
public sealed class RiderBlink : MonoBehaviour
{
    public const string DataResource = "Race/eyelids";

    [Serializable] public class EyeData
    {
        public int cols, rows;
        public Vector3[] points;    // rows x cols, row 0 = top (above the painted upper lash)
        public Vector3[] normals;
    }
    [Serializable] public class BodyData
    {
        public string key;
        public string headBone = "Head";
        public Vector2 skinUV, lashUV;
        public int submesh;
        public EyeData[] eyes;
    }
    [Serializable] public class DataFile { public BodyData[] bodies; }

    public float activeRadiusM = 30f;
    public float closeSeconds = 0.075f, holdSeconds = 0.035f, openSeconds = 0.13f;
    public float minInterval = 2f, maxInterval = 6f;
    [Range(0f, 1f)] public float doubleBlinkChance = 0.16f;
    [Tooltip("Lash strip thickness as a fraction of the eye height.")]
    public float lashFraction = 0.1f;

    private static DataFile _data;
    private static bool _loaded;
    private static readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();

    /// <summary>Maps a derived body mesh (e.g. the player's refined KuroKitSplit, which adds
    /// vertices but keeps the rig) to the body key it was built from. The eyelid points live in the
    /// Head bone's frame, so the source's data applies unchanged.</summary>
    public static void Alias(string derivedKey, string sourceKey)
    {
        if (!string.IsNullOrEmpty(derivedKey) && !string.IsNullOrEmpty(sourceKey) && derivedKey != sourceKey)
            _aliases[derivedKey] = sourceKey;
    }

    private BodyData _body;
    private Renderer _bodyRenderer;
    private Transform _head;
    private readonly List<Mesh> _meshes = new List<Mesh>();
    private readonly List<MeshRenderer> _lids = new List<MeshRenderer>();
    private readonly List<EyeData> _eyes = new List<EyeData>();
    private float _next, _t = -1f;
    private int _pending;
    private float _amount;

    /// <summary>Force a closed amount (0 open .. 1 shut) for captures; NaN = normal blinking.</summary>
    [NonSerialized] public float Override = float.NaN;

    public static BodyData Lookup(string key)
    {
        if (!_loaded)
        {
            _loaded = true;
            var ta = Resources.Load<TextAsset>(DataResource);
            if (ta != null)
            {
                try { _data = JsonUtility.FromJson<DataFile>(ta.text); }
                catch (Exception e) { Debug.LogWarning("[blink] bad eyelid data: " + e.Message); }
            }
        }
        if (_data?.bodies == null || string.IsNullOrEmpty(key)) return null;
        if (_aliases.TryGetValue(key, out var source)) key = source;
        foreach (var b in _data.bodies) if (b.key == key) return b;
        // Kuro's body is swapped at run time for the shop's per-garment split of the SAME mesh
        // (KuroKitSplitBake: same vertices, new name), so fall back to the vertex count.
        int hash = key.LastIndexOf('#');
        string count = hash >= 0 ? key.Substring(hash) : "";
        if (count.Length > 1)
            foreach (var b in _data.bodies) if (b.key.EndsWith(count)) return b;
        return null;
    }

    /// <summary>Stable key for a body mesh (a mesh name alone is not unique across GLBs).</summary>
    public static string KeyFor(SkinnedMeshRenderer smr) =>
        smr == null || smr.sharedMesh == null ? "" : smr.sharedMesh.name + "#" + smr.sharedMesh.vertexCount;

    /// <summary>Adds blinking to a rider if its face has baked eyelid data. Returns null otherwise.</summary>
    public static RiderBlink Attach(GameObject rider)
    {
        if (rider == null) return null;
        var existing = rider.GetComponent<RiderBlink>();
        if (existing != null) return existing;
        SkinnedMeshRenderer smr = null;
        Transform head = null;
        BodyData data = null;
        foreach (var r in rider.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var d = Lookup(KeyFor(r));
            if (d == null || r.bones == null) continue;
            foreach (var bone in r.bones)
                if (bone != null && bone.name == d.headBone) { head = bone; break; }
            if (head != null) { smr = r; data = d; break; }
        }
        if (data == null || head == null) return null;
        var b = rider.AddComponent<RiderBlink>();
        b.Setup(data, smr, head);
        return b;
    }

    private void Setup(BodyData data, SkinnedMeshRenderer smr, Transform head)
    {
        _body = data;
        _bodyRenderer = smr;
        _head = head;
        var mats = smr.sharedMaterials;
        var mat = mats.Length > 0 ? mats[Mathf.Clamp(data.submesh, 0, mats.Length - 1)] : null;
        foreach (var eye in data.eyes)
        {
            if (eye == null || eye.points == null || eye.points.Length != eye.rows * eye.cols) continue;
            var go = new GameObject("~Eyelid", typeof(MeshFilter), typeof(MeshRenderer));
            // The points are in the Head bone's METRIC frame (its pose at scale 1; the bone's own
            // space carries the armature's import scale), so the lid follows the bone's pose in
            // LateUpdate instead of inheriting its scale as a child.
            go.transform.SetParent(transform, false);
            var mesh = BuildMesh(eye);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.enabled = false;
            _meshes.Add(mesh);
            _lids.Add(mr);
            _eyes.Add(eye);
        }
        ScheduleNext(UnityEngine.Random.Range(0.2f, maxInterval));
    }

    private void OnDestroy()
    {
        foreach (var m in _meshes) if (m != null) Destroy(m);
        foreach (var r in _lids) if (r != null) Destroy(r.gameObject);
    }

    private void OnDisable()
    {
        foreach (var r in _lids) if (r != null) r.enabled = false;
    }

    // ------------------------------------------------------------------ mesh

    private Mesh BuildMesh(EyeData e)
    {
        int C = e.cols, R = e.rows;
        var m = new Mesh { name = "~EyelidMesh" };
        m.MarkDynamic();
        int n = (R + 2) * C;
        var v = new Vector3[n];
        var uv = new Vector2[n];
        var nor = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            bool lash = i >= R * C;
            uv[i] = lash ? _body.lashUV : _body.skinUV;
        }
        var tris = new List<int>();
        for (int r = 0; r < R - 1; r++) Strip(tris, r * C, (r + 1) * C, C);
        Strip(tris, R * C, (R + 1) * C, C);
        m.vertices = v;
        m.uv = uv;
        m.normals = nor;
        m.SetTriangles(tris, 0);
        Deform(m, e, 0f);
        return m;
    }

    private static void Strip(List<int> tris, int a, int b, int C)
    {
        for (int c = 0; c < C - 1; c++)
        {
            tris.Add(a + c); tris.Add(b + c); tris.Add(a + c + 1);
            tris.Add(a + c + 1); tris.Add(b + c); tris.Add(b + c + 1);
        }
    }

    /// <summary>Surface point at column c, fractional row param s (0 top .. 1 bottom).</summary>
    private static void Sample(EyeData e, int c, float s, out Vector3 p, out Vector3 n)
    {
        float f = Mathf.Clamp(s, 0f, 1.1f) * (e.rows - 1);
        int r0 = Mathf.Min(e.rows - 2, Mathf.FloorToInt(f));
        float t = f - r0;
        int i0 = r0 * e.cols + c, i1 = (r0 + 1) * e.cols + c;
        p = Vector3.LerpUnclamped(e.points[i0], e.points[i1], t);
        n = Vector3.Slerp(e.normals[i0], e.normals[i1], Mathf.Clamp01(t)).normalized;
    }

    private void Deform(Mesh m, EyeData e, float amount)
    {
        int C = e.cols, R = e.rows;
        var v = new Vector3[(R + 2) * C];
        var nor = new Vector3[v.Length];
        for (int r = 0; r < R; r++)
            for (int c = 0; c < C; c++)
            {
                Sample(e, c, amount * r / (R - 1f), out var p, out var n);
                v[r * C + c] = p; nor[r * C + c] = n;
            }
        float lash = lashFraction * Mathf.Lerp(0.35f, 1f, amount);
        for (int c = 0; c < C; c++)
        {
            Sample(e, c, amount - lash * 0.35f, out var p0, out var n0);
            Sample(e, c, amount + lash * 0.65f, out var p1, out var n1);
            v[R * C + c] = p0 + n0 * 0.0004f; nor[R * C + c] = n0;
            v[(R + 1) * C + c] = p1 + n1 * 0.0004f; nor[(R + 1) * C + c] = n1;
        }
        m.vertices = v;
        m.normals = nor;
        m.RecalculateBounds();
    }

    // ------------------------------------------------------------------ blinking

    private void ScheduleNext(float inSeconds) { _next = Time.time + inSeconds; }

    private static float Interval(float min, float max)
    {
        // skewed toward the short end, like real inter-blink intervals
        float u = UnityEngine.Random.value;
        return Mathf.Lerp(min, max, u * u * 0.7f + u * 0.3f);
    }

    private void Update()
    {
        if (_lids.Count == 0 || _head == null) return;

        if (!float.IsNaN(Override)) { Apply(Mathf.Clamp01(Override)); return; }

        if (_t < 0f)
        {
            var cam = Camera.main;
            bool active = cam != null && _bodyRenderer != null && _bodyRenderer.isVisible &&
                          (cam.transform.position - _head.position).sqrMagnitude < activeRadiusM * activeRadiusM;
            if (_amount > 0f) Apply(0f);
            if (!active || Time.time < _next) return;
            _t = 0f;   // start a blink
            if (_pending == 0 && UnityEngine.Random.value < doubleBlinkChance) _pending = 2;
        }

        _t += Time.deltaTime;
        float amount;
        if (_t < closeSeconds) amount = EaseIn(_t / closeSeconds);
        else if (_t < closeSeconds + holdSeconds) amount = 1f;
        else if (_t < closeSeconds + holdSeconds + openSeconds)
            amount = 1f - EaseOut((_t - closeSeconds - holdSeconds) / openSeconds);
        else
        {
            amount = 0f;
            _t = -1f;
            if (_pending == 2) { _pending = 1; ScheduleNext(0.09f); }   // quick second blink
            else { _pending = 0; ScheduleNext(Interval(minInterval, maxInterval)); }
        }
        Apply(amount);
    }

    private void LateUpdate()
    {
        if (_amount <= 0.02f || _head == null) return;
        foreach (var lid in _lids)
        {
            if (lid == null) continue;
            var t = lid.transform;
            t.SetPositionAndRotation(_head.position, _head.rotation);
            var ps = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(1f / Mathf.Max(1e-5f, ps.x), 1f / Mathf.Max(1e-5f, ps.y), 1f / Mathf.Max(1e-5f, ps.z));
        }
    }

    private static float EaseIn(float x) => x * x;
    private static float EaseOut(float x) => 1f - (1f - x) * (1f - x);

    private void Apply(float amount)
    {
        _amount = amount;
        bool on = amount > 0.02f;
        for (int i = 0; i < _lids.Count; i++)
        {
            if (_lids[i].enabled != on) _lids[i].enabled = on;
            if (on) Deform(_meshes[i], _eyes[i], amount);
        }
    }
}
