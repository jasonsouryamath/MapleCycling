using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// The Maple Row shop's 3D fitting room. A clone of the PLAYER's own rider and bike (so fitment is
/// exactly what you'll ride in) stands dismounted on a showroom plinth high above the city, lit
/// by the scene sun plus a warm key and a cool rim, and turns slowly. A dedicated HDRP camera
/// renders it into <see cref="Texture"/>, which the shop UI shows full size. The camera eases
/// between framings per category: the full outfit, the helmet, a wheel, the head unit.
/// The try-on (unowned items included) goes through the clone's own KitAppearance.tryOn, so
/// the player's real kit is never touched.
/// </summary>
public sealed class ShopStudio : MonoBehaviour
{
    public enum Framing { Outfit, Helmet, Wheels, Computer, Frame }

    public RenderTexture Texture { get; private set; }
    public KitAppearance Kit { get; private set; }
    public Framing framing = Framing.Outfit;
    public float spinDegPerSec = 14f;

    private Transform _turntable, _clone;
    private KuroBikeRig _rig;
    private Camera _cam;
    private Vector3 _camPos, _camLook;
    private float _yaw = 200f, _userYaw, _riderHeight = 1.2f;
    private bool _posed;
    private Material _glowMat;

    /// <summary>Builds the studio 1.2 km above <paramref name="source"/> with a clone of it.</summary>
    public static ShopStudio Create(Transform source, int width = 1280, int height = 1080)
    {
        var go = new GameObject("~MapleRowStudio");
        go.transform.position = source.position + Vector3.up * 1200f;
        var s = go.AddComponent<ShopStudio>();
        s.Build(source, width, height);
        return s;
    }

