using System;
using UnityEditor;
using UnityEngine;

public static class ShuntaRouteSmoothValidation
{
    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("[shunta-smooth] " + message);
        Debug.Log("[shunta-smooth] PASS " + message);
    }

    public static void Run()
    {
        GameObject root = null;
        try
        {
            var line = new ShuntaCourseData { elevation = new[] {
                new ShuntaElevationKey { km = 0f, m = 0f },
                new ShuntaElevationKey { km = 0.125f, m = 10f },
                new ShuntaElevationKey { km = 0.25f, m = 20f },
                new ShuntaElevationKey { km = 0.5f, m = 40f } } };
            for (int i = 0; i <= 500; i++)
                if (Mathf.Abs(line.ElevationAtKm(i / 1000f) - i * 0.08f) > 0.0001f)
                    throw new InvalidOperationException("constant grade distorted at " + i + " m");
            Require(true, "unevenly spaced constant-grade keys remain linear");
            var c = ShuntaCourseData.Load();
            Require(c != null, "course loads");
            float maxJump = 0f;
            for (int i = 0; i < c.elevation.Length - 1; i++)
            {
                var a = c.elevation[i]; var b = c.elevation[i + 1];
                Require(Mathf.Abs(c.ElevationAtKm(a.km) - a.m) < 0.001f, "key " + i + " preserved");
                for (int j = 0; j <= 50; j++)
                {
                    float y = c.ElevationAtKm(Mathf.Lerp(a.km, b.km, j / 50f));
                    if (y < Mathf.Min(a.m, b.m) - 0.001f || y > Mathf.Max(a.m, b.m) + 0.001f)
                        throw new InvalidOperationException("elevation overshoot at key " + i);
                }
                if (i > 0)
                {
                    const float h = 0.001f;
                    float left = (a.m - c.ElevationAtKm(a.km - h)) / h;
                    float right = (c.ElevationAtKm(a.km + h) - a.m) / h;
                    maxJump = Mathf.Max(maxJump, Mathf.Abs(left - right) / 10f);
                }
            }
            Require(maxJump < 0.6f, "continuous key slopes; 1 m one-sided grade difference " + maxJump.ToString("F3") + "%");
            root = new GameObject("Shunta smooth validation");
            root.SetActive(false);
            var bld = root.AddComponent<ShuntaRouteBuilder>();
            bld.buildRibbon = bld.buildGates = false;
            bld.Rebuild();
            float maxSpacing = 0f, minSpacing = float.MaxValue, worstGrade = 0f;
            for (int i = 1; i < bld.Positions.Length; i++)
            {
                Vector3 d = bld.Positions[i] - bld.Positions[i - 1];
                float horizontal = new Vector2(d.x, d.z).magnitude;
                maxSpacing = Mathf.Max(maxSpacing, horizontal);
                if (i < bld.Positions.Length - 1) minSpacing = Mathf.Min(minSpacing, horizontal);
                if (bld.Km[i] <= bld.Km[i - 1]) throw new InvalidOperationException("unordered sample " + i);
                worstGrade = Mathf.Max(worstGrade, Mathf.Abs(d.y) / horizontal * 100f);
            }
            Require(maxSpacing <= 10.01f && minSpacing >= 9.7f, "horizontal spacing " + minSpacing.ToString("F3") + ".." + maxSpacing.ToString("F3") + " m");
            Require(Mathf.Abs(bld.Km[bld.Km.Length - 1] - c.distanceKm) < 0.00001f, "exact finish km");
            float riddenGain = 0f;
            for (int i = 1; i < c.elevation.Length; i++)
            {
                float a = Mathf.Clamp(c.elevation[i - 1].km, 0f, c.distanceKm);
                float b = Mathf.Clamp(c.elevation[i].km, 0f, c.distanceKm);
                riddenGain += Mathf.Max(0f, c.ElevationAtKm(b) - c.ElevationAtKm(a));
            }
            Require(Mathf.Abs(bld.Ascent - riddenGain) < 0.1f, "ridden gain preserved " + bld.Ascent.ToString("F2") + " vs " + riddenGain.ToString("F2"));
            Debug.Log("[shunta-smooth] samples=" + bld.Positions.Length + " worst segment grade=" + worstGrade.ToString("F2") + "% key slope difference=" + maxJump.ToString("F3") + "%");
            Debug.Log("[shunta-smooth] ALL PASS");
        }
        finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); }
    }
}
