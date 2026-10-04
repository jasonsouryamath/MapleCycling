using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// B2 "finished" start style. Used only on the courses listed in <see cref="FinishedStartCourses"/>;
/// every other map keeps the plain neutral apron from RideStartPad.cs.
///
/// It replaces the flat floating quad with a proper race start: a grounded tarmac deck with
/// painted markings (checkered line, edge lines, grid slots and START in road paint, all baked
/// into one texture so nothing z-fights), red/white race kerbs whose outer faces drop into the
/// ground so there is never a gap under the slab, a start gantry in Maple Ride livery over the
/// line, and livery crowd barriers closing the start pen at the back.
/// </summary>
public partial class RideStartPad
{
    private static readonly HashSet<string> FinishedStartCourses = new HashSet<string>
    {
        "minato_crossing",
        "maple_city_crit",
    };

    /// <summary>QA only: when set, every course gets the plain pad (for before/after captures).</summary>
    public static bool ForcePlainStyle;

    public static bool UsesFinishedStart(string courseId) =>
        !ForcePlainStyle && !string.IsNullOrEmpty(courseId) && FinishedStartCourses.Contains(courseId);

    private static readonly Color LvNavy = new Color(0.07f, 0.10f, 0.20f);
    private static readonly Color LvVermilion = new Color(0.769f, 0.267f, 0.180f);
    private static readonly Color LvChalk = new Color(0.976f, 0.965f, 0.937f);
    private static readonly Color LvGold = new Color(0.847f, 0.647f, 0.180f);

    // Above the road's own markings (+1.5 cm in Maple City) with margin for camber.
    private const float FinishedLift = 0.045f;
    private const float KerbW = 0.36f;
    private const float KerbH = 0.13f;
    private const float EdgeLineX = 3.45f;
    private const float GantryX = 5.15f;
    private const float BarrierX = 4.95f;

    private Material _fsDeck, _fsKerb, _fsConcrete, _fsNavy, _fsBanner, _fsPanel, _fsSteel;

