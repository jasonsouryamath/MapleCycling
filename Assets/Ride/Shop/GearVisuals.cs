using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Real 3D gear for Maple Row purchases, on the player's bike and on the shop mannequin:
///  * CARBONFORGE wheels: a deep-section carbon rim ring on each axle (depth from the product:
///    45 or 65 mm, scaled to this bike's wheel) with a brand-colour band, so CF45 and CF65 look
///    different, instead of only tinting the stock rim.
///  * APEX computers: an out-front head unit on the bars (steers with them) with a live screen:
///    speed and power, plus W/kg and a power-zone bar on the Elite.
/// Everything hangs off the bike's own sockets (Axle_F / Axle_R / SteerPivot / Hood_L / Hood_R),
/// is named "~Gear*", and is rebuilt idempotently.
/// </summary>
public static class GearVisuals
{
    private const string RimName = "~GearDeepRim", UnitName = "~GearHeadUnit";

    public static void ApplyWheels(Transform root, ShopItem wheels)
    {
        foreach (var axleName in new[] { "Axle_F", "Axle_R" })
        {
            var axle = FindDeep(root, axleName);
            if (axle == null) continue;
            var old = axle.Find(RimName);
            if (old != null) Object.Destroy(old.gameObject);
            if (wheels == null) continue;

            float outer = RimRadius(axle, root);                     // rim edge = the tyre's inner edge
            if (outer <= 0.01f) continue;
            // The stock chibi Colnago already has a ~17 %-of-radius rim, and the first rings sat
            // INSIDE it (invisible). So the rings are wider than the stock rim and deeper than it:
            // CF45 20 %, CF65 30 % of the rim radius (the chibi bike exaggerates proportions).
            // CF30 (climbing, CdaScale >= 0.988) gets a shallow 13 % ring so the three depths read apart.
            float depthFrac = wheels.CdaScale < 0.975f ? 0.30f : wheels.CdaScale < 0.988f ? 0.20f : 0.13f;
            float inner = outer * (1f - depthFrac);
            float width = outer * 0.19f;

            var go = new GameObject(RimName);
            go.transform.SetParent(axle, false);
            var s = axle.lossyScale;
            go.transform.localScale = new Vector3(1f / Mathf.Max(1e-4f, s.x), 1f / Mathf.Max(1e-4f, s.y), 1f / Mathf.Max(1e-4f, s.z));
            // One mesh, two submeshes: carbon (dark satin grey, not pure black, so the deep section
            // reads) and the brand band across the middle of the rim wall, proud of both faces.
            // (First pass used a separate band object and it never showed in the captures.)
            var carbon = BuildRing(outer, inner, width, faces: true, walls: true);
            float bandHi = Mathf.Lerp(inner, outer, 0.62f), bandLo = Mathf.Lerp(inner, outer, 0.46f);
            var band = BuildRing(bandHi, bandLo, width * 1.12f, faces: true, walls: false);
            var mesh = new Mesh { name = "~GearRim" };
            var verts = new System.Collections.Generic.List<Vector3>(carbon.v); verts.AddRange(band.v);
            var norms = new System.Collections.Generic.List<Vector3>(carbon.n); norms.AddRange(band.n);
            mesh.SetVertices(verts); mesh.SetNormals(norms);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(carbon.t, 0);
            mesh.SetTriangles(band.t.ConvertAll(i => i + carbon.v.Count), 1);
            mesh.RecalculateBounds();
            var rimGo = new GameObject("Rim", typeof(MeshFilter), typeof(MeshRenderer));
            rimGo.transform.SetParent(go.transform, false);
            rimGo.GetComponent<MeshFilter>().sharedMesh = mesh;
            rimGo.GetComponent<MeshRenderer>().sharedMaterials = new[]
            {
                Lit(Color.Lerp(wheels.Primary, new Color(0.22f, 0.23f, 0.25f), 0.45f), 0.85f),
                Lit(wheels.Trim, 1.0f),
            };
        }
    }

