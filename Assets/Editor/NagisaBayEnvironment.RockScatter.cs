// NB-ROCKS: three sculpted, moss-masked rock variants on Nagisa Bay headlands.
// Owns only this new stage; shared route/ground helpers remain read-only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class NagisaBayEnvironment
{
    private const string RockModelDir = "Assets/Environment/NagisaBay/Models";
    private const string RockShader = "MapleRide/HDRP/RockMoss";

    [NagisaStage(72, "RockScatter")]
    private static void BuildRockScatterStage(Transform group)
    {
        EnsureRouteGround();
        var mats = new[]
        {
            RockMaterial("Nagisa_RockMoss_A_Mat", new Color(0.40f, 0.37f, 0.32f), new Color(0.24f, 0.39f, 0.18f)),
            RockMaterial("Nagisa_RockMoss_B_Mat", new Color(0.35f, 0.34f, 0.32f), new Color(0.22f, 0.36f, 0.17f)),
            RockMaterial("Nagisa_RockMoss_C_Mat", new Color(0.30f, 0.31f, 0.30f), new Color(0.18f, 0.33f, 0.16f)),
        };
        var prefabs = new[] { LoadRock(0), LoadRock(1), LoadRock(2) };
        if (prefabs.Any(p => p == null))
        {
            Debug.LogWarning("[nagisa-rocks] one or more GLBs are missing; run build_nagisa_rocks_scatter.py first");
            return;
        }

        int placed = 0;
        // Coastal headlands and rocky shore: sample both sides, then keep only points
        // with ample coast distance so the highway/promenade bands remain untouched.
        for (int i = 0; i < _route.Count; i += 9)
        {
            float d = _route.Distance[i];
            if (!((d >= 700f && d <= 2100f) || (d >= 3900f && d <= 4700f) ||
                  (d >= 12200f && d <= 13900f) || (d >= 14600f && d <= 15200f))) continue;
            Vector3 side = _route.SideFlat(i);
            float sign = ((i / 9) & 1) == 0 ? 1f : -1f;
            float offset = 18f + ((i / 9) % 4) * 7f;
            // NB3 owns the inland -8..-42 m band for car traffic, sound wall and
            // frontage.  The generic CanPlace road corridor is intentionally
            // narrower, so explicitly keep inland rock silhouettes beyond that
            // band while retaining the authored seaward headland rhythm.
            if (sign < 0f && offset < 44f) continue;
            Vector3 p = _route.Position[i] + side * (sign * offset);
            if (_ground.Coast(p.x, p.z) < 16f) continue;
            float footprint = sign > 0f ? 4.5f : 5.5f;
            if (!CanPlace(p.x, p.z, footprint, out float y, 12f)) continue;
            int variant = (placed + i / 9) % 3;
            float scale = variant == 0 ? 0.86f + (i % 5) * 0.028f :
                          variant == 1 ? 0.78f + (i % 4) * 0.035f : 0.64f + (i % 6) * 0.026f;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[variant], group);
            go.name = "Nagisa Rock Moss " + (char)('A' + variant) + " " + placed.ToString("00");
            go.transform.position = new Vector3(p.x, y - 0.04f, p.z);
            go.transform.rotation = Quaternion.Euler(0f, (i * 37) % 360, 0f);
            go.transform.localScale = Vector3.one * scale;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.sharedMaterials = new[] { mats[variant] };
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
            placed++;
            if (placed >= 48) break;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[nagisa-rocks] RockScatter placed {placed} sculpted moss rocks (A/B/C, coast/headland only)");
    }

    private static GameObject LoadRock(int variant)
    {
        string path = $"{RockModelDir}/Nagisa_RockMoss_{(char)('A' + variant)}.glb";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static Material RockMaterial(string name, Color rock, Color moss)
    {
        var m = LoadOrCreate(name, RockShader);
        m.SetColor("_Color", rock);
        m.SetColor("_MossColor", moss);
        m.SetFloat("_MossMaskGamma", 1.15f);
        m.SetFloat("_MossRoughness", 0.78f);
        m.SetFloat("_DetailScale", 5.5f);
        m.SetFloat("_DetailAmount", 0.22f);
        m.SetFloat("_ShadeStrength", 0.46f);
        m.SetColor("_ShadeColor", new Color(0.25f, 0.27f, 0.28f));
        m.SetFloat("_AmbientStrength", 0.92f);
        m.SetFloat("_ShadowAmbient", 0.58f);
        m.SetFloat("_RimStrength", 0.25f);
        EditorUtility.SetDirty(m);
        return m;
    }
}
