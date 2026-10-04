using UnityEngine;

/// <summary>
/// Handoff section 17 - camera direction for the Hanakage encounter.
///
/// The design's constraint is unusual and worth restating, because it is the reason this file
/// is deliberately small: the player is PHYSICALLY PEDALLING while this plays. Anything that
/// takes the road away, cuts, spins, or asks them to stop is forbidden outright. So this driver
/// never takes ownership of the camera - it nudges the additive modifier layer on
/// <see cref="KuroFollowCamera"/> and blends everything back to neutral the moment the beat
/// passes. Worst case (this component missing, disabled, or throwing) the camera is simply the
/// normal chase camera, which is the correct failure mode.
///
/// The tightening is keyed to the ENCOUNTER PHASE, and the brief glance toward her is keyed to
/// <see cref="HanakagePerformance.LookBackWeight"/> so that the camera leans in on exactly the
/// frames her head is turned rather than on a second timer that can drift out of sync.
/// </summary>
[DefaultExecutionOrder(90)]   // after HanakagePerformance (80), before the camera's LateUpdate
public class HanakageCameraDirector : MonoBehaviour
{
    [Header("Wiring")]
    public HanakageEncounter encounter;
    public HanakagePerformance performance;
    public HanakageRider rider;
    public KuroFollowCamera followCamera;

    // ---------------------------------------------------------------- tuning
    // ALL PROVISIONAL. Section 17 says "subtle" and gives no numbers, so these are chosen to be
    // felt rather than seen: roughly a 7 % pull-in and 4 degrees of FOV over ~1.6 s. If a
    // playtest says the attack should bite harder, this is the block to move - not the camera.

    [Header("Approach / on-the-wheel (PROVISIONAL)")]
    /// <summary>Mild pull-in as the gap closes - the world starts to narrow around the chase.</summary>
    public float wheelDistanceScale = 0.95f;
    public float wheelFovDelta = -1.5f;

    [Header("Attack (PROVISIONAL)")]
    /// <summary>Section 17's "subtle camera tightening during attack".</summary>
    public float attackDistanceScale = 0.87f;
    public float attackFovDelta = -4f;
    /// <summary>A touch lower, which reads as effort without moving the horizon much.</summary>
    public float attackHeightDelta = -0.25f;

    [Header("Final hairpin (PROVISIONAL)")]
    public float hairpinDistanceScale = 0.92f;
    public float hairpinFovDelta = -2.5f;

    [Header("Look-back focus (PROVISIONAL)")]
    /// <summary>
    /// Peak aim bias toward her during the glance. Clamped again by
    /// <see cref="KuroFollowCamera.MaxAimBias"/>; this is the "brief focus toward Hanakage when
    /// she looks back" the design allows, not a lock-on.
    /// </summary>
    [Range(0f, KuroFollowCamera.MaxAimBias)] public float lookBackAimBias = 0.20f;

    [Header("Blending")]
    /// <summary>Seconds to reach a new target. Slow on purpose - a fast blend reads as a cut.</summary>
    public float blendSeconds = 1.6f;
    /// <summary>Faster release, so the camera is fully back to normal before the descent.</summary>
    public float releaseSeconds = 2.2f;

    // ---------------------------------------------------------------- live state
    private float _distTarget = 1f, _fovTarget = 0f, _heightTarget = 0f;
    private float _dist = 1f, _fov = 0f, _height = 0f;

    /// <summary>Exposed so a capture harness / QA can assert the camera actually moved.</summary>
    public float CurrentDistanceScale => _dist;
    public float CurrentFovDelta => _fov;
    public float CurrentAimBias { get; private set; }

    private void OnDisable()
    {
        if (followCamera != null) followCamera.ClearModifiers();
    }

    private void LateUpdate()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>dt-driven, like every other Hanakage system, so the harness can step it.</summary>
    public void Tick(float dt)
    {
        if (followCamera == null) return;
        if (dt <= 0f) dt = 1f / 60f;

        var phase = encounter != null ? encounter.State : HanakageEncounter.Phase.Dormant;
        switch (phase)
        {
            case HanakageEncounter.Phase.OnWheel:
                _distTarget = wheelDistanceScale; _fovTarget = wheelFovDelta; _heightTarget = 0f;
                break;
            case HanakageEncounter.Phase.Attack:
                _distTarget = attackDistanceScale; _fovTarget = attackFovDelta;
                _heightTarget = attackHeightDelta;
                break;
            case HanakageEncounter.Phase.FinalHairpin:
                _distTarget = hairpinDistanceScale; _fovTarget = hairpinFovDelta;
                _heightTarget = 0f;
                break;
            default:
                // Escape included: the camera relaxes as she goes, which is what sells the loss.
                _distTarget = 1f; _fovTarget = 0f; _heightTarget = 0f;
                break;
        }

        bool releasing = Mathf.Approximately(_distTarget, 1f) && Mathf.Approximately(_fovTarget, 0f);
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f,
                                    (releasing ? releaseSeconds : blendSeconds) / 3f));

        _dist = Mathf.Lerp(_dist, _distTarget, k);
        _fov = Mathf.Lerp(_fov, _fovTarget, k);
        _height = Mathf.Lerp(_height, _heightTarget, k);

        followCamera.modDistanceScale = _dist;
        followCamera.modFovDelta = _fov;
        followCamera.modHeightDelta = _height;

        // The glance. Only while she is actually present and actually turning her head.
        float w = performance != null ? performance.LookBackWeight : 0f;
        bool present = rider != null && rider.Present;
        CurrentAimBias = present ? lookBackAimBias * w : 0f;
        followCamera.modAimBias = CurrentAimBias;
        followCamera.modAimTarget = present && rider != null ? rider.transform : null;
    }
}
