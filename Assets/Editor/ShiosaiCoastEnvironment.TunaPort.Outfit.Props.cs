using System.Collections.Generic;
using UnityEngine;

// Tuna port, part 7c: hand props and the per-trade wardrobe (see TunaPort.Outfit.cs).
public static partial class ShiosaiCoastEnvironment
{
    /// <summary>Hand frame: a = along the arm (points DOWN once MinatoCrowdActor drops the arms),
    /// p = forward, q = sideways; u = hand length.</summary>
    private static bool HandFrame(FigScan s, string side, out Vector3 hc, out Vector3 a, out Vector3 p, out Vector3 q, out float u)
    {
        hc = a = p = q = default; u = 0f;
        var hand = s.B(side + "Hand"); var fore = s.B(side + "ForeArm");
        if (hand == null || fore == null) return false;
        a = (hand.position - fore.position).normalized;
        p = Vector3.ProjectOnPlane(s.Fw, a).normalized; q = Vector3.Cross(a, p);
        var pts = s.Prefix(side + "Hand");
        if (!AxisBounds(pts, hand.position, a, p, q, out var mn, out var mx)) { hc = hand.position; u = 0.06f * s.FigH; return true; }
        var mid = (mn + mx) * 0.5f;
        hc = hand.position + a * mid.x + p * mid.y + q * mid.z;
        u = Mathf.Max(mx.x - mn.x, 0.03f * s.FigH);
        return true;
    }

    /// <summary>How far below the hand a hanging prop may reach before it hits the ground, once the
    /// arm hangs (shoulder height minus arm length), keeping a small clearance.</summary>
    private static float HangRoom(FigScan s, string side, float u)
    {
        var arm = s.B(side + "Arm"); var fore = s.B(side + "ForeArm"); var hand = s.B(side + "Hand");
        if (arm == null || fore == null || hand == null) return 0f;
        float armLen = (fore.position - arm.position).magnitude + (hand.position - fore.position).magnitude + 0.5f * u;
        float handH = s.H(arm.position) - s.sole - armLen * 0.96f;
        return handH - 0.08f * s.FigH;
    }

