using UnityEngine;

/// <summary>
/// Per-rider level of detail for Shiosai's ambient traffic, driven once per frame by
/// ShiosaiTrafficDirector.
///
/// Measured motivation: with all thirty riders drawn at full detail the 1300 m station ran at
/// 59.2 ms/frame with 4,740 draw calls; the same frame with the traffic renderers disabled ran
/// at 6.8 ms with 582 draws. The riders were ~4,150 draw calls on their own, because each of
/// the thirty bicycles was seventy-eight separate parts, each submitted again for the shadow
/// pass. ShiosaiBikeLod merges those parts; this component decides how much of the result is
/// worth drawing at a given distance.
///
/// All distances are PROVISIONAL tuning - they are the first values that held up in a capture,
/// not a design requirement.
/// </summary>
[DisallowMultipleComponent]
public class ShiosaiRiderLod : MonoBehaviour
{
    [Header("Wiring (filled in by the staging pass)")]
    public SkinnedMeshRenderer[] body;
    /// <summary>One combined renderer per driven bike socket - the full, moving bicycle.</summary>
    public Renderer[] bikeDetail;
    /// <summary>A single fully merged bicycle with frozen wheels, for distance.</summary>
    public Renderer bikeFar;
    public KuroBikeRig rig;
    public MonoBehaviour greeting;

    [Header("Thresholds (metres from the camera) - PROVISIONAL")]
    /// <summary>Inside this, the full moving bicycle and a per-frame IK solve.</summary>
    public float detailM = 42f;
    /// <summary>Inside this, riders cast shadows.</summary>
    public float shadowM = 48f;
    /// <summary>Past this, the rig solves only occasionally.</summary>
    public float coarseM = 120f;
    /// <summary>Past this, the rider is not drawn at all.</summary>
    public float cullM = 240f;

    int _level = -1;          // 0 detail, 1 near, 2 coarse, 3 culled
    bool _shadows = true;
    bool _loggedMissingBike;  // one error per broken spell, not one per frame
    bool _partsFallback;      // merged LOD meshes were missing; drawing the source parts instead

    void Awake()
    {
        // Spread the coarse solves across frames so thirty riders never bunch onto one.
        if (rig != null) rig.solvePhase = Mathf.Abs(GetInstanceID()) % 7;
        RepairMissingBikeMeshes();
    }

    /// <summary>
    /// The merged BikeLod_* meshes are shared asset files. If one is deleted or re-created under
    /// a new GUID, this rider's MeshFilters go null while the renderers stay "enabled" - the
    /// rider then pedals an invisible bicycle and the enabled-flag guard below cannot see it.
    /// Rather than ship that, fall back to the bike's original per-part renderers (they carry
    /// the rider's livery; the LOD bake only disabled them). Costs draw calls, never correctness.
    /// </summary>
    void RepairMissingBikeMeshes()
    {
        bool ok = bikeFar != null && HasMesh(bikeFar) && bikeDetail != null && bikeDetail.Length > 0;
        if (ok)
            for (int i = 0; i < bikeDetail.Length; i++)
                if (!HasMesh(bikeDetail[i])) { ok = false; break; }
        if (ok) return;

        var bike = FindDeep(transform, "Bike");
        if (bike == null) return;
        var parts = new System.Collections.Generic.List<Renderer>();
        foreach (var mr in bike.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr.gameObject.name.StartsWith("BikeLod_")) { mr.enabled = false; continue; }
            if (HasMesh(mr)) parts.Add(mr);
        }
        if (parts.Count == 0) return;

