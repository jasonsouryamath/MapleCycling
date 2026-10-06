using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Per-NPC "style" for Shunta: a different hair mesh + hair colour and different eyes on the SAME body.
///  * Hair (walkers only; riders keep their helmets): PedestrianKuroLook.ApplyHair, from the project's authored PedHair set.
///  * Eyes (walkers and riders): a thin overlay of procedurally drawn eyes laid ON the face surface over the painted eyes,
///    using the face grids the blink system already stores (Resources/Race/eyelids). The body mesh, skin weights, UVs and
///    body texture are never edited. Overlays hide while a blink is closing so the lids still read.
/// Everything is deterministic from the NPC index and every step fails soft (no data = the original look is kept).
/// </summary>
public static class ShuntaNpcStyle
{
    public const int EyeStyleCount = 8;
    public const float SurfaceOffsetM = 0.0003f;   // head-frame metres off the face surface (lash strips sit at 0.0004)

    public struct Style { public int hair, hairColour, eye; }

    sealed class EyeLook { public string name; public Color iris; public float irisR, pupilR, lash; }
    static readonly EyeLook[] Looks =
    {
        new EyeLook { name = "Amber round",    iris = new Color(.95f, .62f, .12f), irisR = .56f, pupilR = .34f, lash = .10f },
        new EyeLook { name = "Teal sharp",     iris = new Color(.10f, .65f, .70f), irisR = .46f, pupilR = .30f, lash = .16f },
        new EyeLook { name = "Violet wide",    iris = new Color(.50f, .30f, .85f), irisR = .62f, pupilR = .40f, lash = .09f },
        new EyeLook { name = "Emerald calm",   iris = new Color(.15f, .65f, .30f), irisR = .50f, pupilR = .28f, lash = .12f },
        new EyeLook { name = "Rose round",     iris = new Color(.90f, .35f, .55f), irisR = .56f, pupilR = .34f, lash = .10f },
        new EyeLook { name = "Sky sharp",      iris = new Color(.30f, .55f, .95f), irisR = .46f, pupilR = .30f, lash = .16f },
        new EyeLook { name = "Crimson wide",   iris = new Color(.80f, .15f, .20f), irisR = .62f, pupilR = .40f, lash = .09f },
        new EyeLook { name = "Graphite sleepy", iris = new Color(.30f, .32f, .38f), irisR = .50f, pupilR = .28f, lash = .26f },
    };
    public static string EyeName(int eye) => Looks[Mathf.Abs(eye) % Looks.Length].name;

    /// <summary>Deterministic, well-spread style for NPC number <paramref name="index"/>; neighbours differ in hair and eyes.</summary>
    public static Style Pick(int index, int hairStyles, int hairColours)
    {
        index = Mathf.Abs(index);
        return new Style
        {
            hair = hairStyles > 0 ? (index * 3 + index / 8) % hairStyles : -1,
            hairColour = hairColours > 0 ? (index * 5 + 1) % hairColours : 0,
            eye = (index * 3 + 1) % EyeStyleCount,
        };
    }

    // ------------------------------------------------------------------ apply
    public static int HairApplied, EyesApplied, Skipped;

    /// <summary>Applies the style to a cloned NPC. Returns what was changed; never throws on missing data.</summary>
    public static bool Apply(GameObject npc, int index, bool allowHair)
    {
        if (npc == null || npc.GetComponent<ShuntaNpcEyes>() != null) return false;
        var library = Resources.Load<PedestrianModelLibrary>("Pedestrians/PedestrianModelLibrary");
        var set = library != null ? library.kuroRider : null;
        var style = Pick(index, set != null && set.hairStyles != null ? set.hairStyles.Length : 0,
                                set != null && set.hairColours != null ? set.hairColours.Length : 0);
        bool changed = false;
        if (allowHair && set != null && style.hair >= 0 && PedestrianKuroLook.ApplyHair(npc, set, style.hair, style.hairColour) != null)
        { HairApplied++; changed = true; }
        if (ApplyEyes(npc, style.eye)) { EyesApplied++; changed = true; }
        if (!changed) Skipped++;
        return changed;
    }

