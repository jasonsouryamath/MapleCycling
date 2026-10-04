using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// WP-F winter townsfolk (copilot session 1). Maple City's civilian crowd, re-dressed for a Swiss
// winter: the SAME crowd donors and the SAME MapleCityLife.Dress pipeline, with the "W" look
// atlases (tools/blender/build_maple_city_life_textures.py --winter-only: long-sleeve puffers and
// wool coats, gloves, winter boots, knitted collar scarves, beanies on ~65%), plus a modelled
// knitted scarf ring with a hanging tail so the silhouette reads "cold" at distance.
//
// NON-INVASIVE BY DESIGN. WP-C's village code decides WHERE people are and spawns raw crowd donor
// clones (still in cycling kit). This pass runs after every builder and re-dresses EVERY crowd
// actor under the Azora root that has not been civilian-dressed yet (no MapleCityLook), whatever
// package spawned it. So WP-C keeps its Person() spawns unchanged (WinterTownsfolkEnabled stays
// false = "keep spawning"), and no figure is ever duplicated or left in lycra.
public static partial class AzoraHighlandsEnvironment
{
    /// <summary>false = village packages keep spawning their own figures; WP-F re-dresses them.</summary>
    private const bool WinterTownsfolkEnabled = false;

    private static readonly Color[] ScarfColors =
    {
        new Color(0.76f, 0.23f, 0.20f), new Color(0.90f, 0.85f, 0.72f), new Color(0.18f, 0.30f, 0.48f),
        new Color(0.85f, 0.64f, 0.25f), new Color(0.24f, 0.42f, 0.29f), new Color(0.55f, 0.18f, 0.35f),
        new Color(0.94f, 0.93f, 0.91f), new Color(0.62f, 0.29f, 0.16f),
    };

    private static readonly Dictionary<string, Mesh> ScarfMeshes = new Dictionary<string, Mesh>();

    private static void BuildWinterTownsfolk(Transform root, AzoraRoute route)
    {
        ScarfMeshes.Clear();

        // Donor identity by skin mesh: a clone keeps its donor's Mesh_0 sharedMesh.
        var donorByMesh = new Dictionary<Mesh, string>();
        foreach (var a in Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (a == null || a.transform.IsChildOf(root) || !a.name.StartsWith("Crowd_", System.StringComparison.Ordinal)) continue;
            var smr = SkinOf(a.transform);
            if (smr != null && smr.sharedMesh != null && !donorByMesh.ContainsKey(smr.sharedMesh))
                donorByMesh[smr.sharedMesh] = a.name;
        }

        int dressed = 0, scarves = 0, unknown = 0;
        foreach (var a in root.GetComponentsInChildren<MinatoCrowdActor>(true))
        {
            if (a.GetComponent<MapleCityLook>() != null) continue;
            var smr = SkinOf(a.transform);
            if (smr == null || smr.sharedMesh == null || !donorByMesh.TryGetValue(smr.sharedMesh, out var donor)) { unknown++; continue; }
            bool sit = a.motion == MinatoCrowdActor.MotionKind.Sit;
            var go = a.gameObject;
            string baseName = go.name;
            // MapleCityLife.Dress reads the role from the name prefix.
            go.name = (sit ? "Customer_" : a.motion == MinatoCrowdActor.MotionKind.Walk ? "Walker_" : "Chat_") + baseName;
            MapleCityLife.DressForRegion(go, donor, "W", sit);
            go.name = baseName;
            dressed++;
            float h = H01(go.transform.position.x * 0.37f, go.transform.position.z * 0.53f);
            var high = go.transform.Find("LOD0 High Skinned");
            if (high != null && h < 0.72f &&
                Scarf(go, high, donor, ScarfColors[(int)(H01(h * 91f, 2.2f) * ScarfColors.Length) % ScarfColors.Length], sit))
                scarves++;
        }
        Debug.Log($"[azora] winter townsfolk: {dressed} villagers re-dressed in W looks, {scarves} scarves" +
                  (unknown > 0 ? $", {unknown} with no known donor left as-is." : "."));
    }

