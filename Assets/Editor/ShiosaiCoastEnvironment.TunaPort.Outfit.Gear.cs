using System.Collections.Generic;
using UnityEngine;

// Tuna port, part 7b: head and body gear (see TunaPort.Outfit.cs for the fitting approach).
public static partial class ShiosaiCoastEnvironment
{
    /// <summary>Skull extents: top (hair cap included), chin, and head height.</summary>
    private static bool HeadDims(FigScan s, out float top, out float headH, out List<Vector3> pts)
    {
        pts = s.Of("Head", "HairCap");
        var skull = s.Of("Head");
        top = float.NegativeInfinity; float bot = float.PositiveInfinity;
        foreach (var p in pts) top = Mathf.Max(top, s.H(p));
        foreach (var p in skull) bot = Mathf.Min(bot, s.H(p));
        headH = top - bot;
        return pts.Count > 20 && headH > 0.02f;
    }

    /// <summary>Twisted towel headband (nejiri hachimaki) at the brow, knot at the front-left.</summary>
    private static void BuildHachimaki(GearBins g, FigScan s)
    {
        if (!HeadDims(s, out float top, out float hh, out var pts)) return;
        float h = top - 0.43f * hh;
        if (!Slice(s, pts, h, 0.06f * hh, out var c, out float rx, out float rz, out _)) return;
        c -= s.U * h; // GBand heights are relative
        rx *= 1.05f; rz *= 1.05f;
        GBand(g[0], s, c, rx, rz, rx * 0.985f, rz * 0.985f, h - 0.045f * hh, h + 0.045f * hh, 22);
        // twist ridges
        GRing(g[0], s, c + s.U * (h + 0.02f * hh), rx * 1.01f, rz * 1.01f, 0.022f * hh, 22, 5);
        float th = -28f * Mathf.Deg2Rad;
        var k = c + s.U * h + s.Fw * (Mathf.Cos(th) * rz * 1.04f) + s.R * (Mathf.Sin(th) * rx * 1.04f);
        GEll(g[0], k, s.R * 0.06f * hh, s.U * 0.055f * hh, s.Fw * 0.05f * hh, 8, 6);
        foreach (float sg in new[] { -1f, 1f })
        {
            var tip = k + s.U * (0.12f * hh) + s.R * (sg * 0.07f * hh) + s.Fw * (0.03f * hh);
            Beam(g[0], k, tip, 0.018f * hh);
        }
    }

