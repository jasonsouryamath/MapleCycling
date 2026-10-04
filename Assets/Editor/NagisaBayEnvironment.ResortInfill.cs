// NB13: coastal resort infill. The seaward walk is paved and populated; the
// inland highway reservation remains clear for NB3. All distances are from
// the ride centreline and use SeaSign so the east coast reverses correctly.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using MK = MinatoCrowdActor.MotionKind;

public static partial class NagisaBayEnvironment
{
    private const float Nb13StepM = 28f;
    private const float Nb13VergeInM = 4.35f, Nb13VergeOutM = 7.35f;
    private const float Nb13WalkInM = 7.45f, Nb13WalkOutM = 12.35f;
    private const float Nb13LiftM = 0.085f;

    [NagisaStage(85, "ResortInfill")]
    private static void BuildResortInfill(Transform group)
    {
        EnsureRouteGround();
        if (_nb.Count == 0)
        {
            Debug.LogError("[nb13] Run NagisaBayEnvironment.Apply before ResortInfill.");
            return;
        }
        var cast = _lastCast ?? NbCrowdCast(group.root);
        var paving = Nb13Child(group, "Coastal stone and mosaic");
        var people = Nb13Child(group, "Promenade guests and runners");
        var details = Nb13Child(group, "Resort promenade furnishings");
        var rng = new System.Random(1313);
        int paved = 0, guests = 0, runners = 0, terraces = 0;
        var walkMat = _nb["NB_DeckStone"];
        var vergeMat = Cel("NB13_MosaicVerge", new Color(0.74f, 0.85f, 0.82f), 0.32f, 0.18f, 0.12f);
        foreach (var span in new[] { (0f, 4720f), (12167f, 14991f) })
        {
            int i0 = _route.IndexAt(span.Item1 + 8f);
            int i1 = _route.IndexAt(span.Item2 - 8f);
            var wv = new List<Vector3>(); var wu = new List<Vector2>(); var wt = new List<int>();
            var vv = new List<Vector3>(); var vu = new List<Vector2>(); var vt = new List<int>();
            float lastPerson = -999f, lastHub = -999f;
            for (int i = i0; i < i1; i++)
            {
                if (_route.OnBridge(i)) continue;
                var d = _route.Distance[i];
                var sd = _route.SideFlat(i) * SeaSign(i);
                var a = _route.Position[i] + sd * Nb13WalkOutM;
                if (_ground.Coast(a.x, a.z) < 2f || InAnyPad(a.x, a.z, 3f)) continue;
                if (!Nb13AddQuad(i, i + 1, Nb13WalkInM, Nb13WalkOutM, wv, wu, wt)) continue;
                Nb13AddQuad(i, i + 1, Nb13VergeInM, Nb13VergeOutM, vv, vu, vt);
                paved++;

                if (d - lastPerson >= Nb13StepM)
                {
                    lastPerson = d;
                    int j = _route.IndexAt(Mathf.Min(d + 15f, span.Item2 - 9f));
                    if (j <= i || _route.OnBridge(j)) continue;
                    var sdj = _route.SideFlat(j) * SeaSign(j);
                    float offset = 8.3f + (float)rng.NextDouble() * 3.0f;
                    var start = Nb13At(i, sd, offset);
                    var end = Nb13At(j, sdj, offset);
                    if (_ground.Coast(end.x, end.z) < 2f || InAnyPad(end.x, end.z, 3f)) continue;
                    bool run = rng.NextDouble() < 0.29;
                    bool forward = rng.NextDouble() < 0.5;
                    var actor = Walker(cast, people, run ? "ResortRunner" : "ResortGuest",
                                       forward ? start : end, forward ? end : start,
                                       (float)rng.NextDouble(), run ? 2.55f : 0.93f);
                    if (actor != null) { if (run) runners++; else guests++; }
                    // Small groups create a social rhythm without blocking the path.
                    if (!run && rng.NextDouble() < 0.27)
                    {
                        var side = sd * 0.9f;
                        if (Walker(cast, people, "ResortGuest", start + side, end + sdj * 0.9f,
                                   (float)rng.NextDouble(), 0.85f) != null) guests++;
                    }
                }

                // Periodic service nodes on the beach edge: cabanas and lounges
                // use the existing LOD assets and avoid the NB6 beach furniture.
                if (d > _route.BeachStartM && d < _route.BeachEndM && d - lastHub > 180f)
                {
                    var site = Nb13At(i, sd, 18.5f);
                    if (_ground.Coast(site.x, site.z) > 12f && !InAnyPad(site.x, site.z, 9f) &&
                        Nb13Unoccupied(group.root, site, 7f))
                    {
                        lastHub = d;
                        float yaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
                        if (PlaceWorld("Nagisa_S_Cabana", details, site, yaw, 1f, SmallPropCull) != null)
                        {
                            terraces++;
                            var p = Nb13At(i, sd, 21f);
                            var guest = NbPerson(cast.stand, people, "CabanaGuest", p, -sd, MK.Idle,
                                                 (float)rng.NextDouble());
                            if (guest != null) { FinishPerson(guest); guests++; }
                        }
                    }
                }
            }
            Nb13Flush(paving, "Stone promenade", wv, wu, wt, walkMat, true);
            Nb13Flush(paving, "Mosaic verge", vv, vu, vt, vergeMat, false);
        }
        foreach (var r in details.GetComponentsInChildren<MeshRenderer>(true))
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
        int resortBuildings = Nb13InlandTerraces(group, vergeMat);
        int eastBeachSets = Nb13EastBeach(group);
        Debug.Log($"[nb13] resort infill: {paved} paved coastal samples, {guests} guests, " +
                  $"{runners} runners, {terraces} cabana nodes, {resortBuildings} inland buildings, " +
                  $"{eastBeachSets} east beach sets; " +
                  "no people or props in highway band.");
    }

