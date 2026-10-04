using System;
using UnityEngine;

/// <summary>
/// Shared culling utility for ambient movers (W0 kit).
/// Ensures ZERO work beyond ~1.5 km of the player/camera.
/// Evaluates camera distance throttled (a few times a second) to avoid per-frame overhead.
/// Supports a test camera override and target position override for headless tests and captures.
/// </summary>
public static class AmbientCull
{
    public const float DefaultCullDistance = 1500f;

    /// <summary>Optional camera override (for captures / headless test runners).</summary>
    public static Camera CameraOverride;

    /// <summary>Optional target position delegate override (for tests without a Camera).</summary>
    public static Func<Vector3> TargetPositionOverride;

    /// <summary>
    /// Gets reference position (camera or player). Returns false if no reference is active.
    /// </summary>
    public static Vector3 GetReferencePosition(out bool hasReference)
    {
        if (TargetPositionOverride != null)
        {
            hasReference = true;
            return TargetPositionOverride();
        }

        var cam = CameraOverride != null ? CameraOverride : Camera.main;
        if (cam != null)
        {
            hasReference = true;
            return cam.transform.position;
        }

        hasReference = false;
        return Vector3.zero;
    }
}
