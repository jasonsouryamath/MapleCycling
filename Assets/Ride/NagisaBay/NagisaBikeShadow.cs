using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lightweight ground silhouette for Nagisa cyclists.  A translucent ellipse hides the bike's
/// identity at road level; this mesh keeps the two wheel rings, frame triangle, saddle and bars
/// legible without enabling another full-bike shadow caster.
/// </summary>
[ExecuteAlways]
public sealed class NagisaBikeShadow : MonoBehaviour
{
    private const string ChildName = "Bike Silhouette Shadow";
    private const int WheelSegments = 12;
    private static Material _material;

    [SerializeField] private float wheelRadius = 0.31f;
    [SerializeField] private float wheelSpacing = 1.12f;

    public static void Attach(GameObject riderRoot)
    {
        if (riderRoot == null) return;
        var shadow = riderRoot.GetComponent<NagisaBikeShadow>();
        if (shadow == null) shadow = riderRoot.AddComponent<NagisaBikeShadow>();
        shadow.Rebuild();
    }

    private void OnEnable() => Rebuild();

    public void Rebuild()
    {
        var old = transform.Find(ChildName);
        if (old != null) DestroySafe(old.gameObject);

        var mesh = new Mesh { name = "Nagisa_BikeShadow_Silhouette" };
        var vertices = new List<Vector3>(96);
        var triangles = new List<int>(180);
        AddRing(vertices, triangles, new Vector3(0f, 0f, -wheelSpacing * 0.5f));
        AddRing(vertices, triangles, new Vector3(0f, 0f, wheelSpacing * 0.5f));

        // The frame is deliberately a thin, open triangle so the road still shows through it.
        AddRod(vertices, triangles, new Vector3(0f, 0f, -wheelSpacing * 0.5f), new Vector3(0f, 0f, 0.08f), 0.055f);
        AddRod(vertices, triangles, new Vector3(0f, 0f, wheelSpacing * 0.5f), new Vector3(0f, 0f, 0.08f), 0.055f);
        AddRod(vertices, triangles, new Vector3(0f, 0f, 0.08f), new Vector3(0.22f, 0f, -0.08f), 0.05f);
        AddRod(vertices, triangles, new Vector3(0.22f, 0f, -0.08f), new Vector3(0f, 0f, -wheelSpacing * 0.5f), 0.05f);
        AddRod(vertices, triangles, new Vector3(0f, 0f, 0.08f), new Vector3(-0.20f, 0f, 0.28f), 0.045f);
        AddRod(vertices, triangles, new Vector3(-0.20f, 0f, 0.28f), new Vector3(0.02f, 0f, 0.42f), 0.042f);
        AddRod(vertices, triangles, new Vector3(0.02f, 0f, -0.02f), new Vector3(0.12f, 0f, -0.15f), 0.06f); // saddle

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        var go = new GameObject(ChildName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.014f, 0f);
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = SharedMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    private void AddRing(List<Vector3> v, List<int> t, Vector3 center)
    {
        for (int i = 0; i < WheelSegments; i++)
        {
            float a0 = i * Mathf.PI * 2f / WheelSegments;
            float a1 = (i + 1) * Mathf.PI * 2f / WheelSegments;
            float inner = wheelRadius * 0.63f;
            int n = v.Count;
            v.Add(center + new Vector3(Mathf.Cos(a0) * wheelRadius, 0f, Mathf.Sin(a0) * wheelRadius));
            v.Add(center + new Vector3(Mathf.Cos(a1) * wheelRadius, 0f, Mathf.Sin(a1) * wheelRadius));
            v.Add(center + new Vector3(Mathf.Cos(a1) * inner, 0f, Mathf.Sin(a1) * inner));
            v.Add(center + new Vector3(Mathf.Cos(a0) * inner, 0f, Mathf.Sin(a0) * inner));
            AddQuad(t, n);
        }
    }

    private static void AddRod(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, float width)
    {
        Vector3 d = b - a;
        Vector3 side = new Vector3(-d.z, 0f, d.x).normalized * width;
        int n = v.Count;
        v.Add(a + side); v.Add(b + side); v.Add(b - side); v.Add(a - side);
        AddQuad(t, n);
    }

    private static void AddQuad(List<int> t, int n)
    {
        t.Add(n); t.Add(n + 1); t.Add(n + 2);
        t.Add(n); t.Add(n + 2); t.Add(n + 3);
    }

    private static Material SharedMaterial()
    {
        if (_material != null) return _material;
        var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
        _material = new Material(shader) { name = "Nagisa Bike Silhouette Shadow" };
        if (_material.HasProperty("_UnlitColor")) _material.SetColor("_UnlitColor", new Color(0.015f, 0.025f, 0.04f, 0.46f));
        if (_material.HasProperty("_Color")) _material.SetColor("_Color", new Color(0.015f, 0.025f, 0.04f, 0.46f));
        if (_material.HasProperty("_SurfaceType")) _material.SetFloat("_SurfaceType", 1f);
        if (_material.HasProperty("_BlendMode")) _material.SetFloat("_BlendMode", 0f);
        if (_material.HasProperty("_TransparentZWrite")) _material.SetFloat("_TransparentZWrite", 0f);
        _material.renderQueue = 3000;
        return _material;
    }

    private static void DestroySafe(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
    }
}