        Debug.LogWarning($"[shiosai-riderlod] '{name}': merged bike LOD meshes are missing " +
                         $"(stale asset reference) - drawing the {parts.Count} source bike parts " +
                         "instead. Re-stage this region's traffic to restore the merged LOD.");
        bikeDetail = parts.ToArray();
        bikeFar = null;
        _partsFallback = true;
        _level = -1;
    }

    static bool HasMesh(Renderer r)
    {
        if (r == null) return false;
        if (!(r is MeshRenderer)) return true;
        var mf = r.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null;
    }

    static Transform FindDeep(Transform root, string exact)
    {
        foreach (Transform c in root)
        {
            if (c.name == exact) return c;
            var f = FindDeep(c, exact);
            if (f != null) return f;
        }
        return null;
    }

    /// <summary>Called by the director; cheap and idempotent - only acts on a level change.</summary>
    public void Apply(float distance)
    {
        int level = distance > cullM ? 3
                  : distance > coarseM ? 2
                  : distance > detailM ? 1 : 0;
        bool shadows = distance <= shadowM;

        if (level != _level)
        {
            _level = level;
            bool drawn = level < 3;
            bool detail = level == 0;

            SetRenderers(body, drawn);
            SetRenderers(bikeDetail, drawn && (detail || _partsFallback));
            if (bikeFar != null) bikeFar.enabled = drawn && !detail;

            if (rig != null)
                rig.solveEveryNFrames = level == 0 ? 1 : level == 1 ? 3 : 12;
            // NpcGreeting re-searches the rig for its smile renderer whenever it has not found
            // one, so leaving it running on a rider nobody can see is not free.
            if (greeting != null) greeting.enabled = level <= 1;

            // PERMANENT regression guard for the "ambient rider rendered running with no bike"
            // defect (Minato Coast QA finding, confirmed by a user screenshot but never caught
            // by a scripted diagnostic across 180 s / 18 of 30 slots - i.e. intermittent).
            // Whenever the body is meant to be drawn, at least one bike renderer must be
            // enabled too: every reachable level ties them together (0: body+bikeDetail,
            // 1/2: body+bikeFar, 3: neither), so seeing body-drawn with zero bike renderers
            // enabled can only mean a stale renderer state survived a respawn/reactivation, or
            // a socket/bake lookup silently failed at staging time. Checked here - the ONE
            // place the visible state actually changes - rather than left to an offline
            // diagnostic, so it is caught live, in every build, the instant it happens.
            if (drawn)
            {
                bool anyBikeOn = (bikeFar != null && bikeFar.enabled && HasMesh(bikeFar))
                                 || AnyEnabled(bikeDetail);
                if (!anyBikeOn)
                {
                    if (!_loggedMissingBike)
                    {
                        Debug.LogError($"[shiosai-riderlod] '{name}' at level {level} " +
                            $"(dist {distance:0.1}m) has the rider body drawn with ZERO " +
                            "enabled bike renderers (no bikeDetail parts on, bikeFar off/null) " +
                            "- THIS IS THE MISSING-BIKE SIGNATURE.");
                        _loggedMissingBike = true;
                    }
                }
                else _loggedMissingBike = false;
            }
        }

        if (shadows != _shadows)
        {
            _shadows = shadows;
            var mode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On
                               : UnityEngine.Rendering.ShadowCastingMode.Off;
            SetShadows(body, mode);
            SetShadows(bikeDetail, mode);
            if (bikeFar != null) bikeFar.shadowCastingMode = mode;
        }
    }

    /// <summary>Forces full detail (used when the rider is the player's drafting target).</summary>
    public void Invalidate() { _level = -1; _shadows = !_shadows; }

    static void SetRenderers(Renderer[] list, bool on)
    {
        if (list == null) return;
        for (int i = 0; i < list.Length; i++)
            if (list[i] != null) list[i].enabled = on;
    }

    static bool AnyEnabled(Renderer[] list)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Length; i++)
            if (list[i] != null && list[i].enabled && HasMesh(list[i])) return true;
        return false;
    }

    static void SetShadows(Renderer[] list, UnityEngine.Rendering.ShadowCastingMode mode)
    {
        if (list == null) return;
        for (int i = 0; i < list.Length; i++)
            if (list[i] != null) list[i].shadowCastingMode = mode;
    }
}
