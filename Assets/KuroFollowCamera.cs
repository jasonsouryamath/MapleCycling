using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// The ride chase camera.
///
/// Everything below <see cref="followSharpness"/> is an ADDITIVE modifier layer driven by
/// behaviour (currently <c>HanakageCameraDirector</c>). All of it defaults to neutral, so with
/// nothing driving it this component behaves exactly as it did before the layer existed - that
/// matters because this is the player's camera on every ride, not just during an encounter.
///
/// Handoff section 17 is explicit about the limits: subtle tightening, small FOV changes, a
/// brief look toward Hanakage - and NO cuts, NO spinning, and never a frame where the player
/// cannot see the road they are physically pedalling up. Hence modifiers with clamped ranges
/// and a smoothed aim bias rather than a cinematic camera that takes control away.
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(200)] // RouteFollower is 50 and KuroBikeRig is 100; camera must see both current-frame results.
public class KuroFollowCamera : MonoBehaviour
{
    public Transform target;
    // Hero-scale gameplay framing. This is deliberately enforced at runtime because region
    // bootstrap/capture scenes may carry older serialized values.
    public Vector3 offset = new Vector3(0f, 1.15f, -2.20f);
    public float followSharpness = 6f;

    [Header("Player readability")]
    public bool enforceGameplayPresentation = true;
    public Vector3 gameplayOffset = new Vector3(0f, 1.45f, -3.60f);
    [Range(30f, 60f)] public float gameplayFieldOfView = 44f;
    [Tooltip("Camera-mounted cool fill that keeps Kuro's black helmet, hair, kit and bike distinct.")]
    public bool addGameplayFill = false;
    [Range(0f, 1500f)] public float gameplayFillIntensity = 550f;

    [Header("Chase frame (handoff section 33)")]

    /// <summary>
    /// Build the chase frame from the rider's HEADING ONLY - never from their full rotation.
    ///
    /// This is the fix for the reported "I'm looking at my character from the side". The rider
    /// transform is not just a heading: <c>RouteFollower</c> sets
    /// <c>LookRotation(tangent, course.UpAt(d)) * Euler(0, 0, -lean)</c>, which folds in the
    /// road's authored banking AND up to 14 degrees of extra corner lean. Feeding the chase
    /// offset through <c>target.TransformDirection</c> then ORBITS the camera about the rider's
    /// forward axis by that whole roll: at 20 degrees of combined bank a (0, 3.2, -7) offset
    /// swings 3.2*sin(20) = 1.1 m sideways and drops 0.2 m, while <c>LookAt</c> keeps the horizon
    /// level - so the rider slides off-centre and the shot reads as side-on, and it SWINGS
    /// through every corner because the lean is signed and changes with the road.
    ///
    /// Projecting the rider's forward onto the horizontal plane removes it exactly: roll is a
    /// rotation ABOUT forward, so forward itself is immune to it.
    /// </summary>
    public bool yawOnlyFrame = true;

    /// <summary>
    /// PROVISIONAL: how much of the road's PITCH the chase frame follows, 0..1.
    ///
    /// Separate from roll on purpose. Roll is what broke the shot and is off entirely; a little
    /// pitch is wanted, or on the 10.4 % section the camera sits flat behind a rider who is
    /// climbing away from it and the framing slowly drifts high. 0.35 keeps the rider in the
    /// same part of the frame up, over and down the pass without the camera pumping.
    /// </summary>
    [Range(0f, 1f)] public float pitchFollowFraction = 0.35f;

    /// <summary>
    /// PROVISIONAL: how much of the bike's LEAN the chase frame follows, 0..1.
    ///
    /// Zero, deliberately, and it is a field only so a future tuning pass has a dial rather than
    /// a rewrite. The player explicitly rejected a banking chase camera; anything above about
    /// 0.1 here starts to reproduce the side-on swing this field exists to document.
    /// </summary>
    [Range(0f, 0.35f)] public float bankFollowFraction = 0f;

    /// <summary>
    /// PROVISIONAL: the hardest the camera may ever sit off "directly behind", in degrees.
    ///
    /// This is the invariant the complaint asks for, expressed as a number that can be measured
    /// in a capture. Damping alone cannot promise it: a first-order follow has a steady-state lag
    /// proportional to how fast the rider is turning, so the sharpest hairpin on Sakura Pass
    /// (10.8 deg/m) will always pull the camera toward the outside of the bend. The clamp means
    /// that in the worst corner the framing degrades to a slight over-the-shoulder angle and
    /// stops there, instead of continuing round to the side of the bike.
    /// </summary>
    [Range(0f, 30f)] public float maxHeadingLagDegrees = 8f;

