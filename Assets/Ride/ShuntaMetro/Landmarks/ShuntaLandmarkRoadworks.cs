using System.Collections.Generic;
using UnityEngine;

// Road-bound set pieces: Rainbow Bridge (zone 10), multi-level interchange (zone 9), tunnel arches (zone 4), railway arches (zone 8).
public sealed partial class ShuntaLandmarkSet
{
    // ------------------------------------------------------------------ Rainbow Bridge

    void BuildRainbowBridge()
    {
        var root = Child("Rainbow Bridge"); var bag = new ShuntaLookKit.MeshBag();
        var steel = M("BridgeSteel", new Color(0.78f, 0.78f, 0.82f), 0.45f, new Color(1f, 0.7f, 0.9f), 0.4f);
        var cable = M("BridgeCable", new Color(0.85f, 0.85f, 0.88f), 0.45f, new Color(1f, 0.9f, 0.97f), 1.6f);
        var hanger = M("BridgeHanger", new Color(0.8f, 0.8f, 0.85f), 0.45f, new Color(1f, 0.88f, 0.95f), 1.2f);
        var rail = M("BridgeRail", new Color(0.6f, 0.6f, 0.65f), 0.4f, new Color(1f, 0.4f, 0.8f), 2.4f);
        var deckM = M("BridgeDeck", new Color(0.3f, 0.3f, 0.34f), 0.4f, new Color(0.6f, 0.4f, 0.8f), 0.12f);
        var red = M("BridgeBeacon", new Color(0.5f, 0.1f, 0.1f), 0.3f, new Color(1f, 0.15f, 0.1f), 4.5f);
        Color[] rainbow = { new Color(1f, 0.2f, 0.25f), new Color(1f, 0.6f, 0.15f), new Color(1f, 0.95f, 0.3f), new Color(0.3f, 1f, 0.45f), new Color(0.25f, 0.7f, 1f), new Color(0.5f, 0.4f, 1f), new Color(1f, 0.35f, 0.85f) };
        var bulbs = new Material[7];
        for (int k = 0; k < 7; k++) bulbs[k] = M("BridgeBulb" + k, new Color(rainbow[k].r * 0.5f, rainbow[k].g * 0.5f, rainbow[k].b * 0.5f), 0.4f, rainbow[k], 4f);

        var P = route.Positions; var K = route.Km;
        const float k0 = 21.0f, k1 = 24.0f, kt0 = 21.8f, kt1 = 23.3f;
        int i0 = IdxAtKm(k0), i1 = IdxAtKm(k1);
        float half = Half, Lc = half + 3.0f, Ht = 78f, Hmid = 13f;
        float HRel(float km)
        {
            if (km <= kt0) { float s = Mathf.Clamp01((km - k0) / (kt0 - k0)); return Mathf.Lerp(7f, Ht, Mathf.Pow(s, 1.35f)); }
            if (km >= kt1) { float s = Mathf.Clamp01((k1 - km) / (k1 - kt1)); return Mathf.Lerp(7f, Ht, Mathf.Pow(s, 1.35f)); }
            float u = (km - kt0) / (kt1 - kt0); float q = 2f * u - 1f;
            return Hmid + (Ht - Hmid) * q * q;
        }
        float seaY = GroundY + 1.2f;
        int hangN = 0, bulbN = 0;
        for (int i = i0; i < i1; i++)
        {
            Vector3 a = P[i], b = P[i + 1], ra = RightAt(i), rb2 = RightAt(i + 1);
            var d = b - a; if (d.sqrMagnitude < 1e-4f) continue;
            var rot = Quaternion.LookRotation(d.normalized, Vector3.up); float len = d.magnitude;
            // deck slab under the ribbon (top 0.12 m below it so it never z-fights) + side girders
            bag.Box(deckM, (a + b) * 0.5f - Vector3.up * 0.7f, rot, new Vector3(route.roadWidth + 4.2f, 1.2f, len + 0.08f));
            foreach (int s in new[] { -1, 1 })
            {
                float lr = half + 2.1f;
                Vector3 ea = a + ra * (s * lr), eb = b + rb2 * (s * lr);
                bag.Beam(steel, ea + Vector3.up * 0.55f, eb + Vector3.up * 0.55f, 0.3f, 1.1f);                // parapet
                bag.Beam(rail, ea + Vector3.up * 1.18f, eb + Vector3.up * 1.18f, 0.28f, 0.12f);               // lit handrail
                float lc = half + 1.6f;
                Vector3 ca = a + ra * (s * lc) - Vector3.up * 5.6f, cb = b + rb2 * (s * lc) - Vector3.up * 5.6f;
                bag.Beam(steel, ca, cb, 1.0f, 1.0f);                                                         // lower chord
                if (i % 2 == 0) bag.Beam(steel, ca, b + rb2 * (s * lc) - Vector3.up * 1.0f, 0.5f);          // zig-zag truss
                else bag.Beam(steel, a + ra * (s * lc) - Vector3.up * 1.0f, cb, 0.5f);
                if (i % 2 == 0) bag.Beam(steel, a + ra * (s * lc) - Vector3.up * 1.2f, a + ra * (s * lc) - Vector3.up * 5.6f, 0.45f);
            }
            if (i % 3 == 0) bag.Beam(steel, a - ra * (half + 1.6f) - Vector3.up * 1.2f, a + ra * (half + 1.6f) - Vector3.up * 1.2f, 0.6f, 0.7f); // cross girder
            // main cables + hangers
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 pa = a + ra * (s * Lc) + Vector3.up * HRel(K[i]), pb = b + rb2 * (s * Lc) + Vector3.up * HRel(K[i + 1]);
                bag.Beam(cable, pa, pb, 1.2f, 1.2f);
                bool nearTower = Mathf.Abs(K[i] - kt0) < 0.03f || Mathf.Abs(K[i] - kt1) < 0.03f;
                if (i % 2 == 0 && !nearTower)
                {
                    bag.Beam(hanger, a + ra * (s * Lc) + Vector3.up * 0.9f, pa, 0.2f);
                    hangN++;
                    if ((i / 2) % 4 == 0) { bag.Box(bulbs[bulbN % 7], pa - Vector3.up * 1.0f, Quaternion.identity, new Vector3(1.6f, 1.6f, 1.6f)); bulbN++; }
                }
            }
        }
        // towers (portal frames) at kt0 and kt1
        foreach (float tk in new[] { kt0, kt1 })
        {
            int it = IdxAtKm(tk); var p = P[it]; var r = RightAt(it);
            var f = Vector3.Cross(Vector3.up, r); // forward-ish (unused sign is fine, only orientation matters)
            var rot = Quaternion.LookRotation(new Vector3(-r.z, 0f, r.x), Vector3.up);
            float topY = p.y + Ht;
            foreach (int s in new[] { -1, 1 })
            {
                var lp = p + r * (s * Lc);
                float bot = seaY - 4f;
                bag.Box(steel, new Vector3(lp.x, (bot + topY) * 0.5f, lp.z), rot, new Vector3(3.6f, topY - bot, 4.6f));
                bag.Box(steel, new Vector3(lp.x, bot + 6f, lp.z), rot, new Vector3(6.4f, 12f, 8f));      // pier footing
                bag.Box(steel, new Vector3(lp.x, topY + 1.5f, lp.z), rot, new Vector3(4.2f, 3f, 5.2f));   // saddle
                bag.Box(red, new Vector3(lp.x, topY + 3.6f, lp.z), Quaternion.identity, new Vector3(1.6f, 1.6f, 1.6f));
                // light strip up the leg
                bag.Box(rail, new Vector3(lp.x, (bot + topY) * 0.5f, lp.z) - r * (s * 1.85f), rot, new Vector3(0.18f, topY - bot, 0.5f));
            }
            float[] lv = { p.y + 24f, p.y + 46f, p.y + 66f, topY - 1f };
            for (int q = 0; q < lv.Length; q++)
            {
                var la = p + r * (-Lc); la.y = lv[q]; var lb = p + r * Lc; lb.y = lv[q];
                bag.Beam(steel, la, lb, 2.2f, 2.2f);
                if (q > 0)
                {
                    var pa = p + r * (-Lc); pa.y = lv[q - 1]; var pb = p + r * Lc; pb.y = lv[q];
                    var pc = p + r * Lc; pc.y = lv[q - 1]; var pd = p + r * (-Lc); pd.y = lv[q];
                    bag.Beam(steel, pa, pb, 0.9f); bag.Beam(steel, pc, pd, 0.9f);
                }
            }
        }
        // anchor piers at both ends
        foreach (float ak in new[] { k0, k1 })
        {
            int ia = IdxAtKm(ak); var p = P[ia]; var r = RightAt(ia);
            var rot = Quaternion.LookRotation(new Vector3(-r.z, 0f, r.x), Vector3.up);
            float h = Mathf.Max(p.y - 1.5f - seaY, 4f);
            bag.Box(steel, new Vector3(p.x, seaY + h * 0.5f - 1f, p.z), rot, new Vector3(route.roadWidth + 9f, h, 14f));
        }
        Flush(bag, root, "RainbowBridge");
        rep.Append($"bridge hangers {hangN} bulbs {bulbN}; ");
    }

    // ------------------------------------------------------------------ interchange decks (zone 9)

    void BuildInterchange()
    {
        var root = Child("Interchange"); var bag = new ShuntaLookKit.MeshBag();
        var P = route.Positions; var K = route.Km;
        var fork = ShuntaInterchangeFork.Default(route.Course);
        var conc = M("IxConcrete", new Color(0.42f, 0.42f, 0.45f), 0.25f, new Color(0.35f, 0.3f, 0.3f), 0.12f);
        var barrier = M("IxBarrier", new Color(0.5f, 0.5f, 0.52f), 0.3f, new Color(0.5f, 0.45f, 0.4f), 0.15f);
        var stripU = M("IxStripUpper", new Color(0.6f, 0.4f, 0.2f), 0.4f, new Color(1f, 0.62f, 0.22f), 2.6f);
        var stripL = M("IxStripLower", new Color(0.2f, 0.5f, 0.6f), 0.4f, new Color(0.3f, 0.85f, 1f), 2.6f);
        var road = RoadMat("IxRoad", new Color(1.0f, 1.0f, 1.04f));
        float w = route.roadWidth - 1f;

        int firstMain = 0; while (firstMain < K.Length && K[firstMain] <= fork.ForkKm) firstMain++;
        int branchSegs = 0, pillars = 0;
        foreach (var which in new[] { ShuntaForkBranch.Upper, ShuntaForkBranch.Lower })
        {
            var pts = fork.BuildBranch(P, K, which);
            if (pts.Length < 3) continue;
            var skip = new bool[pts.Length - 1];
            for (int j = 0; j < pts.Length - 1; j++)
            {
                bool near0 = NearMain(pts[j], j, firstMain), near1 = NearMain(pts[j + 1], j + 1, firstMain);
                skip[j] = near0 && near1;
            }
            RibbonAlong(root, "Deck " + which, pts, w, 0.02f, road, skip);
            var strip = which == ShuntaForkBranch.Upper ? stripU : stripL;
            for (int j = 0; j < pts.Length - 1; j++)
            {
                if (skip[j]) continue;
                Vector3 a = pts[j], b = pts[j + 1]; var d = b - a; if (d.sqrMagnitude < 1e-4f) continue;
                var rot = Quaternion.LookRotation(d.normalized, Vector3.up); float len = d.magnitude;
                Vector3 ra = TanRight(pts, j), rb2 = TanRight(pts, j + 1);
                bag.Box(conc, (a + b) * 0.5f - Vector3.up * 0.6f, rot, new Vector3(w + 1.4f, 1.0f, len + 0.06f));
                foreach (int s in new[] { -1, 1 })
                {
                    Vector3 ea = a + ra * (s * (w * 0.5f + 0.5f)), eb = b + rb2 * (s * (w * 0.5f + 0.5f));
                    bag.Beam(barrier, ea + Vector3.up * 0.5f, eb + Vector3.up * 0.5f, 0.35f, 1.0f);
                    bag.Beam(strip, ea + Vector3.up * 1.05f, eb + Vector3.up * 1.05f, 0.22f, 0.1f);
                }
                if (j % 4 == 0 && a.y - GroundY > 3f)
                {   // visual-only pillar + cap beam (no colliders anywhere in this set)
                    float topY = a.y - 1.1f; float h = topY - GroundY;
                    bag.Box(conc, new Vector3(a.x, GroundY + h * 0.5f, a.z), rot, new Vector3(2.4f, h, 2.4f));
                    bag.Box(conc, new Vector3(a.x, topY - 0.5f, a.z), rot, new Vector3(w + 3.4f, 1.3f, 2.6f));
                    pillars++;
                }
                branchSegs++;
            }
        }
        // support the main viaduct through the interchange as well
        int m0 = IdxAtKm(18.45f), m1 = IdxAtKm(20.95f);
        var mainRot = Quaternion.identity;
        for (int i = m0; i < m1; i += 4)
        {
            var p = P[i]; if (p.y - GroundY < 4f) continue;
            var d = P[Mathf.Min(i + 1, P.Length - 1)] - P[i]; d.y = 0f; var rot = Quaternion.LookRotation(d.sqrMagnitude < 1e-6f ? Vector3.forward : d.normalized, Vector3.up);
            float topY = p.y - 0.25f; float h = topY - GroundY;
            bag.Box(conc, new Vector3(p.x, GroundY + h * 0.5f, p.z), rot, new Vector3(2.6f, h, 2.6f));
            bag.Box(conc, new Vector3(p.x, topY - 0.55f, p.z), rot, new Vector3(route.roadWidth + 3.0f, 1.1f, 3.0f));
            pillars++;
        }
        Flush(bag, root, "Interchange");
        rep.Append($"interchange segs {branchSegs} pillars {pillars}; ");
    }

    bool NearMain(Vector3 p, int j, int firstMain)
    {
        var P = route.Positions;
        int idx = Mathf.Clamp(firstMain + j - 1, 0, P.Length - 1);
        var m = P[idx];
        float dh = Mathf.Sqrt((p.x - m.x) * (p.x - m.x) + (p.z - m.z) * (p.z - m.z));
        return dh < 10f && Mathf.Abs(p.y - m.y) < 2.4f;
    }

    static Vector3 TanRight(Vector3[] pts, int i) { var f = Tangent(pts, i); return new Vector3(f.z, 0f, -f.x); }

    // ------------------------------------------------------------------ zone 4 tunnel arches with cyan chevrons

    void BuildTunnelArches()
    {
        var root = Child("Tunnel Arches"); var bag = new ShuntaLookKit.MeshBag();
        var P = route.Positions; var K = route.Km;
        var rib = M("TunnelRib", new Color(0.2f, 0.24f, 0.27f), 0.4f, new Color(0.15f, 0.4f, 0.45f), 0.25f);
        var cyan = M("TunnelCyan", new Color(0.15f, 0.4f, 0.45f), 0.4f, new Color(0.15f, 0.95f, 1f), 3.2f);
        var ZS = route.Course.ZoneAtKm(7.0f); float ks = ZS != null ? ZS.startKm : 6.2f, ke = ZS != null ? ZS.endKm : 8.6f;
        int i0 = IdxAtKm(ks + 0.05f), i1 = IdxAtKm(ke - 0.05f);
        int arches = 0, chev = 0;
        for (int i = i0; i < i1; i += 3)
        {
            var d = P[Mathf.Min(i + 1, P.Length - 1)] - P[Mathf.Max(i - 1, 0)]; d.y = 0f; if (d.sqrMagnitude < 1e-6f) continue;
            var rot = Quaternion.LookRotation(d.normalized, Vector3.up); var pos = P[i];
            Vector3 L(float x, float y, float z) => pos + rot * new Vector3(x, y, z);
            const float X = 4.85f;
            foreach (int s in new[] { -1, 1 })
            {
                bag.Box(rib, L(s * X, 2.3f, 0f), rot, new Vector3(0.5f, 4.6f, 0.7f));
                bag.Beam(rib, L(s * X, 4.5f, 0f), L(s * 3.3f, 5.95f, 0f), 0.5f, 0.55f);
                bag.Box(cyan, L(s * (X - 0.27f), 2.3f, 0f), rot, new Vector3(0.06f, 4.2f, 0.16f));   // lit inner edge
            }
            bag.Box(rib, L(0f, 5.95f, 0f), rot, new Vector3(6.8f, 0.5f, 0.7f));
            bag.Box(cyan, L(0f, 5.66f, 0f), rot, new Vector3(6.4f, 0.06f, 0.16f));
            arches++;
            // chevrons midway between arches: ceiling V pointing forward + wall chevrons
            float z = 12f;
            bag.Beam(cyan, L(-2.7f, 6.16f, z - 1.4f), L(0f, 6.16f, z + 1.1f), 0.3f, 0.05f);
            bag.Beam(cyan, L(2.7f, 6.16f, z - 1.4f), L(0f, 6.16f, z + 1.1f), 0.3f, 0.05f);
            foreach (int s in new[] { -1, 1 })
            {
                bag.Beam(cyan, L(s * 5.06f, 1.2f, z - 1.2f), L(s * 5.06f, 2.3f, z + 0.3f), 0.05f, 0.3f);
                bag.Beam(cyan, L(s * 5.06f, 3.4f, z - 1.2f), L(s * 5.06f, 2.3f, z + 0.3f), 0.05f, 0.3f);
            }
            chev++;
        }
        Flush(bag, root, "TunnelArches");
        rep.Append($"tunnel arches {arches} chevrons {chev}; ");
    }

    // ------------------------------------------------------------------ zone 8 railway arches with a passing-light strip

    void BuildRailwayArches()
    {
        var root = Child("Railway Arches"); var bag = new ShuntaLookKit.MeshBag();
        var P = route.Positions;
        var brick = M("RailBrick", new Color(0.4f, 0.2f, 0.16f), 0.3f, new Color(0.7f, 0.35f, 0.25f), 0.18f);
        var steel = M("RailSteel", new Color(0.35f, 0.38f, 0.42f), 0.4f, new Color(0.3f, 0.4f, 0.5f), 0.2f);
        var win = M("RailWindow", new Color(0.4f, 0.4f, 0.3f), 0.3f, new Color(1f, 0.95f, 0.7f), 2.4f);
        var chase = new Material[8];
        for (int k = 0; k < 8; k++) chase[k] = M("RailLamp" + k, new Color(0.3f, 0.5f, 0.45f), 0.4f, new Color(0.55f, 1f, 0.85f), 1f);
        var ZS = route.Course.ZoneAtKm(17.0f); float ks = ZS != null ? ZS.startKm : 16.2f, ke = ZS != null ? ZS.endKm : 18.4f;
        int i0 = IdxAtKm(ks + 0.03f), i1 = IdxAtKm(ke - 0.03f);
        int arches = 0, lamps = 0;
        const float Rr = 6.2f, cy = 3.5f;
        for (int i = i0; i < i1; i += 2)
        {
            var d = P[Mathf.Min(i + 1, P.Length - 1)] - P[Mathf.Max(i - 1, 0)]; d.y = 0f; if (d.sqrMagnitude < 1e-6f) continue;
            var rot = Quaternion.LookRotation(d.normalized, Vector3.up); var pos = P[i];
            Vector3 L(float x, float y, float z) => pos + rot * new Vector3(x, y, z);
            foreach (int s in new[] { -1, 1 })
                bag.Box(brick, L(s * Rr, cy * 0.5f, 0f), rot, new Vector3(0.7f, cy, 1.3f));
            Vector3 prev = L(-Rr, cy, 0f);
            for (int k = 1; k <= 14; k++)
            {
                float a = Mathf.PI - k * Mathf.PI / 14f;
                var q = L(Mathf.Cos(a) * Rr, cy + Mathf.Sin(a) * (Rr * 0.98f), 0f);
                bag.Beam(brick, prev, q, 0.7f, 0.7f);
                prev = q;
            }
            // passing-light lamp strip along the crown (8 phase-shifted materials = a chase of light moving with the road)
            bag.Box(chase[(i / 2) % 8], L(0f, cy + Rr * 0.98f - 0.5f, 0f), rot, new Vector3(2.4f, 0.14f, 1.1f));
            lamps++;
            // lit train windows along the deck edge above (every arch, both sides)
            bag.Box(steel, L(0f, 12.4f, 0f), rot, new Vector3(6.0f, 3.0f, 17.5f));          // commuter-train car on the elevated line
            foreach (int s in new[] { -1, 1 })
                bag.Box(win, L(s * 3.02f, 12.8f, 0f), rot, new Vector3(0.1f, 1.0f, 15.5f));  // lit windows
            arches++;
        }
        // continuous steel parapet along the elevated rail line
        for (int i = i0; i < i1; i += 2)
        {
            var d = P[Mathf.Min(i + 2, P.Length - 1)] - P[i]; d.y = 0f; if (d.sqrMagnitude < 1e-6f) continue;
            var rot = Quaternion.LookRotation(d.normalized, Vector3.up); var r = new Vector3(d.normalized.z, 0f, -d.normalized.x);
            foreach (int s in new[] { -1, 1 })
                bag.Box(steel, P[i] + r * (s * 9.2f) + Vector3.up * 11.3f, rot, new Vector3(0.3f, 1.2f, d.magnitude + 0.1f));
        }
        Flush(bag, root, "RailwayArches");
        motion.chase = chase; motion.chaseRgb = new Color(0.55f, 1f, 0.85f); motion.chaseRel = 3.4f;
        rep.Append($"railway arches {arches} lamps {lamps}; ");
    }
}