    /// <summary>kind 0 = baseball cap (slot1 badge), 1 = knit fisherman's cap, 2 = straw hat,
    /// 3 = tenugui headscarf, 4 = bucket hat.</summary>
    private static void BuildHat(GearBins g, FigScan s, int kind)
    {
        if (!HeadDims(s, out float top, out float hh, out var pts)) return;
        float baseF = kind == 3 ? 0.60f : kind == 1 ? 0.50f : kind == 0 ? 0.40f : 0.46f;
        float h = top - baseF * hh;
        if (!Slice(s, pts, h, 0.06f * hh, out var c, out float rx, out float rz, out _)) return;
        float grow = kind == 3 || kind == 0 ? 1.03f : 1.07f;
        rx *= grow; rz *= grow;
        float ry = (top - h) * (kind == 1 ? 1.22f : kind == 2 ? 1.14f : kind == 0 ? 1.0f : 1.07f) + 0.01f * hh;
        GEll(g[0], c, s.R * rx, s.U * ry, s.Fw * rz, 18, 7, -6f, 90f);
        switch (kind)
        {
            case 0:
                GBrim(g[0], s, c, rx * 0.99f, rz * 0.99f, -58f, 58f, 10,
                      t => rz * 0.8f * Mathf.Pow(Mathf.Cos(t * Mathf.Deg2Rad * 0.9f), 1.2f), 0.035f * hh, 0.014f * hh);
                GEll(g[0], c + s.U * ry * 0.98f, s.R * 0.035f * hh, s.U * 0.02f * hh, s.Fw * 0.035f * hh, 8, 4);
                // licence badge on the crown front
                var bc = c + s.U * (ry * 0.42f) + s.Fw * (rz * 0.93f);
                var bn = (s.Fw * 0.9f + s.U * 0.42f).normalized;
                PBox(g[1], bc, s.R * (0.12f * hh), Vector3.Cross(bn, s.R).normalized * (0.07f * hh), bn * (0.01f * hh));
                break;
            case 1:
                GBand(g[0], s, c - s.U * h * 0f, rx * 1.05f, rz * 1.05f, rx * 1.07f, rz * 1.07f, -0.03f * hh, 0.13f * hh, 18);
                GRing(g[0], s, c + s.U * 0.13f * hh, rx * 1.03f, rz * 1.03f, 0.03f * hh, 18, 5);
                break;
            case 2:
                GBrim(g[0], s, c, rx, rz, 0f, 360f, 24, t => Mathf.Max(rx, rz) * 0.95f, 0.14f * hh, 0.01f * hh);
                GBand(g[1], s, c, rx * 1.01f, rz * 1.01f, rx * 0.98f, rz * 0.98f, 0.01f * hh, 0.12f * hh, 18);
                break;
            case 3:
                // knot and two tails at the nape
                var nape = c - s.Fw * (rz * 0.98f) - s.U * 0.02f * hh;
                GEll(g[0], nape, s.R * 0.07f * hh, s.U * 0.06f * hh, s.Fw * 0.05f * hh, 8, 6);
                foreach (float sg in new[] { -1f, 1f })
                    PCard(g[0], nape - s.U * 0.12f * hh + s.R * (sg * 0.05f * hh) - s.Fw * 0.02f * hh,
                          s.R * 0.035f * hh, s.U * 0.11f * hh, -s.Fw, twoFaced: true);
                break;
            case 4:
                GBrim(g[0], s, c, rx, rz, 0f, 360f, 20, t => Mathf.Max(rx, rz) * 0.34f, 0.12f * hh, 0.012f * hh);
                break;
        }
    }

    /// <summary>Towel slung round the neck with both ends down the chest.</summary>
    private static void BuildNeckTowel(GearBins g, FigScan s)
    {
        var head = s.B("Head");
        if (head == null) return;
        float fh = s.FigH;
        float hn = s.H(head.position);
        var chest = s.Of("Spine02");
        if (!Slice(s, chest, hn - 0.03f * fh, 0.035f * fh, out var c, out float rx, out float rz, out _)) return;
        rx *= 1.06f; rz *= 1.1f;
        float tr = 0.022f * fh;
        GRing(g[0], s, c, rx, rz, tr, 18, 6);
        foreach (float sg in new[] { -1f, 1f })
        {
            float th = sg * 30f * Mathf.Deg2Rad;
            var p = c + s.Fw * (Mathf.Cos(th) * rz) + s.R * (Mathf.Sin(th) * rx);
            var n = (s.Fw + s.R * (sg * 0.3f)).normalized;
            PBox(g[0], p - s.U * (0.07f * fh) + n * tr * 0.6f, s.R * (0.028f * fh), s.U * (0.07f * fh), n * (0.008f * fh));
        }
    }