    static bool ApplyEyes(GameObject npc, int eyeStyle)
    {
        SkinnedMeshRenderer body = null;
        foreach (var r in npc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name == "Mesh_0" && r.enabled && r.sharedMesh != null) { body = r; break; }
        if (body == null) return false;
        // Donor bodies are the same head sculpt on a re-exported body: alias them to Kuro's eyelid data, as the crowd does.
        RiderBlink.Alias(RiderBlink.KeyFor(body), PedestrianKuroLook.BlinkSourceKey);
        var data = RiderBlink.Lookup(RiderBlink.KeyFor(body));
        if (data == null || data.eyes == null || data.eyes.Length == 0) return false;
        Transform head = null;
        foreach (var bone in body.bones) if (bone != null && bone.name == data.headBone) { head = bone; break; }
        if (head == null) return false;
        var blink = npc.GetComponent<RiderBlink>() ?? RiderBlink.Attach(npc);

        var mats = body.sharedMaterials;
        Material like = null;
        if (mats.Length > 0) like = mats[Mathf.Clamp(data.submesh, 0, mats.Length - 1)];
        if (like == null || !like.HasProperty("_MainTex")) return false;
        var material = EyeMaterial(like, eyeStyle);

        var comp = npc.AddComponent<ShuntaNpcEyes>();
        comp.head = head; comp.blink = blink;
        var renderers = new List<Renderer>();
        foreach (var eye in data.eyes)
        {
            if (eye == null || eye.points == null || eye.normals == null || eye.points.Length != eye.rows * eye.cols || eye.rows < 2 || eye.cols < 2) continue;
            var go = new GameObject("Shunta Eye", typeof(MeshFilter), typeof(MeshRenderer)) { layer = npc.layer };
            go.transform.SetParent(npc.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = BuildEyeMesh(eye);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.BlendProbes;
            comp.eyes.Add(go.transform); comp.renderers.Add(mr); renderers.Add(mr);
        }
        if (renderers.Count == 0) { Object.Destroy(comp); return false; }
        PedestrianKuroLook.RegisterOnLod0(npc, renderers);
        comp.style = eyeStyle;
        return true;
    }

    // ------------------------------------------------------------------ material (cloned from the body's cel material so it lights like the face)
    static readonly Dictionary<(Shader, int), Material> _materials = new Dictionary<(Shader, int), Material>();
    static Material EyeMaterial(Material like, int style)
    {
        style = Mathf.Abs(style) % Looks.Length;
        if (_materials.TryGetValue((like.shader, style), out var m) && m != null) return m;
        m = new Material(like) { name = "ShuntaEye_" + Looks[style].name };
        // The body material enables a tangent-space normal map; flat-shade the eyes (see PedestrianKuroLook.HairMaterial).
        if (m.HasProperty("_NormalStrength")) m.SetFloat("_NormalStrength", 0f);
        m.SetTexture("_MainTex", EyeTexture(style));
        m.SetTextureScale("_MainTex", Vector2.one);
        m.SetTextureOffset("_MainTex", Vector2.zero);
        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        _materials[(like.shader, style)] = m;
        return m;
    }

    // ------------------------------------------------------------------ eye texture (opaque; the mesh outline is the eye shape)
    public const int TexSize = 96;
    static readonly Dictionary<int, Texture2D> _textures = new Dictionary<int, Texture2D>();

    public static Texture2D EyeTexture(int style)
    {
        style = Mathf.Abs(style) % Looks.Length;
        if (_textures.TryGetValue(style, out var cached) && cached != null) return cached;
        var look = Looks[style];
        var px = new Color32[TexSize * TexSize];
        for (int j = 0; j < TexSize; j++)
            for (int i = 0; i < TexSize; i++)
                px[j * TexSize + i] = EyePixel(look, ((i + .5f) / TexSize) * 2f - 1f, ((j + .5f) / TexSize) * 2f - 1f);
        var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) { name = "ShuntaEye_" + look.name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        tex.SetPixels32(px); tex.Apply(false, false);
        _textures[style] = tex;
        return tex;
    }

    /// <summary>One texel; x right, y UP, both -1..1 across the eye ellipse. Pure, so it is testable and previewable.</summary>
    public static Color32 EyePixel(Color iris, float irisR, float pupilR, float lash, float x, float y)
    {
        var c = new Color(.97f, .96f, .97f);
        c *= Mathf.Lerp(.80f, 1f, Mathf.SmoothStep(0f, 1f, (.9f - y) / 1.1f));            // shadow from the upper lid
        float d = Mathf.Sqrt(x * x + (y + .06f) * (y + .06f)) / irisR;
        if (d < 1f)
        {
            float t = Mathf.Clamp01((1f - (y + .06f) / irisR) * .5f);                      // 0 bottom .. 1 top
            var ic = Color.Lerp(Scale(iris, 1.3f), Scale(iris, .5f), t);
            if (d > .86f) ic = Color.Lerp(ic, Scale(iris, .3f), (d - .86f) / .14f);          // limbal ring
            c = ic;
            if (d < pupilR / irisR) c = new Color(.04f, .03f, .06f);
        }
        float hx = x + .30f, hy = y - .30f;
        if (hx * hx + hy * hy < .03f) c = Color.white;                                       // main highlight
        float sx = x - .28f, sy = y + .32f;
        if (sx * sx + sy * sy < .006f) c = Color.Lerp(c, Color.white, .85f);                // small catch-light
        float edge = Mathf.Sqrt(x * x + y * y);
        float band = 1f - lash;
        if (edge > band && y > -.25f) c = Color.Lerp(c, new Color(.07f, .05f, .09f), Mathf.Clamp01((edge - band) / .05f));   // upper lash
        else if (edge > .93f) c = Color.Lerp(c, new Color(.45f, .35f, .35f), Mathf.Clamp01((edge - .93f) / .05f));          // soft lower line
        return new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), 255);
    }
    static Color32 EyePixel(EyeLook l, float x, float y) => EyePixel(l.iris, l.irisR, l.pupilR, l.lash, x, y);
    static Color Scale(Color c, float k) => new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);

    public static Color32 EyePixelFor(int style, float x, float y) => EyePixel(Looks[Mathf.Abs(style) % Looks.Length], x, y);

    // ------------------------------------------------------------------ eye mesh (an ellipse laid on the stored face-surface grid)
    const int Segments = 24, Rings = 3;
    const float CentreU = .5f, CentreV = .54f, RadiusU = .44f, RadiusV = .38f;

    /// <summary>Bilinear point and normal on the eyelid grid; u across the columns, v down the rows (0 = above the upper lash).</summary>
    public static void Surface(RiderBlink.EyeData e, float u, float v, out Vector3 p, out Vector3 n)
    {
        float f = Mathf.Clamp01(u) * (e.cols - 1), g = Mathf.Clamp01(v) * (e.rows - 1);
        int c0 = Mathf.Min(e.cols - 2, (int)f), r0 = Mathf.Min(e.rows - 2, (int)g);
        float tu = f - c0, tv = g - r0;
        Vector3 P(int r, int c) => e.points[r * e.cols + c];
        Vector3 N(int r, int c) => e.normals[r * e.cols + c];
        p = Vector3.Lerp(Vector3.Lerp(P(r0, c0), P(r0, c0 + 1), tu), Vector3.Lerp(P(r0 + 1, c0), P(r0 + 1, c0 + 1), tu), tv);
        n = Vector3.Lerp(Vector3.Lerp(N(r0, c0), N(r0, c0 + 1), tu), Vector3.Lerp(N(r0 + 1, c0), N(r0 + 1, c0 + 1), tu), tv);
        n = n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.forward;
    }

    public static Mesh BuildEyeMesh(RiderBlink.EyeData e)
    {
        // Keep the texture's "left" on screen-left for both eyes whatever order the grid columns run in.
        bool flip = e.points[e.cols - 1].x < e.points[0].x;
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tans = new List<Vector4>(); var uvs = new List<Vector2>();
        void Add(float k, float a)
        {
            float cu = Mathf.Cos(a), su = Mathf.Sin(a);
            float gu = CentreU + RadiusU * k * cu, gv = CentreV + RadiusV * k * su;
            Surface(e, gu, gv, out var p, out var n);
            Surface(e, Mathf.Clamp01(gu + .02f), gv, out var p2, out _);
            var t = (p2 - p); if (flip) t = -t;
            t = t.sqrMagnitude > 1e-10f ? (t - n * Vector3.Dot(t, n)).normalized : Vector3.right;
            verts.Add(p + n * SurfaceOffsetM); norms.Add(n); tans.Add(new Vector4(t.x, t.y, t.z, 1f));
            float tu = .5f + .5f * k * cu * (flip ? -1f : 1f), tv = .5f - .5f * k * su;      // grid v grows DOWN the face, texture v grows UP
            uvs.Add(new Vector2(tu, tv));
        }
        Add(0f, 0f);
        for (int r = 1; r <= Rings; r++)
            for (int s = 0; s < Segments; s++) Add(r / (float)Rings, Mathf.PI * 2f * s / Segments);
        var tris = new List<int>();
        for (int s = 0; s < Segments; s++) { int a = 1 + s, b = 1 + (s + 1) % Segments; tris.Add(0); tris.Add(a); tris.Add(b); }
        for (int r = 1; r < Rings; r++)
            for (int s = 0; s < Segments; s++)
            {
                int a = 1 + (r - 1) * Segments + s, b = 1 + (r - 1) * Segments + (s + 1) % Segments;
                int c = 1 + r * Segments + s, d = 1 + r * Segments + (s + 1) % Segments;
                tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d);
            }
        var mesh = new Mesh { name = "ShuntaEyeMesh" };
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetTangents(tans); mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        // Winding: make sure the front face points along the surface normal (grid order can run either way).
        if (Vector3.Dot(Vector3.Cross(verts[tris[1]] - verts[tris[0]], verts[tris[2]] - verts[tris[0]]), norms[0]) < 0f)
        {
            for (int i = 0; i < tris.Count; i += 3) { int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
            mesh.SetTriangles(tris, 0);
        }
        mesh.RecalculateBounds();
        return mesh;
    }
}

/// <summary>Keeps the eye overlays on the head bone (same frame trick as RiderBlink's lids) and hides them while a blink closes.</summary>
[DisallowMultipleComponent]
public sealed class ShuntaNpcEyes : MonoBehaviour
{
    public Transform head;
    public RiderBlink blink;
    public int style;
    public readonly List<Transform> eyes = new List<Transform>();
    public readonly List<Renderer> renderers = new List<Renderer>();
    public float hideAboveClosed = .15f;

    void LateUpdate()
    {
        if (head == null) return;
        bool show = blink == null || blink.ClosedAmount < hideAboveClosed;
        for (int i = 0; i < eyes.Count; i++)
        {
            var t = eyes[i]; if (t == null) continue;
            if (renderers[i] != null && renderers[i].enabled != show) renderers[i].enabled = show;
            t.SetPositionAndRotation(head.position, head.rotation);
            var ps = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(1f / Mathf.Max(1e-5f, ps.x), 1f / Mathf.Max(1e-5f, ps.y), 1f / Mathf.Max(1e-5f, ps.z));
        }
    }
}
