using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// MAPLE CITY - VAPORWAVE NEO-TOKYO LAYER (user, 2026-10-03: "vapor wave and more intricate details of the town. And a good
/// background. Future Tokyo vibes"). Built for BOTH districts with the same code so the whole city reads as one place:
///   * blade signs   - the 32-tile Japanese/English neon atlas on vertical boards at facade height, each framed in neon tube;
///   * holograms     - animated additive billboards on pylons arching over the pavement;
///   * maglev        - an elevated viaduct above the boulevard (portal frames every 36 m, glowing edge tubes) with a looping
///                     four-car train (MapleCityMaglevTrain);
///   * megatower     - a 330 m hero tower with neon rings and an atlas plate, between the two districts (the "background");
///   * airships      - slow orbiting blimps with lit gondolas and atlas plates (MapleCityAirship).
/// Same helpers (Box/Finish/AddMesh/RoadY/GroundHeight/CarriagewayHalfWidth) and the same CelMaterial look as the rest of the
/// city; emissive parts use the project's own HDR "Unlit" pattern (MapleCity_WindowGlow) so they bloom exactly like the existing
/// windows and lanterns. Meshes persist under Neo_ / EastNeo_ names so nothing existing is overwritten.
/// </summary>
public static partial class MapleCityEnvironment
{
    public const string NeoGroupName = "Maple City Neo Layer";
    private const string NeoAtlasPath = "Assets/Resources/ShuntaMetro/City/NeoTokyoSigns.png";
    private const string NeoSignShader = "MapleRide/City/NeonUnlit";
    private const string NeoHoloShader = "MapleRide/City/HologramAdd";

    private static Material NeoTube(string name, Color hdr)
    {
        var m = LoadOrCreate(name, "Unlit/Color");
        m.SetColor("_Color", hdr);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material NeoSignMat()
    {
        var m = LoadOrCreate("MapleNeo_Signs", NeoSignShader);
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(NeoAtlasPath));
        m.SetColor("_Color", new Color(2.3f, 2.3f, 2.3f, 1f));
        m.SetFloat("_Flicker", 0.18f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material NeoHoloMat(string name, Color tint)
    {
        var m = LoadOrCreate(name, NeoHoloShader);
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(NeoAtlasPath));
        m.SetColor("_Color", tint);
        m.SetFloat("_Scan", 0.35f);
        m.SetFloat("_Glitch", 0.5f);
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Rect NeoTile(int tile)
    {
        int col = tile % 8, row = (tile / 8) % 4;
        const float inset = 0.0015f;
        return new Rect(col * 0.125f + inset, (3 - row) * 0.25f + inset, 0.125f - 2 * inset, 0.25f - 2 * inset);
    }

    /// <summary>Two-sided atlas quad (the back face is mirrored so lettering stays correct from behind).</summary>
    private static void NeoQuad(List<Vector3> v, List<Vector2> uv, List<Vector2> uv2, List<int> tri,
                                Vector3 centre, Vector3 right, Vector3 up, Rect r, bool flip = false)
    {
        // The two faces' winding flips with the handedness of (right, up). flip = true swaps which face is mirrored, so the face
        // the VIEWER actually sees always reads left-to-right (signs on the +side of the road, tower plates, one airship flank).
        float uL = flip ? r.xMax : r.xMin, uR = flip ? r.xMin : r.xMax;      // Rect normalises negative widths, so use explicit U values
        int b = v.Count;
        v.Add(centre - right - up); v.Add(centre + right - up); v.Add(centre - right + up); v.Add(centre + right + up);
        uv.Add(new Vector2(uL, r.yMin)); uv.Add(new Vector2(uR, r.yMin)); uv.Add(new Vector2(uL, r.yMax)); uv.Add(new Vector2(uR, r.yMax));
        uv2.Add(new Vector2(0, 0)); uv2.Add(new Vector2(1, 0)); uv2.Add(new Vector2(0, 1)); uv2.Add(new Vector2(1, 1));
        tri.Add(b); tri.Add(b + 2); tri.Add(b + 1); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);
        int c = v.Count;
        v.Add(centre - right - up); v.Add(centre + right - up); v.Add(centre - right + up); v.Add(centre + right + up);
        uv.Add(new Vector2(uR, r.yMin)); uv.Add(new Vector2(uL, r.yMin)); uv.Add(new Vector2(uR, r.yMax)); uv.Add(new Vector2(uL, r.yMax));
        uv2.Add(new Vector2(1, 0)); uv2.Add(new Vector2(0, 0)); uv2.Add(new Vector2(1, 1)); uv2.Add(new Vector2(0, 1));
        tri.Add(c); tri.Add(c + 1); tri.Add(c + 2); tri.Add(c + 1); tri.Add(c + 3); tri.Add(c + 2);
    }

    private sealed class NeoBag
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Vector2> UV2 = new List<Vector2>();
        public readonly List<int> T = new List<int>();
        public bool Any => V.Count > 0;
    }