    /// <summary>Long rubber bib apron from the chest to mid-shin, fitted to the torso slices, with a
    /// neck strap and waist ties (slot0 rubber, slot1 straps).</summary>
    private static void BuildApron(GearBins g, FigScan s)
    {
        var hips = s.B("Hips"); var kl = s.B("LeftLeg"); var kr = s.B("RightLeg"); var head = s.B("Head");
        if (hips == null || kl == null || kr == null || head == null) return;
        var torso = s.Of("Spine02", "Spine", "Spine01", "Hips");
        var legs = s.Of("LeftUpLeg", "RightUpLeg", "Hips");
        if (torso.Count < 20) return;
        float fh = s.FigH;
        float hip = s.H(hips.position), knee = (s.H(kl.position) + s.H(kr.position)) * 0.5f;
        float sh = float.NegativeInfinity;
        foreach (var p in s.Of("Spine02")) sh = Mathf.Max(sh, s.H(p));
        if (float.IsInfinity(sh)) sh = s.H(head.position);
        float y1 = hip + 0.64f * (sh - hip), y0 = knee - 0.45f * (knee - s.sole);
        const int nv = 9, nu = 6;
        var rowC = new Vector3[nv + 1]; var rowW = new float[nv + 1]; var rowZ = new float[nv + 1];
        float band = 0.035f * fh;
        Slice(s, torso, hip + 0.1f * (sh - hip), band, out var cHip, out float wHip, out _, out float zHip);
        if (wHip <= 0f) return;
        for (int j = 0; j <= nv; j++)
        {
            float y = Mathf.Lerp(y0, y1, (float)j / nv);
            bool upper = y >= hip;
            float w = wHip, z = zHip;
            if (Slice(s, upper ? torso : legs, y, band, out var cc, out float rx, out _, out float fr))
            {
                if (upper) { w = rx; z = fr; } else { w = Mathf.Max(wHip, rx); z = Mathf.Max(zHip, fr); }
            }
            if (!upper) z = Mathf.Max(z, zHip);
            float frac = upper ? Mathf.Lerp(0.98f, 0.70f, (y - hip) / Mathf.Max(1e-3f, y1 - hip)) : 1.06f;
            rowW[j] = w * frac; rowZ[j] = z + 0.02f * fh;
            rowC[j] = s.fig.position + s.R * s.X(cHip) + s.U * y;
        }
        PGrid(g[0], nu, nv,
              (i, j) =>
              {
                  float x = Mathf.Lerp(-1f, 1f, (float)i / nu);
                  return rowC[j] + s.R * (x * rowW[j]) + s.Fw * (rowZ[j] - x * x * 0.28f * rowW[j]);
              },
              (i, j) => new Vector2((float)i / nu, (float)j / nv),
              (i, j) => rowC[j] + s.Fw * (rowZ[j] - rowW[j]));
        // neck strap behind the head, and waist ties round to the back
        var neck = head.position - s.Fw * (0.02f * fh);
        foreach (float sg in new[] { -1f, 1f })
        {
            var corner = rowC[nv] + s.R * (sg * rowW[nv] * 0.8f) + s.Fw * (rowZ[nv] - 0.2f * rowW[nv]);
            Beam(g[1], corner, neck + s.R * (sg * 0.05f * fh), 0.006f * fh);
            int jw = Mathf.Clamp(Mathf.RoundToInt(nv * (hip + 0.12f * (sh - hip) - y0) / (y1 - y0)), 0, nv);
            var side = rowC[jw] + s.R * (sg * rowW[jw]) + s.Fw * (rowZ[jw] - 0.28f * rowW[jw]);
            var back = rowC[jw] + s.R * (sg * rowW[jw] * 0.6f) - s.Fw * (Mathf.Abs(zHip) + 0.03f * fh);
            Beam(g[1], side, back, 0.006f * fh);
        }
    }

    /// <summary>Knee-high rubber boot: a tapered shaft on the shin bone (slot0).</summary>
    private static void BuildBootShaft(GearBins g, FigScan s, string side)
    {
        var knee = s.B(side + "Leg"); var ankle = s.B(side + "Foot");
        if (knee == null || ankle == null) return;
        var shin = s.Of(side + "Leg");
        var a = knee.position - ankle.position; float len = a.magnitude; if (len < 1e-3f) return; a /= len;
        float rA = RadialMax(shin, ankle.position, a, len, 0f, 0.3f), rT = RadialMax(shin, ankle.position, a, len, 0.42f, 0.66f);
        if (rA <= 0f && rT <= 0f) return;
        if (rA <= 0f) rA = rT; if (rT <= 0f) rT = rA;
        rA = Mathf.Clamp(rA, rT * 1.0f, rT * 1.2f);   // the ankle slice catches the shoe collar: no bell-bottoms
        float e = 0.006f * s.FigH;
        var b0 = ankle.position - a * (0.08f * len);
        float h = 0.70f * len;
        PCyl(g[0], b0, a, rA * 1.12f + e, rT * 1.14f + e, h, 14, false);
        PCyl(g[0], b0 + a * (h - 0.06f * len), a, rT * 1.22f + e, rT * 1.22f + e, 0.07f * len, 14, false);
    }

