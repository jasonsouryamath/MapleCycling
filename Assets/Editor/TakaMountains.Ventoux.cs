using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TAKA as MT. VENTOUX (user direction 2026-09-26): a Provencal start village with a cafe and a
/// boulangerie, a Chalet-Reynard-style cafe stop partway up with parked bikes and a crowd, and a
/// summit with the red-and-white weather tower, the summit sign, crepe/souvenir stalls and a
/// crowd of dismounted cyclists; painted rider names on the upper road. People are clones of
/// the Minato crowd donors (the MapleCityLife approach: find MinatoCrowdActor "Crowd_*" figures
/// already in the scene, Instantiate, Configure). Those donors are dismounted cyclists in kit,
/// which is exactly who stands around a Ventoux cafe or summit sign.
/// </summary>
public static partial class TakaMountainsEnvironment
{
    // Route bands (metres) - see COPILOT_HANDOFF "Taka Mountains (Mt. Ventoux)".
    private const float VillageFromM = 40f, VillageToM = 760f;
    private const float ChaletM = 9400f;          // ~6 km below the summit, as on Ventoux
    private const float SummitM = CpCol;          // 15,400 m

    private static Material Stucco(int k)
    {
        var cols = new[] { new Color(0.86f, 0.80f, 0.66f), new Color(0.84f, 0.68f, 0.50f),
                           new Color(0.88f, 0.76f, 0.70f), new Color(0.80f, 0.78f, 0.72f) };
        return CelMaterial($"TakaV_Stucco{k}", cols[k % cols.Length], gloss: 0.12f, spec: 0.06f, rim: 0.25f,
                           texture: TakaTexture("Taka_Concrete_Albedo.png"));
    }
    private static Material Plain(string name, Color c, float gloss = 0.25f) =>
        CelMaterial(name, c, gloss: gloss, spec: 0.12f, rim: 0.3f);
    /// <summary>
    /// All of Ventoux's and Living's signs (village/cafe/boulangerie/chalet/crepes/summit/KOM/
    /// finish) now share ONE trim sheet + ONE material instead of 9 separate 1k/2k textures
    /// (Agent HQ ticket "Build one signage trim sheet to replace per-sign 2k textures",
    /// 2026-09-30). Built by tools/textures/make_taka_sign_trimsheet.py, which packs the
    /// original art byte-for-byte - no redraw. Rect = (u0, v0, width, height), U left-to-right,
    /// V bottom-to-top (Unity's texture V convention).
    /// </summary>
    private static readonly Dictionary<string, Rect> TakaSignUV = new Dictionary<string, Rect>
    {
        { "Taka_SummitSign.png", new Rect(0.0f, 0.625f, 0.5f, 0.375f) },
        { "Taka_Sign_Auberge.png", new Rect(0.5f, 0.8125f, 0.5f, 0.1875f) },
        { "Taka_Sign_Boulangerie.png", new Rect(0.5f, 0.625f, 0.5f, 0.1875f) },
        { "Taka_Sign_Cafe.png", new Rect(0.0f, 0.4375f, 0.5f, 0.1875f) },
        { "Taka_Sign_Chalet.png", new Rect(0.5f, 0.4375f, 0.5f, 0.1875f) },
        { "Taka_Sign_Crepes.png", new Rect(0.0f, 0.25f, 0.5f, 0.1875f) },
        { "Taka_Sign_Finish.png", new Rect(0.5f, 0.25f, 0.5f, 0.1875f) },
        { "Taka_Sign_Village.png", new Rect(0.0f, 0.0625f, 0.5f, 0.1875f) },
        { "Taka_Sign_KOM.png", new Rect(0.5f, 0.0625f, 0.25f, 0.1875f) },
    };

    private static Material _signTrimMat;

    /// <summary>Kept per-file for call-site compatibility; now every file resolves to the same
    /// shared trim-sheet material (SignBoard remaps the UVs to that file's sub-rect).</summary>
    private static Material SignMat(string file)
    {
        if (_signTrimMat == null)
        {
            _signTrimMat = CelMaterial("TakaV_SignTrim", Color.white, gloss: 0.2f, spec: 0.1f, rim: 0.1f,
                                       texture: TakaTexture("Taka_Sign_TrimSheet.png"));
            if (_signTrimMat.HasProperty("_Cull")) _signTrimMat.SetFloat("_Cull", 0f);
        }
        return _signTrimMat;
    }

