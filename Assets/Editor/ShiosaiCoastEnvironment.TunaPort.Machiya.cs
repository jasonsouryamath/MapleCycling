using UnityEngine;

// Tuna port, part 7: the HIGH-DETAIL machiya / minka kit. Real architectural parts rather than
// painted boxes: stone plinth, corner posts and nuki rails, cedar koshi-ita skirts, recessed
// windows with modelled lattice bars and glass, a hisashi pent roof with rafter ends, kirizuma
// gables with hafu boards, modelled round eave tiles (nokigawara), a layered noshi ridge with
// onigawara, plus side-wall life (AC units, drainpipes, gas bottles, potted plants). All PBR.
// `detail` 2 = street hero (shop rows / first terrace), 1 = mid terraces, 0 = far hillside.
public static partial class ShiosaiCoastEnvironment
{
    private static Material TPPlaster(int i) => i == 0 ? PMP("Plaster0", "Plaster", Color.white, 3f)
                                              : i == 1 ? PMP("Plaster1", "Plaster", new Color(0.93f, 0.91f, 0.86f), 3f)
                                              : PMP("Plaster2", "Plaster", new Color(0.84f, 0.85f, 0.86f), 3f);
    private static Material TPCedar => PMP("Cedar", "Cedar", Color.white, 1.8f, 0.7f);
    private static Material TPCedarDark => PMP("Yakisugi", "Yakisugi", Color.white, 1.8f, 0.7f);
    private static Material TPSiding(int i) => i == 0 ? PMP("Siding0", "Siding", Color.white, 3f)
                                             : PMP("Siding1", "Siding", new Color(0.78f, 0.84f, 0.88f), 3f);
    private static Material TPKawara(int i) => i == 0 ? PMP("Kawara0", "Kawara", Color.white, 1.8f, 1.3f)
                                             : i == 1 ? PMP("Kawara1", "Kawara", new Color(0.80f, 0.82f, 0.86f), 1.8f, 1.3f)
                                             : i == 2 ? PMP("KawaraBlue", "Kawara", new Color(0.55f, 0.72f, 1.05f), 1.8f, 1.3f)
                                             : PMP("KawaraRed", "Kawara", new Color(1.25f, 0.70f, 0.52f), 1.8f, 1.3f);
    private static Material TPTileEnd(int i) => i == 3 ? PM("TileEndRed", new Color(0.40f, 0.20f, 0.15f), gloss: 0.45f)
                                              : i == 2 ? PM("TileEndBlue", new Color(0.14f, 0.22f, 0.36f), gloss: 0.5f)
                                              : PM("TileEnd", new Color(0.17f, 0.18f, 0.20f), gloss: 0.45f);
    private static Material TPRidge => PM("Ridge", new Color(0.12f, 0.13f, 0.15f), gloss: 0.45f);
    private static Material TPStone => PMP("Stone", "Stone", Color.white, 2.4f, 1.3f);
    private static Material TPKoshiWood => PM("KoshiWood", new Color(0.28f, 0.19f, 0.13f), gloss: 0.25f);
    private static Material TPHafu => PM("Hafu", new Color(0.80f, 0.80f, 0.78f), gloss: 0.2f);
    private static Material TPWindowGlass => PM("WindowGlass", new Color(0.05f, 0.07f, 0.09f), gloss: 0.92f);
    private static Material TPShoji => PM("Shoji", new Color(0.86f, 0.84f, 0.78f), gloss: 0.05f);
    private static Material TPAlu => PM("Aluminium", new Color(0.62f, 0.63f, 0.64f), gloss: 0.6f);
    private static Material TPPlant => PM("Plant", new Color(0.12f, 0.30f, 0.10f), gloss: 0.2f);
    private static Material TPPot => PM("Pot", new Color(0.46f, 0.24f, 0.14f), gloss: 0.25f);
    private static Material TPIceTray => PM("IceTray", Color.white, "TP_IceTray", gloss: 0.7f);
    private static Material TPMaguroCuts => PM("MaguroCuts", Color.white, "TP_MaguroCuts", gloss: 0.5f);
    private static Material TPShopSign(int i) => PM($"ShopSign{i}", Color.white, $"TP_ShopSign_{i}", gloss: 0.3f);
    private static Material TPNoren(int i) => PM($"Noren{i}", Color.white, $"TP_Noren_{i}", twoSided: true, gloss: 0.05f);
    private static Material TPNobori(int i) => PM($"Nobori{i}", Color.white, $"TP_Nobori_{i}", twoSided: true, gloss: 0.05f);
    private static Material TPCanvas(int i) => i == 0 ? PM("CanvasWhite", new Color(0.86f, 0.87f, 0.88f), twoSided: true, gloss: 0.08f)
                                             : PM("CanvasBlue", new Color(0.10f, 0.28f, 0.62f), twoSided: true, gloss: 0.08f);