    public static Transform ApplyComputer(Transform root, ShopItem computer)
    {
        var steer = FindDeep(root, "SteerPivot");
        var hoodL = FindDeep(root, "Hood_L");
        var hoodR = FindDeep(root, "Hood_R");
        if (steer == null) return null;
        var old = steer.Find(UnitName);
        if (old != null) Object.Destroy(old.gameObject);
        if (computer == null || hoodL == null || hoodR == null) return null;

        float span = Vector3.Distance(hoodL.position, hoodR.position);       // bar width sets scale
        Vector3 bikeFwd = root.forward, up = root.up;
        Vector3 mid = (hoodL.position + hoodR.position) * 0.5f;

        var unit = new GameObject(UnitName).transform;
        unit.SetParent(steer, false);
        unit.position = mid + bikeFwd * span * 0.32f + up * span * 0.10f;
        unit.rotation = Quaternion.LookRotation(bikeFwd, up) * Quaternion.Euler(-20f, 0f, 0f);   // screen tilted to the rider
        float w = span * 0.36f, h = span * (computer.ComputerTier >= 2 ? 0.52f : 0.44f), d = span * 0.09f;

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(body.GetComponent<Collider>());
        body.name = "Case";
        body.transform.SetParent(unit, false);
        body.transform.localScale = new Vector3(w, d, h);
        body.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.07f, 0.08f, 0.09f), 0.9f);

        var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(arm.GetComponent<Collider>());
        arm.name = "Mount";
        arm.transform.SetParent(unit, false);
        // a short clamp straight under the case (the first version was a long bar reaching back
        // toward the rider and read as a floating black slab in the shop close-ups)
        arm.transform.localPosition = new Vector3(0f, -d * 0.9f, -h * 0.30f);
        arm.transform.localScale = new Vector3(w * 0.30f, d * 0.9f, h * 0.22f);
        arm.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.12f, 0.12f, 0.13f), 0.9f);

        // live screen: a tiny world-space canvas on the top face
        var cv = new GameObject("Screen", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        cv.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)cv.transform;
        rt.SetParent(unit, false);
        rt.sizeDelta = new Vector2(200f, 200f * h / w);
        rt.localScale = Vector3.one * (w * 0.86f / 200f);
        rt.localPosition = new Vector3(0f, d * 0.51f, 0f);
        rt.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        bg.transform.SetParent(rt, false);
        Stretch(bg.rectTransform);
        bg.color = new Color(0.72f, 0.80f, 0.74f, 1f);             // transflective LCD
        var txt = new GameObject("Readout", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        txt.transform.SetParent(rt, false);
        Stretch(txt.rectTransform, 8f);
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.color = new Color(0.06f, 0.07f, 0.08f);
        txt.alignment = TextAnchor.MiddleCenter;
        txt.resizeTextForBestFit = true; txt.resizeTextMinSize = 10; txt.resizeTextMaxSize = 60;
        txt.supportRichText = true;
        unit.gameObject.AddComponent<HeadUnitScreen>().Init(txt, computer.ComputerTier);
        return unit;
    }

    private static void Stretch(RectTransform r, float pad = 0f)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(pad, pad); r.offsetMax = new Vector2(-pad, -pad);
    }

    /// <summary>
    /// Where the rim's outer edge is, in world units: the INNER edge of this wheel's tyre, measured
    /// from the tyre mesh itself (vertices of any renderer whose material is a tyre, within reach
    /// of this axle, in the axle's spin plane). The rig's drivetrain radius and renderer bounds
    /// were both tried first and put the ring on or outside the tyre in the play test.
    /// </summary>
    private static float RimRadius(Transform axle, Transform root)
    {
        var rig = root.GetComponentInChildren<KuroBikeRig>(true);
        float guess = rig != null && rig.wheelRadius > 0.01f ? rig.wheelRadius : 0.175f;
        var dists = new System.Collections.Generic.List<float>();
        Vector3 c = axle.position, axis = axle.right;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            bool tyre = false;
            foreach (var m in r.sharedMaterials)
                if (m != null && (m.name.Contains("Tyre") || m.name.Contains("Tire"))) tyre = true;
            if (!tyre) continue;
            Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            var l2w = r.transform.localToWorldMatrix;
            foreach (var v in mesh.vertices)
            {
                var p = l2w.MultiplyPoint3x4(v) - c;
                if (Mathf.Abs(Vector3.Dot(p, axis)) > guess * 0.4f) continue;     // other wheel / off-plane
                float d = Vector3.ProjectOnPlane(p, axis).magnitude;
                if (d > guess * 0.5f && d < guess * 1.6f) dists.Add(d);
            }
        }
        if (dists.Count < 16)
        {
            Debug.Log($"[gear] {axle.name}: no readable tyre mesh near the axle, rim radius falls back to {guess * 0.80f:0.000}");
            return guess * 0.80f;
        }
        dists.Sort();
        float rim = dists[Mathf.Clamp(dists.Count / 50, 0, dists.Count - 1)] * 0.99f;      // ~2nd percentile
        Debug.Log($"[gear] {axle.name}: tyre {dists[0]:0.000}-{dists[dists.Count - 1]:0.000} m ({dists.Count} verts), rim edge {rim:0.000} m (rig radius {guess:0.000})");
        return rim;
    }

    private static Material Lit(Color c, float smooth)
    {
        var sh = Shader.Find("MapleRide/HDRP/CelLit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = "~GearMat" };
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        return m;
    }

    /// <summary>An annulus in the axle's local YZ plane (axles spin about local X), extruded
    /// along X: two side faces and/or the outer and inner walls. Double-sided.</summary>
    private static (System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<Vector3> n, System.Collections.Generic.List<int> t)
        BuildRing(float rOut, float rIn, float width, bool faces, bool walls)
    {
        var tmp = new GameObject("~tmp");
        AddRing(tmp.transform, "x", rOut, rIn, width, null, wallsOnly: !faces, facesOnly: !walls);
        var m = tmp.GetComponentInChildren<MeshFilter>().sharedMesh;
        var res = (new System.Collections.Generic.List<Vector3>(m.vertices), new System.Collections.Generic.List<Vector3>(m.normals),
                   new System.Collections.Generic.List<int>(m.triangles));
        Object.Destroy(m);
        Object.Destroy(tmp);
        return res;
    }

    private static void AddRing(Transform parent, string name, float rOut, float rIn, float width, Material mat,
                                bool wallsOnly = false, bool facesOnly = false)
    {
        const int N = 72;
        var v = new System.Collections.Generic.List<Vector3>();
        var n = new System.Collections.Generic.List<Vector3>();
        var tri = new System.Collections.Generic.List<int>();
        float hx = width * 0.5f;
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            n.Add(nn); n.Add(nn); n.Add(nn); n.Add(nn);
            tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); tri.Add(i); tri.Add(i + 2); tri.Add(i + 3);
            tri.Add(i); tri.Add(i + 2); tri.Add(i + 1); tri.Add(i); tri.Add(i + 3); tri.Add(i + 2);   // double-sided
        }
        for (int k = 0; k < N; k++)
        {
            float a0 = k * Mathf.PI * 2f / N, a1 = (k + 1) * Mathf.PI * 2f / N;
            Vector3 d0 = new Vector3(0f, Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector3(0f, Mathf.Cos(a1), Mathf.Sin(a1));
            if (!wallsOnly)
                foreach (float sx in new[] { -hx, hx })
                {
                    var o = new Vector3(sx, 0f, 0f);
                    Quad(o + d0 * rIn, o + d0 * rOut, o + d1 * rOut, o + d1 * rIn, new Vector3(Mathf.Sign(sx), 0f, 0f));
                }
            if (!facesOnly)
            {
                Quad(new Vector3(-hx, 0, 0) + d0 * rOut, new Vector3(hx, 0, 0) + d0 * rOut,
                     new Vector3(hx, 0, 0) + d1 * rOut, new Vector3(-hx, 0, 0) + d1 * rOut, d0);
                Quad(new Vector3(-hx, 0, 0) + d0 * rIn, new Vector3(hx, 0, 0) + d0 * rIn,
                     new Vector3(hx, 0, 0) + d1 * rIn, new Vector3(-hx, 0, 0) + d1 * rIn, -d0);
            }
        }
        var mesh = new Mesh { name = "~Gear" + name };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(tri, 0);
        mesh.RecalculateBounds();
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>
    /// The chibi bike's saddle floats a few centimetres above its seat post (hidden while Kuro
    /// sits on it, obvious once he is standing beside the bike in the shop). Fills the gap with a
    /// post extension in the post's own material, parented to the post. Idempotent.
    /// </summary>
    public static void FixSeatpost(Transform root)
    {
        var saddle = FindDeep(root, "RaceSaddle");
        var post = FindDeep(root, "AeroSeatPost");
        if (saddle == null || post == null || post.Find("~GearPostExtension") != null) return;
        var sr = saddle.GetComponent<Renderer>();
        var pr = post.GetComponent<Renderer>();
        if (sr == null || pr == null) return;
        float top = pr.bounds.max.y, bottom = sr.bounds.min.y + sr.bounds.size.y * 0.25f;
        float gap = bottom - top;
        if (gap < 0.004f || gap > 0.25f) return;
        var ext = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(ext.GetComponent<Collider>());
        ext.name = "~GearPostExtension";
        float r = Mathf.Min(pr.bounds.extents.x, pr.bounds.extents.z) * 0.8f;
        var c = pr.bounds.center;
        ext.transform.position = new Vector3(c.x, top + gap * 0.5f - 0.004f, c.z);
        ext.transform.rotation = Quaternion.identity;
        ext.transform.localScale = new Vector3(r * 2f, gap * 0.5f + 0.006f, r * 2f);
        ext.transform.SetParent(post, true);
        ext.GetComponent<Renderer>().sharedMaterial = pr.sharedMaterial;
        Debug.Log($"[gear] seat post extended by {gap * 1000f:0} mm to meet the saddle");
    }

    public static Transform FindDeep(Transform t, string n)
    {
        if (t == null) return null;
        if (t.name == n) return t;
        foreach (Transform c in t) { var r = FindDeep(c, n); if (r != null) return r; }
        return null;
    }
}

/// <summary>Live readout on the 3D head unit (player bike: the real ride; mannequin: a demo).</summary>
public sealed class HeadUnitScreen : MonoBehaviour
{
    private Text _t;
    private int _tier;
    private RideSession _session;
    private float _next;

    public void Init(Text t, int tier)
    {
        _t = t; _tier = tier;
        _session = GetComponentInParent<RideSession>() ?? FindFirstObjectByType<RideSession>();
    }

    private void Update()
    {
        if (_t == null || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.25f;
        float kph = 32.4f, watts = 238f, wkg = 3.4f;
        if (_session != null && _session.devices != null)
        {
            kph = _session.SpeedMps * 3.6f;
            watts = _session.devices.Telemetry.Watts;
            wkg = watts / Mathf.Max(40f, _session.physics != null ? _session.physics.riderMassKg : 70f);
        }
        string s = $"<size=58><b>{kph:0.0}</b></size><size=22> km/h</size>\n<size=46><b>{watts:0}</b></size><size=22> W</size>";
        if (_tier >= 2) s += $"\n<size=30>{wkg:0.0} W/kg</size>";
        s += "\n<size=18>A P E X</size>";
        _t.text = s;
    }
}