    private static void NeoFlush(Transform group, string name, NeoBag bag, Material mat, bool shadows = false)
    {
        if (!bag.Any) return;
        while (bag.UV2.Count < bag.V.Count) bag.UV2.Add(Vector2.zero);
        var go = AddMesh(group, name, Finish(NeoMeshName(name), bag.V.ToArray(), bag.UV.ToArray(), bag.T, bag.UV2.ToArray()), mat, collider: false);
        go.GetComponent<MeshRenderer>().shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static string NeoMeshName(string n) => "MapleNeo_" + n.Replace(' ', '_');

    private static void NeoTube(NeoBag bag, Vector3 a, Vector3 b, float thickness)
    {
        // a thin square bar between two points, built with the shared Box helper (right/back are HALF extents)
        var d = b - a; float len = d.magnitude; if (len < 0.01f) return;
        var dir = d / len;
        var side = Vector3.Cross(Vector3.up, dir); if (side.sqrMagnitude < 1e-4f) side = Vector3.right; side.Normalize();
        var mid = (a + b) * 0.5f;
        // Box rises in world up from "centre"; for arbitrary bars use two crossed quads' worth: keep it simple and axis-aware.
        if (Mathf.Abs(dir.y) > 0.9f)
            Box(bag.V, bag.UV, bag.T, null, a, side * thickness, Vector3.Cross(side, Vector3.up) * thickness, len, default);
        else
            Box(bag.V, bag.UV, bag.T, null, mid, dir * (len * 0.5f), side * thickness, thickness * 2f, default, centredY: true);
    }

    // =================================================================== entry

    /// <summary>Builds the vaporwave layer for one district under <paramref name="parent"/>. tag: "Old" or "East".</summary>
    public static void BuildNeoLayer(Transform parent, CityRoute route, string tag)
    {
        if (parent == null || route == null || route.Count < 4) return;
        for (int k = parent.childCount - 1; k >= 0; k--)
            if (parent.GetChild(k).name == NeoGroupName) UnityEngine.Object.DestroyImmediate(parent.GetChild(k).gameObject);
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(NeoAtlasPath);
        if (atlas == null) { Debug.LogWarning($"[maple-neo] sign atlas missing: {NeoAtlasPath}"); return; }

        string prevPrefix = MeshPrefix;
        MeshPrefix = (tag == "East" ? "East_" : "") + "Neo_";
        var group = new GameObject(NeoGroupName).transform;
        group.SetParent(parent, false);
        try
        {
            int seed = tag == "East" ? 7771 : 4421;
            BuildNeoSigns(group, route, tag, seed);
            BuildNeoHolograms(group, route, tag, seed + 1);
            BuildNeoMaglev(group, route, tag);
            BuildNeoTrainLines(group, route, tag);
            if (tag == "East") BuildNeoBackdrop(group, route);
        }
        finally { MeshPrefix = prevPrefix; }
        Debug.Log($"[maple-neo] {tag}: layer built, {group.GetComponentsInChildren<MeshRenderer>(true).Length} renderers.");
    }

    // =================================================================== blade signs + neon frames

    private static bool NeoSkip(CityRoute route, float d, int side, string tag)
    {
        float frac = d / Mathf.Max(1f, route.Length);
        if (tag == "Old" && d > MapleRowBoutiques.StreetStartM - 20f && d < MapleRowBoutiques.StreetEndM + 20f) return true;     // Maple Row has its own dressing
        if (side < 0 && frac > CanalFromFrac - 0.012f && frac < CanalToFrac + 0.012f) return true;                              // canal side: no facades
        if (tag == "East") foreach (var ko in EastKeepOut) if (d > ko.x && d < ko.y) return false;                               // landmark zones keep their signs
        return false;
    }

    private static void BuildNeoSigns(Transform group, CityRoute route, string tag, int seed)
    {
        var rng = new System.Random(seed);
        var signs = new NeoBag();
        var pink = new NeoBag(); var cyan = new NeoBag(); var amber = new NeoBag(); var violet = new NeoBag(); var bracket = new NeoBag();
        int count = 0;
        for (int side = -1; side <= 1; side += 2)
        {
            float d = 12f + (side > 0 ? 9f : 0f);
            while (d < route.Length - 12f)
            {
                d += Mathf.Lerp(15f, 33f, (float)rng.NextDouble());
                if (NeoSkip(route, d, side, tag)) continue;
                int i = route.IndexAt(d);
                float frac = route.Frac(i);
                var dis = DistrictAt(frac);
                float half = CarriagewayHalfWidth(frac);
                float front = half + PavementWidthM + dis.Setback;
                Vector3 p = route.Position[i];
                Vector3 s = route.SideFlat(i) * side;                                   // lateral, pointing away from the road on this side
                Vector3 t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
                float ground = Mathf.Max(GroundHeight(route, i, side * front), RoadY(route, i, side * half) + KerbHeightM);
                float w = Mathf.Lerp(1.7f, 2.9f, (float)rng.NextDouble());             // tiles are square: w x w
                float yc = ground + Mathf.Lerp(5.4f, 12.5f, (float)rng.NextDouble());
                float lat = front - 0.25f - w * 0.5f;                                   // protrudes from the facade toward the street
                Vector3 c = new Vector3(p.x, 0f, p.z) + s * lat; c.y = yc;
                int tile = rng.Next(32);
                NeoQuad(signs.V, signs.UV, signs.UV2, signs.T, c, s * (w * 0.5f), Vector3.up * (w * 0.5f), NeoTile(tile), flip: side > 0);

                // neon frame in the tile's own outline colour family (pink / cyan / amber / lavender by atlas column)
                var tube = (tile % 4) switch { 0 => pink, 1 => cyan, 2 => amber, _ => violet };
                float th = 0.06f;
                // the sign plane is perpendicular to the street: it spans lateral (s) x vertical; tubes sit on its edges, offset along t
                Vector3 tl = c - s * (w * 0.5f) + Vector3.up * (w * 0.5f), tr = c + s * (w * 0.5f) + Vector3.up * (w * 0.5f);
                Vector3 bl = c - s * (w * 0.5f) - Vector3.up * (w * 0.5f), br = c + s * (w * 0.5f) - Vector3.up * (w * 0.5f);
                NeoTube(tube, tl, tr, th); NeoTube(tube, bl, br, th); NeoTube(tube, bl, tl, th); NeoTube(tube, br, tr, th);
                // facade bracket: a dark arm back to the building line
                NeoTube(bracket, c + Vector3.up * (w * 0.5f) + s * (w * 0.5f), c + Vector3.up * (w * 0.5f) + s * (w * 0.5f + 0.35f), 0.05f);
                NeoTube(bracket, c - Vector3.up * (w * 0.5f) + s * (w * 0.5f), c - Vector3.up * (w * 0.5f) + s * (w * 0.5f + 0.35f), 0.05f);
                count++;
            }
        }
        NeoFlush(group, "Blade Signs", signs, NeoSignMat());
        NeoFlush(group, "Neon Pink", pink, NeoTube("MapleNeo_TubePink", new Color(2.3f, 0.30f, 1.15f, 1f)));
        NeoFlush(group, "Neon Cyan", cyan, NeoTube("MapleNeo_TubeCyan", new Color(0.30f, 1.80f, 2.30f, 1f)));
        NeoFlush(group, "Neon Amber", amber, NeoTube("MapleNeo_TubeAmber", new Color(2.3f, 1.35f, 0.35f, 1f)));
        NeoFlush(group, "Neon Violet", violet, NeoTube("MapleNeo_TubeViolet", new Color(1.1f, 0.55f, 2.3f, 1f)));
        NeoFlush(group, "Sign Brackets", bracket, CelMaterial("MapleCity_NeoBracket", new Color(0.16f, 0.16f, 0.22f), gloss: 0.3f, spec: 0.3f, rim: 0.4f));
        Debug.Log($"[maple-neo] {tag}: {count} blade signs.");
    }

    // =================================================================== hologram billboards

    private static void BuildNeoHolograms(Transform group, CityRoute route, string tag, int seed)
    {
        var rng = new System.Random(seed);
        var cyanH = new NeoBag(); var pinkH = new NeoBag(); var pylons = new NeoBag();
        int count = 0, side = 1;
        for (float d = 70f; d < route.Length - 60f; d += Mathf.Lerp(95f, 150f, (float)rng.NextDouble()))
        {
            side = -side;
            if (NeoSkip(route, d, side, tag)) continue;
            if (tag == "Old" && d > MapleRowBoutiques.StreetStartM - 40f && d < MapleRowBoutiques.StreetEndM + 40f) continue;
            int i = route.IndexAt(d);
            float frac = route.Frac(i);
            float half = CarriagewayHalfWidth(frac);
            Vector3 p = route.Position[i];
            Vector3 s = route.SideFlat(i) * side;
            Vector3 t = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
            float latPylon = half + PavementWidthM - 0.55f;
            float baseY = Mathf.Max(GroundHeight(route, i, side * latPylon), RoadY(route, i, side * half) + KerbHeightM);
            Vector3 foot = new Vector3(p.x, baseY, p.z) + s * latPylon;
            const float H = 15.5f;
            Box(pylons.V, pylons.UV, pylons.T, null, foot, t * 0.26f, s * 0.26f, H, default);
            Box(pylons.V, pylons.UV, pylons.T, null, foot + Vector3.up * (H - 0.4f) - s * 2.0f, t * 0.16f, s * 2.1f, 0.34f, default);      // arm toward the street
            float w = Mathf.Lerp(8f, 11f, (float)rng.NextDouble()), h = w * 0.5f;
            Vector3 c = foot + Vector3.up * (H + h * 0.5f + 0.6f) - s * (w * 0.5f + 0.5f);
            int tile = rng.Next(32);
            // wide panels: stretch the square tile over a 2:1 panel by using two adjacent tiles' span when possible
            var tileRect = NeoTile(tile);
            var r2 = new Rect(tileRect.x, tileRect.y, tileRect.width, tileRect.height);
            var bag = (tile % 2 == 0) ? cyanH : pinkH;
            NeoQuad(bag.V, bag.UV, bag.UV2, bag.T, c, s * (w * 0.5f), Vector3.up * (h * 0.5f), r2, flip: side > 0);
            count++;
        }
        NeoFlush(group, "Hologram Pylons", pylons, CelMaterial("MapleCity_NeoBracket", new Color(0.16f, 0.16f, 0.22f), gloss: 0.3f, spec: 0.3f, rim: 0.4f), shadows: true);
        NeoFlush(group, "Holograms Cyan", cyanH, NeoHoloMat("MapleNeo_HoloCyan", new Color(0.35f, 1.55f, 2.1f, 1f)));
        NeoFlush(group, "Holograms Pink", pinkH, NeoHoloMat("MapleNeo_HoloPink", new Color(2.1f, 0.45f, 1.5f, 1f)));
        Debug.Log($"[maple-neo] {tag}: {count} hologram billboards.");
    }

    // =================================================================== maglev viaduct + train

    private static void BuildNeoMaglev(Transform group, CityRoute route, string tag)
    {
        const float Lift = 15.0f;                  // deck centre above the route (clears trams, lanterns, canopies)
        var deck = new NeoBag(); var struc = new NeoBag(); var tubeA = new NeoBag(); var tubeB = new NeoBag();
        int n = route.Count;
        for (int i = 0; i + 2 < n; i += 2)
        {
            Vector3 a = route.Position[i], b = route.Position[Mathf.Min(i + 2, n - 1)];
            Vector3 mid = (a + b) * 0.5f + Vector3.up * Lift;
            Vector3 dir = new Vector3(b.x - a.x, 0f, b.z - a.z); float len = dir.magnitude; if (len < 0.5f) continue; dir /= len;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            float tilt = (b.y - a.y);
            Vector3 along = (b - a).normalized;
            // deck slab (half extents: lateral 2.9 m, along len/2 + overlap), 1.0 m thick
            Box(deck.V, deck.UV, deck.T, null, mid - Vector3.up * 0.5f, side * 2.9f, along * (len * 0.5f + 0.45f), 1.0f, default);
            // glowing edge tubes under both deck edges, alternating colour every ~180 m
            var tb = ((int)(route.Distance[i] / 180f) % 2 == 0) ? tubeA : tubeB;
            Box(tb.V, tb.UV, tb.T, null, mid - Vector3.up * 0.62f + side * 2.95f, side * 0.07f, along * (len * 0.5f + 0.3f), 0.14f, default);
            Box(tb.V, tb.UV, tb.T, null, mid - Vector3.up * 0.62f - side * 2.95f, side * 0.07f, along * (len * 0.5f + 0.3f), 0.14f, default);
            // guard rails
            Box(deck.V, deck.UV, deck.T, null, mid + Vector3.up * 0.5f + side * 2.75f, side * 0.1f, along * (len * 0.5f + 0.4f), 0.9f, default);
            Box(deck.V, deck.UV, deck.T, null, mid + Vector3.up * 0.5f - side * 2.75f, side * 0.1f, along * (len * 0.5f + 0.4f), 0.9f, default);
            // portal frame every 36 m: two columns at the pavement edges + a cross beam under the deck + struts
            if (i % 12 == 0)
            {
                float frac = route.Frac(i);
                float half = CarriagewayHalfWidth(frac);
                float lat = half + PavementWidthM - 0.45f;
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Vector3 sd = route.SideFlat(i) * sg;
                    float by = Mathf.Max(GroundHeight(route, i, sg * lat), RoadY(route, i, sg * half) + KerbHeightM);
                    Vector3 foot = new Vector3(a.x, by, a.z) + sd * lat;
                    float colH = (a.y + Lift - 1.4f) - by;
                    Box(struc.V, struc.UV, struc.T, null, foot, dir * 0.55f, sd * 0.55f, colH, default);
                    Box(struc.V, struc.UV, struc.T, null, foot + Vector3.up * (colH - 0.2f), dir * 0.8f, sd * 0.8f, 0.5f, default);
                }
                Vector3 centreFoot = new Vector3(a.x, a.y + Lift - 1.9f, a.z);
                Box(struc.V, struc.UV, struc.T, null, centreFoot, dir * 0.6f, Vector3.Cross(Vector3.up, dir) * lat, 0.9f, default);   // cross beam
                for (int sg = -1; sg <= 1; sg += 2)
                    Box(struc.V, struc.UV, struc.T, null, centreFoot + Vector3.Cross(Vector3.up, dir) * (sg * 2.2f), dir * 0.35f, Vector3.Cross(Vector3.up, dir) * 0.35f, 1.4f, default);
            }
        }
        NeoFlush(group, "Maglev Deck", deck, CelMaterial("MapleCity_MaglevDeck", new Color(0.30f, 0.30f, 0.38f), gloss: 0.34f, spec: 0.3f, rim: 0.5f), shadows: true);
        NeoFlush(group, "Maglev Structure", struc, CelMaterial("MapleCity_MaglevPylon", new Color(0.42f, 0.44f, 0.50f), gloss: 0.18f, spec: 0.12f, rim: 0.4f), shadows: true);
        NeoFlush(group, "Maglev Glow A", tubeA, NeoTube("MapleNeo_TubePink", new Color(2.3f, 0.30f, 1.15f, 1f)));
        NeoFlush(group, "Maglev Glow B", tubeB, NeoTube("MapleNeo_TubeCyan", new Color(0.30f, 1.80f, 2.30f, 1f)));

        // ---- train: four cars sharing one set of meshes, driven by MapleCityMaglevTrain along the rail
        var pathList = new List<Vector3>(); var cum = new List<float>();
        float acc = 0f;
        for (int i = 0; i < n; i++)
        {
            var p = route.Position[i] + Vector3.up * (Lift + 1.55f);
            if (i > 0) acc += Vector3.Distance(pathList[pathList.Count - 1], p);
            pathList.Add(p); cum.Add(acc);
        }
        var trainRoot = new GameObject("Maglev Train").transform;
        trainRoot.SetParent(group, false);
        var car0 = new GameObject("Car 0").transform;
        car0.SetParent(trainRoot, false);
        var body = new NeoBag(); var windows = new NeoBag(); var stripe = new NeoBag();
        Box(body.V, body.UV, body.T, null, Vector3.zero, Vector3.right * 1.45f, Vector3.forward * 4.7f, 2.7f, default);                     // hull
        Box(body.V, body.UV, body.T, null, Vector3.up * 2.7f, Vector3.right * 1.1f, Vector3.forward * 4.3f, 0.35f, default);               // roof cap
        Box(body.V, body.UV, body.T, null, Vector3.forward * 4.9f + Vector3.up * 0.25f, Vector3.right * 1.2f, Vector3.forward * 0.4f, 2.0f, default);   // nose block
        Box(windows.V, windows.UV, windows.T, null, new Vector3(1.47f, 1.25f, 0f), Vector3.forward * 4.0f, Vector3.right * 0.02f, 0.85f, default, centredY: true);
        Box(windows.V, windows.UV, windows.T, null, new Vector3(-1.47f, 1.25f, 0f), Vector3.forward * 4.0f, Vector3.right * 0.02f, 0.85f, default, centredY: true);
        Box(stripe.V, stripe.UV, stripe.T, null, new Vector3(1.47f, 0.55f, 0f), Vector3.forward * 4.7f, Vector3.right * 0.02f, 0.12f, default, centredY: true);
        Box(stripe.V, stripe.UV, stripe.T, null, new Vector3(-1.47f, 0.55f, 0f), Vector3.forward * 4.7f, Vector3.right * 0.02f, 0.12f, default, centredY: true);
        NeoFlush(car0, "Car Body", body, CelMaterial("MapleCity_MaglevBody", new Color(0.88f, 0.86f, 0.96f), gloss: 0.5f, spec: 0.45f, rim: 0.6f), shadows: true);
        NeoFlush(car0, "Car Windows", windows, NeoTube("MapleNeo_TrainWindow", new Color(0.7f, 1.6f, 2.2f, 1f)));
        NeoFlush(car0, "Car Stripe", stripe, NeoTube("MapleNeo_TubePink", new Color(2.3f, 0.30f, 1.15f, 1f)));
        var cars = new List<Transform> { car0 };
        for (int k = 1; k < 4; k++)
        {
            var c = UnityEngine.Object.Instantiate(car0.gameObject, trainRoot);
            c.name = "Car " + k; cars.Add(c.transform);
        }
        var train = trainRoot.gameObject.AddComponent<MapleCityMaglevTrain>();
        train.path = pathList.ToArray(); train.cumulative = cum.ToArray(); train.cars = cars.ToArray();
        train.speed = tag == "East" ? 24f : 27f; train.startOffset = tag == "East" ? 600f : 1500f;
        train.Apply(0f);
    }