    /// <summary>
    /// One building. frontBase = centre of the street wall at FLOOR level; F points to the street.
    /// shop &gt;= 0: open fishmonger front (sign index), -1: house.
    /// </summary>
    private static void PMinka(PortBins big, PortBins small, Vector3 frontBase, Vector3 F, float w, float d,
                               int storeys, int palette, int shop, System.Random r, PortCast cast, Transform people,
                               int detail = 2)
    {
        F.y = 0f; F.Normalize();
        var A = Vector3.Cross(Vector3.up, F).normalized;
        var up = Vector3.up;
        float fy = frontBase.y;
        var C = frontBase - F * (d * 0.5f);
        float h1 = 2.9f, h2 = storeys >= 2 ? 2.55f : 0f;
        float y1 = fy + 0.35f;
        float wallTop = y1 + h1 + h2;
        bool modern = shop < 0 && palette % 4 == 3;
        var wallMat = modern ? TPSiding(palette % 2) : palette % 3 == 2 ? TPCedar : TPPlaster(palette % 3);
        var lowMat = modern ? TPSiding(palette % 2) : palette % 2 == 0 ? TPCedarDark : TPCedar;
        int roofIdx = modern ? (palette % 3 == 0 ? 2 : 1) : palette % 7 == 5 ? 3 : palette % 2;
        var roofMat = TPKawara(roofIdx);
        Vector3 L(float x, float y, float z) => C + A * x + up * (y - fy) + F * z;   // local (along, abs y, toward street)

        // ---- stone plinth (ishigaki), deep enough for any slope ------------------------------
        PBox(big[TPStone], new Vector3(C.x, fy - 1.9f, C.z) + up * 0.12f, A * (w * 0.5f + 0.15f), up * 2.15f, F * (d * 0.5f + 0.15f));
        PBox(small[TPConcreteDark], L(0, y1 - 0.04f, 0), A * (w * 0.5f + 0.05f), up * 0.05f, F * (d * 0.5f + 0.05f));

        // ---- walls ----------------------------------------------------------------------------
        const float recess = 2.5f;
        if (shop < 0)
            PBox(big[wallMat], L(0, y1 + h1 * 0.5f, 0), A * (w * 0.5f), up * (h1 * 0.5f), F * (d * 0.5f));
        else
        {
            PBox(big[TPCedarDark], L(0, y1 + h1 * 0.5f, -recess * 0.5f), A * (w * 0.5f), up * (h1 * 0.5f), F * (d * 0.5f - recess * 0.5f));
            foreach (int sg in new[] { -1, 1 })
                PBox(big[lowMat], L(sg * (w * 0.5f - 0.08f), y1 + h1 * 0.5f, d * 0.5f - recess * 0.5f), A * 0.08f, up * (h1 * 0.5f), F * (recess * 0.5f));
            PQ(_walk[TPWetFloor], L(w * 0.5f, y1, d * 0.5f), L(-w * 0.5f, y1, d * 0.5f), L(-w * 0.5f, y1, d * 0.5f - recess), L(w * 0.5f, y1, d * 0.5f - recess),
               Vector2.zero, new Vector2(w, 0), new Vector2(w, recess), new Vector2(0, recess), up);
        }
        if (h2 > 0f)
            PBox(big[wallMat], L(0, y1 + h1 + h2 * 0.5f, 0), A * (w * 0.5f), up * (h2 * 0.5f), F * (d * 0.5f), 1f, shop >= 0);
        else if (shop >= 0)
            PBox(big[TPCedarDark], L(0, y1 + h1 - 0.12f, 0), A * (w * 0.5f), up * 0.12f, F * (d * 0.5f), 1f, true);

        // ---- timber frame: corner posts, nuki rail, floor band, koshi-ita skirt ---------------
        if (!modern && detail >= 1)
        {
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    PBox(big[TPKoshiWood], L(sx * (w * 0.5f - 0.02f), y1 + (h1 + h2) * 0.5f, sz * (d * 0.5f - 0.02f)),
                         A * 0.1f, up * ((h1 + h2) * 0.5f), F * 0.1f);
            PBox(small[TPKoshiWood], L(0, y1 + h1, 0), A * (w * 0.5f + 0.05f), up * 0.1f, F * (d * 0.5f + 0.05f));
            if (wallMat != TPCedar && shop < 0)
                PBox(big[TPCedar], L(0, y1 + 0.5f, 0), A * (w * 0.5f + 0.03f), up * 0.5f, F * (d * 0.5f + 0.03f));
        }

        // ---- street elevation -------------------------------------------------------------------
        float zf = d * 0.5f;
        if (shop < 0)
        {
            if (modern)
            {
                AluWindow(big, small, L(-w * 0.18f, y1 + 1.55f, zf), A, F, 1.7f, 1.2f);
                PBox(big[TPKoshiWood], L(w * 0.3f, y1 + 1.05f, zf + 0.03f), A * 0.46f, up * 1.05f, F * 0.04f);   // door
                PBox(small[TPAlu], L(w * 0.3f, y1 + 2.2f, zf + 0.35f), A * 0.6f, up * 0.04f, F * 0.35f);        // door canopy
            }
            else
            {
                KoshiWindow(big, small, L(w * 0.1f, y1 + 1.35f, zf), A, F, w * 0.55f, 1.8f, detail);
                PBox(big[TPKoshiWood], L(-w * 0.34f, y1 + 1.05f, zf + 0.03f), A * 0.46f, up * 1.05f, F * 0.04f);
                PCard(small[TPShoji], L(-w * 0.34f, y1 + 1.35f, zf + 0.075f), A * 0.36f, up * 0.62f, F);
            }
        }
        else ShopFront(big, small, L(0, y1, zf), A, F, w, h1, recess, shop, r, cast, people);

        if (h2 > 0f)
        {
            float wy = y1 + h1 + 1.3f;
            if (modern)
            {
                AluWindow(big, small, L(0, wy, zf), A, F, 2.4f, 1.1f);
                // balcony with railing and a laundry pole
                PBox(big[TPConcrete], L(0, y1 + h1 + 0.12f, zf + 0.55f), A * (w * 0.4f), up * 0.1f, F * 0.55f, 1f, true);
                for (float x = -w * 0.4f; x <= w * 0.4f + 0.01f; x += 0.12f)
                    PBox(small[TPAlu], L(x, y1 + h1 + 0.72f, zf + 1.07f), A * 0.012f, up * 0.5f, F * 0.012f);
                PBox(small[TPAlu], L(0, y1 + h1 + 1.24f, zf + 1.07f), A * (w * 0.4f), up * 0.03f, F * 0.04f);
                PCyl(small[TPAlu], L(-w * 0.38f, y1 + h1 + 1.9f, zf + 0.8f), A, 0.02f, 0.02f, w * 0.76f, 6, false);
                if (r.NextDouble() < 0.6)
                    for (int k = 0; k < 4; k++)
                        PCard(small[k % 2 == 0 ? TPCanvas(0) : TPCanvas(1)], L(-w * 0.3f + k * 0.62f, y1 + h1 + 1.55f, zf + 0.8f),
                              A * 0.24f, up * 0.34f, F, twoFaced: true);
            }
            else if (shop >= 0) KoshiWindow(big, small, L(0, wy + 0.2f, zf), A, F, w * 0.62f, 0.9f, detail);
            else KoshiWindow(big, small, L(0, wy, zf), A, F, w * 0.5f, 1.2f, detail);
            // back window
            if (detail >= 1) AluWindow(big, small, L(0, wy, -zf), -A, -F, 1.4f, 1.0f);
        }

        // ---- hisashi (pent roof) with rafter ends -----------------------------------------------
        if (h2 > 0f || shop >= 0)
        {
            float reach = shop >= 0 ? 1.35f : 0.85f;
            var s = (F * Mathf.Cos(22f * Mathf.Deg2Rad) - up * Mathf.Sin(22f * Mathf.Deg2Rad)).normalized;
            var root = L(0, y1 + h1 + 0.28f, zf);
            RoofSlab(big, small, root, A, s, w * 0.5f + 0.12f, reach, roofMat, roofIdx, detail);
            if (detail >= 1)
                for (float x = -w * 0.5f + 0.2f; x < w * 0.5f; x += 0.45f)
                    PBox(small[TPKoshiWood], root + A * x + s * (reach * 0.5f) - Vector3.up * 0.1f, A * 0.035f, up * 0.045f, s * (reach * 0.5f));
        }

        // ---- kirizuma gable roof ------------------------------------------------------------------
        const float over = 0.65f;
        float pitch = modern ? 22f : 27f;
        float tan = Mathf.Tan(pitch * Mathf.Deg2Rad);
        var ridge = L(0, wallTop + d * 0.5f * tan, 0);
        float verge = w * 0.5f + 0.4f;
        foreach (int sg in new[] { -1, 1 })
        {
            var down = (F * sg * Mathf.Cos(pitch * Mathf.Deg2Rad) - up * Mathf.Sin(pitch * Mathf.Deg2Rad)).normalized;
            float len = (d * 0.5f + over) / Mathf.Cos(pitch * Mathf.Deg2Rad);
            RoofSlab(big, small, ridge + up * 0.02f, A, down, verge, len, roofMat, roofIdx, detail);
            // rafter ends under the main eave (street + back)
            if (detail >= 2)
            {
                var eave = ridge + down * len;
                for (float x = -w * 0.5f + 0.2f; x < w * 0.5f; x += 0.45f)
                    PBox(small[TPKoshiWood], eave + A * x - down * 0.45f - up * 0.12f, A * 0.04f, up * 0.05f, down * 0.42f);
            }
        }
        // gable infill + hafu boards
        foreach (int sg in new[] { -1, 1 })
        {
            var p0 = L(sg * w * 0.5f, wallTop, zf); var p1 = L(sg * w * 0.5f, wallTop, -zf);
            var p2 = ridge + A * (sg * w * 0.5f);
            PTri(big[wallMat], p0, p1, p2, new Vector2(0, 0), new Vector2(d, 0), new Vector2(d * 0.5f, d * 0.5f * tan), A * sg);
            if (!modern && detail >= 1)
            {
                var e0 = L(sg * verge, wallTop - over * tan, zf + over) + up * 0.05f;
                var e1 = L(sg * verge, wallTop - over * tan, -zf - over) + up * 0.05f;
                var rp = ridge + A * (sg * verge) + up * 0.08f;
                Beam(small[TPHafu], e0, rp, 0.11f);
                Beam(small[TPHafu], e1, rp, 0.11f);
                // a small louvred vent in the gable
                PBox(small[TPKoshiWood], ridge + A * (sg * (w * 0.5f + 0.02f)) - up * (d * 0.22f * tan + 0.4f), A * 0.03f, up * 0.25f, F * 0.35f);
            }
        }
        // layered noshi ridge + round cap + onigawara
        {
            var ridgeTop = ridge + up * 0.1f;
            PBox(big[TPRidge], ridgeTop + up * 0.08f, A * verge, up * 0.08f, F * 0.26f);
            if (detail >= 1)
            {
                PBox(big[TPRidge], ridgeTop + up * 0.22f, A * verge, up * 0.06f, F * 0.2f);
                PBox(big[TPRidge], ridgeTop + up * 0.33f, A * verge, up * 0.05f, F * 0.15f);
                PCyl(small[TPRidge], ridgeTop + up * 0.42f - A * verge, A, 0.1f, 0.1f, verge * 2f, 10, true);
                foreach (int sg in new[] { -1, 1 })
                {
                    var o = ridgeTop + A * (sg * (verge + 0.08f)) + up * 0.28f;
                    PBox(big[TPRidge], o, A * 0.1f, up * 0.3f, F * 0.3f);
                    PEllipsoid(big[TPRidge], o + up * 0.3f, A * 0.1f, up * 0.2f, F * 0.3f, 12, 6);
                }
            }
        }

        // ---- life on the side walls -----------------------------------------------------------------
        if (detail >= 1)
            RoofExtras(small, C, A, F, ridge, d, w, pitch, wallTop, y1, h1, h2 > 0f, modern);
        if (detail >= 1)
        {
            int sgs = r.NextDouble() < 0.5 ? -1 : 1;
            var sideN = A * sgs;
            if (r.NextDouble() < 0.7)
            {
                // AC outdoor unit on a bracket + its pipe
                var ac = L(sgs * (w * 0.5f + 0.32f), y1 + (h2 > 0f ? h1 + 0.4f : 0.45f), -d * 0.15f);
                PBox(big[TPWhitePaint], ac, sideN * 0.3f, up * 0.28f, F * 0.4f);
                PEllipsoid(small[TPBlack], ac + sideN * 0.31f + F * 0.1f, F * 0.2f, up * 0.2f, sideN * 0.01f, 14, 8);
                PCyl(small[TPWhitePaint], ac - sideN * 0.2f + up * 0.2f, up, 0.04f, 0.04f, 0.8f, 6, false);
            }
            if (r.NextDouble() < 0.8)
                PCyl(small[TPAlu], L(sgs * (w * 0.5f + 0.06f), y1 - 0.3f, zf - 0.25f), up, 0.05f, 0.05f, wallTop - y1 + 0.2f, 8, false);
            if (r.NextDouble() < 0.4)
                for (int k = 0; k < 2; k++)
                    PCyl(small[TPCrateBlue], L(sgs * (w * 0.5f + 0.25f), y1, -zf + 0.6f + k * 0.5f), up, 0.18f, 0.18f, 1.0f, 12, true);
            if (shop < 0 && r.NextDouble() < 0.7)
                for (int k = 0; k < 3; k++)
                {
                    var pp = L(-w * 0.2f + k * 0.5f + (float)r.NextDouble() * 0.2f, y1, zf + 0.35f);
                    PCyl(small[TPPot], pp, up, 0.16f, 0.2f, 0.32f, 10, true);
                    PEllipsoid(small[TPPlant], pp + up * 0.5f, A * 0.24f, up * 0.28f, F * 0.24f, 14, 8);
                }
        }
    }