    /// <summary>kind: Hook (tekagi), Bell, Torch, Clipboard, Bag, Basket, Knife.</summary>
    private static void BuildProp(GearBins g, FigScan s, string side, string kind)
    {
        if (!HandFrame(s, side, out var hc, out var a, out var p, out var q, out float u)) return;
        switch (kind)
        {
            case "Hook":   // wooden grip through the fist, steel hook forward-down (slot0 wood, slot1 steel)
            {
                PCyl(g[0], hc - p * (0.9f * u), p, 0.17f * u, 0.17f * u, 1.9f * u, 10, true);
                var p0 = hc + p * (0.95f * u); var p1 = p0 + a * (1.2f * u) + p * (0.15f * u);
                var p2 = p1 + a * (0.35f * u) + p * (0.45f * u); var p3 = p2 - a * (0.3f * u) + p * (0.25f * u);
                Beam(g[1], p0, p1, 0.06f * u); Beam(g[1], p1, p2, 0.055f * u); Beam(g[1], p2, p3, 0.045f * u);
                break;
            }
            case "Bell":   // auctioneer's hand bell (slot0 wood, slot1 brass)
                PCyl(g[0], hc, a, 0.14f * u, 0.14f * u, 1.1f * u, 10, true);
                PCyl(g[1], hc + a * (1.0f * u), a, 0.2f * u, 0.62f * u, 0.8f * u, 14, false);
                GEll(g[1], hc + a * (1.85f * u), p * 0.12f * u, a * 0.12f * u, q * 0.12f * u, 8, 6);
                break;
            case "Torch":  // flashlight for checking the tail cut (slot0 black, slot1 lit lens)
                PCyl(g[0], hc - p * (0.55f * u), p, 0.17f * u, 0.17f * u, 1.7f * u, 10, true);
                PCyl(g[0], hc + p * (1.1f * u), p, 0.2f * u, 0.27f * u, 0.4f * u, 10, true);
                PCyl(g[1], hc + p * (1.5f * u), p, 0.24f * u, 0.24f * u, 0.02f * u, 10, true);
                break;
            case "Clipboard": // hangs from the fingers (slot0 board, slot1 paper)
            {
                var bc = hc + a * (1.7f * u);
                PBox(g[0], bc, a * (1.15f * u), p * (0.85f * u), q * (0.04f * u));
                foreach (float sg in new[] { -1f, 1f })
                    PCard(g[1], bc + q * (sg * 0.05f * u) + a * (0.1f * u), a * (0.95f * u), p * (0.72f * u), q * sg);
                PBox(g[2], bc - a * (1.05f * u), a * (0.1f * u), p * (0.35f * u), q * (0.07f * u));
                break;
            }
            case "Bag":    // eco-bag with a negi leek sticking out (slot0 bag, 1 handles, 2 leek green, 3 leek white)
            case "Basket": // bamboo kago with mikan (slot0 bamboo, 1 handle, 2 fruit)
            {
                float room = HangRoom(s, side, u);
                float hl = 0.8f * u;
                float bh = Mathf.Clamp(room - hl, 1.3f * u, 3.4f * u);
                if (room - hl < 1.1f * u) return;
                var topC = hc + a * (hl + 0.1f * u);
                if (kind == "Bag")
                {
                    PBox(g[0], topC + a * (bh * 0.5f), a * (bh * 0.5f), p * (1.3f * u), q * (0.45f * u), 1f, true);
                    foreach (float sg in new[] { -1f, 1f })
                        Beam(g[1], hc + p * (sg * 0.2f * u), topC + p * (sg * 0.75f * u), 0.05f * u);
                    var lk = topC + p * (0.7f * u) + a * (0.6f * u);
                    var ld = (-a + p * 0.22f).normalized;
                    PCyl(g[3], lk, ld, 0.16f * u, 0.16f * u, 1.0f * u, 8, false);
                    PCyl(g[2], lk + ld * (1.0f * u), ld, 0.16f * u, 0.11f * u, 1.3f * u, 8, true);
                }
                else
                {
                    float r = 1.05f * u;
                    PCyl(g[0], topC, a, r, r * 0.85f, bh * 0.8f, 14, true);
                    for (int k = 0; k < 4; k++)
                    {
                        float th = k * Mathf.PI * 0.5f + 0.4f;
                        GEll(g[2], topC + a * (0.05f * u) + (p * Mathf.Cos(th) + q * Mathf.Sin(th)) * (0.45f * u),
                             p * 0.3f * u, a * 0.28f * u, q * 0.3f * u, 8, 6);
                    }
                    Beam(g[1], topC + p * r, hc + p * (0.1f * u), 0.06f * u);
                    Beam(g[1], topC - p * r, hc - p * (0.1f * u), 0.06f * u);
                }
                break;
            }
            case "Knife":  // oroshi-hocho, the long tuna knife (slot0 wood, slot1 steel)
                PCyl(g[0], hc - p * (0.6f * u), p, 0.16f * u, 0.16f * u, 1.4f * u, 10, true);
                PBox(g[1], hc + p * (0.8f + 2.8f) * u, p * (2.8f * u), a * (0.22f * u), q * (0.018f * u));
                break;
        }
    }

    // ------------------------------------------------------------------ materials

    private static Material GearMat(string name, Color c, float gloss, bool two = false) =>
        PM("Gear" + name, c, twoSided: two, gloss: gloss, spec: 0.2f);

    private static readonly Color[] CapCols =
        { new Color(0.08f, 0.12f, 0.30f), new Color(0.80f, 0.80f, 0.78f), new Color(0.06f, 0.06f, 0.07f),
          new Color(0.12f, 0.32f, 0.20f), new Color(0.62f, 0.12f, 0.10f) };
    private static readonly Color[] KnitCols =
        { new Color(0.08f, 0.12f, 0.30f), new Color(0.58f, 0.10f, 0.10f), new Color(0.34f, 0.35f, 0.36f), new Color(0.66f, 0.50f, 0.14f) };
    private static readonly Color[] GloveCols =
        { new Color(0.88f, 0.40f, 0.08f), new Color(0.10f, 0.28f, 0.72f), new Color(0.86f, 0.72f, 0.10f), new Color(0.84f, 0.84f, 0.80f) };
    private static readonly Color[] BootCols =
        { new Color(0.86f, 0.87f, 0.86f), new Color(0.86f, 0.87f, 0.86f), new Color(0.05f, 0.05f, 0.06f), new Color(0.07f, 0.12f, 0.26f) };
    private static readonly Color[] ScarfCols =
        { new Color(0.16f, 0.30f, 0.64f), new Color(0.82f, 0.80f, 0.74f), new Color(0.78f, 0.44f, 0.52f) };
    private static readonly Color[] BagCols =
        { new Color(0.76f, 0.70f, 0.58f), new Color(0.88f, 0.88f, 0.86f), new Color(0.78f, 0.46f, 0.56f), new Color(0.14f, 0.20f, 0.40f) };

