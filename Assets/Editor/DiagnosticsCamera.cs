using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SHARED DIAGNOSTIC CAMERA PLACEMENT, and the reason it exists.
///
/// THE FAILURE THIS REPLACES
/// -------------------------
/// Every region harness up to now placed its cameras the same way: take a station on the route,
/// step back along the tangent, and push sideways by a hand-guessed number of metres. That works
/// for exactly one route shape - a road whose open side happens to be the side somebody guessed -
/// and it has now failed twice, expensively and silently:
///
///   * TAKA, Kori Lake. Three attempts, none of which ever rendered. An eye 6 m above the water
///     was 35-40 m BELOW ground at the standoff distance the shot used, so the camera rendered
///     from inside the hillside.
///   * FUJI, the whole upper mountain. Fuji's plan is a SPIRAL, so "sideways" alternates between
///     open air and the inside of a volcanic cone as the road winds. Every upper-mountain capture
///     came back as a smooth, featureless black surface under a correct sky, and it survived six
///     rounds of lighting changes - key elevation 14 -> 24 -> 40 degrees, cast shadows disabled,
///     the ambient probe lifted to near-white, the scoria albedo tripled - because none of those
///     were the problem. The camera was inside the mountain, photographing backfaces.
///
/// Both failures are the same bug and both are INVISIBLE TO EVERY METRIC. The build logs its
/// renderer count, the capture writes a PNG, the process exits 0, and the image is wrong. That is
/// precisely the class of failure this project keeps paying for, so the fix belongs in one shared
/// place that every region adopts rather than in six hand-tuned copies.
///
/// THE RECIPE
/// ----------
///   1. ANCHOR ON THE ROAD and build the camera basis from the road TANGENT, never from a world
///      axis. The road always knows which way it is going; the world does not.
///   2. CHOOSE THE LATERAL SIDE BY MEASUREMENT. Sample the ground a few metres left and right of
///      the station and offset toward the LOWER one. On a pass that is the valley, on a spiral it
///      is the outside of the turn, on a switchback it is whichever leg is falling away - all of
///      which are "the side with air in it", which is the only property the shot actually needs.
///   3. CLEAR THE GROUND. Sample the terrain under the final eye position and lift the eye if it
///      is below it. This alone would have killed the Kori Lake bug.
///   4. PROVE LINE OF SIGHT. Raycast eye -> target. If something is in the way, step the eye up
///      and outward and try again, up to a budget. Report the result so a shot that could not be
///      cleared is LOUD rather than silently black.
///
/// WHY COLLIDERS ARE STAGED TEMPORARILY
/// ------------------------------------
/// Almost all environment geometry in this project is built with collider: false - it is scenery,
/// nothing ever touches it, and colliders on 24 km of terrain would be pure cost at runtime. But
/// steps 3 and 4 above are raycasts, and a raycast needs something to hit. So the harness stages
/// MeshColliders across the visible region for the duration of the capture and strips them again
/// afterwards. They never reach the saved scene, and <see cref="Release"/> is safe to call twice.
/// </summary>
public static class DiagnosticsCamera
{
    /// <summary>Colliders staged by <see cref="Prime"/>, so <see cref="Release"/> can undo exactly them.</summary>
    private static readonly List<MeshCollider> Staged = new List<MeshCollider>();

    /// <summary>How far above any surface an eye is allowed to sit before it counts as embedded.</summary>
    public const float MinGroundClearanceM = 2.0f;

    /// <summary>Metres left/right of the station used to decide which way the ground falls.</summary>
    private const float SlopeProbeM = 14f;

    /// <summary>Height the ground probe starts from, relative to the station it is testing.</summary>
    private const float ProbeUpM = 4000f;

    /// <summary>Tries allowed when lifting a blocked camera clear. Eight steps is ~120 m of lift.</summary>
    private const int ClearAttempts = 8;

    // ------------------------------------------------------------------ collider staging

    /// <summary>
    /// Stage MeshColliders across a region root so the placement raycasts have something to hit.
    ///
    /// Skips anything already carrying a collider (so a road that legitimately has one is not
    /// doubled), anything with no readable mesh, and particle systems. Non-convex MeshColliders
    /// are legal here because nothing is ever given a Rigidbody.
    /// </summary>
    public static void Prime(Transform regionRoot)
    {
        Release();
        if (regionRoot == null) return;
        int skipped = 0;

        foreach (var mf in regionRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (!mf.gameObject.activeInHierarchy) continue;
            if (mf.GetComponent<Collider>() != null) continue;
            if (IsBackdrop(mf)) { skipped++; continue; }
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            Staged.Add(mc);
        }

        Physics.SyncTransforms();
        Debug.Log($"[diagcam] staged {Staged.Count} temporary mesh colliders under " +
                  $"'{regionRoot.name}' ({skipped} backdrop/atmosphere shells skipped).");
    }

