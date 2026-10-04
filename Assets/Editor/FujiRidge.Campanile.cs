using UnityEngine;

/// <summary>
/// FUJI RIDGE - copilot session 2 (2026-09-26), region queue F3: the piazza campanile.
/// Previously one ochre box with black belfry rectangles. Now an Italian hill-town bell tower
/// in the shared PMesh (outward-wound, one palette material): a battered stone plinth, ochre
/// shaft with granite quoins and window slits, a clock, a cornice, an open belfry (corner piers,
/// round arches, balustrade, a hanging bronze bell on a beam), a second cornice and a tiled
/// terracotta pyramid cap with a finial. All dimensions are PROVISIONAL art tuning.
/// </summary>
public static partial class FujiRidgeEnvironment
{
    private const float CampHalfM = 2.2f;      // shaft half width
    private const float CampPlinthTopM = 3.4f;
    private const float CampShaftTopM = 19.6f;
    private const float CampBelfryTopM = 24.4f;
    private const float CampCapH = 6.2f;
    private const float BellB = 0.9f;   // aged bronze must read against the deep belfry shadow

    private static void Campanile(PMesh m, Vector3 c, Vector3 fwd, Vector3 sf, System.Random rnd)
    {
        Vector3 U(float y) => c + Vector3.up * y;
        // Plinth: sunk 1.6 m (the piazza slopes), battered stone in two steps.
        m.OBox(U((CampPlinthTopM - 1.6f) * 0.5f), fwd * (CampHalfM + 0.55f), Vector3.up * ((CampPlinthTopM + 1.6f) * 0.5f), sf * (CampHalfM + 0.55f), PGranite, 0.5f, 0.62f);
        m.OBox(U(0.25f), fwd * (CampHalfM + 0.8f), Vector3.up * 0.55f, sf * (CampHalfM + 0.8f), PGranite, 0.44f, 0.58f);
        m.OBox(U(CampPlinthTopM + 0.12f), fwd * (CampHalfM + 0.35f), Vector3.up * 0.14f, sf * (CampHalfM + 0.35f), PGranite, 0.62f, 0.75f);
        // Door on the piazza face (-sf faces the street).
        m.OBox(U(1.35f) - sf * (CampHalfM + 0.56f), fwd * 0.75f, Vector3.up * 1.35f, sf * 0.03f, PTimber, 0.35f);
        m.OBox(U(2.85f) - sf * (CampHalfM + 0.58f), fwd * 0.95f, Vector3.up * 0.18f, sf * 0.04f, PGranite, 0.7f);

        // Shaft.
        float sh0 = CampPlinthTopM + 0.2f, sh1 = CampShaftTopM;
        m.OBox(U((sh0 + sh1) * 0.5f), fwd * CampHalfM, Vector3.up * ((sh1 - sh0) * 0.5f + 0.05f), sf * CampHalfM, POchre, 0.6f);
        // Quoins: alternating long/short granite blocks up every corner.
        int q = 0;
        for (float y = sh0 + 0.25f; y < sh1 - 0.2f; y += 0.62f, q++)
            for (int a = -1; a <= 1; a += 2)
                for (int b = -1; b <= 1; b += 2)
                {
                    bool longF = (q & 1) == 0;
                    var corner = U(y) + fwd * (a * CampHalfM) + sf * (b * CampHalfM);
                    float lf = longF ? 0.42f : 0.24f, ls = longF ? 0.24f : 0.42f;
                    m.OBox(corner - fwd * (a * (lf - 0.04f)) - sf * (b * (ls - 0.04f)) + fwd * (a * 0.03f) + sf * (b * 0.03f),
                           fwd * lf, Vector3.up * 0.27f, sf * ls, PGranite, 0.66f + (q % 3) * 0.04f);
                }
        // String course half way and window slits in three tiers on every face.
        m.OBox(U(11.2f), fwd * (CampHalfM + 0.12f), Vector3.up * 0.12f, sf * (CampHalfM + 0.12f), PGranite, 0.62f, 0.74f);
        for (int k = 0; k < 4; k++)
        {
            var n = k == 0 ? -sf : k == 1 ? sf : k == 2 ? fwd : -fwd;
            var t = k < 2 ? fwd : sf;
            foreach (float y in new[] { 6.6f, 9.4f, 14.2f })
            {
                var wc = U(y) + n * (CampHalfM + 0.02f);
                m.OBox(wc, t * 0.13f, Vector3.up * 0.62f, n * 0.03f, PBlack, 0.12f);
                m.OBox(wc + n * 0.02f + Vector3.up * 0.7f, t * 0.26f, Vector3.up * 0.1f, n * 0.05f, PGranite, 0.72f);   // head
                m.OBox(wc + n * 0.04f - Vector3.up * 0.7f, t * 0.28f, Vector3.up * 0.07f, n * 0.1f, PGranite, 0.72f);   // sill
            }
            // Clock: cream face in a granite frame, two black hands.
            var cf = U(17.3f) + n * (CampHalfM + 0.03f);
            m.OBox(cf, t * 0.95f, Vector3.up * 0.95f, n * 0.03f, PGranite, 0.66f);
            m.OBox(cf + n * 0.04f, t * 0.78f, Vector3.up * 0.78f, n * 0.02f, PCream, 0.92f);
            m.OBox(cf + n * 0.07f + Vector3.up * 0.28f, t * 0.04f, Vector3.up * 0.3f, n * 0.01f, PBlack, 0.1f);
            m.OBox(cf + n * 0.07f + t * 0.22f, t * 0.22f, Vector3.up * 0.035f, n * 0.01f, PBlack, 0.1f);
        }
        // Lower cornice (two stepped courses).
        m.OBox(U(sh1 + 0.12f), fwd * (CampHalfM + 0.2f), Vector3.up * 0.14f, sf * (CampHalfM + 0.2f), PGranite, 0.6f, 0.72f);
        m.OBox(U(sh1 + 0.38f), fwd * (CampHalfM + 0.38f), Vector3.up * 0.13f, sf * (CampHalfM + 0.38f), PStucco, 0.74f, 0.84f);

        // Belfry: corner piers + round arches on each face, a floor and a ceiling slab.
        float b0 = sh1 + 0.5f, b1 = CampBelfryTopM, pier = 0.55f;
        m.OBox(U(b0 + 0.08f), fwd * (CampHalfM - 0.05f), Vector3.up * 0.1f, sf * (CampHalfM - 0.05f), PGranite, 0.45f);
        m.OBox(U(b1 - 0.35f), fwd * CampHalfM, Vector3.up * 0.35f, sf * CampHalfM, POchre, 0.58f);
        for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
                m.OBox(U((b0 + b1) * 0.5f) + fwd * (a * (CampHalfM - pier)) + sf * (b * (CampHalfM - pier)),
                       fwd * pier, Vector3.up * ((b1 - b0) * 0.5f), sf * pier, POchre, 0.6f);
        float open = CampHalfM - 2f * pier;             // half width of each opening
        float springY = b1 - 0.7f - open;               // arch springing line
        for (int k = 0; k < 4; k++)
        {
            var n = k == 0 ? -sf : k == 1 ? sf : k == 2 ? fwd : -fwd;
            var t = k < 2 ? fwd : sf;
            var fc = U(0f) + n * (CampHalfM - 0.2f);
            // Spandrel wall above the arch, carved into a round head by stepped voussoirs.
            const int Seg = 7;
            for (int j = 0; j < Seg; j++)
            {
                float a0 = Mathf.PI * j / Seg, a1 = Mathf.PI * (j + 1) / Seg, am = (a0 + a1) * 0.5f;
                float x0 = Mathf.Cos(a0) * open, x1 = Mathf.Cos(a1) * open;
                float yArc = springY + Mathf.Sin(am) * open;
                float cx = (x0 + x1) * 0.5f, hw = Mathf.Abs(x0 - x1) * 0.5f + 0.01f;
                float top = b1 - 0.7f;
                if (top - yArc > 0.02f)
                    m.OBox(fc + t * cx + Vector3.up * ((yArc + top) * 0.5f), t * hw, Vector3.up * ((top - yArc) * 0.5f), n * 0.2f, POchre, 0.6f);
                // Granite archivolt ring.
                var vm = fc + n * 0.21f + t * (Mathf.Cos(am) * (open + 0.1f)) + Vector3.up * (springY + Mathf.Sin(am) * (open + 0.1f));
                var tan = (t * -Mathf.Sin(am) + Vector3.up * Mathf.Cos(am)).normalized;
                var rad = (t * Mathf.Cos(am) + Vector3.up * Mathf.Sin(am)).normalized;
                m.OBox(vm, tan * (open * Mathf.PI / Seg * 0.5f + 0.04f), rad * 0.12f, n * 0.05f, PGranite, 0.74f);
            }
            // Imposts and a low balustrade across the opening.
            for (int e = -1; e <= 1; e += 2)
                m.OBox(fc + n * 0.21f + t * (e * (open + 0.1f)) + Vector3.up * springY, t * 0.14f, Vector3.up * 0.09f, n * 0.08f, PGranite, 0.72f);
            m.OBox(fc + n * 0.1f + Vector3.up * (b0 + 0.85f), t * open, Vector3.up * 0.07f, n * 0.12f, PGranite, 0.7f);
            for (float x = -open + 0.2f; x < open - 0.1f; x += 0.3f)
                m.OBox(fc + n * 0.1f + t * x + Vector3.up * (b0 + 0.5f), t * 0.05f, Vector3.up * 0.32f, n * 0.05f, PGranite, 0.62f);
        }
        // Bell on a timber beam across the belfry.
        float beamY = springY + open * 0.6f;   // high in the arch so the bell clears the balustrade
        var bellFront = -sf * 1.15f;  // piazza-facing arch; centred bell disappeared behind the parapet
        m.OBox(U(beamY) + bellFront, fwd * (CampHalfM - 0.3f), Vector3.up * 0.1f, sf * 0.1f, PTimber, 0.3f);
        m.OBox(U(beamY - 0.18f) + bellFront, fwd * 0.1f, Vector3.up * 0.1f, sf * 0.1f, PBlack, 0.2f);
        float bellH = 1.35f;
        var bellBase = U(beamY - 0.28f - bellH) + bellFront;
        // Aged bronze against the ochre stucco; separate raised bands keep its flared
        // silhouette legible at the roadside capture distance.
        m.Prism(bellBase - Vector3.up * 0.08f, 0.80f, 0.77f, 0.12f, 14, PBoulder, BellB * 1.18f, BellB * 1.28f); // lip
        m.Prism(bellBase, 0.74f, 0.4f, bellH * 0.75f, 14, PTimber, BellB * 0.95f, BellB * 1.15f, false); // bronze waist
        m.Prism(bellBase + Vector3.up * bellH * 0.75f, 0.4f, 0.14f, bellH * 0.25f, 14, PBoulder, BellB * 1.18f, BellB * 1.35f); // crown
        m.Prism(bellBase + Vector3.up * 0.16f, 0.72f, 0.71f, 0.07f, 14, PBoulder, BellB * 1.15f, BellB * 1.28f);
        m.OBox(bellBase + Vector3.up * 0.1f, fwd * 0.06f, Vector3.up * 0.35f, sf * 0.06f, PBlack, 0.15f); // clapper

        // Upper cornice: three stepped overhanging courses.
        m.OBox(U(b1 + 0.12f), fwd * (CampHalfM + 0.18f), Vector3.up * 0.13f, sf * (CampHalfM + 0.18f), PGranite, 0.6f, 0.72f);
        m.OBox(U(b1 + 0.36f), fwd * (CampHalfM + 0.36f), Vector3.up * 0.12f, sf * (CampHalfM + 0.36f), PStucco, 0.74f, 0.84f);
        m.OBox(U(b1 + 0.58f), fwd * (CampHalfM + 0.5f), Vector3.up * 0.1f, sf * (CampHalfM + 0.5f), PGranite, 0.64f, 0.76f);

        // Tiled pyramid cap: 6 overlapping tile courses (each course a frustum whose lower edge
        // overhangs the course below), terracotta alternating brightness.
        float capY = b1 + 0.68f, half = CampHalfM + 0.45f;
        const int Courses = 6;
        for (int k = 0; k < Courses; k++)
        {
            float f0 = k / (float)Courses, f1 = (k + 1) / (float)Courses;
            float y0 = capY + f0 * CampCapH - (k > 0 ? 0.08f : 0f), y1 = capY + f1 * CampCapH;
            float h0 = half * (1f - f0) + (k > 0 ? 0.1f : 0f), h1 = half * (1f - f1);
            Frustum(m, U(y0), U(y1), h0, Mathf.Max(h1, 0.02f), fwd, sf, PTerracotta, 0.62f + (k & 1) * 0.1f, k == Courses - 1);
        }
        // Finial: ball on a stem, and an iron cross.
        var tip = U(capY + CampCapH);
        m.Prism(tip - Vector3.up * 0.3f, 0.09f, 0.07f, 0.6f, 6, PGranite, 0.6f, 0.7f);
        m.Prism(tip + Vector3.up * 0.25f, 0.18f, 0.2f, 0.16f, 8, PGranite, 0.62f, 0.72f);
        m.Prism(tip + Vector3.up * 0.41f, 0.2f, 0.08f, 0.14f, 8, PGranite, 0.62f, 0.72f);
        m.OBox(tip + Vector3.up * 1.05f, fwd * 0.03f, Vector3.up * 0.5f, sf * 0.03f, PBlack, 0.2f);
        m.OBox(tip + Vector3.up * 1.2f, fwd * 0.25f, Vector3.up * 0.03f, sf * 0.03f, PBlack, 0.2f);
    }

