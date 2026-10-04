using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Thin inverted-hull silhouette pass for STATIC (MeshRenderer) geometry only.
///
/// Skinned renderers are refused - see <see cref="IsExcluded"/>. Every character in this project
/// is skinned, so in practice nothing on a rider is hulled any more and no staging pass attaches
/// this component to one. It is kept for static props that genuinely want a cheap outline.
/// </summary>
[ExecuteAlways]
public sealed class KuroOutline : MonoBehaviour
{
    [Range(0.005f, 0.08f)] public float thickness = 0.014f;
    public Color color = new Color(0.003f, 0.004f, 0.008f, 1f);

    [Tooltip("Renderers under a transform with any of these names are left alone. The inverted " +
             "hull is a character silhouette technique and is actively destructive elsewhere: " +
             "scaling a bicycle's thin tubes and spokes about their own origin makes the black " +
             "shell swallow the part it is meant to outline, and shelling the face decal would " +
             "paint a permanent black mouth on Coral even when she is not smiling.")]
    public string[] skipUnderNamed = { "Bike", "SmileDecal", "Kuro Cycling Goggles" };

    const string Marker = "__KuroOutline";
    readonly List<GameObject> created = new List<GameObject>();

    void OnEnable() => Rebuild();
    void Start() => Rebuild();

    /// <summary>
    /// True if <paramref name="t"/> sits under a transform named in <see cref="skipUnderNamed"/>.
    ///
    /// This guard exists because the player and the NPCs happened to differ by construction
    /// order: Kuro's outline was added before his bicycle was parented under him, so it shelled
    /// only his body (1 hull), whereas Coral's was added afterwards and shelled all 78 bicycle
    /// parts as well (80 hulls). Her red Colnago rendered as a solid black shape that vanished
    /// against the asphalt. Relying on ordering like that is a trap; skip explicitly by name.
    /// </summary>
    bool IsExcluded(Transform t)
    {
        // A skinned renderer's vertices are placed entirely by its bones, so the 1 + thickness
        // localScale this pass relies on is simply ignored: the "hull" renders exactly
        // coincident with the body and z-fights it. Which surface wins the tie is decided by
        // per-frame draw-order sorting, so the same setup can look harmless on one character
        // and shred another into black facets with no face - which is exactly what it did to
        // the player. There is no silhouette to gain here, only a duplicated 109k-triangle
        // mesh, so refuse skinned meshes outright rather than guarding them object by object.
        if (t.GetComponent<SkinnedMeshRenderer>() != null) return true;
        if (skipUnderNamed == null || skipUnderNamed.Length == 0) return false;
        for (var c = t; c != null && c != transform.parent; c = c.parent)
            foreach (var name in skipUnderNamed)
                if (!string.IsNullOrEmpty(name) && c.name == name) return true;
        return false;
    }

    void Rebuild()
    {
        if (transform.Find(Marker) != null) return;
        var root = new GameObject(Marker);
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        foreach (var src in GetComponentsInChildren<Renderer>(true))
        {
            if (src == null || src.transform.IsChildOf(root.transform)) continue;
            if (IsExcluded(src.transform)) continue;
            var go = new GameObject(src.gameObject.name + "_Outline");
            go.transform.SetParent(root.transform, false);
            go.transform.position = src.transform.position;
            go.transform.rotation = src.transform.rotation;
            go.transform.localScale = src.transform.lossyScale / transform.lossyScale.x * (1f + thickness);
            var mat = new Material(Shader.Find("Standard"));
            mat.name = "KuroOutlineBlack";
            mat.color = color;
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Glossiness", 0f);
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Front);
            var mf = src.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { DestroyImmediate(go); DestroyImmediate(mat); continue; }
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            created.Add(go);
        }
    }
}
