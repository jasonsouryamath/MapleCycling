using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Cheap per-rider level of detail for the Sakura Pass N1 moving/stopped <see cref="NPCCyclist"/>
/// layer (staged by SakuraNpcRoster.AddMovingCyclistsToScene; claude N1 2026-09-30, a copy of
/// TakaRiderLod).
///
/// Only the two things that are safe without a merged mesh:
///   * throttles the limb/pedal IK solve (<see cref="KuroBikeRig.solveEveryNFrames"/>) with
///     camera distance - the documented LOD hook, never toggling the rig component itself;
///   * drops shadows, then stops drawing the rider entirely, using
///     <see cref="Renderer.forceRenderingOff"/> (separate from <see cref="Renderer.enabled"/>, so
///     greetings and race/ghost code that toggle <c>enabled</c> are never fought with).
/// NPCCyclist keeps moving at every level, so a culled rider is still where it should be. The
/// pass is only ~1.4 km, so the cull sits at 320 m. All distances are PROVISIONAL tuning.
/// </summary>
[DisallowMultipleComponent]
public sealed class SakuraRiderLod : MonoBehaviour
{
    [Header("Thresholds, metres from the camera - PROVISIONAL")]
    [Tooltip("Inside this: IK every frame, shadows on.")]
    public float detailM = 55f;
    [Tooltip("Inside this: IK every 3rd frame, no shadows.")]
    public float nearM = 140f;
    [Tooltip("Inside this: IK every 8th frame. Beyond: not drawn.")]
    public float cullM = 320f;

    private KuroBikeRig _rig;
    private Renderer[] _renderers;
    private ShadowCastingMode[] _shadow;
    private int _level = -1;

    private void Awake()
    {
        _rig = GetComponentInChildren<KuroBikeRig>(true);
        _renderers = GetComponentsInChildren<Renderer>(true);
        _shadow = new ShadowCastingMode[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++) _shadow[i] = _renderers[i].shadowCastingMode;
        if (_rig != null) _rig.solvePhase = Mathf.Abs(GetInstanceID()) % 8;
    }

    private void OnDisable() => SetLevel(0);   // never leave a rider hidden or unshadowed

    /// <summary>Optional camera override (capture harnesses render through a non-main camera).</summary>
    public static Camera ViewCamera;

    private void LateUpdate()
    {
        var cam = ViewCamera != null ? ViewCamera : Camera.main;
        if (cam == null) return;
        float d = Vector3.Distance(cam.transform.position, transform.position);
        int level = d < detailM ? 0 : d < nearM ? 1 : d < cullM ? 2 : 3;
        if (level != _level) SetLevel(level);
    }

    /// <summary>Current level: 0 detail, 1 near, 2 far, 3 not drawn. For harnesses.</summary>
    public int Level => _level;

    private void SetLevel(int level)
    {
        _level = level;
        if (_rig != null) _rig.solveEveryNFrames = level == 0 ? 1 : level == 1 ? 3 : level == 2 ? 8 : 30;
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            if (r == null) continue;
            r.forceRenderingOff = level >= 3;
            r.shadowCastingMode = level == 0 ? _shadow[i] : ShadowCastingMode.Off;
        }
    }
}
