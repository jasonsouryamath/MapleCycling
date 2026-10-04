using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Deterministic, streamed city dressing. Never changes road, physics or authored landmarks.</summary>
public sealed partial class ShuntaNeoTokyo : MonoBehaviour
{
    public const float ChunkKm = 0.2f;
    public ShuntaRouteBuilder route;
    public static ShuntaNeoTokyo Instance { get; private set; }
    public int BuildingCount { get; private set; }
    public int StreetlightCount { get; private set; }
    public int SignCount { get; private set; }
    public int DetailCount { get; private set; }
    public int ChunkCount => chunks.Count;
    public int TriangleCount { get; private set; }
    public float LastBuildMs { get; private set; }
    public bool Ready => route != null && route.Course != null && materials.Count > 0;

    sealed class Block
    {
        public GameObject root, detail;
        public readonly List<Mesh> meshes = new List<Mesh>();
        public readonly List<Light> lights = new List<Light>();
        public readonly List<MeshRenderer> casters = new List<MeshRenderer>();
        public int buildings, signs, details, triangles, streetlights, infill; public readonly int[] families = new int[6];
    }
    readonly Dictionary<int, Block> chunks = new Dictionary<int, Block>();
    readonly List<int> retire = new List<int>();
    readonly List<Material> materials = new List<Material>();
    readonly List<Texture2D> textures = new List<Texture2D>();
    RideSession session;
    float nextResolve;
    Material concrete, brick, metal, glass, warm, cool, neonPink, neonBlue, sign, pavement, canopy, foliage;
    Material tower, streetLamp;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("MR_SHUNTA_CITY") == "0" || Instance != null) return;
        var go = new GameObject("Shunta Neo Tokyo City");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ShuntaNeoTokyo>();
    }

    void Update()
    {
        if (Time.unscaledTime >= nextResolve)
        {
            nextResolve = Time.unscaledTime + 0.5f;
            if (session == null) session = FindFirstObjectByType<RideSession>();
            bool active = session != null && session.Course != null && session.Course.Id == ShuntaRouteProvider.CourseId;
            if (!active) { if (chunks.Count > 0) ClearBlocks(); return; }
            var found = FindFirstObjectByType<ShuntaRouteBuilder>();
            if (found != route) { ClearBlocks(); route = found; ValidateRoadMaterials(); }
            if (route != null && route.Course != null && materials.Count == 0) MakeMaterials();
        }
        if (session == null || session.Course == null || session.Course.Id != ShuntaRouteProvider.CourseId || !Ready) return;
        float km = Mathf.Clamp(session.DistanceM / session.Course.Length * route.Course.distanceKm, 0f, route.Course.distanceKm);
        StreamAround(km, true);
    }

    public void Prepare(ShuntaRouteBuilder builder)
    {
        ClearBlocks(); route = builder;
        if (route != null && route.Course == null) route.Rebuild();
        if (materials.Count == 0) MakeMaterials();
        ValidateRoadMaterials();
    }

    /// <summary>Same chunk content as gameplay, built synchronously for editor validation/captures.</summary>
    public void PreviewAt(float km) { if (Ready) StreamAround(km, false); }

    void StreamAround(float km, bool onePerFrame)
    {
        int centre = Mathf.FloorToInt(km / ChunkKm);
        int last = Mathf.CeilToInt(route.Course.distanceKm / ChunkKm) - 1;
        int budget = onePerFrame ? 1 : 20;
        // Closest blocks first, ahead and behind; render visibility also uses actual world distance.
        for (int offset = 0; offset <= 5 && budget > 0; offset++)
            for (int side = 0; side < (offset == 0 ? 1 : 2) && budget > 0; side++)
            {
                int id = centre + (side == 0 ? offset : -offset);
                if (id < 0 || id > last || chunks.ContainsKey(id)) continue;
                BuildBlock(id); budget--;
            }
        Vector3 rider = route.PositionAtKm(km);
        retire.Clear();
        foreach (var kv in chunks)
        {
            if (Mathf.Abs(kv.Key - centre) > 7) { retire.Add(kv.Key); continue; }
            float distance = Vector3.Distance(rider, route.PositionAtKm((kv.Key + 0.5f) * ChunkKm));
            kv.Value.root.SetActive(distance < 1400f);
            kv.Value.detail.SetActive(distance < 440f);
            foreach (var light in kv.Value.lights) light.enabled = (light.transform.position-rider).sqrMagnitude < 130f*130f;
            foreach (var caster in kv.Value.casters) caster.shadowCastingMode = distance < 300f ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
        foreach (int id in retire) RemoveBlock(id);
    }

    void BuildBlock(int id)
    {
        float start = id * ChunkKm, end = Mathf.Min(start + ChunkKm, route.Course.distanceKm);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var block = new Block { root = new GameObject("Neo Tokyo block " + id) { hideFlags = HideFlags.DontSave } };
        block.root.transform.SetParent(transform, false);
        block.detail = new GameObject("Street detail") { hideFlags = HideFlags.DontSave };
        block.detail.transform.SetParent(block.root.transform, false);
        var coarse = new ShuntaLookKit.MeshBag(); var fine = new ShuntaLookKit.MeshBag();
        var random = new System.Random(7193 + id * 104729);
        BuildStreetInfrastructure(start, end, coarse, block);
        for (float km = start + 0.008f; km < end; km += 0.018f)
        {
            int lot = Mathf.RoundToInt((km - start - .008f) / .018f);
            var zone = route.Course.ZoneAtKm(km);
            if (zone == null || zone.index == 4 || zone.index == 10) continue;
            var p = route.PositionAtKm(km);
            var forward = route.TangentAtKm(km); forward.y = 0f; forward.Normalize();
            var right = new Vector3(forward.z, 0f, -forward.x);
            bool urban = zone.index == 1 || zone.index == 2 || zone.index == 7 || zone.index == 8;
            bool coast = zone.index >= 11;
            for (int side = -1; side <= 1; side += 2)
            {
                if (coast && (side > 0 || random.NextDouble() < 0.05)) continue;
                Quaternion q = Quaternion.LookRotation(right * side, Vector3.up);
                if (urban || coast)
                {
                    float width = R(random, 16.5f, 17.3f), depth = R(random, 6f, 9f);
                    float height = R(random, 9f, zone.index == 1 ? 32f : 20f);
                    var centre = p + right * side * (route.roadWidth * 0.5f + 3.6f + depth * 0.5f);
                    if (ClearFootprint(centre, q, width, depth, route.roadWidth*.5f+1.2f))
                    {
                        BuildShop(coarse, fine, centre, q, width, depth, height, random, block);
                        StreetFurniture(fine, p, forward, right, side, q, random, block);
                        if (lot % 6 == 0) ShopLight(centre - right * side * (depth * .5f + 1f) + Vector3.up * 2.5f, side, block);
                    }
                }

            }
            if (urban && lot % 4 == 0)
                OverheadUtilities(fine, p, forward, right, block);
        }
        BuildSkylineCluster(id, coarse, fine, block);
        BuildCityInfill(id, coarse, fine, block);
        Flush(coarse, block.root.transform, "City mass", block, true);
        Flush(fine, block.detail.transform, "City detail", block, false);
        chunks.Add(id, block);
        BuildingCount += block.buildings; SignCount += block.signs; DetailCount += block.details; TriangleCount += block.triangles; StreetlightCount += block.streetlights;
        watch.Stop(); LastBuildMs = (float)watch.Elapsed.TotalMilliseconds;
    }

    bool ClearOfRoad(Vector3 p, float ownKm, float radius)
    {
        // Protect nearby bends/parallel road sections; no scenery can intrude into another ribbon.
        var pos = route.Positions;
        var matrix = route.transform.localToWorldMatrix;
        Vector3 a = matrix.MultiplyPoint3x4(pos[0]);
        for (int i = 1; i < pos.Length; i++)
        {
            Vector3 b = matrix.MultiplyPoint3x4(pos[i]);
            if (DistanceSquaredToRoadSegment(p, a, b) < radius * radius) return false;
            a = b;
        }
        return true;
    }

    public static float DistanceSquaredToRoadSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        float dx = b.x-a.x, dz = b.z-a.z, length = dx*dx+dz*dz;
        float t = length > .000001f ? Mathf.Clamp01(((p.x-a.x)*dx+(p.z-a.z)*dz)/length) : 0f;
        float x = p.x-a.x-t*dx, z = p.z-a.z-t*dz;
        return x*x+z*z;
    }

    static float R(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    void Flush(ShuntaLookKit.MeshBag bag, Transform parent, string label, Block block, bool shadows)
    {
        foreach (var go in bag.Flush(parent, label))
        {
            var mesh = go.GetComponent<MeshFilter>().sharedMesh; block.meshes.Add(mesh);
            mesh.RecalculateTangents();
            block.triangles += (int)mesh.GetIndexCount(0) / 3;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            if (shadows) block.casters.Add(renderer);
        }
    }
    void RemoveBlock(int id)
    {
        var b = chunks[id]; InfillBuildingCount-=b.infill; for(int i=0;i<6;i++) skylineFamilies[i]-=b.families[i]; BuildingCount -= b.buildings; SignCount -= b.signs; DetailCount -= b.details; TriangleCount -= b.triangles; StreetlightCount -= b.streetlights;
        foreach (var mesh in b.meshes) Dispose(mesh);
        Dispose(b.root); chunks.Remove(id);
    }
    void ClearBlocks()
    {
        retire.Clear(); foreach (int id in chunks.Keys) retire.Add(id);
        foreach (int id in retire) RemoveBlock(id);
    }
    static void Dispose(UnityEngine.Object obj) { if (obj == null) return; if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    void OnDestroy()
    {
        ClearBlocks(); foreach (var m in materials) Dispose(m); foreach (var t in textures) Dispose(t);
        materials.Clear(); textures.Clear(); if (Instance == this) Instance = null;
    }
}