    // =================================================================== backdrop: megatower + airships

    private static void BuildNeoBackdrop(Transform group, CityRoute eastRoute)
    {
        var oldRoute = CityRoute.Load();
        Vector3 Centroid(CityRoute r) { var c = Vector3.zero; for (int i = 0; i < r.Count; i++) c += r.Position[i]; return c / r.Count; }
        Vector3 mid = (Centroid(oldRoute) + Centroid(eastRoute)) * 0.5f;
        float baseY = BasinY - 2f;

        // ---- megatower: tapered tower with ribs, neon rings, atlas plates, crown and spire
        var shell = new NeoBag(); var ribs = new NeoBag(); var ringA = new NeoBag(); var ringB = new NeoBag(); var plates = new NeoBag(); var beacon = new NeoBag();
        const int Seg = 28; const float Hgt = 330f, R0 = 30f, R1 = 15f;
        int levels = 12;
        for (int L = 0; L < levels; L++)
        {
            float t0 = L / (float)levels, t1 = (L + 1) / (float)levels;
            float y0 = baseY + Hgt * t0, y1 = baseY + Hgt * t1;
            float r0 = Mathf.Lerp(R0, R1, t0 * t0), r1 = Mathf.Lerp(R0, R1, t1 * t1);
            for (int sIdx = 0; sIdx < Seg; sIdx++)
            {
                float a0 = 2f * Mathf.PI * sIdx / Seg, a1 = 2f * Mathf.PI * (sIdx + 1) / Seg;
                Vector3 p00 = mid + new Vector3(Mathf.Cos(a0) * r0, 0, Mathf.Sin(a0) * r0), p10 = mid + new Vector3(Mathf.Cos(a1) * r0, 0, Mathf.Sin(a1) * r0);
                Vector3 p01 = mid + new Vector3(Mathf.Cos(a0) * r1, 0, Mathf.Sin(a0) * r1), p11 = mid + new Vector3(Mathf.Cos(a1) * r1, 0, Mathf.Sin(a1) * r1);
                p00.y = y0; p10.y = y0; p01.y = y1; p11.y = y1;
                int b = shell.V.Count;
                shell.V.Add(p00); shell.V.Add(p10); shell.V.Add(p01); shell.V.Add(p11);
                shell.UV.Add(new Vector2(0, 0)); shell.UV.Add(new Vector2(1, 0)); shell.UV.Add(new Vector2(0, 1)); shell.UV.Add(new Vector2(1, 1));
                shell.T.Add(b); shell.T.Add(b + 2); shell.T.Add(b + 1); shell.T.Add(b + 1); shell.T.Add(b + 2); shell.T.Add(b + 3);
            }
        }
        for (int sIdx = 0; sIdx < Seg; sIdx += 4)                                       // vertical neon ribs
        {
            float a = 2f * Mathf.PI * sIdx / Seg;
            for (int L = 0; L < levels; L++)
            {
                float t0 = L / (float)levels, t1 = (L + 1) / (float)levels;
                float r0 = Mathf.Lerp(R0, R1, t0 * t0) + 0.3f, r1 = Mathf.Lerp(R0, R1, t1 * t1) + 0.3f;
                Vector3 a0 = mid + new Vector3(Mathf.Cos(a) * r0, 0, Mathf.Sin(a) * r0); a0.y = baseY + Hgt * t0;
                Vector3 a1 = mid + new Vector3(Mathf.Cos(a) * r1, 0, Mathf.Sin(a) * r1); a1.y = baseY + Hgt * t1;
                NeoTube(ribs, a0, a1, 0.28f);
            }
        }
        float[] ringT = { 0.34f, 0.5f, 0.64f, 0.76f };
        for (int ri = 0; ri < ringT.Length; ri++)
        {
            float t = ringT[ri];
            float r = Mathf.Lerp(R0, R1, t * t) + 5.5f + ri * 1.5f, y = baseY + Hgt * t;
            var bag = (ri % 2 == 0) ? ringA : ringB;
            const int Rs = 56;
            for (int k = 0; k < Rs; k++)
            {
                float a0 = 2f * Mathf.PI * k / Rs, a1 = 2f * Mathf.PI * (k + 1) / Rs;
                Vector3 p0 = mid + new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r), p1 = mid + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
                p0.y = y; p1.y = y;
                NeoTube(bag, p0, p1, 0.55f);
            }
            // struts from tower to ring
            for (int k = 0; k < 4; k++)
            {
                float a = 2f * Mathf.PI * k / 4f + ri;
                Vector3 inner = mid + new Vector3(Mathf.Cos(a) * (r - 5.5f), 0, Mathf.Sin(a) * (r - 5.5f)); inner.y = y;
                Vector3 outer = mid + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r); outer.y = y;
                NeoTube(ribs, inner, outer, 0.3f);
            }
        }
        // big atlas plates on four faces (NEO TOKYO / MIRAI / TOKYO BAY / SKY LOUNGE)
        int[] plateTiles = { 0, 7, 16, 21 };
        for (int k = 0; k < 4; k++)
        {
            float a = 2f * Mathf.PI * k / 4f + Mathf.PI / 4f;
            float t = 0.42f; float r = Mathf.Lerp(R0, R1, t * t) + 0.6f;
            Vector3 c = mid + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r); c.y = baseY + Hgt * t;
            Vector3 outward = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Vector3 tang = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
            NeoQuad(plates.V, plates.UV, plates.UV2, plates.T, c + outward * 0.2f, tang * 15f, Vector3.up * 15f, NeoTile(plateTiles[k]), flip: true);
        }
        // crown + spire + beacon
        Box(shell.V, shell.UV, shell.T, null, new Vector3(mid.x, baseY + Hgt, mid.z), Vector3.right * 9.5f, Vector3.forward * 9.5f, 7f, default);
        NeoTube(ribs, new Vector3(mid.x, baseY + Hgt + 7f, mid.z), new Vector3(mid.x, baseY + Hgt + 52f, mid.z), 0.9f);
        Box(beacon.V, beacon.UV, beacon.T, null, new Vector3(mid.x, baseY + Hgt + 50f, mid.z), Vector3.right * 1.6f, Vector3.forward * 1.6f, 3.2f, default);
        // plaza disc at the foot
        Box(ribs.V, ribs.UV, ribs.T, null, new Vector3(mid.x, baseY - 1f, mid.z), Vector3.right * 62f, Vector3.forward * 62f, 2.2f, default);

        var tower = new GameObject("Neo Tokyo Megatower").transform;
        tower.SetParent(group, false);
        NeoFlush(tower, "Tower Shell", shell, CelMaterial("MapleNeo_TowerGlass", new Color(0.16f, 0.17f, 0.34f), gloss: 0.7f, spec: 0.6f, rim: 0.9f), shadows: false);
        NeoFlush(tower, "Tower Ribs", ribs, CelMaterial("MapleCity_MaglevPylon", new Color(0.42f, 0.44f, 0.50f), gloss: 0.18f, spec: 0.12f, rim: 0.4f));
        NeoFlush(tower, "Tower Rings A", ringA, NeoTube("MapleNeo_TubeCyan", new Color(0.30f, 1.80f, 2.30f, 1f)));
        NeoFlush(tower, "Tower Rings B", ringB, NeoTube("MapleNeo_TubePink", new Color(2.3f, 0.30f, 1.15f, 1f)));
        NeoFlush(tower, "Tower Plates", plates, NeoSignMat());
        NeoFlush(tower, "Tower Beacon", beacon, NeoTube("MapleNeo_Beacon", new Color(3.0f, 0.15f, 0.2f, 1f)));

        // ---- airships: five blimps on slow orbits at different radii / heights / directions
        var airships = new GameObject("Airships").transform;
        airships.SetParent(group, false);
        float[] radii = { 650f, 850f, 1050f, 760f, 1200f };
        float[] heights = { 150f, 200f, 175f, 245f, 215f };
        float[] speeds = { 6.5f, -5.5f, 7.5f, -6.5f, 5.0f };
        for (int k = 0; k < 5; k++)
        {
            var ship = new GameObject("Airship " + (k + 1)).transform;
            ship.SetParent(airships, false);
            var hull = new NeoBag(); var light = new NeoBag(); var logo = new NeoBag();
            // hull = lofted spindle (z along the body), gondola, fins, glowing underside lights, atlas plates on both flanks
            const int Ls = 18, As = 14; const float Len = 46f, Rad = 7.5f;
            for (int l = 0; l < Ls; l++)
            {
                float u0 = l / (float)Ls, u1 = (l + 1) / (float)Ls;
                float z0 = (u0 - 0.5f) * Len, z1 = (u1 - 0.5f) * Len;
                float r0 = Rad * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(2f * u0 - 1f, 2f))), r1 = Rad * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(2f * u1 - 1f, 2f)));
                for (int a = 0; a < As; a++)
                {
                    float a0 = 2f * Mathf.PI * a / As, a1 = 2f * Mathf.PI * (a + 1) / As;
                    int b = hull.V.Count;
                    hull.V.Add(new Vector3(Mathf.Cos(a0) * r0, Mathf.Sin(a0) * r0, z0)); hull.V.Add(new Vector3(Mathf.Cos(a1) * r0, Mathf.Sin(a1) * r0, z0));
                    hull.V.Add(new Vector3(Mathf.Cos(a0) * r1, Mathf.Sin(a0) * r1, z1)); hull.V.Add(new Vector3(Mathf.Cos(a1) * r1, Mathf.Sin(a1) * r1, z1));
                    for (int q = 0; q < 4; q++) hull.UV.Add(Vector2.zero);
                    hull.T.Add(b); hull.T.Add(b + 2); hull.T.Add(b + 1); hull.T.Add(b + 1); hull.T.Add(b + 2); hull.T.Add(b + 3);
                }
            }
            Box(hull.V, hull.UV, hull.T, null, new Vector3(0f, -Rad - 2.4f, 0f), Vector3.right * 1.9f, Vector3.forward * 6f, 2.4f, default);                        // gondola
            Box(hull.V, hull.UV, hull.T, null, new Vector3(0f, Rad * 0.35f, -Len * 0.5f + 2f), Vector3.right * 0.25f, Vector3.forward * 4.2f, Rad * 1.2f, default);       // vertical fin
            Box(hull.V, hull.UV, hull.T, null, new Vector3(0f, -0.2f, -Len * 0.5f + 2f), Vector3.right * Rad * 1.2f, Vector3.forward * 4.2f, 0.4f, default);           // horizontal fin
            for (int q = -2; q <= 2; q++) Box(light.V, light.UV, light.T, null, new Vector3(0f, -Rad - 2.55f, q * 2.2f), Vector3.right * 1.5f, Vector3.forward * 0.5f, 0.15f, default);
            NeoQuad(logo.V, logo.UV, logo.UV2, logo.T, new Vector3(Rad + 0.06f, 0.8f, 0f), Vector3.forward * 9f, Vector3.up * 4.5f, NeoTile(new[] { 0, 7, 16, 21, 28 }[k]), flip: true);
            NeoQuad(logo.V, logo.UV, logo.UV2, logo.T, new Vector3(-Rad - 0.06f, 0.8f, 0f), Vector3.forward * 9f, Vector3.up * 4.5f, NeoTile(new[] { 0, 7, 16, 21, 28 }[k]));
            NeoFlush(ship, "Hull", hull, CelMaterial("MapleNeo_AirshipHull", new Color(0.30f, 0.26f, 0.52f), gloss: 0.5f, spec: 0.45f, rim: 0.9f));
            NeoFlush(ship, "Lights", light, NeoTube(k % 2 == 0 ? "MapleNeo_TubePink" : "MapleNeo_TubeCyan", k % 2 == 0 ? new Color(2.3f, 0.30f, 1.15f, 1f) : new Color(0.30f, 1.80f, 2.30f, 1f)));
            NeoFlush(ship, "Plates", logo, NeoSignMat());
            var drive = ship.gameObject.AddComponent<MapleCityAirship>();
            drive.centre = mid; drive.radius = radii[k]; drive.height = heights[k]; drive.speed = speeds[k]; drive.phase = k * 1.3f;
            drive.Apply(0f);
        }
        BuildNeoSkyVolume(group, mid);
        Debug.Log("[maple-neo] backdrop: megatower + 5 airships + night sky volume + moon.");
    }
    // =================================================================== night sky volume + moon

    private const string NeoSkyProfilePath = "Assets/Environment/MapleCity/MapleNeoSky.asset";

    private static T NeoOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet<T>(out var comp) || comp == null) comp = profile.Add<T>(true);
        if (!AssetDatabase.Contains(comp)) { comp.hideFlags = HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(comp, profile); }
        return comp;
    }

    /// <summary>
    /// HDRP owns the sky in this project (RenderSettings.skybox is ignored), so Maple City gets its own scene-local global volume,
    /// parented under the city root so it switches off with the region - exactly how the Shiosai coast does it.
    /// </summary>
    private static void BuildNeoSkyVolume(Transform group, Vector3 mid)
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(NeoSkyProfilePath);
        if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, NeoSkyProfilePath); }

        var ve = NeoOverride<VisualEnvironment>(profile);
        ve.skyType.overrideState = true; ve.skyType.value = (int)SkyType.HDRI;
        // baked panorama (tools/blender/maple_neo_sky.py): gradient + pink horizon + stars + clouds + a real moon. An HDRI is not
        // fogged, so unlike a mesh disc the moon survives the region's exponential fog.
        const string skyTexPath = "Assets/Environment/MapleCity/Textures/MapleNeoSky.png";
        var ti = AssetImporter.GetAtPath(skyTexPath) as TextureImporter;
        if (ti != null && (ti.textureShape != TextureImporterShape.TextureCube || ti.generateCubemap != TextureImporterGenerateCubemap.AutoCubemap))
        {
            ti.textureShape = TextureImporterShape.TextureCube;
            ti.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
            ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.filterMode = FilterMode.Bilinear; ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 4096;
            ti.SaveAndReimport();
        }
        var skyCube = AssetDatabase.LoadAssetAtPath<Cubemap>(skyTexPath);
        if (skyCube == null) Debug.LogWarning("[maple-neo] sky cubemap missing - run python tools/blender/maple_neo_sky.py");
        var hdri = NeoOverride<HDRISky>(profile);
        hdri.hdriSky.overrideState = true;     hdri.hdriSky.value = skyCube;
        hdri.multiplier.overrideState = true;  hdri.multiplier.value = 1.0f;
        hdri.updateMode.overrideState = true;  hdri.updateMode.value = EnvironmentUpdateMode.OnChanged;

        var fog = NeoOverride<Fog>(profile);
        fog.enabled.overrideState = true;        fog.enabled.value = true;
        fog.meanFreePath.overrideState = true;   fog.meanFreePath.value = 1500f;
        fog.baseHeight.overrideState = true;     fog.baseHeight.value = 0f;
        fog.maximumHeight.overrideState = true;  fog.maximumHeight.value = 500f;
        fog.albedo.overrideState = true;         fog.albedo.value = new Color(0.30f, 0.14f, 0.50f, 1f);
        fog.tint.overrideState = true;           fog.tint.value = Color.white;
        fog.enableVolumetricFog.overrideState = true; fog.enableVolumetricFog.value = false;
        // distant geometry takes the SKY's colour (the pink horizon) so the world fades into the panorama with no seam
        fog.mipFogNear.overrideState = true; fog.mipFogNear.value = 0f;
        fog.mipFogFar.overrideState = true;  fog.mipFogFar.value = 5000f;
        fog.mipFogMaxMip.overrideState = true; fog.mipFogMaxMip.value = 0.55f;

        var ca = NeoOverride<ColorAdjustments>(profile);
        ca.saturation.overrideState = true;   ca.saturation.value = 14f;
        ca.contrast.overrideState = true;     ca.contrast.value = 8f;
        ca.postExposure.overrideState = true; ca.postExposure.value = -0.15f;

        var bloom = NeoOverride<Bloom>(profile);
        bloom.intensity.overrideState = true; bloom.intensity.value = 0.35f;
        bloom.scatter.overrideState = true;   bloom.scatter.value = 0.75f;
        EditorUtility.SetDirty(profile);

        var go = new GameObject("Maple Neo Sky Volume");
        go.transform.SetParent(group, false);
        var vol = go.AddComponent<Volume>();
        vol.isGlobal = true; vol.priority = 50f; vol.weight = 1f; vol.sharedProfile = profile;

        _ = mid;      // moon + stars live in the HDRI panorama
    }
}