    /// <summary>
    /// Is this renderer a BACKDROP rather than a piece of the world you could stand on?
    ///
    /// This distinction is load-bearing and cost a full build cycle to find. Every region ships a
    /// "Distant Ranges" shell - a 28 km unlit ring of silhouette mountains drawn around the
    /// playable area - plus cloud/ash/mist layers. Give those colliders and two things break at
    /// once: the downward ground probe hits the shell's inner surface instead of the terrain (so
    /// every camera is placed off a height that is 80 m wrong), and every outward sight ray hits
    /// the shell, so the occlusion rescue declares the whole horizon "blocked" and lifts the
    /// camera pointlessly into the sky. A vista shot's SUBJECT is the backdrop; it can never be
    /// its own occluder.
    ///
    /// Two discriminators, either sufficient:
    ///   - the shader is Unlit. Backdrop shells are authored unlit precisely because they are
    ///     painted silhouettes, so this catches them across every region without a name list.
    ///   - the object or a parent is named for an atmosphere layer.
    /// </summary>
    private static bool IsBackdrop(MeshFilter mf)
    {
        var r = mf.GetComponent<Renderer>();
        if (r != null)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                if (m.shader.name.StartsWith("Unlit", System.StringComparison.OrdinalIgnoreCase)) return true;
                if (m.renderQueue >= 3000) return true;   // transparent: mist, wisps, glow cards
            }
        }

        for (var t = mf.transform; t != null; t = t.parent)
        {
            var n = t.name.ToLowerInvariant();
            if (n.Contains("distant") || n.Contains("backdrop") || n.Contains("skybox") ||
                n.Contains("cloud") || n.Contains("mist") || n.Contains("haze") ||
                n.Contains("ash") || n.Contains("petal") || n.Contains("leaf") ||
                n.Contains("spindrift") || n.Contains("wisp")) return true;
        }
        return false;
    }

    /// <summary>Strip every collider this utility added. Safe to call when nothing is staged.</summary>
    public static void Release()
    {
        foreach (var mc in Staged)
            if (mc != null) Object.DestroyImmediate(mc);
        Staged.Clear();
        Physics.SyncTransforms();
    }

    // ------------------------------------------------------------------ ground sampling

    /// <summary>
    /// Ground height under a plan position, by dropping a ray from well above the world.
    ///
    /// The start height is relative to a caller-supplied reference rather than absolute, because
    /// this project's regions span 420 m (Fuji's valley floor) to 2,842 m (Taka's summit) and a
    /// fixed "start at y = 3000" would begin BELOW the ground on the highest region in the game.
    /// </summary>
    public static bool SampleGround(Vector3 planPos, float nearY, out float y)
    {
        var from = new Vector3(planPos.x, nearY + ProbeUpM, planPos.z);
        if (Physics.Raycast(from, Vector3.down, out var hit, ProbeUpM * 2.5f,
                            ~0, QueryTriggerInteraction.Ignore))
        {
            y = hit.point.y;
            return true;
        }
        y = nearY;
        return false;
    }

    // ------------------------------------------------------------------ placement

    /// <summary>
    /// Place a camera eye that is guaranteed to be outside the terrain and (where it can be
    /// achieved within the attempt budget) to have clear line of sight to its subject.
    /// </summary>
    /// <param name="station">Road position the shot is anchored to.</param>
    /// <param name="tangent">Road direction at that station. The camera steps BACK along it.</param>
    /// <param name="side">Road's flat side vector at that station; sign is decided here, not by the caller.</param>
    /// <param name="target">What the shot is looking at. Used for the line-of-sight test.</param>
    /// <param name="backM">Metres back along the road from the station.</param>
    /// <param name="lateralM">Metres off the road, toward whichever side the ground falls away.</param>
    /// <param name="heightM">Metres above the local ground.</param>
    /// <param name="label">Shot name, for the log line when a placement has to be rescued.</param>
    public static Vector3 PlaceEye(Vector3 station, Vector3 tangent, Vector3 side, Vector3 target,
                                   float backM, float lateralM, float heightM, string label)
    {
        var t = tangent; t.y = 0f;
        t = t.sqrMagnitude < 1e-4f ? Vector3.forward : t.normalized;
        var s = side; s.y = 0f;
        // Derive the side from the tangent if the caller handed us a degenerate one, so this can
        // never silently collapse to a zero offset and park the camera on the centre line.
        s = s.sqrMagnitude < 1e-4f ? Vector3.Cross(Vector3.up, t) : s.normalized;

        // ---- 1. which way does the ground fall? ------------------------------------------
        bool haveL = SampleGround(station + s * SlopeProbeM, station.y, out float yL);
        bool haveR = SampleGround(station - s * SlopeProbeM, station.y, out float yR);
        // If neither probe hit anything there is no terrain to push into, so either side is fine
        // and the nominal one is kept - that is the correct behaviour over open water or a gap.
        float sign = (haveL && haveR && yL > yR) ? -1f : 1f;

        var chosen = s * sign;
        var eye = station - t * backM + chosen * lateralM;

        // ---- 2. sit above the local ground, not at the station's altitude -----------------
        // Using station.y here was the Kori Lake bug in one line: a camera 6 m above the WATER
        // is tens of metres inside the hillside once it has been pushed 150 m sideways.
        float groundY = station.y;
        if (SampleGround(eye, station.y, out float gy)) groundY = gy;
        eye.y = Mathf.Max(station.y, groundY) + Mathf.Max(heightM, MinGroundClearanceM);

        // ---- 3. prove line of sight, and rescue the shot if there is none -----------------
        for (int attempt = 0; attempt < ClearAttempts; attempt++)
        {
            if (!Blocked(eye, target)) 
            {
                if (attempt > 0)
                    Debug.Log($"[diagcam] {label}: cleared after {attempt} lift step(s), eye {eye}.");
                return eye;
            }

            // Up AND outward together. Lifting alone fails on a cone (the shoulder rises with
            // you); moving outward alone fails in a cutting. The two together escape both.
            eye += Vector3.up * (heightM * 0.5f + 8f) + chosen * (lateralM * 0.35f + 10f);
            if (SampleGround(eye, eye.y, out float g2))
                eye.y = Mathf.Max(eye.y, g2 + MinGroundClearanceM);
        }

        // LOUD, not silent. A shot that could not be cleared is a real finding and the whole
        // point of this utility is that it never again gets written out as a black PNG that
        // every log line calls a success.
        Debug.LogWarning($"[diagcam] {label}: STILL OCCLUDED after {ClearAttempts} attempts - " +
                         $"eye {eye}, target {target}. The capture will not show its subject.");
        return eye;
    }

    /// <summary>
    /// Which way does the ground fall at this station? Returns the side vector, signed toward
    /// the LOWER ground, or the nominal side if neither probe finds terrain.
    ///
    /// Exposed separately from <see cref="PlaceEye"/> because a shot usually needs to AIM the
    /// same way it STANDS. Looking "back down the route" is the natural way to describe a payoff
    /// shot and it is wrong on any route that wraps: on Fuji's spiral, a point 6 km back down the
    /// road is on the far side of the cone, so the camera is aimed straight through a volcano.
    /// Aiming along the falling side instead gives the same picture - height, distance, the world
    /// dropping away - without asking the ray to pass through a mountain.
    /// </summary>
    public static Vector3 DescendingSide(Vector3 station, Vector3 side)
    {
        var s = side; s.y = 0f;
        if (s.sqrMagnitude < 1e-4f) return Vector3.right;
        s = s.normalized;
        bool haveL = SampleGround(station + s * SlopeProbeM, station.y, out float yL);
        bool haveR = SampleGround(station - s * SlopeProbeM, station.y, out float yR);
        return (haveL && haveR && yL > yR) ? -s : s;
    }

    /// <summary>
    /// Is anything between the eye and its subject? The ray stops slightly short of the target so
    /// the subject's own geometry does not count as its own occluder.
    /// </summary>
    public static bool Blocked(Vector3 eye, Vector3 target)
    {
        var to = target - eye;
        float dist = to.magnitude;
        if (dist < 0.5f) return false;
        return Physics.Raycast(eye, to / dist, dist - 1.5f, ~0, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// True if a point is underneath the terrain - the single check that would have caught both
    /// historical failures on their first capture.
    /// </summary>
    public static bool IsEmbedded(Vector3 p, float nearY)
        => SampleGround(p, nearY, out float gy) && p.y < gy + 0.25f;
}