    private static void BuildVentoux(Transform root, TakaRoute route)
    {
        AssetDatabase.ImportAsset($"{TextureDir}/Taka_RoadNames.png");
        AssetDatabase.ImportAsset($"{TextureDir}/Taka_Sign_TrimSheet.png");

        _signSerial = 0;
        var group = new GameObject("Taka Ventoux").transform;
        group.SetParent(root, false);
        var rng = new System.Random(4411);

        var stucco = new Bucket[4];
        for (int k = 0; k < 4; k++) stucco[k] = new Bucket($"VStucco{k}", Stucco(k));
        var tile = new Bucket("VRoofTile", Plain("TakaV_Terracotta", new Color(0.70f, 0.34f, 0.20f)));
        var shutterCols = new[] { new Color(0.35f, 0.55f, 0.66f), new Color(0.42f, 0.58f, 0.40f), new Color(0.56f, 0.50f, 0.70f) };
        var shutter = new Bucket[3];
        for (int k = 0; k < 3; k++) shutter[k] = new Bucket($"VShutter{k}", Plain($"TakaV_Shutter{k}", shutterCols[k]));
        var door = new Bucket("VDoor", Plain("TakaV_Door", new Color(0.30f, 0.20f, 0.13f)));
        var bark = new Bucket("VPlaneBark", Plain("TakaV_PlaneBark", new Color(0.62f, 0.60f, 0.50f), 0.1f));
        var leaf = new Bucket("VPlaneLeaf", Plain("TakaV_PlaneLeaf", new Color(0.30f, 0.46f, 0.20f), 0.1f));
        var awnRed = new Bucket("VAwnRed", Plain("TakaV_AwnRed", new Color(0.72f, 0.16f, 0.14f)));
        var awnWhite = new Bucket("VAwnWhite", Plain("TakaV_AwnWhite", new Color(0.93f, 0.92f, 0.88f)));
        var furniture = new Bucket("VFurniture", Plain("TakaV_Bistro", new Color(0.20f, 0.24f, 0.22f), 0.4f));
        var bike = new Bucket("VBikes", Plain("TakaV_BikeFrame", new Color(0.12f, 0.12f, 0.14f), 0.5f));
        var bikeCol = new Bucket("VBikeColour", Plain("TakaV_BikeColour", new Color(0.10f, 0.45f, 0.75f), 0.6f));
        var towerR = new Bucket("VTowerRed", Plain("TakaV_TowerRed", new Color(0.78f, 0.18f, 0.15f)));
        var towerW = new Bucket("VTowerWhite", Plain("TakaV_TowerWhite", new Color(0.92f, 0.92f, 0.90f)));
        var steelB = new Bucket("VMast", Plain("TakaV_Mast", new Color(0.55f, 0.56f, 0.58f), 0.5f));
        var all = new List<Bucket> { tile, door, bark, leaf, awnRed, awnWhite, furniture, bike, bikeCol, towerR, towerW, steelB };
        all.AddRange(stucco); all.AddRange(shutter);
        // awnings are seen from BELOW (a rider looks up at them): single-sided they culled to a
        // thin striped line (T4 review frame)
        foreach (var m in new[] { awnRed.Mat, awnWhite.Mat })
            if (m.HasProperty("_Cull")) { m.SetFloat("_Cull", 0f); EditorUtility.SetDirty(m); }
        var gravel = new Bucket("VForecourt", CelMaterial("TakaV_Forecourt", new Color(0.72f, 0.68f, 0.62f), gloss: 0.06f, spec: 0.04f,
                                rim: 0.08f, texture: TakaTexture("Taka_Limestone_Albedo.png"), shade: LimeShade));
        all.Add(gravel);

        var peopleSpots = new List<(Vector3 pos, Vector3 face, MinatoCrowdActor.MotionKind kind, Vector3 walkTo)>();
        float kerb = RoadHalfWidth + ShoulderWidth;

        // ------------------------------------------------ Provencal start village
        int houses = 0;
        for (float d = VillageFromM; d < VillageToM; d += 12f)
        {
            int i = route.IndexAt(d);
            for (int s = -1; s <= 1; s += 2)
            {
                if (rng.NextDouble() < 0.12) continue;
                float o = s * (kerb + 7.5f + (float)rng.NextDouble() * 2f);
                Foot(route, i, o, out var f, out var outward);
                if (NearestDist(f) < 9f) continue;
                f.y = Mathf.Max(f.y, RoadY(route, i, 0f) - 0.4f);
                float W = 8f + (float)rng.NextDouble() * 2.5f, D = 7f, H = 6f + (float)rng.NextDouble() * 3.5f;
                if (!Kit2House(f, outward, W, rng))
                    Townhouse(stucco[rng.Next(4)], tile, shutter[rng.Next(3)], door, f, outward, W, D, H, rng);
                houses++;
                float roadFaceOff = kerb + 1.6f;
                // plane trees along the kerb every other slot
                if (((int)(d / 12f)) % 2 == 0)
                {
                    Foot(route, i, s * (kerb + 1.4f), out var tf, out _);
                    tf.y = Mathf.Max(tf.y, RoadY(route, i, 0f) - 0.2f);
                    float planeH = 9f + (float)rng.NextDouble() * 3f;
                    if (!Kit2PlaneTree(tf, planeH, rng)) PlaneTree(bark, leaf, tf, planeH, rng);
                }
                // walkers on the pavement strip between kerb and house
                if (rng.NextDouble() < 0.7)
                {
                    int j = route.IndexAt(d + 10f);
                    Foot(route, i, s * roadFaceOff, out var a, out _);
                    Foot(route, j, s * roadFaceOff, out var b, out _);
                    a.y = Mathf.Max(a.y, RoadY(route, i, 0f)); b.y = Mathf.Max(b.y, RoadY(route, j, 0f));
                    peopleSpots.Add((a, b - a, MinatoCrowdActor.MotionKind.Walk, b));
                }
            }
        }
        // cafe (side +) and boulangerie (side -) with awnings and terraces
        CafeFront(route, route.IndexAt(260f), 1, "Taka_Sign_Cafe.png", awnRed, awnWhite, furniture, peopleSpots, rng, 7, group);
        CafeFront(route, route.IndexAt(420f), -1, "Taka_Sign_Boulangerie.png", awnWhite, awnRed, furniture, peopleSpots, rng, 4, group);
        ParkedBikes(route, route.IndexAt(245f), 1, 8, bike, bikeCol, peopleSpots, rng);
        ParkedBikes(route, route.IndexAt(278f), 1, 6, bike, bikeCol, peopleSpots, rng);
        ParkedBikes(route, route.IndexAt(435f), -1, 6, bike, bikeCol, peopleSpots, rng);
        // 2026-09-26 claude-taka2: more life - a boulangerie queue, a morning market with
        // produce/lavender stalls between the cafe and the bakery, chatting groups on the kerb.
        Queue(route, route.IndexAt(414f), -1, 6, peopleSpots);
        var crate = new Bucket("VCrates", Plain("TakaV_Crate", new Color(0.62f, 0.46f, 0.28f), 0.1f));
        var produce = new Bucket[3];
        var produceCols = new[] { new Color(0.52f, 0.40f, 0.72f), new Color(0.80f, 0.30f, 0.16f), new Color(0.46f, 0.60f, 0.22f) };
        for (int k = 0; k < 3; k++) produce[k] = new Bucket($"VProduce{k}", Plain($"TakaV_Produce{k}", produceCols[k], 0.2f));
        all.Add(crate); all.AddRange(produce);
        for (int k = 0; k < 4; k++)
            Stall(route, route.IndexAt(320f + k * 13f), k % 2 == 0 ? 1 : -1, furniture, k % 2 == 0 ? awnRed : awnWhite,
                  crate, produce[k % 3], peopleSpots, rng);
        foreach (var gd in new[] { 190f, 300f, 360f, 470f, 560f, 650f })
            Group(route, route.IndexAt(gd), rng.NextDouble() < 0.5 ? 1 : -1, 2 + rng.Next(3), peopleSpots, rng);

        // ------------------------------------------------ Chalet-Reynard-style stop
        // 2026-09-26 copilot session 3 (T4): the chalet sat 21 m out behind the roadside parapet,
        // with its awning and sign on a free-standing canopy 5.5 m in front of the building, so
        // from the saddle it read as a cafe up on a bank above a retaining wall. Now: the
        // parapet has a gap here (WallPresence), the building faces the road 16 m out with the
        // striped awning on its own front, and a level gravel forecourt runs from the kerb to the
        // door at road level (the ground within 18 m of the centreline IS the road level - see
        // the road cut in Height() - so the forecourt, tables, racks and people all agree).
        {
            int i = route.IndexAt(ChaletM);
            int s = 1;
            const float Face = 16f;
            Foot(route, i, s * 21f, out var f, out var outward);
            if (NearestDist(f) < 16f) s = -1;
            Foot(route, i, s * Face, out f, out outward);
            f.y = RoadY(route, i, 0f) - 0.05f;
            Townhouse(stucco[0], tile, shutter[1], door, f, outward, 18f, 11f, 5.6f, rng);
            CafeFront(route, i, s, "Taka_Sign_Chalet.png", awnRed, awnWhite, furniture, peopleSpots, rng, 9, group, frontOff: Face);
            Forecourt(route, ChaletM - 34f, ChaletM + 34f, s, kerb, Face + 0.4f, gravel);
            Stall(route, route.IndexAt(ChaletM + 40f), s, furniture, awnRed, furniture, awnWhite, peopleSpots, rng);
            Group(route, route.IndexAt(ChaletM - 35f), s, 4, peopleSpots, rng);
            Group(route, route.IndexAt(ChaletM + 12f), -s, 3, peopleSpots, rng);
            ParkedBikes(route, route.IndexAt(ChaletM - 20f), s, 10, bike, bikeCol, peopleSpots, rng);
            ParkedBikes(route, route.IndexAt(ChaletM + 22f), s, 10, bike, bikeCol, peopleSpots, rng);
            ShotMarker(group, "TakaShot_Chalet", f + Vector3.up * 1.5f, outward);
            var sb = new System.Text.StringBuilder($"[taka] chalet side {s}, road y {RoadY(route, i, 0f):0.00}, ground above road at");
            foreach (var o in new[] { 4f, 8f, 12f, 16f, 21f, 27f })
            {
                Foot(route, i, s * o, out var g, out _);
                sb.Append($" {o:0}m {g.y - RoadY(route, i, 0f):+0.00;-0.00}");
            }
            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------ Summit
        {
            int i = route.IndexAt(SummitM);
            int s = -1;
            Foot(route, i, s * 34f, out var f, out var outward);
            if (NearestDist(f) < 26f) { s = 1; Foot(route, i, s * 34f, out f, out outward); }
            // weather tower: stacked red/white drums (octagonal) then a lattice mast
            float y0 = f.y - 1f;
            if (!Kit2WeatherTower(f))   // E2: Blender weather tower + lattice mast (GLB); procedural drums are the fallback
            {
                for (int k = 0; k < 7; k++)
                {
                    float r = k < 2 ? 7f : 4.2f - k * 0.2f;
                    var b = (k % 2 == 0) ? towerR : towerW;
                    Drum(b, new Vector3(f.x, y0 + k * 5f, f.z), r, 5f, 10);
                }
                Drum(towerW, new Vector3(f.x, y0 + 35f, f.z), 2.2f, 3f, 12);   // radome base
                Drum(towerW, new Vector3(f.x, y0 + 38f, f.z), 1.4f, 2.5f, 12);
                Post(steelB.V, steelB.UV, steelB.T, null, new Vector3(f.x, y0 + 38f, f.z), new Vector3(f.x, y0 + 62f, f.z), 0.35f, Color.white);
            }
            // summit sign on the road side
            Foot(route, route.IndexAt(SummitM - 25f), s * (kerb + 2.6f), out var sf, out var sout);
            sf.y = Mathf.Max(sf.y, RoadY(route, route.IndexAt(SummitM - 25f), 0f));
            var along = Vector3.Cross(Vector3.up, sout).normalized;
            Post(door.V, door.UV, door.T, null, sf - along * 1.5f - Vector3.up * 0.5f, sf - along * 1.5f + Vector3.up * 2.9f, 0.08f, Color.white);
            Post(door.V, door.UV, door.T, null, sf + along * 1.5f - Vector3.up * 0.5f, sf + along * 1.5f + Vector3.up * 2.9f, 0.08f, Color.white);
            SignBoard(group, "Summit Sign", "Taka_SummitSign.png", sf + Vector3.up * 2.1f, along, 3.2f, 1.2f);
            // photo crowd around the sign, bikes held/leaning
            for (int k = 0; k < 22; k++)
            {
                var p = sf + along * ((float)rng.NextDouble() * 12f - 6f) + sout * (0.8f + (float)rng.NextDouble() * 5f);
                p.y = Mathf.Max(Height(p.x, p.z), RoadY(route, i, 0f) - 0.3f);
                peopleSpots.Add((p, -sout + along * ((float)rng.NextDouble() - 0.5f), MinatoCrowdActor.MotionKind.Idle, p));
            }
            ParkedBikes(route, route.IndexAt(SummitM - 45f), s, 12, bike, bikeCol, peopleSpots, rng);
            ParkedBikes(route, route.IndexAt(SummitM + 15f), s, 12, bike, bikeCol, peopleSpots, rng);
            ParkedBikes(route, route.IndexAt(SummitM + 40f), -s, 8, bike, bikeCol, peopleSpots, rng);
            // people at the foot of the tower, looking out at the view
            for (int k = 0; k < 8; k++)
            {
                float ang = k * Mathf.PI * 2f / 8f + 0.3f;
                var p = f + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (9.5f + (float)rng.NextDouble() * 2f);
                p.y = Height(p.x, p.z);
                if (NearestDist(p) < RoadHalfWidth + ShoulderWidth + 0.8f) continue;
                peopleSpots.Add((p, p - f, MinatoCrowdActor.MotionKind.Idle, p));
            }
            Group(route, route.IndexAt(SummitM + 60f), s, 4, peopleSpots, rng);
            Group(route, route.IndexAt(SummitM - 70f), -s, 3, peopleSpots, rng);
            // crepe / souvenir stalls on the other side
            for (int k = 0; k < 4; k++)
            {
                int j = route.IndexAt(SummitM - 30f + k * 14f);
                Foot(route, j, -s * (kerb + 4.5f), out var cf, out var cout);
                cf.y = Mathf.Max(cf.y, RoadY(route, j, 0f) - 0.3f);
                var cal = Vector3.Cross(Vector3.up, cout).normalized;
                Box(furniture.V, furniture.UV, furniture.T, null, cf + cout * 0.8f, cal * 2f, cout * 1.3f, 2.3f, Color.white);
                Band((k % 2 == 0 ? awnRed : awnWhite).V, (k % 2 == 0 ? awnRed : awnWhite).UV, (k % 2 == 0 ? awnRed : awnWhite).T,
                     cf + cout * 0.3f + Vector3.up * 3.1f + cal * 2.3f, cf + cout * 0.3f + Vector3.up * 3.1f - cal * 2.3f,
                     cf - cout * 1.4f + Vector3.up * 2.4f + cal * 2.3f, cf - cout * 1.4f + Vector3.up * 2.4f - cal * 2.3f, 0f);
                if (k == 1) SignBoard(group, "Crepes Sign", "Taka_Sign_Crepes.png", cf - cout * 1.45f + Vector3.up * 2.75f, cal, 2.3f, 0.43f);
                for (int q = 0; q < 3; q++)
                {
                    var p = cf - cout * (2.4f + (float)rng.NextDouble() * 1.2f) + cal * ((float)rng.NextDouble() * 3f - 1.5f);
                    p.y = cf.y;
                    peopleSpots.Add((p, cout, MinatoCrowdActor.MotionKind.Idle, p));
                }
            }
        }

        foreach (var b in all) b.Flush(group);

        // ------------------------------------------------ Provencal lower slopes
        // 2026-09-26 copilot session 3 (T1/T2): the plots are PLANNED before the forest and the
        // crags are scattered (PlanProvence, called from Apply), so the conifers, boulders and
        // tufts keep out of them - the first pass grew pines straight through the lavender and
        // hid the 160 olive trees inside the pine band. Built here from the plan.
        int nLav = 0, nOlive = 0, nBush = 0;
        {
            var lav = new Bucket("VLavender", CelMaterial("TakaV_Lavender", new Color(0.40f, 0.27f, 0.45f),
                                  gloss: 0.12f, spec: 0.05f, rim: 0.30f, shade: new Color(0.56f, 0.49f, 0.58f, 1f)));
            var lavLeaf = new Bucket("VLavLeaf", CelMaterial("TakaV_LavLeaf", new Color(0.30f, 0.35f, 0.26f),
                                  gloss: 0.08f, spec: 0.04f, rim: 0.20f, shade: new Color(0.48f, 0.52f, 0.54f, 1f)));
            var lavSoil = new Bucket("VLavSoil", CelMaterial("TakaV_LavSoil", new Color(0.36f, 0.25f, 0.15f),
                                  gloss: 0.04f, spec: 0.03f, rim: 0.10f, shade: new Color(0.58f, 0.50f, 0.48f, 1f)));
            var olive = new Bucket("VOlive", CelMaterial("TakaV_OliveLeaf", new Color(0.35f, 0.39f, 0.32f),
                                  gloss: 0.18f, spec: 0.12f, rim: 0.45f, shade: new Color(0.50f, 0.52f, 0.52f, 1f)));
            var oliveBark = new Bucket("VOliveBark", CelMaterial("TakaV_OliveBark", new Color(0.33f, 0.28f, 0.22f),
                                  gloss: 0.05f, spec: 0.03f, rim: 0.15f, shade: new Color(0.52f, 0.50f, 0.50f, 1f)));
            var lower = new List<Bucket> { lav, lavLeaf, lavSoil, olive, oliveBark };
            int nShotL = 0, nShotO = 0;
            foreach (var plot in _provence)
            {
                if (plot.Olive)
                {
                    nOlive += OliveGrove(plot, olive, oliveBark, rng);
                    // shot markers only outside the village: behind the houses a verge shot sees walls
                    if (nShotO < 3 && !plot.Village) ShotMarker(group, $"TakaShot_Olive_{nShotO++}", plot);
                }
                else
                {
                    nBush += LavenderField(plot, lav, lavLeaf, lavSoil, rng);
                    nLav++;
                    if (nShotL < 3 && !plot.Village) ShotMarker(group, $"TakaShot_Lavender_{nShotL++}", plot);
                }
                foreach (var b in lower) b.FlushIfBig(group);
            }
            foreach (var b in lower) b.Flush(group);
        }

        // ------------------------------------------------ painted rider names on the upper climb
        {
            var mat = LoadOrCreate("TakaV_RoadPaint", FoliageShaderName);
            mat.SetTexture("_MainTex", TakaTexture("Taka_RoadNames.png"));
            mat.SetColor("_Color", new Color(0.92f, 0.92f, 0.90f, 1f));
            mat.SetFloat("_Cutoff", 0.5f);
            if (mat.HasProperty("_WindStrength")) mat.SetFloat("_WindStrength", 0f);
            if (mat.HasProperty("_Translucency")) mat.SetFloat("_Translucency", 0f);
            if (mat.HasProperty("_RimStrength")) mat.SetFloat("_RimStrength", 0f);
            EditorUtility.SetDirty(mat);
            var paint = new Bucket("VRoadNames", mat);
            int n = 0;
            for (float d = 3000f; d < SummitM - 60f; d += 230f)
            {
                int i = route.IndexAt(d), j = route.IndexAt(d + 4.5f);
                int row = n % 4;
                float o = (n % 3 - 1) * 0.9f;
                float v0 = 1f - (row + 1) / 4f, v1 = 1f - row / 4f;
                // 2026-09-26 claude-taka2: the name used to be ONE flat quad between its two edges,
                // so its middle dipped under the 6 cm road crown ("ALL ... JRO"). It is now 8
                // strips across, each vertex on the crowned surface (RoadY includes CrownAt) plus a
                // 2.5 cm lift, which clears the road's own chord between its 9 columns everywhere.
                const int Strips = 8;
                const float Lift = 0.025f;
                for (int k = 0; k < Strips; k++)
                {
                    float oa = o - 2f + 4f * k / Strips, ob = o - 2f + 4f * (k + 1) / Strips;
                    float ua = (float)k / Strips, ub = (float)(k + 1) / Strips;
                    var a0 = route.Position[i] + route.SideFlat(i) * oa; a0.y = RoadY(route, i, oa) + Lift;
                    var a1 = route.Position[i] + route.SideFlat(i) * ob; a1.y = RoadY(route, i, ob) + Lift;
                    var b0 = route.Position[j] + route.SideFlat(j) * oa; b0.y = RoadY(route, j, oa) + Lift;
                    var b1 = route.Position[j] + route.SideFlat(j) * ob; b1.y = RoadY(route, j, ob) + Lift;
                    // text reads upright for a rider travelling forward: v runs along the road
                    paint.Tri(a0, b0, a1, new Vector2(ua, v0), new Vector2(ua, v1), new Vector2(ub, v0));
                    paint.Tri(a1, b0, b1, new Vector2(ub, v0), new Vector2(ua, v1), new Vector2(ub, v1));
                }
                n++;
            }
            // ensure the quads face up
            FixUp(paint);
            paint.Flush(group);
        }

        int people = SpawnCrowd(group, root, peopleSpots, rng);
        Debug.Log($"[taka] ventoux: {houses} village houses, chalet stop @ {ChaletM:0} m, summit tower, {people} people clones, " +
                  $"{nLav} lavender fields ({nBush} bushes), {nOlive} olive trees in {_provence.Count - nLav} groves.");
    }

    private static float NearestDist(Vector3 p) { NearestStation(p.x, p.z, out float dd); return dd; }

    // =================================================================== Provencal plots (T1/T2)
    // 2026-09-26 copilot session 3. Lavender fields and olive groves on the lower Provencal band
    // (route 60-3,600 m). A plot is a rectangle on the ground: C is the middle of its road-side
    // edge, Along runs with the road, Out points away from it, and the plot covers
    // |u| <= L/2, 0 <= v <= Wd. PlanProvence runs BEFORE the flora, the forest and the crags so
    // every one of them can keep out (InProvence); BuildVentoux builds the plots afterwards.
    private struct ProvencePlot
    {
        public Vector3 C, Along, Out;
        public float L, Wd;
        public bool Olive, Village;
    }
    private static readonly List<ProvencePlot> _provence = new List<ProvencePlot>();

    private static void PlanProvence(TakaRoute route)
    {
        _provence.Clear();
        var rng = new System.Random(5521);
        for (float d = 60f; d < 3600f; d += 70f)
        {
            int i = route.IndexAt(d);
            for (int s = -1; s <= 1; s += 2)
            {
                if (rng.NextDouble() < 0.22) continue;
                bool olive = rng.NextDouble() < 0.42;
                // inside the village the plots sit behind the houses (they reach ~20 m out)
                float off = s * (d < VillageToM + 40f ? 27f + (float)rng.NextDouble() * 14f
                                                      : 10.5f + (float)rng.NextDouble() * 14f);
                float L = olive ? 26f + (float)rng.NextDouble() * 18f : 24f + (float)rng.NextDouble() * 16f;
                float Wd = olive ? 16f + (float)rng.NextDouble() * 10f : 13f + (float)rng.NextDouble() * 7f;
                TryPlot(route, i, off, L, Wd, olive, olive ? 14f : 10f, d < VillageToM + 40f);
            }
        }
        int nl = 0; foreach (var p in _provence) if (!p.Olive) nl++;
        Debug.Log($"[taka] provence plan: {nl} lavender fields, {_provence.Count - nl} olive groves.");
    }

    private static bool TryPlot(TakaRoute route, int i, float off, float L, float Wd, bool olive, float maxRise, bool village)
    {
        Foot(route, i, off, out var c, out var outward);
        outward.y = 0f; outward.Normalize();
        var along = Vector3.Cross(Vector3.up, outward).normalized;
        float hmin = float.MaxValue, hmax = float.MinValue;
        for (int u = -2; u <= 2; u++)
            for (int v = 0; v <= 2; v++)
            {
                var q = c + along * (u * L * 0.25f) + outward * (v * Wd * 0.5f);
                if (NearestDist(q) < 8f) return false;          // never reaches this or another road leg
                float h = Height(q.x, q.z); hmin = Mathf.Min(hmin, h); hmax = Mathf.Max(hmax, h);
            }
        if (hmax - hmin > maxRise) return false;               // terraces are flat-ish ground only
        // no overlap: none of the new plot's sample points inside an old plot (+3 m), and none
        // of an old plot's corners / middle inside the new one
        var plot = new ProvencePlot { C = c, Along = along, Out = outward, L = L, Wd = Wd, Olive = olive, Village = village };
        for (int u = -4; u <= 4; u++)
            for (int v = 0; v <= 3; v++)
                if (InProvence(c + along * (u * L / 8f) + outward * (v * Wd / 3f), 3f)) return false;
        foreach (var p in _provence)
            for (int u = -1; u <= 1; u++)
                for (int v = 0; v <= 2; v++)
                    if (InPlot(plot, p.C + p.Along * (u * p.L * 0.5f) + p.Out * (v * p.Wd * 0.5f), 3f)) return false;
        _provence.Add(plot);
        return true;
    }

    /// <summary>True when p (XZ) is inside a planned plot grown by margin metres.</summary>
    private static bool InProvence(Vector3 p, float margin)
    {
        for (int k = 0; k < _provence.Count; k++)
            if (InPlot(_provence[k], p, margin)) return true;
        return false;
    }

    private static bool InPlot(ProvencePlot q, Vector3 p, float margin)
    {
        float dx = p.x - q.C.x, dz = p.z - q.C.z;
        float lim = q.L * 0.5f + q.Wd + margin;
        if (dx * dx + dz * dz > lim * lim) return false;
        float u = dx * q.Along.x + dz * q.Along.z, v = dx * q.Out.x + dz * q.Out.z;
        return Mathf.Abs(u) <= q.L * 0.5f + margin && v >= -margin && v <= q.Wd + margin;
    }

    // ------------------------------------------------ rendered ground height
    // Height() is the smooth function; what the camera sees is its linear interpolation on the
    // corridor ribbon (12 m rows x the node offsets below) and the 32 m fell grid. On the
    // smoothstep blend out of the road cut the two differ by up to a metre, so a lavender field
    // laid on Height() + 10 cm vanished under the ground in the middle of every concave plot
    // (round 2: rows of bare purple tips). SurfaceY rebuilds the SAME triangles BuildCorridor /
    // BuildBackgroundTerrain emit and returns the highest one over (x, z).
    private static readonly float[] SurfNodes =
    {
        -90f, -70f, -55f, -46f, -34f, -24f, -16f, -10f, -5.2f, 0f,
        5.2f, 10f, 16f, 24f, 34f, 46f, 55f, 70f, 90f,
    };
    private static Vector3[,] _surfRows;
    private static TakaRoute _surfRoute;
    private static readonly Dictionary<long, bool> _fellNear = new Dictionary<long, bool>();

    private static void EnsureSurface()
    {
        if (_surfRows != null && _surfRoute == _route) return;
        _surfRoute = _route;
        _fellNear.Clear();
        var rows = new List<int>();
        for (int i = 0; i < _route.Count; i += CorridorStride) rows.Add(i);
        if (rows[rows.Count - 1] != _route.Count - 1) rows.Add(_route.Count - 1);
        _surfRows = new Vector3[rows.Count, SurfNodes.Length];
        for (int r = 0; r < rows.Count; r++)
        {
            var p = _route.Position[rows[r]];
            var s = _route.SideFlat(rows[r]);
            for (int c = 0; c < SurfNodes.Length; c++)
            {
                float o = SurfNodes[c];
                float px = p.x + s.x * o, pz = p.z + s.z * o;
                _surfRows[r, c] = new Vector3(px, Height(px, pz)
                    + 0.05f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 55f, Mathf.Abs(o))), pz);
            }
        }
    }

    private static bool TriY(Vector3 a, Vector3 b, Vector3 c, float x, float z, out float y)
    {
        y = 0f;
        float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
        if (Mathf.Abs(d) < 1e-6f) return false;
        float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
        float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
        float l3 = 1f - l1 - l2;
        const float E = -1e-4f;
        if (l1 < E || l2 < E || l3 < E) return false;
        y = l1 * a.y + l2 * b.y + l3 * c.y;
        return true;
    }

    private static bool FellCornerNear(int a, int b, float x0, float z0)
    {
        long key = ((long)a << 32) ^ (uint)b;
        if (_fellNear.TryGetValue(key, out bool near)) return near;
        NearestStation(x0 + a * BackgroundCellM, z0 + b * BackgroundCellM, out float d);
        near = d < BackgroundSkipRadiusM;
        _fellNear[key] = near;
        return near;
    }

    /// <summary>Height of the ground mesh actually rendered at (x, z).</summary>
    private static float SurfaceY(float x, float z)
    {
        EnsureSurface();
        float best = float.MinValue;
        int nr = _surfRows.GetLength(0), nc = SurfNodes.Length, mid = nc / 2;
        for (int r = 0; r < nr - 1; r++)
        {
            var m = _surfRows[r, mid];
            float dx = x - m.x, dz = z - m.z;
            if (dx * dx + dz * dz > 110f * 110f) continue;
            for (int c = 0; c < nc - 1; c++)
            {
                Vector3 A = _surfRows[r, c], B = _surfRows[r, c + 1], D = _surfRows[r + 1, c], E = _surfRows[r + 1, c + 1];
                if (TriY(A, D, B, x, z, out float y) && y > best) best = y;
                if (TriY(B, D, E, x, z, out y) && y > best) best = y;
            }
        }
        FieldBounds(_route, out float x0, out float z0, out _, out _);
        int ga = Mathf.FloorToInt((x - x0) / BackgroundCellM), gb = Mathf.FloorToInt((z - z0) / BackgroundCellM);
        if (!(FellCornerNear(ga, gb, x0, z0) && FellCornerNear(ga, gb + 1, x0, z0) &&
              FellCornerNear(ga + 1, gb, x0, z0) && FellCornerNear(ga + 1, gb + 1, x0, z0)))
        {
            float ax = x0 + ga * BackgroundCellM, az = z0 + gb * BackgroundCellM;
            float u = (x - ax) / BackgroundCellM, v = (z - az) / BackgroundCellM;
            float h00 = Height(ax, az), h01 = Height(ax, az + BackgroundCellM);
            float h10 = Height(ax + BackgroundCellM, az), h11 = Height(ax + BackgroundCellM, az + BackgroundCellM);
            float y = u + v <= 1f ? h00 + u * (h10 - h00) + v * (h01 - h00)
                                  : h11 + (1f - u) * (h01 - h11) + (1f - v) * (h10 - h11);
            if (y > best) best = y;
        }
        return best > float.MinValue ? best : Height(x, z);
    }

    /// <summary>
    /// A lavender field: parallel rows of rounded bushes (grey-green foliage below, purple bloom
    /// above) on ridged ochre soil, rows running with the road. The soil is its own terrain-
    /// following grid lifted 10 cm, with 12 cm ridges under the rows and a skirt dropping 0.6 m
    /// round the edge: the ground mesh is a coarse linear interpolation of Height(), so a sheet
    /// laid exactly on Height() both z-fights it and floats over it on the chords - the first
    /// pass's flat 4-quad ribbon cut straight through its own bushes ("a pale sheet with purple
    /// specks"). Returns the number of bushes.
    /// </summary>
    private static int LavenderField(ProvencePlot p, Bucket bloom, Bucket leaf, Bucket soil, System.Random rng)
    {
        const float Pitch = 1.7f, Lift = 0.10f, Ridge = 0.12f, Half = 0.6f;
        int rows = Mathf.Max(1, Mathf.FloorToInt((p.Wd - 1.8f) / Pitch) + 1);
        float W = 0.9f + (rows - 1) * Pitch + 0.9f;
        var vs = new List<float> { 0f }; var lifts = new List<float> { Lift };
        for (int r = 0; r < rows; r++)
        {
            float vr = 0.9f + r * Pitch;
            vs.Add(vr - Half); lifts.Add(Lift);
            vs.Add(vr); lifts.Add(Lift + Ridge);
            vs.Add(vr + Half); lifts.Add(Lift);
        }
        vs.Add(W); lifts.Add(Lift);
        int nu = Mathf.Max(2, Mathf.CeilToInt(p.L / 3f));
        var grid = new Vector3[nu + 1, vs.Count];
        for (int a = 0; a <= nu; a++)
            for (int b = 0; b < vs.Count; b++)
            {
                var q = p.C + p.Along * (-p.L * 0.5f + p.L * a / nu) + p.Out * vs[b];
                q.y = SurfaceY(q.x, q.z) + lifts[b];
                grid[a, b] = q;
            }
        for (int a = 0; a < nu; a++)
            for (int b = 0; b < vs.Count - 1; b++)
            {
                var below = (grid[a, b] + grid[a + 1, b + 1]) * 0.5f - Vector3.up * 2f;
                soil.TriAway(grid[a, b], grid[a + 1, b], grid[a, b + 1], below, 0.25f);
                soil.TriAway(grid[a + 1, b], grid[a + 1, b + 1], grid[a, b + 1], below, 0.25f);
            }
        // skirt round the perimeter
        var centre = p.C + p.Out * (W * 0.5f);
        void Skirt(Vector3 s0, Vector3 s1)
        {
            var d0 = new Vector3(s0.x, SurfaceY(s0.x, s0.z) - 0.6f, s0.z);
            var d1 = new Vector3(s1.x, SurfaceY(s1.x, s1.z) - 0.6f, s1.z);
            soil.TriAway(s0, s1, d0, centre, 0.25f);
            soil.TriAway(s1, d1, d0, centre, 0.25f);
        }
        for (int a = 0; a < nu; a++) { Skirt(grid[a, 0], grid[a + 1, 0]); Skirt(grid[a, vs.Count - 1], grid[a + 1, vs.Count - 1]); }
        for (int b = 0; b < vs.Count - 1; b++) { Skirt(grid[0, b], grid[0, b + 1]); Skirt(grid[nu, b], grid[nu, b + 1]); }

        int n = 0;
        for (int r = 0; r < rows; r++)
        {
            float vr = 0.9f + r * Pitch;
            for (float u = -p.L * 0.5f + 0.7f; u < p.L * 0.5f - 0.6f; u += 1.05f)
            {
                var q = p.C + p.Along * (u + ((float)rng.NextDouble() - 0.5f) * 0.16f) + p.Out * vr;
                q.y = SurfaceY(q.x, q.z) + Lift + Ridge - 0.10f;
                LavBush(bloom, leaf, q, p.Along, p.Out, 0.58f + 0.08f * (float)rng.NextDouble(),
                        0.50f + 0.06f * (float)rng.NextDouble(), 0.60f + 0.14f * (float)rng.NextDouble(), rng);
                n++;
            }
        }
        return n;
    }

    /// <summary>One lavender bush: a low 7-sided dome, grey-green lower band, purple top.</summary>
    private static void LavBush(Bucket bloom, Bucket leaf, Vector3 b, Vector3 along, Vector3 outw,
                                float rx, float rz, float h, System.Random rng)
    {
        const int Lon = 7;
        float[] pr = { 1.00f, 0.97f, 0.76f, 0.40f };
        float[] ph = { -0.05f, 0.36f, 0.70f, 0.93f };
        var ring = new Vector3[4, Lon];
        float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
        for (int k = 0; k < 4; k++)
            for (int x = 0; x < Lon; x++)
            {
                float th = yaw + (x + 0.5f * (k % 2)) * Mathf.PI * 2f / Lon;
                float j = 0.90f + 0.18f * (float)rng.NextDouble();
                ring[k, x] = b + along * (Mathf.Cos(th) * rx * pr[k] * j) + outw * (Mathf.Sin(th) * rz * pr[k] * j)
                               + Vector3.up * (h * ph[k] * (0.94f + 0.12f * (float)rng.NextDouble()));
            }
        var axis = b + Vector3.up * (h * 0.35f);
        var top = b + Vector3.up * h;
        for (int k = 0; k < 3; k++)
        {
            var bk = k == 0 ? leaf : bloom;
            for (int x = 0; x < Lon; x++)
            {
                int xn = (x + 1) % Lon;
                bk.TriAway(ring[k, x], ring[k + 1, x], ring[k, xn], axis, 0.5f);
                bk.TriAway(ring[k, xn], ring[k + 1, x], ring[k + 1, xn], axis, 0.5f);
            }
        }
        for (int x = 0; x < Lon; x++) bloom.TriAway(ring[3, x], top, ring[3, (x + 1) % Lon], axis, 0.5f);
    }

    /// <summary>An olive grove: trees on a jittered ~7.5 m grid. Returns the tree count.</summary>
    private static int OliveGrove(ProvencePlot p, Bucket leaf, Bucket bark, System.Random rng)
    {
        int n = 0;
        for (float v = 3.5f; v < p.Wd - 2.5f; v += 7f)
            for (float u = -p.L * 0.5f + 3.5f; u < p.L * 0.5f - 2.5f; u += 7.5f)
            {
                if (rng.NextDouble() < 0.10) continue;
                var f = p.C + p.Along * (u + ((float)rng.NextDouble() - 0.5f) * 2.2f)
                            + p.Out * (v + ((float)rng.NextDouble() - 0.5f) * 2.0f);
                if (NearestDist(f) < 9f) continue;
                f.y = SurfaceY(f.x, f.z);
                OliveTree(leaf, bark, f, 4.6f + 1.8f * (float)rng.NextDouble(), rng);
                n++;
            }
        return n;
    }

    /// <summary>
    /// Olive tree: a gnarled trunk of two kinked segments, limbs out to 4-5 squat silver-green
    /// canopy clumps plus a crown clump - a wide, rounded, open canopy about 1.2x as wide as
    /// the tree is tall, so it can never be mistaken for the dark conical pines of the band.
    /// </summary>
    private static void OliveTree(Bucket leaf, Bucket bark, Vector3 f, float h, System.Random rng)
    {
        Vector3 J(float s) => new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * s;
        var p0 = f - Vector3.up * 0.3f;
        var p1 = f + Vector3.up * (h * 0.17f) + J(0.6f);
        var p2 = p1 + Vector3.up * (h * 0.15f) + J(0.8f);
        Post(bark.V, bark.UV, bark.T, null, p0, p1, 0.26f, Color.white);
        Post(bark.V, bark.UV, bark.T, null, p1, p2, 0.20f, Color.white);
        var crown = p2 + Vector3.up * (h * 0.24f);
        int limbs = 4 + rng.Next(2);
        float a0 = (float)rng.NextDouble() * Mathf.PI * 2f;
        for (int k = 0; k < limbs; k++)
        {
            float ang = a0 + k * Mathf.PI * 2f / limbs + ((float)rng.NextDouble() - 0.5f) * 0.6f;
            float rr = h * (0.22f + 0.12f * (float)rng.NextDouble());
            var c = crown + new Vector3(Mathf.Cos(ang) * rr, h * (0.02f + 0.14f * (float)rng.NextDouble()), Mathf.Sin(ang) * rr);
            Post(bark.V, bark.UV, bark.T, null, p2, c - Vector3.up * (h * 0.07f), 0.09f, Color.white);
            float r = h * (0.20f + 0.06f * (float)rng.NextDouble());
            Rock(leaf, leaf, c, new Vector3(r * 1.1f, r * 0.62f, r), (float)rng.NextDouble() * 360f, 7, 3, rng,
                 snowBias: -9999f, uvScale: 0.5f);
        }
        Rock(leaf, leaf, crown + Vector3.up * (h * 0.10f), new Vector3(h * 0.22f, h * 0.14f, h * 0.22f),
             (float)rng.NextDouble() * 360f, 7, 3, rng, snowBias: -9999f, uvScale: 0.5f);
    }

    /// <summary>An empty named transform the capture pass aims at (forward = away from the road).</summary>
    private static void ShotMarker(Transform group, string name, Vector3 pos, Vector3 outward)
    {
        var go = new GameObject(name);
        go.transform.SetParent(group, false);
        go.transform.position = pos;
        outward.y = 0f;
        go.transform.rotation = Quaternion.LookRotation(outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.forward, Vector3.up);
    }
    private static void ShotMarker(Transform group, string name, ProvencePlot p)
    {
        var mid = p.C + p.Out * (p.Wd * 0.5f);
        mid.y = SurfaceY(mid.x, mid.z);
        ShotMarker(group, name, mid, p.Out);
    }

    /// <summary>A level gravel forecourt beside the road, from..to metres out on side s, lifted
    /// 4 cm over the ground (which is road level inside the 18 m road cut).</summary>
    private static void Forecourt(TakaRoute route, float d0, float d1, int s, float from, float to, Bucket b)
    {
        const int Across = 6;
        Vector3 P(int st, float o)
        {
            Foot(route, st, o, out var q, out _);
            q.y += 0.04f;
            return q;
        }
        Vector2 U(Vector3 q) => new Vector2(q.x * 0.22f, q.z * 0.22f);
        void T(Vector3 a, Vector3 c, Vector3 e)
        {
            if (Vector3.Cross(c - a, e - a).y < 0f) { var x = c; c = e; e = x; }
            b.Tri(a, c, e, U(a), U(c), U(e));
        }
        for (float d = d0; d < d1 - 0.01f; d += 4f)
        {
            int i = route.IndexAt(d), j = route.IndexAt(Mathf.Min(d + 4f, d1));
            for (int k = 0; k < Across; k++)
            {
                float o0 = s * Mathf.Lerp(from + 0.05f, to, k / (float)Across);
                float o1 = s * Mathf.Lerp(from + 0.05f, to, (k + 1) / (float)Across);
                var a0 = P(i, o0); var a1 = P(i, o1); var b0 = P(j, o0); var b1 = P(j, o1);
                T(a0, b0, a1); T(a1, b0, b1);
            }
        }
    }

    /// <summary>A queue of standers along the shop front.</summary>
    private static void Queue(TakaRoute route, int i, int s, int n,
                              List<(Vector3, Vector3, MinatoCrowdActor.MotionKind, Vector3)> spots)
    {
        float kerb = RoadHalfWidth + ShoulderWidth;
        Foot(route, i, s * (kerb + 3.2f), out var f, out var outward);
        f.y = Mathf.Max(f.y, RoadY(route, i, 0f));
        var along = Vector3.Cross(Vector3.up, outward).normalized;
        for (int k = 0; k < n; k++)
        {
            var p = f + along * (k * 0.85f);
            spots.Add((p, -along, MinatoCrowdActor.MotionKind.Idle, p));
        }
    }

    /// <summary>A loose chatting group on the pavement, facing each other.</summary>
    private static void Group(TakaRoute route, int i, int s, int n,
                              List<(Vector3, Vector3, MinatoCrowdActor.MotionKind, Vector3)> spots, System.Random rng)
    {
        float kerb = RoadHalfWidth + ShoulderWidth;
        Foot(route, i, s * (kerb + 2.4f), out var c, out _);
        c.y = Mathf.Max(c.y, RoadY(route, i, 0f));
        for (int k = 0; k < n; k++)
        {
            float a = k * Mathf.PI * 2f / n + (float)rng.NextDouble() * 0.4f;
            var p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.75f;
            if (NearestDist(p) < kerb + 0.6f) continue;
            p.y = c.y;
            spots.Add((p, c - p, MinatoCrowdActor.MotionKind.Idle, p));
        }
    }

    /// <summary>A market stall: table, canopy, crates of produce, a vendor and customers.</summary>
    private static void Stall(TakaRoute route, int i, int s, Bucket furn, Bucket canopy, Bucket crate, Bucket produce,
                              List<(Vector3, Vector3, MinatoCrowdActor.MotionKind, Vector3)> spots, System.Random rng)
    {
        float kerb = RoadHalfWidth + ShoulderWidth;
        Foot(route, i, s * (kerb + 3.4f), out var f, out var outward);
        f.y = Mathf.Max(f.y, RoadY(route, i, 0f) - 0.2f);
        outward.y = 0; outward.Normalize();
        var along = Vector3.Cross(Vector3.up, outward).normalized;
        Box(furn.V, furn.UV, furn.T, null, f, along * 1.4f, outward * 0.5f, 0.85f, Color.white);   // table
        for (int k = -1; k <= 1; k++)
        {
            var cc = f + along * (k * 0.9f) + Vector3.up * 0.85f;
            Box(crate.V, crate.UV, crate.T, null, cc, along * 0.38f, outward * 0.3f, 0.22f, Color.white);
            Rock(produce, produce, cc + Vector3.up * 0.2f, new Vector3(0.34f, 0.12f, 0.26f), 0f, 6, 2, rng, snowBias: -9999f, uvScale: 0.5f);
        }
        foreach (var px in new[] { -1.4f, 1.4f })
            foreach (var pz in new[] { -0.6f, 1.1f })
                Post(furn.V, furn.UV, furn.T, null, f + along * px + outward * pz, f + along * px + outward * pz + Vector3.up * 2.3f, 0.04f, Color.white);
        Band(canopy.V, canopy.UV, canopy.T, f + along * 1.6f - outward * 0.8f + Vector3.up * 2.2f, f - along * 1.6f - outward * 0.8f + Vector3.up * 2.2f,
             f + along * 1.6f + outward * 1.3f + Vector3.up * 2.5f, f - along * 1.6f + outward * 1.3f + Vector3.up * 2.5f, 0f);
        var vendor = f + outward * 0.9f; spots.Add((vendor, -outward, MinatoCrowdActor.MotionKind.Idle, vendor));
        int buyers = 1 + rng.Next(2);
        for (int k = 0; k < buyers; k++)
        {
            var p = f - outward * 1.1f + along * ((float)rng.NextDouble() * 2f - 1f);
            spots.Add((p, outward, MinatoCrowdActor.MotionKind.Idle, p));
        }
    }

    private static void FixUp(Bucket b)
    {
        for (int t = 0; t < b.T.Count; t += 3)
        {
            var a = b.V[b.T[t]]; var c1 = b.V[b.T[t + 1]]; var c2 = b.V[b.T[t + 2]];
            if (Vector3.Cross(c1 - a, c2 - a).y < 0f) { int x = b.T[t + 1]; b.T[t + 1] = b.T[t + 2]; b.T[t + 2] = x; }
        }
    }

    private static void Townhouse(Bucket wall, Bucket roof, Bucket shut, Bucket door, Vector3 f, Vector3 outward,
                                  float W, float D, float H, System.Random rng)
    {
        outward.y = 0f; outward.Normalize();
        var along = Vector3.Cross(Vector3.up, outward).normalized;
        var c = f + outward * (D * 0.5f);   // front face sits at f, body extends away from the road
        Box(wall.V, wall.UV, wall.T, null, c - Vector3.up * 1.5f, along * (W * 0.5f), outward * (D * 0.5f), H + 1.5f, Color.white);
        float eave = f.y + H;
        var a0 = c + along * (W * 0.55f) - outward * (D * 0.58f); a0.y = eave - 0.1f;
        var a1 = c - along * (W * 0.55f) - outward * (D * 0.58f); a1.y = eave - 0.1f;
        var b0 = c + along * (W * 0.55f) + outward * (D * 0.58f); b0.y = eave - 0.1f;
        var b1 = c - along * (W * 0.55f) + outward * (D * 0.58f); b1.y = eave - 0.1f;
        var r0 = c + along * (W * 0.55f); r0.y = eave + D * 0.22f;
        var r1 = c - along * (W * 0.55f); r1.y = eave + D * 0.22f;
        Band(roof.V, roof.UV, roof.T, a0, a1, r0, r1, 0f);
        Band(roof.V, roof.UV, roof.T, b0, b1, r0, r1, 0f);
        wall.Tri2(a0 + outward * 0.1f, b0 - outward * 0.1f, r0);
        wall.Tri2(a1 + outward * 0.1f, b1 - outward * 0.1f, r1);
        // shutters: pairs either side of each window, on the road face, per storey
        int storeys = Mathf.Max(1, Mathf.FloorToInt(H / 2.9f));
        int bays = Mathf.Max(2, Mathf.FloorToInt(W / 2.6f));
        for (int st = 0; st < storeys; st++)
            for (int bay = 0; bay < bays; bay++)
            {
                float x = (bay + 0.5f) / bays * W - W * 0.5f;
                var wc = f + along * x + Vector3.up * (1.2f + st * 2.9f) - outward * 0.06f;
                if (st == 0 && bay == bays / 2)
                {
                    Box(door.V, door.UV, door.T, null, wc - Vector3.up * 1.2f, along * 0.6f, outward * 0.08f, 2.3f, Color.white);
                    continue;
                }
                Box(door.V, door.UV, door.T, null, wc, along * 0.45f, outward * 0.05f, 1.4f, Color.white);   // dark window
                Box(shut.V, shut.UV, shut.T, null, wc - along * 0.78f - outward * 0.04f, along * 0.3f, outward * 0.05f, 1.5f, Color.white);
                Box(shut.V, shut.UV, shut.T, null, wc + along * 0.78f - outward * 0.04f, along * 0.3f, outward * 0.05f, 1.5f, Color.white);
            }
    }

    private static void PlaneTree(Bucket bark, Bucket leaf, Vector3 f, float h, System.Random rng)
    {
        Post(bark.V, bark.UV, bark.T, null, f - Vector3.up * 0.3f, f + Vector3.up * h * 0.62f, 0.22f, Color.white);
        for (int k = 0; k < 4; k++)
        {
            var c = f + Vector3.up * h * (0.6f + 0.12f * (float)rng.NextDouble())
                      + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * h * 0.35f;
            float r = h * (0.22f + 0.08f * (float)rng.NextDouble());
            Rock(leaf, leaf, c, new Vector3(r, r * 0.75f, r), (float)rng.NextDouble() * 360f, 7, 4, rng, snowBias: -9999f, uvScale: 0.5f);
        }
    }

    private static void CafeFront(TakaRoute route, int i, int s, string signFile, Bucket awnA, Bucket awnB, Bucket furn,
                                  List<(Vector3, Vector3, MinatoCrowdActor.MotionKind, Vector3)> spots, System.Random rng,
                                  int tables, Transform group, float frontOff = -1f)
    {
        float kerb = RoadHalfWidth + ShoulderWidth;
        float face = frontOff > 0f ? frontOff : kerb + 7.5f;
        Foot(route, i, s * face, out var f, out var outward);
        f.y = Mathf.Max(f.y, RoadY(route, i, 0f) - 0.4f);
        outward.y = 0; outward.Normalize();
        var along = Vector3.Cross(Vector3.up, outward).normalized;
        float W = 3f + tables * 1.6f;
        // striped awning: alternating red/white strips sloping toward the road
        int strips = Mathf.CeilToInt(W / 0.8f);
        for (int k = 0; k < strips; k++)
        {
            float x0 = -W * 0.5f + k * W / strips, x1 = x0 + W / strips;
            var b = k % 2 == 0 ? awnA : awnB;
            Band(b.V, b.UV, b.T, f + along * x0 + Vector3.up * 3.4f, f + along * x1 + Vector3.up * 3.4f,
                 f + along * x0 - outward * 2.6f + Vector3.up * 2.7f, f + along * x1 - outward * 2.6f + Vector3.up * 2.7f, 0f);
        }
        SignBoard(group, "Shop Sign", signFile, f - outward * 0.12f + Vector3.up * 4.1f, along, Mathf.Min(W * 0.8f, 5f), Mathf.Min(W * 0.8f, 5f) * 0.1875f);
        // terrace tables + parasol-less bistro sets, a seated/standing customer at each
        for (int t = 0; t < tables; t++)
        {
            float x = -W * 0.5f + (t + 0.5f) * W / tables;
            float depth = 1.4f + (t % 2) * 1.6f;
            if (face - depth < kerb + 0.9f) depth = face - kerb - 0.9f;
            var tc = f + along * x - outward * depth;
            Post(furn.V, furn.UV, furn.T, null, tc, tc + Vector3.up * 0.74f, 0.04f, Color.white);
            Drum(furn, tc + Vector3.up * 0.72f, 0.38f, 0.04f, 8);
            spots.Add((tc + along * 0.6f, -along, MinatoCrowdActor.MotionKind.Idle, tc));
            if (rng.NextDouble() < 0.6) spots.Add((tc - along * 0.6f, along, MinatoCrowdActor.MotionKind.Idle, tc));
        }
    }

    private static void ParkedBikes(TakaRoute route, int i, int s, int count, Bucket frame, Bucket colour,
                                    List<(Vector3, Vector3, MinatoCrowdActor.MotionKind, Vector3)> spots, System.Random rng)
    {
        float kerb = RoadHalfWidth + ShoulderWidth;
        Foot(route, i, s * (kerb + 1.5f), out var f, out var outward);
        f.y = Mathf.Max(f.y, RoadY(route, i, 0f) - 0.1f);
        outward.y = 0; outward.Normalize();
        var along = Vector3.Cross(Vector3.up, outward).normalized;
        // rack rail
        Post(frame.V, frame.UV, frame.T, null, f - along * (count * 0.35f) + Vector3.up * 0.7f,
             f + along * (count * 0.35f) + Vector3.up * 0.7f, 0.03f, Color.white);
        for (int k = 0; k < count; k++)
        {
            var c = f + along * ((k - count * 0.5f) * 0.7f);
            // bike stands perpendicular to the rail, front wheel against it
            var dir = outward;
            var w0 = c + dir * 0.2f + Vector3.up * 0.34f; var w1 = c + dir * 1.2f + Vector3.up * 0.34f;
            Wheel(frame, w0, along, 0.34f); Wheel(frame, w1, along, 0.34f);
            var bb = c + dir * 0.75f + Vector3.up * 0.3f;
            var seat = c + dir * 0.95f + Vector3.up * 0.9f; var head = c + dir * 0.35f + Vector3.up * 0.85f;
            Post(colour.V, colour.UV, colour.T, null, bb, seat, 0.025f, Color.white);
            Post(colour.V, colour.UV, colour.T, null, bb, head, 0.025f, Color.white);
            Post(colour.V, colour.UV, colour.T, null, seat - dir * 0.05f, head, 0.022f, Color.white);
            Post(frame.V, frame.UV, frame.T, null, head, w0, 0.018f, Color.white);
            Post(frame.V, frame.UV, frame.T, null, bb, w1, 0.018f, Color.white);
            Post(frame.V, frame.UV, frame.T, null, head - along * 0.2f, head + along * 0.2f, 0.018f, Color.white);
            if (rng.NextDouble() < 0.45)
            {
                var p = c + dir * 1.7f + along * 0.3f; p.y = f.y;
                spots.Add((p, -dir, MinatoCrowdActor.MotionKind.Idle, p));
            }
        }
    }

    private static void Wheel(Bucket b, Vector3 c, Vector3 axle, float r)
    {
        var u = Vector3.up; var v = Vector3.Cross(axle, u).normalized;
        const int N = 10;
        for (int k = 0; k < N; k++)
        {
            float a0 = k * Mathf.PI * 2f / N, a1 = (k + 1) * Mathf.PI * 2f / N;
            Post(b.V, b.UV, b.T, null, c + (u * Mathf.Cos(a0) + v * Mathf.Sin(a0)) * r,
                 c + (u * Mathf.Cos(a1) + v * Mathf.Sin(a1)) * r, 0.018f, Color.white);
        }
    }

    private static void Drum(Bucket b, Vector3 baseC, float r, float h, int sides)
    {
        var top = baseC + Vector3.up * h;
        for (int k = 0; k < sides; k++)
        {
            float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
            var p0 = new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r); var p1 = new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
            b.TriAway(baseC + p0, baseC + p1, top + p0, baseC + Vector3.up * h * 0.5f, 0.3f);
            b.TriAway(baseC + p1, top + p1, top + p0, baseC + Vector3.up * h * 0.5f, 0.3f);
            b.TriAway(top + p0, top + p1, top, top - Vector3.up, 0.3f);
        }
    }

    private static void SignBoard(Transform group, string name, string file, Vector3 centre, Vector3 along, float w, float h)
    {
        var bk = new Bucket(name.Replace(" ", ""), SignMat(file));
        along.y = 0; along.Normalize();
        // Two faces, each readable from its own side. They used to be COPLANAR on a _Cull 0
        // material, so the two fought in the depth buffer and the road-side face lost on the
        // +1 side of the road (CAFE DU COL / CHALET REYNAUD rendered mirrored). The back face
        // now sits 4 cm behind the front one (normal = Cross(along, up) points away from the
        // reader of the front face).
        var back = Vector3.Cross(along, Vector3.up).normalized * 0.04f;
        var a = centre - along * (w * 0.5f) - Vector3.up * (h * 0.5f);
        var b = centre + along * (w * 0.5f) - Vector3.up * (h * 0.5f);
        var c = centre - along * (w * 0.5f) + Vector3.up * (h * 0.5f);
        var d = centre + along * (w * 0.5f) + Vector3.up * (h * 0.5f);
        // Remap the old hard-coded full 0..1 UVs into this sign's sub-rect on the shared trim
        // sheet (falls back to the old full-texture 0..1 rect if `file` isn't in the table).
        var r = TakaSignUV.TryGetValue(file, out var rect) ? rect : new Rect(0, 0, 1, 1);
        Vector2 uv00 = new Vector2(r.xMin, r.yMin), uv01 = new Vector2(r.xMin, r.yMax);
        Vector2 uv10 = new Vector2(r.xMax, r.yMin), uv11 = new Vector2(r.xMax, r.yMax);
        bk.Tri(a, c, b, uv00, uv01, uv10);
        bk.Tri(b, c, d, uv10, uv01, uv11);
        bk.Tri(a + back, b + back, c + back, uv10, uv00, uv11);
        bk.Tri(b + back, d + back, c + back, uv00, uv01, uv11);
        bk.Part = _signSerial++;
        bk.Flush(group);
    }
    private static int _signSerial;

    /// <summary>Clone Minato crowd donors onto the collected spots (MapleCityLife approach).</summary>
    private static int SpawnCrowd(Transform group, Transform takaRoot,
                                  List<(Vector3 pos, Vector3 face, MinatoCrowdActor.MotionKind kind, Vector3 walkTo)> spots,
                                  System.Random rng)
    {
        var walkers = new List<MinatoCrowdActor>(); var standers = new List<MinatoCrowdActor>();
        var seen = new HashSet<string>();
        foreach (var a in Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (a == null || a.transform.IsChildOf(takaRoot)) continue;
            if (!a.name.StartsWith("Crowd_", System.StringComparison.Ordinal)) continue;
            if (a.transform.Find("LOD0 High Skinned/Rigged Character") == null) continue;
            if (!seen.Add(a.name)) continue;
            if (a.motion == MinatoCrowdActor.MotionKind.Walk) walkers.Add(a);
            else if (a.motion != MinatoCrowdActor.MotionKind.Sit) standers.Add(a);
        }
        walkers.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        standers.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        if (walkers.Count + standers.Count == 0)
        {
            Debug.LogWarning("[taka] no Minato crowd donors in the scene - Ventoux crowds empty.");
            return 0;
        }
        if (walkers.Count == 0) walkers = standers;
        if (standers.Count == 0) standers = walkers;

        var crowd = new GameObject("Taka Ventoux Crowd").transform;
        crowd.SetParent(group, false);
        int n = 0;
        foreach (var sp in spots)
        {
            bool walk = sp.kind == MinatoCrowdActor.MotionKind.Walk;
            var src = walk ? walkers[rng.Next(walkers.Count)] : standers[rng.Next(standers.Count)];
            var face = sp.face; face.y = 0f;
            if (face.sqrMagnitude < 1e-4f) face = Vector3.forward;
            var go = (GameObject)Object.Instantiate(src.gameObject, crowd);
            go.name = $"TakaCrowd_{n:000}_{src.name}";
            go.hideFlags = HideFlags.None;
            go.SetActive(true);
            var bench = go.transform.Find("Timber Waterfront Bench");
            if (bench != null) Object.DestroyImmediate(bench.gameObject);
            go.transform.SetPositionAndRotation(sp.pos, Quaternion.LookRotation(face.normalized, Vector3.up));
            go.transform.localScale = Vector3.one * (0.94f + 0.12f * (float)rng.NextDouble());
            var rig = go.transform.Find("LOD0 High Skinned/Rigged Character");
            var actor = go.GetComponent<MinatoCrowdActor>();
            if (actor != null && rig != null)
            {
                var kind = walk ? MinatoCrowdActor.MotionKind.Walk
                                : (rng.NextDouble() < 0.25 ? MinatoCrowdActor.MotionKind.Wave : MinatoCrowdActor.MotionKind.Idle);
                actor.Configure(kind, rig, sp.pos, walk ? sp.walkTo : sp.pos, walk ? 1.2f + 0.3f * (float)rng.NextDouble() : 0f,
                                (float)rng.NextDouble() * 10f);
                actor.armDropDegrees = 32f;
            }
            n++;
        }
        return n;
    }
}