    /// <summary>Boot foot: a rounded shell over the shoe, on the foot bone (slot0).</summary>
    private static void BuildBootFoot(GearBins g, FigScan s, string side)
    {
        var pts = s.Prefix(side + "Foot"); pts.AddRange(s.Prefix(side + "Toe"));
        if (pts.Count < 8) return;
        var o = s.fig.position;
        if (!AxisBounds(pts, o, s.U, s.Fw, s.R, out var mn, out var mx)) return;
        var mid = (mn + mx) * 0.5f; var half = (mx - mn) * 0.5f;
        var c = o + s.U * mid.x + s.Fw * mid.y + s.R * mid.z;
        GEll(g[0], c + s.U * (half.x * 0.1f), s.R * (half.z * 1.2f + 0.004f * s.FigH), s.U * (half.x * 1.28f),
             s.Fw * (half.y * 1.14f + 0.004f * s.FigH), 14, 8);
    }

    /// <summary>Rubber glove over the hand + a flared cuff (slot0).</summary>
    private static void BuildGlove(GearBins g, FigScan s, string side)
    {
        var hand = s.B(side + "Hand"); var fore = s.B(side + "ForeArm");
        if (hand == null || fore == null) return;
        var pts = s.Prefix(side + "Hand");
        if (pts.Count < 6) return;
        var a = (hand.position - fore.position).normalized;
        var p = Vector3.ProjectOnPlane(s.Fw, a).normalized; var q = Vector3.Cross(a, p);
        if (!AxisBounds(pts, hand.position, a, p, q, out var mn, out var mx)) return;
        var mid = (mn + mx) * 0.5f; var half = (mx - mn) * 0.5f;
        float e = 0.004f * s.FigH;
        var c = hand.position + a * mid.x + p * mid.y + q * mid.z;
        GEll(g[0], c, p * (half.y * 1.16f + e), a * (half.x * 1.1f + e), q * (half.z * 1.16f + e), 12, 8);
        float cr = Mathf.Max(half.y, half.z) * 1.1f + e;
        var wrist = hand.position + a * mn.x;
        PCyl(g[0], wrist - a * (0.45f * half.x * 2f), a, cr * 1.18f, cr, 0.5f * half.x * 2f, 12, false);
    }

    /// <summary>Udenuki arm covers on the forearm (slot0).</summary>
    private static void BuildArmCover(GearBins g, FigScan s, string side)
    {
        var hand = s.B(side + "Hand"); var fore = s.B(side + "ForeArm");
        if (hand == null || fore == null) return;
        var pts = s.Of(side + "ForeArm");
        var a = hand.position - fore.position; float len = a.magnitude; if (len < 1e-3f) return; a /= len;
        float r0 = RadialMax(pts, fore.position, a, len, 0.1f, 0.4f), r1 = RadialMax(pts, fore.position, a, len, 0.6f, 0.95f);
        if (r0 <= 0f || r1 <= 0f) return;
        float e = 0.005f * s.FigH;
        PCyl(g[0], fore.position + a * (0.12f * len), a, r0 * 1.14f + e, r1 * 1.16f + e, 0.86f * len, 12, false);
        PCyl(g[0], fore.position + a * (0.9f * len), a, r1 * 1.24f + e, r1 * 1.24f + e, 0.07f * len, 12, false);
    }
}