    [Header("Modifiers (runtime, driven by behaviour - neutral by default)")]

    /// <summary>Multiplies the BACK/UP components of <see cref="offset"/>. Below 1 = tighter.</summary>
    [Range(0.6f, 1.4f)] public float modDistanceScale = 1f;

    /// <summary>Added to the camera's field of view, in degrees. Negative = tighter.</summary>
    [Range(-12f, 12f)] public float modFovDelta = 0f;

    /// <summary>Extra metres of camera height, on top of <see cref="offset"/>.</summary>
    [Range(-1.5f, 1.5f)] public float modHeightDelta = 0f;

    /// <summary>
    /// 0 = look where we always look. 1 = look straight at <see cref="modAimTarget"/>.
    /// Capped at <see cref="MaxAimBias"/> so the road can never leave the frame.
    /// </summary>
    [Range(0f, 1f)] public float modAimBias = 0f;

    /// <summary>Who the aim bias leans toward (Hanakage during her look-back).</summary>
    public Transform modAimTarget;

    /// <summary>
    /// PROVISIONAL: the hard ceiling on the aim bias. At 0.45 the road still fills most of the
    /// frame; much past that and a rider on a trainer loses their line, which section 17 forbids
    /// outright. Kept as a constant rather than a field so a tuning pass cannot quietly break
    /// the rule the design states.
    /// </summary>
    public const float MaxAimBias = 0.45f;

    [Header("Keyboard orbit")]

    /// <summary>
    /// LEFT/RIGHT sweep the chase camera around the rider while preserving the target, height,
    /// follow distance and bicycle heading. Arrow keys are camera controls, not steering.
    /// </summary>
    public bool enableDebugOrbit = true;

    /// <summary>Degrees per second the arrow keys sweep the debug orbit.</summary>
    [Range(15f, 360f)] public float debugOrbitSpeed = 90f;
    [Tooltip("How quickly held/released orbit input reaches its target. Higher is snappier.")]
    [Range(1f, 30f)] public float keyboardOrbitResponse = 12f;
    [System.NonSerialized] public float OrbitInputOverride = float.NaN;
    public float KeyboardOrbitYawDegrees => _debugOrbitYaw;

    private float _debugOrbitYaw;
    private float _orbitInput;

