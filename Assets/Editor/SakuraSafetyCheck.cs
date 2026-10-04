using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Regression guard for the invisible-wall class of bug: walks the authored road centreline from
/// SakuraRoute.json and asks the rider's safety net whether it would displace a rider sitting
/// exactly on it. Any displacement, or any sample outside the recovery bounds, is an invisible
/// wall across the road. Run this after every route change.
/// </summary>
public static class SakuraSafetyCheck
{
    [MenuItem("MapleRide/Check Road Safety")]
    public static void Check()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity");

        var safety = Object.FindObjectsByType<KuroRoadSafety>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None).FirstOrDefault();
        if (safety == null) { Debug.LogError("[check] no KuroRoadSafety"); return; }

        var json = System.IO.File.ReadAllText("Assets/Environment/SakuraPass/SakuraRoute.json");
        // samples are  {"x":..,"y":..,"z":..,"d":..}
        var ms = System.Text.RegularExpressions.Regex.Matches(
            json, "\"p\"\\s*:\\s*\\[\\s*(-?[\\d.eE+]+)\\s*,\\s*(-?[\\d.eE+]+)\\s*,\\s*(-?[\\d.eE+]+)\\s*\\][\\s\\S]*?\"d\"\\s*:\\s*(-?[\\d.eE+]+)");
        Debug.Log($"[check] {ms.Count} road samples, safety polyline {safety.route.Length} pts, corridor {safety.roadCorridor}");

        int outside = 0, teleport = 0;
        float worst = 0f; Vector3 worstAt = Vector3.zero; float worstD = 0f;

        foreach (System.Text.RegularExpressions.Match m in ms)
        {
            var p = new Vector3(float.Parse(m.Groups[1].Value), float.Parse(m.Groups[2].Value),
                                float.Parse(m.Groups[3].Value));
            float d = float.Parse(m.Groups[4].Value);

            if (p.x < safety.minX || p.x > safety.maxX || p.z < safety.minZ ||
                p.z > safety.maxZ || p.y < safety.minY)
            {
                teleport++;
                Debug.LogError($"[check] TELEPORT at d={d:F0} m  {p}");
                continue;
            }

            float best = float.MaxValue;
            for (int i = 0; i < safety.route.Length - 1; i++)
            {
                Vector3 a = safety.route[i], b = safety.route[i + 1];
                var ab = new Vector3(b.x - a.x, 0f, b.z - a.z);
                var ap = new Vector3(p.x - a.x, 0f, p.z - a.z);
                float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(.001f, ab.sqrMagnitude));
                var q = a + (b - a) * t;
                float sqr = (new Vector3(p.x, 0, p.z) - new Vector3(q.x, 0, q.z)).sqrMagnitude;
                if (sqr < best) best = sqr;
            }
            float dist = Mathf.Sqrt(best);
            if (dist > worst) { worst = dist; worstAt = p; worstD = d; }
            if (dist > safety.roadCorridor) outside++;
        }

        Debug.Log($"[check] worst centreline deviation {worst:F2} m at d={worstD:F0} m {worstAt}");
        Debug.Log(outside == 0 && teleport == 0
            ? "[check] PASS - the whole road is inside the corridor; no invisible wall."
            : $"[check] FAIL - {outside} samples outside the corridor, {teleport} would teleport.");
    }
}
