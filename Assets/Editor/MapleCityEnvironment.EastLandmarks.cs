using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Maple City East landmarks: the hand-authored Blender kit (tools/blender/build_maple_east_landmarks.py ->
/// Assets/Environment/MapleCity/East/MapleEast_*.glb) placed along the district route.
///
/// CONSISTENCY WITH THE OLD TOWN. Every material slot on every landmark is REPLACED with a MapleCity CelMaterial (same CelLit
/// shader, same shade ramp, same rim/ambient settings as the Old Town's gate, canal stone and tram rails) - the GLBs'
/// own PBR materials are never rendered. Colours are the Old Town's warm pastel family. Triangle budgets sit at the same
/// 2-7k level as the Old Town props (crane 2.9k, lighthouse 3.4k, containers 6.6k, boat 1.8k, warehouse 3.6k, canopy 4k, stalls 6.8k).
/// Placement uses the SAME helpers as the Old Town builders (GroundHeight, RoadY, CarriagewayHalfWidth, PavementWidthM), and the
/// zones are written into <see cref="EastKeepOut"/> so BuildBuildings leaves room for them instead of intersecting them.
/// </summary>
public static partial class MapleCityEnvironment
{
    private const string EastKitDir = "Assets/Environment/MapleCity/East";

    /// <summary>Route-metre intervals the East building row must leave free (landmark zones). Empty for the Old Town.</summary>
    private static readonly List<Vector2> EastKeepOut = new List<Vector2>();

    private struct EastPlace
    {
        public string Asset;
        public float RouteM;        // metres along the district route
        public int Side;            // -1 = left (canal side), +1 = right (inland)
        public float Setback;       // metres beyond the pavement edge (ignored when Lateral > 0)
        public float Lateral;       // absolute lateral offset from the centreline, > 0 overrides Setback
        public float Yaw;           // extra yaw on top of "street-facing"
        public bool AlongRoute;     // true = long axis along the road (boats), not facing it
        public bool OnWater;        // y = canal water level instead of ground
    }

    /// <summary>The district plan. PROVISIONAL art tuning, like every number in this region.</summary>
    private static readonly EastPlace[] EastPlan =
    {
        // ---- Harbour Plaza (the 450-700 m gap shared with the Old Town's Maple Row guard)
        new EastPlace { Asset = "container_stack", RouteM = 478f, Side = +1, Setback = 4f },
        new EastPlace { Asset = "container_stack", RouteM = 506f, Side = +1, Setback = 4f, Yaw = 180f * 0f },
        new EastPlace { Asset = "gantry_crane",    RouteM = 548f, Side = +1, Setback = 9f },
        new EastPlace { Asset = "container_stack", RouteM = 590f, Side = +1, Setback = 4f },
        new EastPlace { Asset = "warehouse",       RouteM = 650f, Side = +1, Setback = 5f },
        new EastPlace { Asset = "fishing_boat",    RouteM = 520f, Side = -1, Lateral = 13f, AlongRoute = true, OnWater = true },
        new EastPlace { Asset = "fishing_boat",    RouteM = 612f, Side = -1, Lateral = 13.5f, AlongRoute = true, OnWater = true, Yaw = 180f },
        // ---- Fish Market (around 1.5-1.7 km)
        new EastPlace { Asset = "market_stalls",   RouteM = 1560f, Side = +1, Setback = 0.9f },
        new EastPlace { Asset = "warehouse",       RouteM = 1612f, Side = -1, Setback = 5f },
        // ---- Ferry Terminal finish
        new EastPlace { Asset = "ferry_canopy",    RouteM = 4570f, Side = -1, Setback = 2.5f },
        new EastPlace { Asset = "lighthouse",      RouteM = 4690f, Side = -1, Setback = 7f },
    };

    private static void PrepareEastKeepOut()
    {
        EastKeepOut.Clear();
        EastKeepOut.Add(new Vector2(1470f, 1670f));    // fish market
        EastKeepOut.Add(new Vector2(4510f, 4750f));    // ferry terminal + lighthouse
    }

    private static Color EastColor(string key)
    {
        switch (key)
        {
            case "steel": return new Color(0.66f, 0.68f, 0.70f);
            case "steel_dark": return new Color(0.30f, 0.32f, 0.36f);
            case "paint_red": return new Color(0.784f, 0.192f, 0.157f);          // = MapleCity_Vermilion (the Maple Gate)
            case "paint_white": return new Color(0.90f, 0.88f, 0.82f);
            case "paint_yellow": return new Color(0.93f, 0.74f, 0.22f);
            case "paint_blue": return new Color(0.20f, 0.36f, 0.58f);            // = the cycle-lane blue family
            case "brick": return new Color(0.62f, 0.38f, 0.30f);
            case "concrete": return new Color(0.495f, 0.535f, 0.520f);           // = MapleCity_CanalStone
            case "roof_metal": return new Color(0.42f, 0.46f, 0.50f);
            case "glass": return new Color(0.50f, 0.68f, 0.76f);
            case "wood": return new Color(0.50f, 0.36f, 0.24f);
            case "rope": return new Color(0.66f, 0.60f, 0.46f);
            case "tarp_a": return new Color(0.80f, 0.30f, 0.26f);
            case "tarp_b": return new Color(0.22f, 0.50f, 0.62f);
            case "tarp_c": return new Color(0.92f, 0.74f, 0.30f);
            case "container_red": return new Color(0.68f, 0.24f, 0.20f);
            case "container_blue": return new Color(0.22f, 0.36f, 0.58f);
            case "container_green": return new Color(0.24f, 0.46f, 0.34f);
            case "container_orange": return new Color(0.86f, 0.46f, 0.18f);
            case "container_grey": return new Color(0.56f, 0.58f, 0.60f);
            case "container_white": return new Color(0.86f, 0.86f, 0.82f);
            case "lamp": return new Color(1.0f, 0.88f, 0.62f);
            default: return new Color(0.6f, 0.6f, 0.6f);
        }
    }

    private static Material EastMaterial(string key)
    {
        float gloss = 0.22f, spec = 0.18f, rim = 0.45f;
        if (key == "glass") { gloss = 0.7f; spec = 0.4f; rim = 0.5f; }
        else if (key.StartsWith("steel") || key == "roof_metal") { gloss = 0.34f; spec = 0.3f; }
        else if (key == "brick" || key == "concrete" || key == "wood" || key == "rope") { gloss = 0.12f; spec = 0.08f; }
        else if (key == "lamp") { gloss = 0.5f; spec = 0.5f; rim = 0.8f; }
        return CelMaterial("MapleEast_" + key, EastColor(key), gloss: gloss, spec: spec, rim: rim);
    }

    private static void BuildEastLandmarks(Transform east, CityRoute route)
    {
        PrepareEastKeepOutIfNeeded();
        var group = new GameObject("East Landmarks").transform;
        group.SetParent(east, false);
        int placed = 0, missing = 0;
        foreach (var pl in EastPlan)
        {
            string path = $"{EastKitDir}/MapleEast_{pl.Asset}.glb";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { missing++; Debug.LogWarning($"[maple-east] landmark missing: {path}"); continue; }

            int i = route.IndexAt(pl.RouteM);
            float half = CarriagewayHalfWidth(route.Frac(i));
            float lateral = pl.Lateral > 0f ? pl.Lateral : half + PavementWidthM + pl.Setback;
            Vector3 side = route.SideFlat(i);
            Vector3 p = route.Position[i] + side * (pl.Side * lateral);
            // The road is on the opposite side of the asset: its +Z (street-facing) must point from the asset towards the road.
            Vector3 towardRoad = -side * pl.Side;
            Vector3 tangent = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
            Quaternion rot = pl.AlongRoute ? Quaternion.LookRotation(tangent, Vector3.up)
                                           : Quaternion.LookRotation(towardRoad, Vector3.up);
            rot *= Quaternion.Euler(0f, pl.Yaw, 0f);

            float y;
            if (pl.OnWater) y = RoadY(route, i, -half) + KerbHeightM - 0.06f;            // = the canal water plane
            else y = GroundHeight(route, i, pl.Side * lateral) + 0.02f;
            p.y = y;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            go.name = $"East {pl.Asset} @{pl.RouteM:0}";
            go.transform.SetPositionAndRotation(p, rot);
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var src = mr.sharedMaterials;
                var dst = new Material[src.Length];
                for (int s = 0; s < src.Length; s++)
                    dst[s] = EastMaterial(src[s] != null ? src[s].name : "concrete");
                mr.sharedMaterials = dst;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.OccludeeStatic);
            placed++;
        }
        Debug.Log($"[maple-east] landmarks: placed {placed}, missing {missing}.");
    }

    private static void PrepareEastKeepOutIfNeeded() { if (EastKeepOut.Count == 0) PrepareEastKeepOut(); }
}