    private sealed class MeshKit
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                         Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            // Vertices are listed counter-clockwise as seen from the front; Unity's front face is
            // clockwise, so the triangles are (a,c,b) and (a,d,c) and the normal is cross(c-a, b-a).
            Vector3 n = Vector3.Cross(c - a, b - a);
            if (n.sqrMagnitude < 1e-10f) n = Vector3.Cross(d - a, c - a);
            n.Normalize();
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            N.Add(n); N.Add(n); N.Add(n); N.Add(n);
            UV.Add(ua); UV.Add(ub); UV.Add(uc); UV.Add(ud);
            // a-b-c-d counter-clockwise seen from the normal side -> Unity (clockwise) winding.
            T.Add(i); T.Add(i + 2); T.Add(i + 1);
            T.Add(i); T.Add(i + 3); T.Add(i + 2);
        }
        // (Triangles above: (a,c,b) and (a,d,c).)

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) =>
            Quad(a, b, c, d, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));

        /// <summary>Box on the ground frame (H, right = up x H, up): centre o, half extents hs
        /// along H and hx along right, from hy0 to hy1 above o. The +/-H faces carry 0..1 UVs
        /// when <paramref name="bannerFaces"/> is set.</summary>
        public void Box(Vector3 o, Vector3 H, float hs, float hx, float hy0, float hy1,
                        bool bannerFaces = false)
        {
            Vector3 up = Vector3.up;
            Vector3 R = Vector3.Cross(up, H).normalized;
            Vector3 P(float s, float x, float y) => o + H * s + R * x + up * y;
            // front (+H) face and back (-H) face carry the banner UVs when asked.
            Vector2 u0 = new Vector2(0, 0), u1 = new Vector2(1, 0), u2 = new Vector2(1, 1), u3 = new Vector2(0, 1);
            Vector2 z = Vector2.zero;
            // +H face (seen from ahead): left-to-right is -R.
            Quad(P(hs, hx, hy0), P(hs, -hx, hy0), P(hs, -hx, hy1), P(hs, hx, hy1),
                 bannerFaces ? u0 : z, bannerFaces ? u1 : z, bannerFaces ? u2 : z, bannerFaces ? u3 : z);
            // -H face (seen by the rider approaching): left-to-right is +R.
            Quad(P(-hs, -hx, hy0), P(-hs, hx, hy0), P(-hs, hx, hy1), P(-hs, -hx, hy1),
                 bannerFaces ? u0 : z, bannerFaces ? u1 : z, bannerFaces ? u2 : z, bannerFaces ? u3 : z);
            Quad(P(-hs, hx, hy0), P(hs, hx, hy0), P(hs, hx, hy1), P(-hs, hx, hy1), z, z, z, z);     // +R
            Quad(P(hs, -hx, hy0), P(-hs, -hx, hy0), P(-hs, -hx, hy1), P(hs, -hx, hy1), z, z, z, z); // -R
            Quad(P(-hs, -hx, hy1), P(-hs, hx, hy1), P(hs, hx, hy1), P(hs, -hx, hy1), z, z, z, z);   // top
            Quad(P(hs, -hx, hy0), P(hs, hx, hy0), P(-hs, hx, hy0), P(-hs, -hx, hy0), z, z, z, z);   // bottom
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    private void BuildFinishedStart(RouteCourse course, float d0, Vector3 p0, float roadY,
                                    Vector3 H, Vector3 R)
    {
        AdoptRegionShading(p0, roadY);
        float deckY = roadY + FinishedLift;
        Vector3 O = new Vector3(p0.x, deckY, p0.z);
        float hw = halfWidth;

        // Deck heights follow the real road mesh (probed across the carriageway) so the slab
        // never floats off a grade and the road's own paint (1.5 cm up) never pokes through.
        // The course polyline is only the fallback / sanity window.
        float CourseH(float s) =>
            (s >= 0f || course.Closed) ? course.PositionAt(d0 + s).y - p0.y : 0f;
        var heightCache = new Dictionary<float, float>();
        float HeightAt(float s)
        {
            if (heightCache.TryGetValue(s, out float cached)) return cached;
            float guess = CourseH(s);
            float refY = roadY + guess;
            Vector3 c = new Vector3(p0.x, 0f, p0.z) + H * s;
            float best = float.NegativeInfinity;
            for (int k = -1; k <= 1; k++)
            {
                Vector3 at = c + R * (k * 1.6f);
                var hits = Physics.RaycastAll(new Vector3(at.x, refY + 3f, at.z), Vector3.down, 6f,
                                              surfaceMask, QueryTriggerInteraction.Ignore);
                foreach (var hit in hits)
                    if (hit.point.y < refY + 0.8f && hit.point.y > best) best = hit.point.y;
            }
            // Only ever RAISE the deck off the course reference: behind an open course's start
            // the probe can find a collider well below the rendered ground (Minato) and sink it.
            float h = float.IsNegativeInfinity(best)
                ? guess
                : Mathf.Clamp(best - roadY, guess - 0.02f, guess + 0.6f);
            heightCache[s] = h;
            return h;
        }

        const float step = 2f;
        var stations = new List<float>();
        for (float s = -back; s < forward - 0.01f; s += step) stations.Add(s);
        stations.Add(forward);

        float Ground(Vector3 at, float refY)
        {
            float best = float.NegativeInfinity;
            var hits = Physics.RaycastAll(new Vector3(at.x, refY + 8f, at.z), Vector3.down, 40f,
                                          surfaceMask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if (h.point.y < refY + 0.6f && h.point.y > best) best = h.point.y;
            return float.IsNegativeInfinity(best) ? refY - 1.5f : best;
        }

        float Drop(Vector3 at, float topY) =>
            Mathf.Max(topY - 6f, Mathf.Min(Ground(at, topY) - 0.35f, topY - 0.25f)) - topY;

        Vector3 P(float s, float x, float h) => O + H * s + R * x + Vector3.up * h;

        // ---------------------------------------------------------------- painted deck
        var deck = new MeshKit();
        float len = back + forward;
        for (int i = 0; i + 1 < stations.Count; i++)
        {
            float s0 = stations[i], s1 = stations[i + 1];
            float h0 = HeightAt(s0), h1 = HeightAt(s1);
            float v0 = (s0 + back) / len, v1 = (s1 + back) / len;
            deck.Quad(P(s0, -hw, h0), P(s0, hw, h0), P(s1, hw, h1), P(s1, -hw, h1),
                      new Vector2(0, v0), new Vector2(1, v0), new Vector2(1, v1), new Vector2(0, v1));
        }
        MakePart("Painted Deck", deck, DeckMat(hw, len), false);

        // ---------------------------------------------------------------- race kerbs
        var kerb = new MeshKit();
        var concrete = new MeshKit();
        for (int side = -1; side <= 1; side += 2)
        {
            float xi = side * hw, xo = side * (hw + KerbW);
            for (int i = 0; i + 1 < stations.Count; i++)
            {
                float s0 = stations[i], s1 = stations[i + 1];
                float h0 = HeightAt(s0), h1 = HeightAt(s1);
                // Ramp the kerb down over the last 1.5 m so it melts into the road ahead.
                float k0 = KerbH * Mathf.Clamp01((forward - s0) / 1.5f);
                float k1 = KerbH * Mathf.Clamp01((forward - s1) / 1.5f);
                Vector2 ua = new Vector2(0, (s0 + back) * 0.5f), ub = new Vector2(0, (s1 + back) * 0.5f);
                Vector2 uc = new Vector2(1, (s1 + back) * 0.5f), ud = new Vector2(1, (s0 + back) * 0.5f);
                // top (striped: 1 m vermilion, 1 m chalk)
                if (side > 0) kerb.Quad(P(s0, xi, h0 + k0), P(s0, xo, h0 + k0), P(s1, xo, h1 + k1), P(s1, xi, h1 + k1),
                                        ua, ud, uc, ub);
                else kerb.Quad(P(s0, xo, h0 + k0), P(s0, xi, h0 + k0), P(s1, xi, h1 + k1), P(s1, xo, h1 + k1),
                               ua, ud, uc, ub);
                // inner face (striped, faces the deck)
                if (side > 0) kerb.Quad(P(s1, xi, h1), P(s0, xi, h0), P(s0, xi, h0 + k0), P(s1, xi, h1 + k1),
                                        ub, ua, ua, ub);
                else kerb.Quad(P(s0, xi, h0), P(s1, xi, h1), P(s1, xi, h1 + k1), P(s0, xi, h0 + k0),
                               ua, ub, ub, ua);
                // outer face drops into the ground - this is what grounds the slab.
                float g0 = Drop(P(s0, xo + side * 0.3f, h0), deckY + h0);
                float g1 = Drop(P(s1, xo + side * 0.3f, h1), deckY + h1);
                if (side > 0) concrete.Quad(P(s0, xo, h0 + g0), P(s1, xo, h1 + g1), P(s1, xo, h1 + k1), P(s0, xo, h0 + k0));
                else concrete.Quad(P(s1, xo, h1 + g1), P(s0, xo, h0 + g0), P(s0, xo, h0 + k0), P(s1, xo, h1 + k1));
            }
            // forward end cap of the skirt, so the kerb has no open end facing the road ahead
            {
                float hf = HeightAt(forward);
                float gf = Drop(P(forward, xo + side * 0.3f, hf), deckY + hf);
                if (side > 0) concrete.Quad(P(forward, xo, hf + gf), P(forward, xi, hf + gf), P(forward, xi, hf), P(forward, xo, hf));
                else concrete.Quad(P(forward, xi, hf + gf), P(forward, xo, hf + gf), P(forward, xo, hf), P(forward, xi, hf));
            }
        }

        // back kerb closing the pen, and its skirt
        {
            float hb = HeightAt(-back);
            float sb = -back, so = -back - KerbW;
            float xl = -(hw + KerbW), xr = hw + KerbW;
            float span = (xr - xl) * 0.5f;
            kerb.Quad(P(so, xl, hb + KerbH), P(so, xr, hb + KerbH), P(sb, xr, hb + KerbH), P(sb, xl, hb + KerbH),
                      new Vector2(0, 0), new Vector2(0, span), new Vector2(1, span), new Vector2(1, 0));
            kerb.Quad(P(sb, xr, hb), P(sb, xl, hb), P(sb, xl, hb + KerbH), P(sb, xr, hb + KerbH),
                      new Vector2(0, span), new Vector2(0, 0), new Vector2(0.1f, 0), new Vector2(0.1f, span));
            int n = 6;
            for (int i = 0; i < n; i++)
            {
                float x0 = Mathf.Lerp(xl, xr, i / (float)n), x1 = Mathf.Lerp(xl, xr, (i + 1) / (float)n);
                float g0 = Drop(P(so - 0.3f, x0, hb), deckY + hb), g1 = Drop(P(so - 0.3f, x1, hb), deckY + hb);
                concrete.Quad(P(so, x0, hb + g0), P(so, x1, hb + g1), P(so, x1, hb + KerbH), P(so, x0, hb + KerbH));
            }
            float gl = Drop(P(so, xl - 0.3f, hb), deckY + hb), gr = Drop(P(so, xr + 0.3f, hb), deckY + hb);
            concrete.Quad(P(sb, xl, hb + gl), P(so, xl, hb + gl), P(so, xl, hb + KerbH), P(sb, xl, hb + KerbH));
            concrete.Quad(P(so, xr, hb + gr), P(sb, xr, hb + gr), P(sb, xr, hb + KerbH), P(so, xr, hb + KerbH));
        }
        MakePart("Race Kerbs", kerb, KerbMat(), false);

        // ---------------------------------------------------------------- start gantry
        var navy = new MeshKit();
        var banner = new MeshKit();
        var steel = new MeshKit();
        float hLine = HeightAt(0f);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 foot = P(0f, side * GantryX, hLine);
            float g = Drop(foot + R * side * 0.6f, foot.y);
            concrete.Box(foot, H, 0.55f, 0.55f, g, 0.42f);
            navy.Box(foot, H, 0.24f, 0.24f, 0.4f, 5.95f);
            // livery bands on the legs
            banner.Box(foot, H, 0.25f, 0.25f, 2.6f, 3.4f);
        }
        {
            Vector3 mid = P(0f, 0f, hLine);
            banner.Box(mid, H, 0.32f, GantryX + 0.28f, 4.55f, 5.95f, bannerFaces: true);
            navy.Box(mid, H, 0.36f, GantryX + 0.32f, 5.95f, 6.08f);
            navy.Box(mid, H, 0.36f, GantryX + 0.32f, 4.42f, 4.55f);
        }

        // ---------------------------------------------------------------- livery barriers
        var panels = new MeshKit();
        const float panelLen = 2.2f;
        // a = panel centre on the deck line, along = the run of the fence, outward = away from the deck.
        void Barrier(Vector3 a, Vector3 along, Vector3 outward)
        {
            float gy = Ground(a + outward * 0.1f, a.y);
            if (gy < a.y - 0.8f) gy = a.y - 0.05f;   // over water / a drop: stand it on the deck line
            Vector3 b = new Vector3(a.x, gy, a.z);
            for (int e = -1; e <= 1; e += 2)
            {
                Vector3 post = b + along * (e * panelLen * 0.5f);
                steel.Box(post, outward, 0.03f, 0.03f, -0.1f, 1.12f);
                steel.Box(post, outward, 0.32f, 0.03f, 0.0f, 0.05f);   // splayed foot
            }
            steel.Box(b, outward, 0.025f, panelLen * 0.5f, 1.07f, 1.12f);
            panels.Box(b, outward, 0.012f, panelLen * 0.5f - 0.04f, 0.14f, 1.03f, bannerFaces: true);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 outward = R * side;
            for (float s = -back + 1.3f; s + panelLen * 0.5f < -1.6f; s += panelLen + 0.08f)
                Barrier(P(s, side * BarrierX, HeightAt(s)), H, outward);
        }
        for (float x = -hw + 1.1f; x + panelLen * 0.5f <= hw + 0.05f; x += panelLen + 0.08f)
            Barrier(P(-back - KerbW - 0.45f, x, 0f), R, -H);

        MakePart("Kerb Skirt + Plinths", concrete, ConcreteMat(), true);
        MakePart("Gantry Frame", navy, NavyMat(), true);
        MakePart("Gantry Banner", banner, BannerMat(), true);
        MakePart("Barrier Frames", steel, SteelMat(), true);
        MakePart("Barrier Panels", panels, PanelMat(), true);
    }

    private void MakePart(string name, MeshKit kit, Material mat, bool shadows)
    {
        if (kit.V.Count == 0) return;
        var go = new GameObject(name);
        go.transform.SetParent(_pad.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh(name.Replace(" ", ""));
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
    }

    // ==================================================================== materials

    private const string RegionCelShader = "MapleRide/HDRP/CelLit";

    /// <summary>The region road's own cel material, when it has one (Maple City). The start
    /// pieces are cloned from it so they take the region's shade tint and cut sky ambient;
    /// plain HDRP/Lit took the full blue sky term there and the chalk paint read bright blue.</summary>
    private Material _celTemplate;

    private void AdoptRegionShading(Vector3 p0, float roadY)
    {
        Material found = null;
        float bestY = float.NegativeInfinity;
        var hits = Physics.RaycastAll(new Vector3(p0.x, roadY + 3f, p0.z), Vector3.down, 6f,
                                      surfaceMask, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (hit.point.y > roadY + 0.8f || hit.point.y < bestY) continue;
            var r = hit.collider.GetComponent<Renderer>();
            var m = r != null ? r.sharedMaterial : null;
            if (m != null && m.shader != null && m.shader.name == RegionCelShader) { found = m; bestY = hit.point.y; }
        }
        if (found == _celTemplate && _fsDeck != null) return;
        _celTemplate = found;
        _fsDeck = _fsKerb = _fsConcrete = _fsNavy = _fsBanner = _fsPanel = _fsSteel = null;
        Debug.Log($"[ride] finished start shading: {(found != null ? "region cel (" + found.name + ")" : "HDRP/Lit")}");
    }

    private Material LitMat(string name, Color c, float smooth, Texture2D tex = null, float metal = 0f)
    {
        Color baseCol = tex != null ? Color.white : c;
        if (_celTemplate != null)
        {
            var cm = new Material(_celTemplate) { name = name };
            cm.SetColor("_Color", baseCol);
            cm.SetTexture("_MainTex", tex != null ? (Texture)tex : Texture2D.whiteTexture);
            cm.SetTextureScale("_MainTex", Vector2.one);
            cm.SetTextureOffset("_MainTex", Vector2.zero);
            cm.SetFloat("_RimStrength", 0f);
            cm.SetFloat("_TintVariation", 0f);
            cm.SetFloat("_WeatherAmount", 0f);
            cm.SetFloat("_NormalStrength", 0f);
            cm.SetFloat("_Gloss", Mathf.Clamp(smooth, 0.05f, 0.6f));
            cm.SetFloat("_SpecStrength", 0.05f + metal * 0.3f);
            return cm;
        }
        var sh = Shader.Find("HDRP/Lit");
        var m = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
        m.name = name;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseCol);
        if (m.HasProperty("_Color")) m.SetColor("_Color", baseCol);
        if (tex != null)
        {
            if (m.HasProperty("_BaseColorMap")) m.SetTexture("_BaseColorMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        return m;
    }

    private Material DeckMat(float hw, float len)
    {
        if (_fsDeck != null) return _fsDeck;
        return _fsDeck = LitMat("StartDeck", Color.white, 0.14f, MakeDeckTexture(hw, len));
    }

    private Material KerbMat()
    {
        if (_fsKerb != null) return _fsKerb;
        const int px = 16;
        var tex = new Texture2D(4, px * 2, TextureFormat.RGBA32, true)
        { name = "StartKerbStripe", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
        var cols = new Color[4 * px * 2];
        for (int y = 0; y < px * 2; y++)
            for (int x = 0; x < 4; x++)
                cols[y * 4 + x] = y < px ? LvVermilion : LvChalk;
        tex.SetPixels(cols);
        tex.Apply(true, false);
        return _fsKerb = LitMat("StartKerb", Color.white, 0.22f, tex);
    }

    private Material ConcreteMat() =>
        _fsConcrete != null ? _fsConcrete : (_fsConcrete = LitMat("StartConcrete", new Color(0.58f, 0.57f, 0.55f), 0.1f));

    private Material NavyMat() =>
        _fsNavy != null ? _fsNavy : (_fsNavy = LitMat("StartGantryNavy", LvNavy, 0.35f, null, 0.2f));

    private Material SteelMat() =>
        _fsSteel != null ? _fsSteel : (_fsSteel = LitMat("StartBarrierSteel", new Color(0.72f, 0.73f, 0.75f), 0.45f, null, 0.6f));

    private Material BannerMat()
    {
        if (_fsBanner != null) return _fsBanner;
        return _fsBanner = LitMat("StartGantryBanner", Color.white, 0.3f, MakeBanner(1536, 204, "MAPLE RIDE", 15, true));
    }

    private Material PanelMat()
    {
        if (_fsPanel != null) return _fsPanel;
        return _fsPanel = LitMat("StartBarrierPanel", Color.white, 0.3f, MakeBanner(512, 208, "MAPLE RIDE", 7, false));
    }

    // ==================================================================== textures

    private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
        ['D'] = new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
        ['E'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
        ['I'] = new[] { "01110", "00100", "00100", "00100", "00100", "00100", "01110" },
        ['L'] = new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
        ['M'] = new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" },
        ['P'] = new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
        ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
        ['S'] = new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
        ['T'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
    };

    /// <summary>True when (gx, gy) - in glyph cells, y down from the top - is ink.</summary>
    private static bool Ink(string text, float gx, float gy)
    {
        if (gy < 0f || gy >= 7f || gx < 0f) return false;
        int ch = Mathf.FloorToInt(gx / 6f);
        if (ch >= text.Length) return false;
        int cx = Mathf.FloorToInt(gx - ch * 6f);
        if (cx >= 5) return false;
        if (!Glyphs.TryGetValue(text[ch], out var g)) return false;
        return g[Mathf.FloorToInt(gy)][cx] == '1';
    }

    private static Texture2D MakeBanner(int w, int h, string text, int cell, bool leaves)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
        { name = "StartBanner_" + w, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        var cols = new Color[w * h];
        int band = Mathf.RoundToInt(h * 0.17f), gold = Mathf.Max(2, h / 40);
        float textW = (text.Length * 6f - 1f) * cell, textH = 7f * cell;
        float tx0 = (w - textW) * 0.5f;
        float ty0 = band + (h - band - textH) * 0.5f;   // pixel rows measured from the bottom
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = LvNavy;
                if (y < band) c = LvVermilion;
                else if (y < band + gold) c = LvGold;
                else if (y >= h - gold) c = LvGold;
                // 2x2 supersampled glyph coverage for soft edges
                float cov = 0f;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float fx = (x + 0.25f + 0.5f * sx - tx0) / cell;
                        float fy = 7f - (y + 0.25f + 0.5f * sy - ty0) / cell;
                        if (Ink(text, fx, fy)) cov += 0.25f;
                    }
                c = Color.Lerp(c, LvChalk, cov);
                if (leaves && y >= band + gold)
                {
                    // gold diamonds either side of the wordmark
                    float cy = ty0 + textH * 0.5f, r = textH * 0.42f;
                    float dl = Mathf.Abs(x - (tx0 - cell * 4f)) + Mathf.Abs(y - cy);
                    float dr = Mathf.Abs(x - (tx0 + textW + cell * 4f)) + Mathf.Abs(y - cy);
                    if (dl < r || dr < r) c = LvGold;
                    if (dl < r * 0.45f || dr < r * 0.45f) c = LvVermilion;
                }
                cols[y * w + x] = c;
            }
        tex.SetPixels(cols);
        tex.Apply(true, false);
        return tex;
    }

    private static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }

    /// <summary>
    /// The whole deck in one texture: u runs across the deck (-hw..hw), v along it
    /// (-back..forward). Asphalt grain, edge lines, the checkered line, grid slots and START.
    /// </summary>
    private Texture2D MakeDeckTexture(float hw, float len)
    {
        const int W = 512;
        int Hh = Mathf.Clamp(Mathf.RoundToInt(W * len / (2f * hw)), 256, 2048);
        var tex = new Texture2D(W, Hh, TextureFormat.RGBA32, true)
        { name = "StartDeckPaint", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        var cols = new Color[W * Hh];
        var paint = new Color(0.86f, 0.86f, 0.84f);
        var dark = new Color(0.05f, 0.05f, 0.06f);
        const float checkerHalf = 0.65f;
        int cellsX = 16;
        float cellW = 2f * EdgeLineX / cellsX;
        float cellD = 2f * checkerHalf / 3f;
        for (int py = 0; py < Hh; py++)
        {
            float s = -back + (py + 0.5f) / Hh * len;
            for (int px = 0; px < W; px++)
            {
                float x = -hw + (px + 0.5f) / W * 2f * hw;
                // asphalt: fine grain + soft 0.5 m blotches
                float g = Hash(px, py) * 0.05f - 0.025f;
                float bl = Hash(Mathf.FloorToInt((x + 10f) * 2f), Mathf.FloorToInt((s + 50f) * 2f)) * 0.03f;
                float a = 0.165f + g + bl;
                Color c = new Color(a, a, a * 1.04f);
                float ax = Mathf.Abs(x);

                // edge lines
                if (ax > EdgeLineX - 0.075f && ax < EdgeLineX + 0.075f) c = paint;

                // checkered start line bordered by solid white bars
                if (ax < EdgeLineX + 0.075f)
                {
                    if (Mathf.Abs(s) < checkerHalf)
                    {
                        int cx = Mathf.FloorToInt((x + EdgeLineX) / cellW);
                        int cz = Mathf.FloorToInt((s + checkerHalf) / cellD);
                        c = ((cx + cz) & 1) == 0 ? paint : dark;
                    }
                    else if (Mathf.Abs(s) < checkerHalf + 0.14f) c = paint;
                }

                // staggered grid slots behind the line: a bar plus a short return
                for (int k = 0; k < 4; k++)
                {
                    float gs = -5.2f - k * 4f;
                    float cxs = (k & 1) == 0 ? -1.75f : 1.75f;
                    bool bar = Mathf.Abs(s - gs) < 0.07f && Mathf.Abs(x - cxs) < 0.85f;
                    bool ret = Mathf.Abs(x - (cxs - 0.85f)) < 0.07f && s > gs - 0.6f && s < gs;
                    bool ret2 = Mathf.Abs(x - (cxs + 0.85f)) < 0.07f && s > gs - 0.6f && s < gs;
                    if (bar || ret || ret2) c = paint;
                }

                // START in road paint (letters elongated 2.2x along travel, like real road text)
                {
                    const float letterW = 0.62f, textS0 = -3.9f, textS1 = -1.5f;
                    float totalW = (5f * 6f - 1f) / 5f * letterW;
                    float gx = (x + totalW * 0.5f) / letterW * 5f;
                    float gy = (textS1 - s) / (textS1 - textS0) * 7f;
                    if (Ink("START", gx, gy)) c = paint;
                }
                cols[py * W + px] = c;
            }
        }
        tex.SetPixels(cols);
        tex.Apply(true, false);
        return tex;
    }
}