    private static SkinnedMeshRenderer SkinOf(Transform t)
    {
        var high = t.Find("LOD0 High Skinned");
        if (high == null) return null;
        foreach (var s in high.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (s.name == "Mesh_0") return s;
        return null;
    }

    /// <summary>A knitted scarf ring around the neck bone with a tail hanging at the chest.</summary>
    private static bool Scarf(GameObject go, Transform high, string donor, Color colour, bool sit)
    {
        var smr = SkinOf(go.transform);
        if (smr == null) return false;
        Transform neck = null, head = null;
        foreach (var b in smr.bones)
        {
            if (b == null) continue;
            if (b.name == "neck" || b.name == "Neck") neck = b;
            else if (b.name == "Head") head = b;
        }
        if (neck == null || head == null) return false;

        if (!ScarfMeshes.TryGetValue(donor, out var mesh))
        {
            mesh = BuildScarfMesh(donor, smr, neck, head, sit ? -go.transform.forward : go.transform.forward);
            ScarfMeshes[donor] = mesh;
        }
        if (mesh == null) return false;

        string hex = ColorUtility.ToHtmlStringRGB(colour);
        var mat = CelMaterial($"AzoraF_Scarf_{hex}", colour, gloss: 0.04f, spec: 0.02f, rim: 0.18f);
        mat.SetFloat("_Cull", 0f);   // procedural torus: double-sided so winding can never hide it
        var sc = new GameObject("Winter Scarf", typeof(MeshFilter), typeof(MeshRenderer));
        sc.transform.SetParent(neck, false);
        sc.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = sc.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        // Cull with the figure's LOD0.
        var lod = go.GetComponent<LODGroup>();
        if (lod != null)
        {
            var lods = lod.GetLODs();
            if (lods.Length > 0)
            {
                var list = new List<Renderer>(lods[0].renderers) { mr };
                lods[0].renderers = list.ToArray();
                lod.SetLODs(lods);
            }
        }
        return true;
    }

    /// <summary>
    /// Measured per donor at build time in the NECK BONE's local space (so it follows the head
    /// and scales with the clone): ring radius from the baked skin's vertices around the neck,
    /// lifted a third of the way to the head, plus a tail strip over the chest.
    /// </summary>
    private static Mesh BuildScarfMesh(string donor, SkinnedMeshRenderer smr, Transform neck, Transform head, Vector3 front)
    {
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        Vector3 n0 = neck.position, h0 = head.position;
        Vector3 axis = (h0 - n0).normalized;
        Vector3 centre = n0 + (h0 - n0) * 0.30f;
        float band = Vector3.Distance(n0, h0) * 0.30f;
        var radii = new List<float>();
        foreach (var v0 in baked.vertices)
        {
            var v = m.MultiplyPoint3x4(v0);
            float along = Vector3.Dot(v - centre, axis);
            if (Mathf.Abs(along) > band) continue;
            var radial = (v - centre) - axis * along;
            if (radial.magnitude < Vector3.Distance(n0, h0) * 2.5f) radii.Add(radial.magnitude);
        }
        Object.DestroyImmediate(baked);
        if (radii.Count < 8) { Debug.LogWarning($"[azora] scarf {donor}: neck not measurable"); return null; }
        radii.Sort();
        float ring = Mathf.Max(radii[(int)(radii.Count * 0.6f)] * 1.12f, 0.015f);
        float tube = ring * 0.34f;

        front = (front - axis * Vector3.Dot(front, axis)).normalized;
        var side = Vector3.Cross(axis, front).normalized;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        const int Seg = 20, Tube = 8;
        for (int i = 0; i <= Seg; i++)
        {
            float a = i / (float)Seg * Mathf.PI * 2f;
            var dir = front * Mathf.Cos(a) + side * Mathf.Sin(a);
            // looser and lower at the front, snug at the back
            float drop = Mathf.Max(0f, Mathf.Cos(a)) * tube * 0.9f;
            var c = centre + dir * ring - axis * drop;
            for (int k = 0; k <= Tube; k++)
            {
                float b = k / (float)Tube * Mathf.PI * 2f;
                verts.Add(c + (dir * Mathf.Cos(b) + axis * Mathf.Sin(b) * 0.85f) * tube);
                uvs.Add(new Vector2(i / (float)Seg * 6f, k / (float)Tube));
            }
        }
        for (int i = 0; i < Seg; i++)
        for (int k = 0; k < Tube; k++)
        {
            int a = i * (Tube + 1) + k, b = a + Tube + 1;
            tris.AddRange(new[] { a, b, a + 1, b, b + 1, a + 1 });
        }
        // tail: a thick flat strip from the front of the ring, down the chest, a little to one side
        var top = centre + front * (ring + tube * 0.4f) + side * ring * 0.35f - axis * tube;
        float len = ring * 3.0f, w = tube * 1.6f, th = tube * 0.45f;
        int t0 = verts.Count;
        var corners = new[]
        {
            top - side * w * 0.5f, top + side * w * 0.5f,
            top - side * w * 0.5f - axis * len + front * th, top + side * w * 0.5f - axis * len + front * th,
        };
        foreach (var o in new[] { Vector3.zero, front * th })
            foreach (var cpt in corners) { verts.Add(cpt + o); uvs.Add(Vector2.zero); }
        void Q(int a, int b, int c, int d) { tris.AddRange(new[] { t0 + a, t0 + b, t0 + c, t0 + c, t0 + b, t0 + d }); }
        void Q2(int a, int b, int c, int d) { Q(a, b, c, d); }   // material is _Cull 0 (double-sided)
        Q2(4, 6, 5, 7); Q2(0, 1, 2, 3); Q2(0, 2, 4, 6); Q2(1, 3, 5, 7); Q2(2, 3, 6, 7);

        var inv = neck.worldToLocalMatrix;
        for (int i = 0; i < verts.Count; i++) verts[i] = inv.MultiplyPoint3x4(verts[i]);

        string path = $"{MeshDir}/AzoraF_Scarf_{donor}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool fresh = mesh == null;
        if (fresh) mesh = new Mesh();
        mesh.Clear();
        mesh.name = $"AzoraF_Scarf_{donor}";
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (fresh) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }
}