    private float _baseFov = -1f;
    private Vector3 _lastFlatForward = Vector3.forward;
    private float _yawDeg;
    private float _pitchDeg;
    private bool _framePrimed;
    private Light _gameplayFill;
    private Camera _camera;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        if (_camera == null) _camera = GetComponent<Camera>();
        ApplyGameplayPresentation();
    }

    private void ApplyGameplayPresentation()
    {
        if (!enforceGameplayPresentation) return;

        offset = gameplayOffset;
        var cam = _camera;
        if (cam != null)
        {
            cam.fieldOfView = gameplayFieldOfView;
            cam.allowHDR = true;
            // HDRP ignores the legacy QualitySettings MSAA value for deferred cameras.  The
            // ride camera previously had no HDAdditionalCameraData at all, which left the road,
            // railings, spokes, foliage and small crowd silhouettes completely un-antialiased.
            // High-quality TAA is the best fit for a continuously moving chase camera; moderate
            // post sharpening restores texture/character definition without bringing the road
            // shimmer back.
            cam.allowMSAA = false;
            ConfigureImageQuality(cam);
            _baseFov = gameplayFieldOfView;
        }

        var child = transform.Find("~KuroGameplayFill");
        // A near-camera HDRP spot light turns glTF materials into a chrome-white blob.  The
        // scene sun/sky already provide the intended illumination, so leave any old helper
        // light disabled unless a deliberately authored lighting pass opts in again.
        if (!addGameplayFill)
        {
            if (child != null) child.gameObject.SetActive(false);
            _gameplayFill = null;
            ApplyMatteRiderMaterials();
            return;
        }
        if (child == null)
        {
            var go = new GameObject("~KuroGameplayFill");
            child = go.transform;
            child.SetParent(transform, false);
        }
        _gameplayFill = child.GetComponent<Light>();
        if (_gameplayFill == null) _gameplayFill = child.gameObject.AddComponent<Light>();
        child.localPosition = new Vector3(0f, 0.15f, 0.15f);
        child.localRotation = Quaternion.identity;
        _gameplayFill.type = LightType.Spot;
        _gameplayFill.color = new Color(0.72f, 0.82f, 1f, 1f);
        _gameplayFill.intensity = gameplayFillIntensity;
        _gameplayFill.range = 7f;
        _gameplayFill.spotAngle = 52f;
        _gameplayFill.innerSpotAngle = 34f;
        _gameplayFill.shadows = LightShadows.None;
        ApplyMatteRiderMaterials();
    }

    private static void ConfigureImageQuality(Camera cam)
    {
        var hd = cam.GetComponent<HDAdditionalCameraData>();
        if (hd == null) hd = cam.gameObject.AddComponent<HDAdditionalCameraData>();

        hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
        hd.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
        hd.taaSharpenMode = HDAdditionalCameraData.TAASharpenMode.PostSharpen;
        hd.taaSharpenStrength = 0.32f;
        hd.taaHistorySharpening = 0.22f;
        hd.taaAntiFlicker = 0.62f;
        hd.taaMotionVectorRejection = 0.28f;
        hd.taaRingingReduction = 0.35f;
        hd.taaAntiHistoryRinging = true;
        hd.taaBaseBlendFactor = 0.86f;
        hd.taaJitterScale = 0.82f;
        hd.dithering = true;
        hd.stopNaNs = true;
        hd.allowDynamicResolution = false;

        // Keep oblique road/terrain textures sharp and retain the authored high-detail meshes.
        // Never lower a user-selected value: these are quality floors for the presentation the
        // project is authored around, not an attempt to own the player's graphics menu.
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        QualitySettings.globalTextureMipmapLimit = 0;
        QualitySettings.lodBias = Mathf.Max(QualitySettings.lodBias, 2f);
        QualitySettings.maximumLODLevel = 0;
        QualitySettings.realtimeReflectionProbes = true;
    }

    // glTF PBR imports sometimes preserve an emissive texture/factor that is harmless in a
    // neutral DCC viewer but becomes a blinding white coating under the game's HDRP exposure.
    // This runs on the temporary runtime material instances only: it does not mutate shared
    // source assets or other riders.
    private void ApplyMatteRiderMaterials()
    {
        if (target == null) return;
        foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.materials)
            {
                if (material == null) continue;
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.08f);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.08f);
                if (material.HasProperty("metallicFactor")) material.SetFloat("metallicFactor", 0f);
                if (material.HasProperty("roughnessFactor")) material.SetFloat("roughnessFactor", 0.88f);
                if (material.HasProperty("emissiveTexture")) material.SetTexture("emissiveTexture", null);
                if (material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", null);
                if (material.HasProperty("emissiveFactor")) material.SetColor("emissiveFactor", Color.black);
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
            }
        }
    }

    /// <summary>
    /// The frame the chase offset is expressed in, already smoothed and clamped. Public so a
    /// diagnostic harness can measure where the camera SHOULD be without duplicating the maths.
    /// </summary>
    public Quaternion ChaseFrame()
    {
        if (target == null) return Quaternion.identity;
        if (!yawOnlyFrame) return target.rotation;
        var frame = Quaternion.Euler(_pitchDeg, _yawDeg, 0f);
        if (bankFollowFraction > 0.001f)
        {
            // Signed lean, recovered from the rider's right vector: positive when banked right.
            float leanDeg = Mathf.Asin(Mathf.Clamp(target.right.y, -1f, 1f)) * Mathf.Rad2Deg;
            frame *= Quaternion.Euler(0f, 0f, leanDeg * bankFollowFraction);
        }
        return frame;
    }

    /// <summary>Advances the smoothed heading/pitch toward the rider's, then clamps the lag.</summary>
    private void UpdateFrame(float dt)
    {
        Vector3 fwd = target.forward;

        // Horizontal heading. Roll cannot affect this - it is a rotation about forward - so this
        // single projection is what removes the inherited bank.
        Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
        if (flat.sqrMagnitude < 1e-6f) flat = _lastFlatForward;   // rider pointing at the sky
        flat.Normalize();
        _lastFlatForward = flat;

        float targetYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;

        // NONE of the lean, and only a fraction of the pitch.
        float elevationDeg = Mathf.Asin(Mathf.Clamp(fwd.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        float targetPitch = -elevationDeg * pitchFollowFraction;

        if (!_framePrimed)
        {
            _yawDeg = targetYaw;
            _pitchDeg = targetPitch;
            _framePrimed = true;
            return;
        }

        float k = 1f - Mathf.Exp(-followSharpness * Mathf.Max(1e-4f, dt));
        _yawDeg = Mathf.LerpAngle(_yawDeg, targetYaw, k);
        _pitchDeg = Mathf.Lerp(_pitchDeg, targetPitch, k);

        // The hard promise: never further off directly-behind than maxHeadingLagDegrees.
        float lag = Mathf.DeltaAngle(_yawDeg, targetYaw);
        if (Mathf.Abs(lag) > maxHeadingLagDegrees)
            _yawDeg = targetYaw - Mathf.Sign(lag) * maxHeadingLagDegrees;
    }

    void LateUpdate()
    {
        ApplyCamera(Time.deltaTime);
    }

    public void TickForValidation(float dt)
    {
        ApplyCamera(Mathf.Max(0f, dt));
    }

    public void ResetKeyboardOrbit()
    {
        _debugOrbitYaw = 0f;
        _orbitInput = 0f;
    }

    public void SetDebugOrbitYawForValidation(float yawDegrees)
    {
        _debugOrbitYaw = yawDegrees;
        _orbitInput = 0f;
    }

    private void ApplyCamera(float dt)
    {
        if (target == null) return;
        if (_camera == null) _camera = GetComponent<Camera>();

        // Region loading and encounter staging are allowed to add temporary modifiers, but the
        // base gameplay shot remains hero-scale and cannot silently fall back to an old scene.
        if (enforceGameplayPresentation)
        {
            offset = gameplayOffset;
            if (_gameplayFill != null) _gameplayFill.intensity = gameplayFillIntensity;
        }

        var cam = _camera;
        if (cam != null)
        {
            if (_baseFov < 0f) _baseFov = cam.fieldOfView;
            cam.fieldOfView = _baseFov + modFovDelta;
        }

        // Only the pull-back and the lift scale; the lateral component stays put, or a tightening
        // camera would also drift sideways across the road.
        Vector3 scaled = new Vector3(offset.x,
                                     offset.y * modDistanceScale + modHeightDelta,
                                     offset.z * modDistanceScale);

        if (yawOnlyFrame)
        {
            UpdateFrame(dt);

            // Placed EXACTLY, because the smoothing now lives in the heading instead of in the
            // world position. Lerping the position is what made the camera cut the inside of a
            // bend: a first-order positional follow lags by roughly speed/sharpness metres, and
            // on a turning rider that lag is almost entirely LATERAL - which is the side-on shot
            // the player reported. Damping the angle gives the same soft feel with none of that.
            Vector3 framed = ChaseFrame() * scaled;

            // TEMPORARY debug orbit: rotate ONLY the camera's seat about the rider's vertical
            // axis so the model can be inspected from any angle. LookAt below still targets the
            // rider, so this reads as circling the character.
            if (enableDebugOrbit)
            {
                _orbitInput = Mathf.MoveTowards(
                    _orbitInput, KeyboardOrbitInput(), keyboardOrbitResponse * dt);
                _debugOrbitYaw += _orbitInput * debugOrbitSpeed * dt;
                framed = Quaternion.AngleAxis(_debugOrbitYaw, Vector3.up) * framed;
            }

            transform.position = target.position + framed;
        }
        else
        {
            // Legacy frame, retained only so a capture can shoot a true before/after.
            Vector3 desired = target.position + target.TransformDirection(scaled);
            transform.position = Vector3.Lerp(transform.position, desired,
                                              1f - Mathf.Exp(-followSharpness * dt));
        }

        Vector3 aim = target.position + Vector3.up * 1.1f;
        if (modAimTarget != null && modAimBias > 0.001f)
            aim = Vector3.Lerp(aim, modAimTarget.position + Vector3.up * 1.0f,
                               Mathf.Min(modAimBias, MaxAimBias));

        // World up, always: the horizon stays level no matter what the bike is doing.
        transform.LookAt(aim, Vector3.up);
    }

    /// <summary>
    /// TEMPORARY: -1 while the LEFT arrow is held, +1 while RIGHT is held, 0 otherwise. Read as
    /// raw key state (not the "Horizontal" steering axis) so it never fights rider input.
    /// </summary>
    private float KeyboardOrbitInput()
    {
        if (!float.IsNaN(OrbitInputOverride))
            return Mathf.Clamp(OrbitInputOverride, -1f, 1f);
        float v = 0f;
        if (Input.GetKey(KeyCode.LeftArrow)) v -= 1f;
        if (Input.GetKey(KeyCode.RightArrow)) v += 1f;
        return v;
    }

    /// <summary>Returns every modifier to neutral. Called when an encounter ends or aborts.</summary>
    public void ClearModifiers()
    {
        modDistanceScale = 1f;
        modFovDelta = 0f;
        modHeightDelta = 0f;
        modAimBias = 0f;
        modAimTarget = null;
        var cam = _camera;
        if (cam != null && _baseFov > 0f) cam.fieldOfView = _baseFov;
    }
}
