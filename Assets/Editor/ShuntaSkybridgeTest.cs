using UnityEditor;
using UnityEngine;

/// <summary>
/// Headless checks for the Shunta skybridge placement rules and clearances (pure maths, no scene).
/// Run: Unity -batchmode -quit -executeMethod ShuntaSkybridgeTest.Run
/// Exits non-zero on failure.
/// </summary>
public static class ShuntaSkybridgeTest
{
    public static void Run()
    {
        int fails = 0;
        void Check(bool ok, string what) { Debug.Log($"[shunta-skybridge] {(ok ? "PASS" : "FAIL")} {what}"); if (!ok) fails++; }
        const float chunk = ShuntaNeoTokyo.ChunkKm;
        const int chunks = 1000;

        // Clearance: above the 10.3 m utility poles and the ~7.2 m streetlight arms, well below the sky trains (62 m).
        Check(ShuntaNeoTokyo.SkybridgeDeckHeight > 10.3f + .5f, "deck underside clears utility poles");
        Check(ShuntaNeoTokyo.SkybridgeDeckHeight + 3.1f < 40f, "bridge top stays far below the sky-train lines");
        Check(ShuntaNeoTokyo.SkybridgeLegOffset > 2.4f + .6f, "legs sit outside the utility poles");

        int count = 0; float prev = -999f, minGap = 999f; bool deterministic = true, inside = true;
        for (int id = 0; id < chunks; id++)
        {
            float start = id * chunk, end = start + chunk;
            float a = ShuntaNeoTokyo.SkybridgeKmInChunk(id, start, end);
            float b = ShuntaNeoTokyo.SkybridgeKmInChunk(id, start, end);
            if (a != b) deterministic = false;
            if (a < 0f) continue;
            count++;
            if (a < start + ShuntaNeoTokyo.SkybridgeEdgeMarginKm - 1e-5f || a > end - ShuntaNeoTokyo.SkybridgeEdgeMarginKm + 1e-5f) inside = false;
            if (prev > -900f) minGap = Mathf.Min(minGap, a - prev);
            prev = a;
        }
        Check(deterministic, "placement is deterministic per chunk");
        Check(inside, "every bridge lies inside its own chunk, away from the edges");
        Check(count > chunks * .4f && count < chunks * .7f, $"about {ShuntaNeoTokyo.SkybridgeChance:P0} of chunks get a bridge (got {count}/{chunks})");
        Check(minGap >= .05f, $"bridges are at least 50 m apart (min gap {minGap * 1000f:0} m)");
        Check(ShuntaNeoTokyo.SkybridgeKmInChunk(0, 0f, .02f) < 0f, "a chunk too short for the margins gets no bridge");

        if (fails > 0) { Debug.LogError($"[shunta-skybridge] FAILED: {fails} check(s)"); if (Application.isBatchMode) EditorApplication.Exit(2); }
        else Debug.Log("[shunta-skybridge] ALL PASS");
    }
}
