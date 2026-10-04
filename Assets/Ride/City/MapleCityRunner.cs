using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MAPLE CITY LIFE - RUNNERS (copilot C8, 2026-09-25). A jogger on a pavement lane: the
/// <see cref="MapleCityWalker"/> on the same object moves it along the lane at running speed and
/// this component supplies the gait and the overtaking.
///
/// GAIT. Kuro's authored "running" clip (Assets/Kuro/kuro_run_fixed.glb), retargeted offline onto
/// each Minato walker donor by tools/blender/build_maple_city_life_textures.py (--run-only) into
/// Life/Anim/MapleLife_RunCycle_&lt;donor&gt;.json: per-frame local bone rotations plus the hips
/// translation, already grounded so the lowest sole of the cycle is the rest-pose sole. The
/// crowd actor on a runner is configured as Idle (it only grounds the origin); this runs after it
/// (order 70) and overwrites the pose, in Update so that coroutine captures see it too.
///
/// OVERTAKING. Every frame near the camera it scores a few lateral offsets inside the lane's
/// baked clear range (<see cref="MapleCityWalkLanes.LateralRange"/>, which already excludes the
/// kerb, shopfronts and street clutter) against every pavement figure just ahead, and eases
/// <see cref="MapleCityWalker.lateral"/> toward the cheapest one.
/// </summary>
[DefaultExecutionOrder(70)]
[DisallowMultipleComponent]
public sealed class MapleCityRunner : MonoBehaviour
{
    public TextAsset cycle;
    public Transform rigRoot;
    public MapleCityWalker walker;
    [Range(0f, 1f)] public float phase;
    /// <summary>Cycles per second relative to the clip (speed / (natural speed x figure scale)).</summary>
    public float playback = 1f;
    /// <summary>The lateral offset the runner returns to once clear.</summary>
    public float baseLateral;
    public float animationDistance = 90f;

    [Serializable]
    private sealed class Data
    {
        public string donor;
        public float duration;
        public int frames;
        public float naturalSpeed;
        public string[] bones;
        public float[] rot;
        public float[] hips;
        public float[] restRot;
        public float[] restHips;
    }

    private static readonly Dictionary<TextAsset, Data> Cache = new Dictionary<TextAsset, Data>();

    private Data _d;
    private Transform[] _bones;
    private Quaternion[] _pre;
    private Transform _hips;
    private Vector3 _hipsRest, _hipsBakedRest;
    private float _offset;
    private bool _ok;

    public bool Ready => _ok;
    public string DonorKey => _d != null ? _d.donor : "";

    private void Awake() => Resolve();

    private void Resolve()
    {
        if (_ok || cycle == null || rigRoot == null) return;
        if (!Cache.TryGetValue(cycle, out _d))
        {
            try { _d = JsonUtility.FromJson<Data>(cycle.text); }
            catch (Exception e) { Debug.LogError($"[maple-run] bad cycle '{cycle.name}': {e.Message}"); _d = null; }
            Cache[cycle] = _d;
        }
        if (_d == null || _d.bones == null || _d.frames < 2) return;
        int nb = _d.bones.Length;
        if (_d.rot == null || _d.rot.Length != nb * _d.frames * 4 || _d.hips == null || _d.hips.Length != _d.frames * 3)
            return;
        var byName = new Dictionary<string, Transform>();
        foreach (var t in rigRoot.GetComponentsInChildren<Transform>(true))
            if (!byName.ContainsKey(t.name)) byName.Add(t.name, t);
        _bones = new Transform[nb];
        _pre = new Quaternion[nb];
        for (int b = 0; b < nb; b++)
        {
            byName.TryGetValue(_d.bones[b], out _bones[b]);
            _pre[b] = Quaternion.identity;
            if (_bones[b] == null || _d.restRot == null || _d.restRot.Length != nb * 4) continue;
            // Scene rest vs the donor rest the bake assumed: identical for these donors, but carry
            // any difference so a re-posed rest cannot twist the whole cycle.
            var baked = Q(_d.restRot, b);
            _pre[b] = _bones[b].localRotation * Quaternion.Inverse(baked);
            if (Quaternion.Angle(_pre[b], Quaternion.identity) < 0.5f) _pre[b] = Quaternion.identity;
        }
        byName.TryGetValue("Hips", out _hips);
        if (_hips == null) return;
        _hipsRest = _hips.localPosition;
        _hipsBakedRest = _d.restHips != null && _d.restHips.Length == 3
            ? new Vector3(_d.restHips[0], _d.restHips[1], _d.restHips[2]) : _hipsRest;
        _offset = 0f;
        _ok = true;
    }