    private void Build(Transform source, int w, int h)
    {
        _turntable = new GameObject("Turntable").transform;
        _turntable.SetParent(transform, false);

        // Clone INACTIVE so none of the ride scripts wake up, then strip everything that is not
        // the rig, the kit, the dismount pose or plain rendering.
        var holder = new GameObject("~CloneHolder");
        holder.SetActive(false);
        holder.transform.SetParent(_turntable, false);
        _clone = Instantiate(source.gameObject, holder.transform).transform;
        _clone.name = "Mannequin";
        _clone.localPosition = Vector3.zero;
        _clone.localRotation = Quaternion.identity;
        foreach (var mb in _clone.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(mb is KuroBikeRig) && !(mb is KitAppearance) && !(mb is KuroDismount) && !(mb is HeadUnitScreen) &&
                !(mb is UnityEngine.UI.Graphic) && !(mb is Canvas) && !(mb is UnityEngine.EventSystems.UIBehaviour))
                DestroyImmediate(mb);
        foreach (var c in _clone.GetComponentsInChildren<Camera>(true)) DestroyImmediate(c.gameObject);
        foreach (var c in _clone.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var c in _clone.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(c);
        foreach (var c in _clone.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(c);
        foreach (var c in _clone.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(c);

        _rig = _clone.GetComponentInChildren<KuroBikeRig>(true);
        if (_rig != null) { _rig.useRideCadenceForCrank = false; _rig.rideSession = null; _rig.previewCadenceRpm = 0f; }
        Kit = _clone.GetComponent<KitAppearance>() ?? _clone.gameObject.AddComponent<KitAppearance>();
        Kit.session = null; Kit.hud = null; Kit.previewOnly = false;
        var dis = _clone.GetComponent<KuroDismount>() ?? _clone.gameObject.AddComponent<KuroDismount>();
        dis.rig = _rig;

        _clone.SetParent(_turntable, false);
        Destroy(holder);
        // Player-only ride extras must not show on the mannequin: the time-trial aero cockpit
        // (KuroRidePose hides it at runtime, but that component is stripped from the clone).
        var pose = source.GetComponentInChildren<KuroRidePose>(true);
        if (pose != null && pose.aeroCockpitVisual != null)
        {
            var path = PathFrom(source, pose.aeroCockpitVisual.transform);
            var twin = path != null ? _clone.Find(path) : null;
            if (twin != null) twin.gameObject.SetActive(pose.aeroCockpitVisual.activeInHierarchy);
        }
        var names = new System.Text.StringBuilder();
        foreach (var r in _clone.GetComponentsInChildren<Renderer>(false))
            if (r.enabled) names.Append(r.name).Append(r is SkinnedMeshRenderer ? "(skin)" : "").Append(", ");
        Debug.Log("[studio] mannequin renderers: " + names);
        foreach (var t in _clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;

        // showroom plinth: polished dark disc with a warm glowing edge
        var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(plinth.GetComponent<Collider>());
        plinth.name = "Plinth";
        plinth.transform.SetParent(transform, false);
        plinth.transform.localPosition = new Vector3(0f, -0.04f, 0f);
        plinth.transform.localScale = new Vector3(2.4f, 0.04f, 2.4f);
        plinth.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.09f, 0.09f, 0.10f), 0.9f);
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(ring.GetComponent<Collider>());
        ring.name = "Glow";
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = new Vector3(0f, -0.06f, 0f);
        ring.transform.localScale = new Vector3(2.52f, 0.03f, 2.52f);
        _glowMat = Mat(new Color(0.886f, 0.745f, 0.431f), 0.5f, emissive: 3f);
        ring.GetComponent<Renderer>().sharedMaterial = _glowMat;

        // The scene sun and sky already light the studio (it is outdoors, 1.2 km up). First pass
        // used 900-2600 lm points and blew the black bike and plinth out to white, so these are
        // only a faint warm kick and a cool rim for shape.
        AddLight("Key", new Vector3(1.6f, 2.4f, 1.8f), new Color(1f, 0.90f, 0.78f), 60f);
        AddLight("Rim", new Vector3(-0.6f, 2.0f, -2.2f), new Color(0.75f, 0.85f, 1f), 45f);

        Texture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "~MapleRowStudioRT" };
        var camGo = new GameObject("StudioCam");
        camGo.transform.SetParent(transform, false);
        _cam = camGo.AddComponent<Camera>();
        _cam.targetTexture = Texture;
        _cam.fieldOfView = 30f;
        _cam.nearClipPlane = 0.05f;
        _cam.farClipPlane = 4000f;
        _cam.allowHDR = true;
        var hd = camGo.AddComponent<HDAdditionalCameraData>();
        hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        _cam.depth = -50f;
    }

    private void Start()
    {
        // The rig needs one frame to Setup before the standing pose can be held.
        _clone.gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        if (!_posed && _rig != null)
        {
            _rig.ForceSolveOnce();
            var dis = _clone.GetComponent<KuroDismount>();
            if (dis != null) dis.HoldStanding();
            _riderHeight = Mathf.Max(0.8f, _rig.LegLength * 2.6f);
            _posed = true;
        }
        _yaw += spinDegPerSec * Time.unscaledDeltaTime;
        float turn = (Input.GetKey(KeyCode.Q) ? 90f : 0f) - (Input.GetKey(KeyCode.E) ? 90f : 0f);
        _yaw += turn * Time.unscaledDeltaTime;
        _userYaw += turn * Time.unscaledDeltaTime;
        _turntable.localRotation = Quaternion.Euler(0f, _yaw, 0f);

        Frame(out var pos, out var look);
        float k = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
        _camPos = _camPos == Vector3.zero ? pos : Vector3.Lerp(_camPos, pos, k);
        _camLook = _camLook == Vector3.zero ? look : Vector3.Lerp(_camLook, look, k);
        _cam.transform.position = _camPos;
        _cam.transform.LookAt(_camLook);
    }

    /// <summary>Camera goal for the current framing. The camera stays put in the studio frame
    /// while the turntable spins, except for the wheel and computer close-ups, which follow the
    /// part so it stays in view.</summary>
    private void Frame(out Vector3 pos, out Vector3 look)
    {
        var o = transform.position;
        float h = _riderHeight;
        switch (framing)
        {
            case Framing.Helmet:
            {
                // Kuro stands BESIDE the bike, off the turntable axis, so track the head itself.
                var head = GearVisuals.FindDeep(_clone, "Head");
                // the big chibi helmet sits well above the head bone; at 0.6 h the camera ended
                // up inside the shell
                look = head != null ? head.position + Vector3.up * h * 0.12f : o + Vector3.up * h * 0.90f;
                var toCam = Vector3.ProjectOnPlane(_cam.transform.position - look, Vector3.up).normalized;
                if (toCam.sqrMagnitude < 0.5f) toCam = Vector3.forward;
                // head-and-shoulders: at 1.25 h the helmet filled and overflowed the frame
                pos = look + (toCam * 0.95f + Vector3.up * 0.22f) * h * 2.1f;
                return;
            }
            case Framing.Wheels:
            {
                var axle = GearVisuals.FindDeep(_clone, "Axle_F");
                look = axle != null ? axle.position : o + Vector3.up * 0.2f;
                var side = axle != null ? _clone.right : Vector3.right;
                pos = look + side * h * 0.95f + Vector3.up * h * 0.12f + _clone.forward * h * 0.15f;
                return;
            }
            case Framing.Computer:
            {
                var steer = GearVisuals.FindDeep(_clone, "SteerPivot");
                var unit = GearVisuals.FindDeep(_clone, "~GearHeadUnit");
                look = unit != null ? unit.position : steer != null ? steer.position + _clone.forward * 0.03f : o + Vector3.up * h * 0.55f;
                // the rider's view (the screen faces the saddle), from high and to the left:
                // Kuro stands on the right holding the right hood, so a low view hid the screen
                // behind his hand, and a view from ahead read the screen sideways
                pos = look - _clone.forward * h * 0.30f + Vector3.up * h * 0.46f - _clone.right * h * 0.16f;
                return;
            }
            case Framing.Frame:
            {
                // The whole bike, side-on, from the +right side the wheel close-up uses. Kuro stands
                // on the far side of the bike there. A camera fixed in the studio frame put Kuro
                // between it and the frame for half of every turntable turn. The view sways
                // ±35° (front 3/4 to rear 3/4), and Q/E still turn it.
                var fa = GearVisuals.FindDeep(_clone, "Axle_F");
                var ra = GearVisuals.FindDeep(_clone, "Axle_R");
                look = fa != null && ra != null ? (fa.position + ra.position) * 0.5f + Vector3.up * h * 0.16f : o + Vector3.up * h * 0.3f;
                float sway = 35f * Mathf.Sin(Time.unscaledTime * 0.35f);
                var dir = Quaternion.Euler(0f, sway - _userYaw, 0f) * _clone.right;
                pos = look + dir * h * 2.05f + Vector3.up * h * 0.34f;
                return;
            }
            default:
                look = o + Vector3.up * h * 0.50f;
                pos = o + new Vector3(0f, h * 0.62f, h * 2.9f);
                return;
        }
    }

    /// <summary>Recolours the plinth's glowing edge to the store's accent (store identity).</summary>
    public void SetAccent(Color c)
    {
        if (_glowMat == null) return;
        if (_glowMat.HasProperty("_BaseColor")) _glowMat.SetColor("_BaseColor", c);
        if (_glowMat.HasProperty("_Color")) _glowMat.SetColor("_Color", c);
        if (_glowMat.HasProperty("_EmissiveColor")) _glowMat.SetColor("_EmissiveColor", c * 3f);
    }

    public void TryOn(ShopItem item)
    {
        if (Kit == null) return;
        Kit.tryOn.Clear();
        // wear everything the player already has on, then the item being looked at on top
        foreach (ShopCategory c in System.Enum.GetValues(typeof(ShopCategory)))
        {
            var eq = PlayerWardrobe.Equipped(c);
            if (eq != null) Kit.tryOn[c] = eq;
        }
        if (item != null) Kit.tryOn[item.Category] = item;
        Kit.previewOnly = true;
        Kit.Apply();
        framing = item == null ? Framing.Outfit
                : item.Category == ShopCategory.Helmet ? Framing.Helmet
                : item.Category == ShopCategory.Wheels ? Framing.Wheels
                : item.Category == ShopCategory.BikeComputer ? Framing.Computer
                : item.Category == ShopCategory.Frame ? Framing.Frame
                : Framing.Outfit;
    }

    private static string PathFrom(Transform root, Transform t)
    {
        if (t == null || !t.IsChildOf(root)) return null;
        var parts = new System.Collections.Generic.List<string>();
        for (var p = t; p != root; p = p.parent) parts.Insert(0, p.name);
        return string.Join("/", parts);
    }

    private void AddLight(string n, Vector3 local, Color c, float intensity)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = local;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.range = 8f;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        if (go.GetComponent<HDAdditionalLightData>() == null) go.AddComponent<HDAdditionalLightData>();
    }

    private static Material Mat(Color c, float smooth, float emissive = 0f)
    {
        var sh = Shader.Find("HDRP/Lit") ?? Shader.Find("MapleRide/HDRP/CelLit");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (emissive > 0f && m.HasProperty("_EmissiveColor"))
        {
            m.EnableKeyword("_EMISSIVE_COLOR_MAP");
            m.SetColor("_EmissiveColor", c * emissive);
        }
        return m;
    }

    private void OnDestroy()
    {
        if (_glowMat != null) Destroy(_glowMat);
        if (Texture != null) { Texture.Release(); Destroy(Texture); }
    }
}
