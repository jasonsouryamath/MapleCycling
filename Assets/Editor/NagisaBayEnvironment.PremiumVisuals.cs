using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>NAGISA-DEST V: authored horizon depth, premium atmosphere, animated ambient clusters and shared-mesh budgets.</summary>
public static partial class NagisaBayEnvironment
{
    private const int PremiumSeed = 27183;

    [NagisaStage(118, "PremiumVisuals")]
    private static void BuildPremiumVisualsStage(Transform group)
    {
        EnsureRouteGround();
        TuneOceanMaterials(group.root);
        int islands = BuildDistantIslands(group);
        int clouds = BuildPremiumClouds(group);
        int palms = BuildAnimatedPalms(group);
        int banners = BuildAnimatedBanners(group);
        int boats = BuildAnimatedBoats(group);
        Debug.Log($"[nagisa-premium] ocean tuned, islands={islands} (LOD3), cloud clusters={clouds}, palms={palms}, flags/banners={banners}, boats={boats}; shared meshes + distance activation enabled.");
    }

    private static void TuneOceanMaterials(Transform root)
    {
        int tuned = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var mat = renderer.sharedMaterial;
            if (mat == null || mat.shader == null || mat.shader.name != "MapleRide/HDRP/NagisaOcean") continue;
            SetF(mat, "_SkyGain", 1.12f); SetF(mat, "_SpecGain", 0.55f);
            SetF(mat, "_WhitecapAmount", 0.42f); SetF(mat, "_DetailStrength", 1.08f);
            SetF(mat, "_FoamGain", 1.12f); SetF(mat, "_SwashAlpha", 0.9f);
            mat.enableInstancing = true; EditorUtility.SetDirty(mat); tuned++;
        }
        Shader.SetGlobalColor("_NagisaHazeTint", new Color(0.58f, 0.76f, 0.86f, 1f));
        Debug.Log($"[nagisa-premium] tuned {tuned} NagisaOcean material bindings; haze tint cyan-gold for bay depth.");
    }

    private static int BuildDistantIslands(Transform group)
    {
        var mat = LoadOrCreate("Nagisa_PremiumIsland_Haze", "MapleRide/HDRP/RidgeHaze");
        mat.SetColor("_Color", new Color(0.16f, 0.29f, 0.30f, 1f));
        mat.SetColor("_CrestColor", new Color(0.28f, 0.43f, 0.40f, 1f));
        mat.SetColor("_HazeColor", new Color(0.55f, 0.76f, 0.82f, 1f));
        mat.SetFloat("_HazeStart", 900f); mat.SetFloat("_HazeFull", 6200f);
        mat.SetFloat("_HazeMin", 0.18f); mat.SetFloat("_HazeMax", 0.88f);
        mat.enableInstancing = true;
        var low = IslandMesh("Nagisa_PremiumIsland_LOD0", 11, 1f);
        var mid = IslandMesh("Nagisa_PremiumIsland_LOD1", 7, 0.92f);
        var far = IslandMesh("Nagisa_PremiumIsland_LOD2", 4, 0.84f);
        var sites = new[] { new Vector3(-2100f, 5f, -31600f), new Vector3(-720f, 3f, -33100f), new Vector3(1280f, 4f, -32300f), new Vector3(2400f, 7f, -30800f), new Vector3(-2780f, 8f, -29200f) };
        int made = 0;
        foreach (var p in sites)
        {
            var island = new GameObject("Nagisa Distant Island " + made.ToString("00")).transform;
            island.SetParent(group, false); island.position = p; island.localScale = new Vector3(1.0f + made * 0.08f, 1f, 0.75f + made * 0.06f);
            var l0 = AddMesh(island, "Island LOD0", low, mat, false); var l1 = AddMesh(island, "Island LOD1", mid, mat, false); var l2 = AddMesh(island, "Island LOD2", far, mat, false);
            var lod = island.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.52f, new[] { l0.GetComponent<Renderer>() }), new LOD(0.20f, new[] { l1.GetComponent<Renderer>() }), new LOD(0.03f, new[] { l2.GetComponent<Renderer>() }) });
            lod.RecalculateBounds(); made++;
        }
        return made;
    }

    private static Mesh IslandMesh(string name, int points, float height)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int i = 0; i < points; i++) { float a = i * Mathf.PI * 2f / points; float r = 42f + 10f * Mathf.Sin(i * 2.7f); v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r * 0.45f)); uv.Add(Vector2.zero); }
        for (int i = 0; i < points; i++) { float a = i * Mathf.PI * 2f / points; float r = 27f + 8f * Mathf.Sin(i * 1.9f); v.Add(new Vector3(Mathf.Cos(a) * r, (0.45f + 0.55f * Mathf.Sin(a * 2.1f)) * 28f * height, Mathf.Sin(a) * r * 0.35f)); uv.Add(Vector2.up); }
        for (int i = 0; i < points; i++) { int n = (i + 1) % points; t.AddRange(new[] { i, n, points + i, n, points + n, points + i }); }
        return Finish(name, v, uv, t);
    }

    private static int BuildPremiumClouds(Transform group)
    {
        var mat = Cel("Nagisa_PremiumCloud", new Color(0.97f, 0.99f, 1f), 0f, 0f, 0f);
        mat.SetFloat("_Cull", 0f); mat.SetFloat("_ShadowAmbient", 1f); mat.enableInstancing = true;
        var mesh = CloudMesh("Nagisa_PremiumCloudMesh"); int count = 0;
        for (int i = 0; i < 12; i++)
        {
            var go = AddMesh(group, "Nagisa Cloud Cluster " + i.ToString("00"), mesh, mat, false);
            go.transform.position = new Vector3(-2200f + (i % 6) * 820f, 110f + (i % 3) * 12f, -34400f + (i / 6) * 960f);
            go.transform.localScale = new Vector3(1.5f + (i % 4) * 0.18f, 0.42f + (i % 3) * 0.08f, 0.8f + (i % 2) * 0.16f);
            count++;
        }
        return count;
    }

    private static int BuildAnimatedPalms(Transform group)
    {
        var mat = Cel("Nagisa_PremiumPalm", new Color(0.22f, 0.36f, 0.12f), 0.08f, 0.05f, 0.04f); mat.enableInstancing = true;
        var frond = FrondMesh("Nagisa_PremiumPalmFrond"); var roots = new List<Transform>(); var renderers = new List<Renderer>();
        for (int i = 0; i < 10; i++)
        {
            var root = new GameObject("Nagisa Animated Palm " + i.ToString("00")).transform; root.SetParent(group, false);
            root.position = _route.Position[_route.IndexAt(180f + i * 260f)] + _route.SideFlat(_route.IndexAt(180f + i * 260f)) * (i % 2 == 0 ? 8.6f : -8.6f); root.position = new Vector3(root.position.x, _ground.Height(root.position.x, root.position.z), root.position.z);
            var crown = new GameObject("Palm crown").transform; crown.SetParent(root, false); crown.localPosition = Vector3.up * 5.2f;
            for (int f = 0; f < 5; f++) { var leaf = AddMesh(crown, "Palm frond", frond, mat, false); leaf.transform.localRotation = Quaternion.Euler(0f, f * 72f, -18f - (f % 2) * 8f); renderers.Add(leaf.GetComponent<Renderer>()); }
            roots.Add(crown);
        }
        AddAmbientDriver(group, roots.ToArray(), new Transform[0], new Transform[0], renderers.ToArray(), 560f, 5f, 0f, 0f, 0.8f); return roots.Count;
    }

    private static int BuildAnimatedBanners(Transform group)
    {
        var mat = Cel("Nagisa_PremiumBanner", new Color(0.92f, 0.22f, 0.22f), 0.12f, 0.08f, 0.02f); mat.SetFloat("_Cull", 0f); mat.enableInstancing = true;
        var mesh = BannerMesh("Nagisa_PremiumBannerMesh"); var roots = new List<Transform>(); var renderers = new List<Renderer>();
        for (int i = 0; i < 8; i++) { var go = AddMesh(group, "Nagisa Resort Banner " + i.ToString("00"), mesh, mat, false); int ri = _route.IndexAt(240f + i * 330f); go.transform.position = _route.Position[ri] + _route.SideFlat(ri) * (i % 2 == 0 ? 7.1f : -7.1f) + Vector3.up * 3.1f; go.transform.rotation = Quaternion.LookRotation(_route.Tangent[ri], Vector3.up); roots.Add(go.transform); renderers.Add(go.GetComponent<Renderer>()); }
        AddAmbientDriver(group, roots.ToArray(), new Transform[0], new Transform[0], renderers.ToArray(), 420f, 9f, 0f, 0f, 1.3f); return roots.Count;
    }

    private static int BuildAnimatedBoats(Transform group)
    {
        var mat = Cel("Nagisa_PremiumBoat", new Color(0.92f, 0.92f, 0.86f), 0.35f, 0.25f, 0.05f); mat.enableInstancing = true;
        var mesh = BoatMesh("Nagisa_PremiumBoatMesh"); var roots = new List<Transform>(); var renderers = new List<Renderer>();
        for (int i = 0; i < 5; i++) { var go = AddMesh(group, "Nagisa Ambient Boat " + i.ToString("00"), mesh, mat, false); go.transform.position = new Vector3(-1500f + i * 650f, 0.38f, -30500f - (i % 2) * 700f); go.transform.localScale = Vector3.one * (0.8f + i * 0.08f); roots.Add(go.transform); renderers.Add(go.GetComponent<Renderer>()); }
        AddAmbientDriver(group, new Transform[0], roots.ToArray(), roots.ToArray(), renderers.ToArray(), 780f, 0f, 0.12f, 0.5f, 0.55f); return roots.Count;
    }

    private static void AddAmbientDriver(Transform group, Transform[] swayTargetsIn, Transform[] bobTargetsIn, Transform[] driftTargetsIn, Renderer[] renderers, float distance, float swayDeg, float bobHeightValue, float driftDistanceValue, float speed)
    {
        var host = new GameObject("Premium ambient activation").transform; host.SetParent(group, false);
        Transform[] all = swayTargetsIn.Length > 0 ? swayTargetsIn : (bobTargetsIn.Length > 0 ? bobTargetsIn : driftTargetsIn);
        if (all.Length > 0) host.position = all[0].position;
        var d = host.gameObject.AddComponent<NagisaPremiumAmbient>(); d.swayTargets = swayTargetsIn; d.bobTargets = bobTargetsIn; d.driftTargets = driftTargetsIn; d.renderers = renderers; d.activationDistance = distance; d.swayDegrees = swayDeg; d.bobHeight = bobHeightValue; d.driftDistance = driftDistanceValue; d.speed = speed;
    }

    private static Mesh CloudMesh(string name) { return EllipsoidMesh(name, 18, 10, 8, 1.0f); }
    private static Mesh FrondMesh(string name) { var v = new List<Vector3> { new Vector3(-.12f,0,0),new Vector3(.12f,0,0),new Vector3(.7f,0,2.6f),new Vector3(-.12f,.05f,0),new Vector3(.12f,.05f,0),new Vector3(.7f,.05f,2.6f) }; return Finish(name,v,new List<Vector2>{new Vector2(0,0),new Vector2(1,0),new Vector2(.8f,1),new Vector2(0,0),new Vector2(1,0),new Vector2(.8f,1)},new List<int>{0,2,1,3,4,5}); }
    private static Mesh BannerMesh(string name) { var v = new List<Vector3>{new Vector3(0,0,0),new Vector3(1.4f,0,0),new Vector3(1.4f,.75f,0),new Vector3(0,.75f,0)}; return Finish(name,v,new List<Vector2>{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)},new List<int>{0,1,2,0,2,3}); }
    private static Mesh BoatMesh(string name) { var v = new List<Vector3>{new Vector3(-1,0,-2),new Vector3(1,0,-2),new Vector3(1,0,1.5f),new Vector3(-1,0,1.5f),new Vector3(0,.7f,.2f)}; return Finish(name,v,new List<Vector2>{Vector2.zero,Vector2.right,Vector2.one,Vector2.up,new Vector2(.5f,.5f)},new List<int>{0,1,4,1,2,4,2,3,4,3,0,4}); }
    private static Mesh EllipsoidMesh(string name, int lon, int lat, int rings, float scale) { var v=new List<Vector3>(); var uv=new List<Vector2>(); var t=new List<int>(); for(int y=0;y<=rings;y++){float p=Mathf.PI*y/rings; for(int x=0;x<lon;x++){float a=Mathf.PI*2f*x/lon; v.Add(new Vector3(Mathf.Sin(p)*Mathf.Cos(a)*scale,Mathf.Cos(p)*scale*.55f,Mathf.Sin(p)*Mathf.Sin(a)*scale)); uv.Add(new Vector2((float)x/lon,(float)y/rings));}} for(int y=0;y<rings;y++)for(int x=0;x<lon;x++){int n=(x+1)%lon;int a=y*lon+x,b=y*lon+n,c=(y+1)*lon+x,d=(y+1)*lon+n;t.AddRange(new[]{a,c,b,b,c,d});} return Finish(name,v,uv,t); }
}
