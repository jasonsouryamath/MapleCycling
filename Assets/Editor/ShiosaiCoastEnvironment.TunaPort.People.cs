using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Tuna port, part 4: townsfolk. Same donor approach as the Azora villages: clone the Minato
// crowd figures already in the scene, civilian-dress them with MapleCityLife's V looks, and give
// the market trades a rubber apron + a twisted towel headband (hachimaki). See TunaPort.cs.
public static partial class ShiosaiCoastEnvironment
{
    private sealed class PortCast
    {
        public readonly List<MinatoCrowdActor> walk = new List<MinatoCrowdActor>();
        public readonly List<MinatoCrowdActor> stand = new List<MinatoCrowdActor>();
        public readonly List<MinatoCrowdActor> sit = new List<MinatoCrowdActor>();
    }

    private static int _portPeople;
    private static readonly Dictionary<MinatoCrowdActor, string> _portDonor = new Dictionary<MinatoCrowdActor, string>();

    private static PortCast PortCrowdCast()
    {
        var cast = new PortCast();
        _portDonor.Clear();
        var all = Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int named = 0, rigged = 0;
        string sample = null;
        foreach (var a in all)
        {
            if (a == null) continue;
            if (sample == null) sample = a.name;
            if (!a.name.StartsWith("Crowd_", System.StringComparison.Ordinal)) continue;
            named++;
            if (a.transform.Find("LOD0 High Skinned/Rigged Character") == null) continue;
            rigged++;
            if (a.motion == MinatoCrowdActor.MotionKind.Walk) cast.walk.Add(a);
            else if (a.motion == MinatoCrowdActor.MotionKind.Idle || a.motion == MinatoCrowdActor.MotionKind.Wave) cast.stand.Add(a);
            else if (a.motion == MinatoCrowdActor.MotionKind.Sit && a.transform.Find("Timber Waterfront Bench") != null) cast.sit.Add(a);
            _portDonor[a] = a.name;
        }
        Debug.Log($"[tunaport] crowd scan: {all.Length} MinatoCrowdActor, {named} 'Crowd_', {rigged} rigged " +
                  $"(first: {sample ?? "none"}), {cast.sit.Count} bench sitters.");
        if (cast.stand.Count == 0) cast.stand.AddRange(cast.walk);
        if (cast.walk.Count == 0) cast.walk.AddRange(cast.stand);
        cast.walk.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        cast.stand.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        cast.sit.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        return cast;
    }

    private static readonly Color[] ApronColours =
    {
        new Color(0.72f, 0.30f, 0.10f), new Color(0.10f, 0.16f, 0.34f), new Color(0.16f, 0.34f, 0.22f),
        new Color(0.07f, 0.08f, 0.10f), new Color(0.66f, 0.60f, 0.20f),
    };

    /// <summary>
    /// Spawns one townsperson. role: Auctioneer / Buyer / Inspector / Fishmonger / Worker get the
    /// market kit (apron + hachimaki); everyone else is a shopper in plain civilian dress.
    /// </summary>
    private static GameObject PortPerson(List<MinatoCrowdActor> pool, Transform parent, Vector3 pos, Vector3 face,
                                         MinatoCrowdActor.MotionKind kind, float seed, string role, Vector3? end = null)
    {
        if (pool == null || pool.Count == 0) return null;
        seed = Mathf.Repeat(seed, 1f);
        var src = pool[Mathf.Abs((int)(seed * 9973f + _portPeople * 7919)) % pool.Count];
        var go = (GameObject)Object.Instantiate(src.gameObject, parent);
        go.name = $"Port {role} {_portPeople:D3}";
        go.hideFlags = HideFlags.None;
        go.SetActive(true);
        bool seated = kind == MinatoCrowdActor.MotionKind.Sit;
        var bench = go.transform.Find("Timber Waterfront Bench");
        if (bench != null && !seated) Object.DestroyImmediate(bench.gameObject);
        face.y = 0f;
        if (face.sqrMagnitude < 1e-4f) face = Vector3.forward;
        if (seated) face = -face;   // seated donors face local -Z
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face.normalized, Vector3.up));
        go.transform.localScale = src.transform.localScale * Mathf.Lerp(0.95f, 1.06f, seed);
        var high = go.transform.Find("LOD0 High Skinned");
        var rig = high != null ? high.Find("Rigged Character") : null;
        var actor = go.GetComponent<MinatoCrowdActor>();
        if (actor != null && rig != null)
        {
            var e = end ?? pos;
            actor.Configure(kind, rig, pos, e, kind == MinatoCrowdActor.MotionKind.Walk ? Mathf.Lerp(0.55f, 0.9f, seed) : 0f, seed);
            actor.armDropDegrees = seated ? 0f : 32f;
        }
        // civilian dress (look atlas, hair cap instead of helmet, contact shadow)
        string baseName = go.name;
        go.name = (kind == MinatoCrowdActor.MotionKind.Walk ? "Walker_" : seated ? "Customer_" : "Chat_") + baseName;
        try { MapleCityLife.DressForRegion(go, _portDonor.TryGetValue(src, out var dn) ? dn : src.name, "V", seated); }
        catch (System.Exception ex) { Debug.LogWarning($"[tunaport] dress failed for {baseName}: {ex.Message}"); }
        go.name = baseName;

        if (high != null)
        {
            var added = new List<Renderer>();
            try { Outfit(go, high, role, seed, seated, added); }
            catch (System.Exception ex) { Debug.LogWarning($"[tunaport] outfit failed for {baseName}: {ex.Message}"); }
            var lod = go.GetComponent<LODGroup>();
            if (lod != null && added.Count > 0)
            {
                var lods = lod.GetLODs();
                if (lods.Length > 0)
                {
                    var list = new List<Renderer>(lods[0].renderers);
                    list.AddRange(added);
                    lods[0].renderers = list.ToArray();
                    lod.SetLODs(lods);
                }
            }
        }
        _portPeople++;
        return go;
    }

    private static Mesh PersistMesh(Mesh m)
    {
        string path = $"{MeshDir}/{m.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