    private static Quaternion Q(float[] a, int i) =>
        new Quaternion(a[i * 4], a[i * 4 + 1], a[i * 4 + 2], a[i * 4 + 3]);

    private void Update()
    {
        if (!Application.isPlaying) return;
        Resolve();
        if (!_ok) return;
        var cam = Camera.main;
        if (cam != null && (cam.transform.position - transform.position).sqrMagnitude >
            animationDistance * animationDistance) return;
        Avoid(Time.deltaTime);
        Pose(Time.time);
    }

    /// <summary>Also used by diagnostics to pose at a given time.</summary>
    public void Pose(float time)
    {
        Resolve();
        if (!_ok) return;
        int nb = _bones.Length, nf = _d.frames;
        float c = time * playback / Mathf.Max(0.05f, _d.duration) + phase;
        float f = (c - Mathf.Floor(c)) * nf;
        int i0 = Mathf.Min(nf - 1, (int)f), i1 = (i0 + 1) % nf;
        float u = f - i0;
        for (int b = 0; b < nb; b++)
        {
            var bone = _bones[b];
            if (bone == null) continue;
            var q = Quaternion.Slerp(Q(_d.rot, i0 * nb + b), Q(_d.rot, i1 * nb + b), u);
            bone.localRotation = _pre[b] * q;
        }
        var h0 = new Vector3(_d.hips[i0 * 3], _d.hips[i0 * 3 + 1], _d.hips[i0 * 3 + 2]);
        var h1 = new Vector3(_d.hips[i1 * 3], _d.hips[i1 * 3 + 1], _d.hips[i1 * 3 + 2]);
        _hips.localPosition = _hipsRest + (Vector3.Lerp(h0, h1, u) - _hipsBakedRest);
    }

    private void Avoid(float dt)
    {
        if (walker == null || walker.lanes == null || !walker.lanes.Valid(walker.lane)) return;
        walker.lanes.LateralRange(walker.lane, walker.ArcAt(Time.time), out float lo, out float hi);
        if (walker.heading < 0) { float t = lo; lo = -hi; hi = -t; }
        if (hi < lo) { lo = hi = 0f; }
        float cur = walker.lateral;
        var p = transform.position;
        var fwd = transform.forward;
        var right = transform.right;
        var all = MapleCityWalker.Active;

        float best = Mathf.Clamp(baseLateral, lo, hi), bestCost = float.MaxValue;
        for (int k = 0; k <= 8; k++)
        {
            float cand = Mathf.Lerp(lo, hi, k / 8f);
            float cost = 0.5f * Mathf.Abs(cand - baseLateral) + 0.25f * Mathf.Abs(cand - cur);
            for (int w = 0; w < all.Count; w++)
            {
                var o = all[w];
                if (o == null || o == walker) continue;
                var rel = o.transform.position - p;
                if (rel.sqrMagnitude > 36f) continue;
                float along = Vector3.Dot(rel, fwd);
                if (along < -0.7f || along > 5f) continue;
                float side = Vector3.Dot(rel, right) + cur;      // other's offset from the lane line
                float gap = Mathf.Abs(side - cand);
                if (gap >= 0.75f) continue;
                float near = along < 0f ? 0.6f : 1f - along / 6f;
                cost += (0.75f - gap) * 6f * near;
            }
            if (cost < bestCost) { bestCost = cost; best = cand; }
        }
        _offset = Mathf.MoveTowards(cur, best, 1.1f * dt);
        walker.lateral = _offset;
    }
}