    private static Material Pal(string kind, Color[] cols, int i, float gloss, bool two = false)
    {
        var c = cols[Mathf.Abs(i) % cols.Length];
        return GearMat(kind + ColorUtility.ToHtmlStringRGB(c), c, gloss, two);
    }

    private static int _outfitted;
    private static readonly HashSet<string> _scanFailKeys = new HashSet<string>();

    /// <summary>Dress one townsperson for their trade. Everything goes on LOD0 only.</summary>
    private static void Outfit(GameObject go, Transform high, string role, float seed, bool seated, List<Renderer> added)
    {
        SkinnedMeshRenderer smr = null;
        foreach (var r in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name == "Mesh_0") { smr = r; break; }
        if (smr == null || smr.sharedMesh == null) return;
        string mk = smr.sharedMesh.name.Replace("MapleLife_Helmetless_", "").Replace(" ", "");
        FigScan scan = null; bool scanned = false;
        FigScan S() { if (!scanned) { scan = ScanFigure(go, smr); scanned = true; if (scan == null) { _scanFailed++; _scanFailKeys.Add(mk); } } return scan; }
        void Put(string gear, Transform bone, System.Action<GearBins, FigScan> b, params Material[] mats)
        {
            if (bone == null) return;
            Gear(mk, gear, bone, gb => { var sc = S(); if (sc != null) b(gb, sc); }, mats, added);
        }
        int H(int salt) => Mathf.Abs((int)(Mathf.Repeat(seed * 7919.37f + salt * 104.729f, 1f) * 100000f));
        float P(int salt) => (H(salt) % 1000) / 1000f;
        Transform Bone(string n)
        {
            foreach (var b in smr.bones) if (b != null && b.name == n) return b;
            return null;
        }

        var wood = GearMat("Wood", new Color(0.46f, 0.30f, 0.17f), 0.3f);
        var steel = GearMat("Steel", new Color(0.64f, 0.66f, 0.68f), 0.8f);
        var brass = GearMat("Brass", new Color(0.80f, 0.60f, 0.20f), 0.85f);
        var black = GearMat("Black", new Color(0.04f, 0.04f, 0.045f), 0.5f);
        var badge = GearMat("Badge", new Color(0.86f, 0.70f, 0.24f), 0.85f);
        var white = GearMat("Cotton", new Color(0.86f, 0.87f, 0.86f), 0.08f);
        var lens = PMGlow("GearLens", new Color(1f, 0.95f, 0.8f), 60f);

        bool trade = role == "Auctioneer" || role == "Buyer" || role == "Inspector" || role == "Fishmonger" ||
                     role == "Worker" || role == "Cutter" || role == "Fisherman";
        // ---- feet: white (or black/navy) rubber boots for anyone who works the wet floor
        if (trade)
        {
            var boot = Pal("Boot", BootCols, role == "Fisherman" ? 2 : H(1), 0.6f);
            foreach (var sd in new[] { "Left", "Right" })
            {
                string sdc = sd;
                Put("Boot" + sd, Bone(sd + "Leg"), (gb, sc) => BuildBootShaft(gb, sc, sdc), boot);
                Put("BootFoot" + sd, Bone(sd + "Foot"), (gb, sc) => BuildBootFoot(gb, sc, sdc), boot);
            }
        }
        // ---- head
        var headB = Bone("Head");
        int hat = -1; bool band = false;
        switch (role)
        {
            case "Auctioneer": case "Buyer": case "Inspector": hat = 0; break;
            case "Cutter": band = true; break;
            case "Fisherman": hat = P(2) < 0.7f ? 1 : 2; break;
            case "Fishmonger": { float x = P(2); if (x < 0.5f) band = true; else if (x < 0.72f) hat = 3; else if (x < 0.9f) hat = 0; break; }
            case "Worker": { float x = P(2); if (x < 0.35f) hat = 1; else if (x < 0.65f) band = true; else if (x < 0.9f) hat = 0; break; }
            default: { float x = P(2); if (x < 0.14f) hat = 2; else if (x < 0.28f) hat = 0; else if (x < 0.36f) hat = 4; break; }
        }
        if (band) Put("Hachimaki", headB, BuildHachimaki, P(3) < 0.75f ? white : Pal("Band", CapCols, 0, 0.1f));
        if (hat >= 0)
        {
            int k = hat;
            Material m0 = k == 0 ? Pal("Cap", CapCols, role == "Inspector" ? 1 : H(4), 0.25f, true)
                        : k == 1 ? Pal("Knit", KnitCols, H(4), 0.05f)
                        : k == 2 ? GearMat("Straw", new Color(0.80f, 0.68f, 0.44f), 0.15f, true)
                        : k == 3 ? Pal("Scarf", ScarfCols, H(4), 0.06f, true)
                        : Pal("Bucket", BagCols, H(4), 0.08f, true);
            Material m1 = k == 0 ? (role == "Buyer" || role == "Auctioneer" || role == "Inspector" ? badge : m0) : black;
            Put("Hat" + k, headB, (gb, sc) => BuildHat(gb, sc, k), m0, m1);
        }
        // ---- body
        bool apron = role == "Fishmonger" || role == "Cutter" || (role == "Worker" && P(5) < 0.45f);
        if (apron)
        {
            var ap = Pal("Apron", ApronColours, H(6), 0.6f, true);
            Put("Apron", Bone("Hips"), BuildApron, ap, black);
        }
        if ((role == "Worker" && P(7) < 0.5f) || (role == "Fishmonger" && P(7) < 0.3f) || role == "Fisherman" && P(7) < 0.4f)
            Put("NeckTowel", Bone("Spine02"), BuildNeckTowel, P(8) < 0.6f ? white : Pal("Towel", ScarfCols, H(8), 0.06f));
        bool gloves = (role == "Fishmonger" && P(9) < 0.6f) || (role == "Worker" && P(9) < 0.7f) || role == "Cutter";
        if (gloves)
        {
            var gm = Pal("Glove", GloveCols, role == "Worker" && P(10) < 0.4f ? 3 : H(10), 0.55f);
            foreach (var sd in new[] { "Left", "Right" })
            {
                if (role == "Cutter" && sd == "Right") continue;
                string sdc = sd;
                Put("Glove" + sd, Bone(sd + "Hand"), (gb, sc) => BuildGlove(gb, sc, sdc), gm);
            }
        }
        if ((role == "Fishmonger" && P(11) < 0.5f) || (role == "Cutter"))
            foreach (var sd in new[] { "Left", "Right" })
            {
                string sdc = sd;
                Put("ArmCover" + sd, Bone(sd + "ForeArm"), (gb, sc) => BuildArmCover(gb, sc, sdc), black);
            }
        // ---- hand props (never on seated figures: their arms rest on the bench)
        if (!seated)
        {
            string right = null, left = null;
            switch (role)
            {
                case "Auctioneer": right = "Bell"; left = "Clipboard"; break;
                case "Buyer": right = "Hook"; if (P(12) < 0.4f) left = "Torch"; break;
                case "Inspector": right = "Torch"; left = "Clipboard"; break;
                case "Worker": if (P(12) < 0.35f) right = "Hook"; break;
                case "Cutter": right = "Knife"; break;
                case "Fishmonger": case "Fisherman": break;
                default: { float x = P(12); if (x < 0.38f) left = "Bag"; else if (x < 0.52f) left = "Basket"; break; }
            }
            foreach (var (sd, kind) in new[] { ("Right", right), ("Left", left) })
            {
                if (kind == null) continue;
                string sdc = sd, kc = kind;
                Material[] mats =
                    kind == "Hook" ? new[] { wood, steel } :
                    kind == "Bell" ? new[] { wood, brass } :
                    kind == "Torch" ? new[] { black, lens } :
                    kind == "Clipboard" ? new[] { wood, white, steel } :
                    kind == "Bag" ? new[] { Pal("Bag", BagCols, H(13), 0.25f), Pal("Bag", BagCols, H(13), 0.25f),
                                            GearMat("Leek", new Color(0.22f, 0.50f, 0.18f), 0.35f), white } :
                    kind == "Basket" ? new[] { GearMat("Bamboo", new Color(0.62f, 0.48f, 0.28f), 0.2f),
                                               GearMat("Bamboo", new Color(0.62f, 0.48f, 0.28f), 0.2f),
                                               GearMat("Mikan", new Color(0.92f, 0.46f, 0.06f), 0.5f) } :
                    new[] { wood, steel };
                Put(kind + sd, Bone(sd + "Hand"), (gb, sc) => BuildProp(gb, sc, sdc, kc), mats);
            }
        }
        _outfitted++;
    }
}