    private static Transform Nb13Child(Transform parent, string name)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); return t;
    }

    private static Vector3 Nb13At(int i, Vector3 sd, float offset)
    {
        var p = _route.Position[i] + sd * offset;
        p.y = _ground.Height(p.x, p.z) + Nb13LiftM;
        return p;
    }

    private static bool Nb13AddQuad(int i, int j, float inner, float outer,
                                    List<Vector3> v, List<Vector2> uv, List<int> t)
    {
        var s0 = _route.SideFlat(i) * SeaSign(i);
        var s1 = _route.SideFlat(j) * SeaSign(j);
        var a = Nb13At(i, s0, inner); var b = Nb13At(i, s0, outer);
        var c = Nb13At(j, s1, inner); var d = Nb13At(j, s1, outer);
        if (_ground.Coast(b.x, b.z) < 2f || _ground.Coast(d.x, d.z) < 2f ||
            InAnyPad(c.x, c.z, 3f) || Mathf.Abs(a.y - b.y) > 0.9f || Mathf.Abs(c.y - d.y) > 0.9f)
            return false;
        int n = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        float u = _route.Distance[i] / 2.4f;
        uv.Add(new Vector2(u, 0)); uv.Add(new Vector2(u, (outer-inner)/2.4f));
        uv.Add(new Vector2(u+0.5f, 0)); uv.Add(new Vector2(u+0.5f, (outer-inner)/2.4f));
        t.AddRange(new[] { n, n+2, n+1, n+1, n+2, n+3 });
        return true;
    }

    private static void Nb13Flush(Transform parent, string name, List<Vector3> v,
                                  List<Vector2> uv, List<int> t, Material mat, bool walkLayer)
    {
        if (t.Count == 0) return;
        FixWinding(v, t);
        var go = AddMesh(parent, name + (walkLayer ? " - Beach Sand walk layer" : ""),
                         Finish("Nagisa_NB13_" + name.Replace(' ', '_'), v, uv, t), mat, walkLayer);
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    private static bool Nb13Unoccupied(Transform root, Vector3 p, float radius)
    {
        foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
        {
            var q = g.transform.position;
            float dx = p.x - q.x, dz = p.z - q.z;
            if (dx*dx + dz*dz < radius*radius) return false;
        }
        return true;
    }

    // The original town already covers 0-2.6 km. The following two spans are
    // exposed green ground in the diagnostic aerials, past the -42 m highway
    // frontage. A sampled mesh hugs the terrain; individual steep tiles are
    // omitted instead of floating above the hillside.
    private static int Nb13InlandTerraces(Transform group, Material stone)
    {
        var district = Nb13Child(group, "Inland resort district");
        var groundT = Nb13Child(district, "Terraced limestone plazas");
        var buildingsT = Nb13Child(district, "Residences and boutiques");
        int buildings = 0;
        foreach (var span in new[] { (2650f, 4680f), (12200f, 14900f) })
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (float d = span.Item1; d < span.Item2 - 12f; d += 12f)
            {
                int i = _route.IndexAt(d), j = _route.IndexAt(d + 12f);
                if (_route.OnBridge(i) || _route.OnBridge(j)) continue;
                var n0 = -_route.SideFlat(i) * SeaSign(i);
                var n1 = -_route.SideFlat(j) * SeaSign(j);
                for (float off = 44f; off < 152f; off += 12f)
                {
                    var a = Nb13At(i, n0, off); var b = Nb13At(i, n0, off + 12f);
                    var c = Nb13At(j, n1, off); var e = Nb13At(j, n1, off + 12f);
                    if (InAnyPad(a.x, a.z, 8f) || _ground.Coast(e.x, e.z) < 4f ||
                        Mathf.Max(a.y, b.y, c.y, e.y) - Mathf.Min(a.y, b.y, c.y, e.y) > 2.2f) continue;
                    int k = v.Count;
                    v.Add(a); v.Add(b); v.Add(c); v.Add(e);
                    uv.Add(new Vector2(d/3f, off/3f)); uv.Add(new Vector2(d/3f, (off+12f)/3f));
                    uv.Add(new Vector2((d+12f)/3f, off/3f)); uv.Add(new Vector2((d+12f)/3f, (off+12f)/3f));
                    t.AddRange(new[] { k,k+2,k+1, k+1,k+2,k+3 });
                }
            }
            Nb13Flush(groundT, "Limestone terrace", v, uv, t, stone, false);

            for (float d = span.Item1 + 45f; d < span.Item2 - 30f; d += 94f)
            {
                int i = _route.IndexAt(d);
                var sd = _route.SideFlat(i) * SeaSign(i);
                var inland = -sd;
                foreach (float off in new[] { 67f, 127f })
                {
                    var p = Nb13At(i, inland, off);
                    var l = Nb13At(_route.IndexAt(d - 10f), inland, off);
                    var r = Nb13At(_route.IndexAt(d + 10f), inland, off);
                    if (_ground.Coast(p.x, p.z) < 7f || InAnyPad(p.x, p.z, 18f) ||
                        Mathf.Max(p.y, l.y, r.y) - Mathf.Min(p.y, l.y, r.y) > 1.6f ||
                        !Nb13Unoccupied(group.root, p, 23f)) continue;
                    // One skyline accent per block; low-rise premium frontages
                    // carry most of the density at a fraction of the triangle cost.
                    string stem = buildings % 7 == 0 ? "Nagisa_B_Condo2" : "Nagisa_B_Boutique2";
                    float yaw = Mathf.Atan2(sd.x, sd.z) * Mathf.Rad2Deg;
                    if (PlaceWorld(stem, buildingsT, p, yaw, 1f, 0.0015f, null, true) != null)
                        buildings++;
                }
            }
        }
        return buildings;
    }

    private static int Nb13EastBeach(Transform group)
    {
        var sandT = Nb13Child(group, "East coast sand terraces");
        var setsT = Nb13Child(group, "East coast beach services");
        var sand = Cel("NB13_EastSand", new Color(1f, 0.86f, 0.64f), 0.07f, 0.04f, 0.16f,
                       Tex(CoastTex, "Shiosai_Sand_Albedo.png"),
                       Tex(CoastTex, "Shiosai_Sand_Normal.png"), 0.6f);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        int sets = 0;
        for (float d = 12185f; d < 14920f; d += 8f)
        {
            int i = _route.IndexAt(d), j = _route.IndexAt(d + 8f);
            if (_route.OnBridge(i) || _route.OnBridge(j)) continue;
            var s0 = _route.SideFlat(i) * SeaSign(i);
            var s1 = _route.SideFlat(j) * SeaSign(j);
            for (float off = 12.5f; off < 175f; off += 8f)
            {
                var a = Nb13At(i, s0, off); var b = Nb13At(i, s0, off+8f);
                var c = Nb13At(j, s1, off); var e = Nb13At(j, s1, off+8f);
                if (_ground.Coast(a.x,a.z) < 1f || _ground.Coast(b.x,b.z) < 1f ||
                    _ground.Coast(c.x,c.z) < 1f || _ground.Coast(e.x,e.z) < 1f ||
                    InAnyPad(a.x,a.z,4f) ||
                    Mathf.Max(a.y,b.y,c.y,e.y)-Mathf.Min(a.y,b.y,c.y,e.y) > 1.8f) continue;
                int k = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(e);
                uv.Add(new Vector2(d/3f,off/3f)); uv.Add(new Vector2(d/3f,(off+8f)/3f));
                uv.Add(new Vector2((d+8f)/3f,off/3f)); uv.Add(new Vector2((d+8f)/3f,(off+8f)/3f));
                t.AddRange(new[] { k,k+2,k+1,k+1,k+2,k+3 });
            }
            if ((Mathf.FloorToInt(d) % 32) < 8)
            {
                foreach (float off in new[] { 26f, 52f, 78f })
                {
                    var p = Nb13At(i, s0, off);
                    if (_ground.Coast(p.x,p.z) < 15f || !Nb13Unoccupied(group.root,p,7f)) continue;
                    float yaw = Mathf.Atan2(s0.x,s0.z)*Mathf.Rad2Deg;
                    if (PlaceWorld("Nagisa_BeachUmbrella", setsT, p, yaw, 1f, SmallPropCull) != null) sets++;
                }
            }
        }
        Nb13Flush(sandT, "East beach sand", v, uv, t, sand, true);
        return sets;
    }
}