    /// <summary>A square frustum (square cross-sections aligned to fwd/sf), sloped faces + underside,
    /// optional closed top. Wound outward.</summary>
    private static void Frustum(PMesh m, Vector3 b, Vector3 t, float hb, float ht, Vector3 fwd, Vector3 sf, int row, float br, bool capTop)
    {
        var cb = new Vector3[4]; var ct = new Vector3[4];
        for (int k = 0; k < 4; k++)
        {
            float a = (k == 0 || k == 3) ? -1f : 1f, s = k < 2 ? -1f : 1f;
            cb[k] = b + fwd * (a * hb) + sf * (s * hb);
            ct[k] = t + fwd * (a * ht) + sf * (s * ht);
        }
        var mid = (b + t) * 0.5f;
        for (int k = 0; k < 4; k++)
        {
            int k2 = (k + 1) % 4;
            var faceC = (cb[k] + cb[k2] + ct[k] + ct[k2]) * 0.25f;
            var outN = faceC - mid; outN.y = Mathf.Max(outN.y, 0.2f);
            float fb = br * (k % 2 == 0 ? 1f : 0.9f);
            int i0 = m.Vert(cb[k], row, fb), i1 = m.Vert(cb[k2], row, fb), i2 = m.Vert(ct[k2], row, fb), i3 = m.Vert(ct[k], row, fb);
            m.Tri(i0, i1, i2, outN); m.Tri(i0, i2, i3, outN);
        }
        int u0 = m.Vert(cb[0], row, br * 0.5f), u1 = m.Vert(cb[1], row, br * 0.5f), u2 = m.Vert(cb[2], row, br * 0.5f), u3 = m.Vert(cb[3], row, br * 0.5f);
        m.Tri(u0, u1, u2, Vector3.down); m.Tri(u0, u2, u3, Vector3.down);
        if (capTop)
        {
            int t0 = m.Vert(ct[0], row, br), t1 = m.Vert(ct[1], row, br), t2 = m.Vert(ct[2], row, br), t3 = m.Vert(ct[3], row, br);
            m.Tri(t0, t1, t2, Vector3.up); m.Tri(t0, t2, t3, Vector3.up);
        }
    }
}