    /// <summary>A roof slope from its top edge `root` running `length` down `down`, with modelled
    /// round eave tiles along the bottom edge (detail 2) and a thickness board.</summary>
    private static void RoofSlab(PortBins big, PortBins small, Vector3 root, Vector3 A, Vector3 down, float halfW, float length,
                                 Material roofMat, int roofIdx, int detail)
    {
        var nr = Vector3.Cross(down, A).normalized; if (nr.y < 0) nr = -nr;
        var c = root + down * (length * 0.5f) + nr * 0.06f;
        PBox(big[roofMat], c, A * halfW, nr * 0.06f, down * (length * 0.5f), 1f, true);
        if (detail >= 2)
        {
            var eave = root + down * length + nr * 0.1f;
            for (float x = -halfW + 0.15f; x < halfW; x += 0.3f)
                PCyl(small[TPTileEnd(roofIdx)], eave + A * x - down * 0.08f, down, 0.085f, 0.085f, 0.1f, 8, true);
            PBox(small[TPTileEnd(roofIdx)], eave - nr * 0.06f + down * 0.01f, A * halfW, nr * 0.04f, down * 0.03f);
        }
    }

    /// <summary>Machiya street window: timber frame, dark glass, and MODELLED vertical koshi bars
    /// (detail 2) or a lattice card (far).</summary>
    private static void KoshiWindow(PortBins big, PortBins small, Vector3 c, Vector3 A, Vector3 F, float width, float height, int detail)
    {
        var up = Vector3.up;
        PCard(small[TPWindowGlass], c + F * 0.01f, A * (width * 0.5f), up * (height * 0.5f), F);
        PCard(small[TPShoji], c - F * 0.02f, A * (width * 0.5f), up * (height * 0.5f), F);
        foreach (int s in new[] { -1, 1 })
        {
            PBox(big[TPKoshiWood], c + A * (s * (width * 0.5f + 0.04f)) + F * 0.06f, A * 0.05f, up * (height * 0.5f + 0.08f), F * 0.06f);
            PBox(big[TPKoshiWood], c + up * (s * (height * 0.5f + 0.04f)) + F * 0.06f, A * (width * 0.5f + 0.09f), up * 0.05f, F * 0.08f);
        }
        if (detail >= 2)
            for (float x = -width * 0.5f + 0.05f; x < width * 0.5f; x += 0.075f)
                PBox(small[TPKoshiWood], c + A * x + F * 0.07f, A * 0.018f, up * (height * 0.5f), F * 0.03f);
        else
            PCard(small[PM("KoshiCard", new Color(0.9f, 0.9f, 0.9f), "TP_Koshi", gloss: 0.2f)], c + F * 0.08f, A * (width * 0.5f), up * (height * 0.5f), F);
    }

    /// <summary>Aluminium sash window: frame, mullion, glass, sill.</summary>
    private static void AluWindow(PortBins big, PortBins small, Vector3 c, Vector3 A, Vector3 F, float width, float height)
    {
        var up = Vector3.up;
        PCard(small[TPWindowGlass], c + F * 0.015f, A * (width * 0.5f), up * (height * 0.5f), F);
        foreach (int s in new[] { -1, 1 })
        {
            PBox(small[TPAlu], c + A * (s * (width * 0.5f)) + F * 0.04f, A * 0.035f, up * (height * 0.5f + 0.035f), F * 0.04f);
            PBox(small[TPAlu], c + up * (s * (height * 0.5f)) + F * 0.04f, A * (width * 0.5f + 0.035f), up * 0.035f, F * 0.04f);
        }
        PBox(small[TPAlu], c + F * 0.04f, A * 0.025f, up * (height * 0.5f), F * 0.03f);
        PBox(small[TPConcreteDark], c - up * (height * 0.5f + 0.08f) + F * 0.08f, A * (width * 0.5f + 0.1f), up * 0.04f, F * 0.1f);
    }
}
