using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Raycast from the waist camera through a grid of pixels across the black-box screen
/// rect into the BAKED seated Mesh_0, returning the nearest-hit triangle's UV for each ray. This
/// gives the EXACT PlayerBack atlas footprint of the box (occlusion-correct). Read-only.</summary>
public static class KuroBoxRay
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";

    public static void Run()
    {
        // ensure PlayerBack mipmaps are restored (a prior test may have disabled them)
        var pbTi = AssetImporter.GetAtPath("Assets/Kuro/NPC/Textures/KuroAnime_Image_0_PlayerBack.png") as TextureImporter;
        if (pbTi != null && !pbTi.mipmapEnabled) { pbTi.mipmapEnabled = true; EditorUtility.SetDirty(pbTi); pbTi.SaveAndReimport(); Debug.Log("[boxray] restored PlayerBack mipmaps"); }
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }
        SkinnedMeshRenderer body = null;
        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (s.sharedMesh != null && s.sharedMesh.name == "Mesh_0") { body = s; break; }
        var baked = new Mesh(); body.BakeMesh(baked, true);
        Matrix4x4 l2w = body.transform.localToWorldMatrix;
        Vector3[] bv = baked.vertices; int[] tris = baked.triangles;
        Vector2[] uvs = body.sharedMesh.uv;
        var wv = new Vector3[bv.Length];
        for (int i=0;i<bv.Length;i++) wv[i]=l2w.MultiplyPoint3x4(bv[i]);

        // read the runtime body atlas into a CPU-readable copy
        Texture2D atlas = null;
        foreach (var pn in new[]{"_BaseColorMap","_BaseMap","_MainTex"})
            if (body.sharedMaterial.HasProperty(pn) && body.sharedMaterial.GetTexture(pn) is Texture2D tx) { atlas = tx; break; }
        Texture2D rd = null;
        if (atlas != null) {
            var art = RenderTexture.GetTemporary(atlas.width, atlas.height);
            Graphics.Blit(atlas, art); var pr = RenderTexture.active; RenderTexture.active = art;
            rd = new Texture2D(atlas.width, atlas.height, TextureFormat.RGB24, false);
            rd.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0); rd.Apply();
            RenderTexture.active = pr; RenderTexture.ReleaseTemporary(art);
        }

        Vector3 f = player.transform.forward, hips = player.transform.position;
        Vector3 camPos = hips - f * 1.3f + Vector3.up * 0.72f;
        Vector3 look = hips + Vector3.up * 0.60f;
        var go = new GameObject("~RayCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = camPos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f; cam.aspect = 1f;

        var hitUV = new List<Vector2>();
        // box screen rect in viewport coords (from waist render: img x[0.428,0.572] y[0.575,0.619] -> vp y=1-imgY)
        for (float vx = 0.44f; vx <= 0.56f; vx += 0.01f)
        for (float vy = 0.385f; vy <= 0.425f; vy += 0.006f)
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0));
            float best = float.MaxValue; Vector2 bestUV = new Vector2(-1,-1);
            for (int t=0;t<tris.Length;t+=3)
            {
                int a=tris[t],b=tris[t+1],c=tris[t+2];
                if (RayTri(ray.origin, ray.direction, wv[a], wv[b], wv[c], out float dist, out float u, out float v) && dist < best)
                {
                    best = dist;
                    float w = 1f-u-v;
                    bestUV = uvs[a]*w + uvs[b]*u + uvs[c]*v;
                }
            }
            if (bestUV.x >= 0) hitUV.Add(bestUV);
        }
        var sb = new StringBuilder();
        sb.AppendLine($"[boxray] rays hit={hitUV.Count}");
        if (hitUV.Count>0)
        {
            float uMin=1,uMax=0,vMin=1,vMax=0;
            foreach (var uv in hitUV){ uMin=Mathf.Min(uMin,uv.x);uMax=Mathf.Max(uMax,uv.x);vMin=Mathf.Min(vMin,uv.y);vMax=Mathf.Max(vMax,uv.y); }
            sb.AppendLine($"[boxray] BOX atlas footprint  U[{uMin:F4},{uMax:F4}] V[{vMin:F4},{vMax:F4}]");
            // sample atlas at each hit UV to learn the box's actual albedo & find the orange/dark
            if (rd != null) {
                var s3 = new StringBuilder("[boxray] hit UV -> atlas RGB: ");
                foreach (var uv in hitUV) {
                    Color px = rd.GetPixelBilinear(uv.x, uv.y);
                    s3.Append($"[{uv.x:F3},{uv.y:F3}]=({(int)(px.r*255)},{(int)(px.g*255)},{(int)(px.b*255)}) ");
                }
                sb.AppendLine(s3.ToString());
            }
        }
        Debug.Log(sb.ToString());
        if (rd != null) Object.DestroyImmediate(rd);

        // ---- normals check on the box hit faces: shading (attribute) normal vs geometric winding ----
        Vector3[] bn = baked.normals;
        int flipped = 0, ntot = 0; float dotSum = 0;
        var nsb = new StringBuilder("[boxray] box-face normal check (shadingN . geomN): ");
        for (float vx = 0.47f; vx <= 0.53f; vx += 0.02f)
        for (float vy = 0.395f; vy <= 0.415f; vy += 0.01f)
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0));
            float best = float.MaxValue; int bi = -1;
            for (int t=0;t<tris.Length;t+=3)
            {
                int a=tris[t],b=tris[t+1],c=tris[t+2];
                if (RayTri(ray.origin, ray.direction, wv[a], wv[b], wv[c], out float dist, out float u, out float v) && dist < best)
                { best = dist; bi = t; }
            }
            if (bi>=0)
            {
                int a=tris[bi],b=tris[bi+1],c=tris[bi+2];
                Vector3 geom = Vector3.Cross(wv[b]-wv[a], wv[c]-wv[a]).normalized;
                Vector3 shad = (l2w.MultiplyVector((bn[a]+bn[b]+bn[c])/3f)).normalized;
                float d = Vector3.Dot(geom, shad);
                dotSum += d; ntot++; if (d < 0) flipped++;
                nsb.Append($"{d:F2} ");
            }
        }
        nsb.Append($"\n[boxray] faces sampled={ntot} flipped(shadingN opposes winding)={flipped} avgDot={(ntot>0?dotSum/ntot:0):F2}");
        Debug.Log(nsb.ToString());
        Object.DestroyImmediate(go); Object.DestroyImmediate(baked);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static bool RayTri(Vector3 o, Vector3 d, Vector3 v0, Vector3 v1, Vector3 v2, out float dist, out float u, out float v)
    {
        dist=0;u=0;v=0;
        Vector3 e1=v1-v0, e2=v2-v0, p=Vector3.Cross(d,e2);
        float det=Vector3.Dot(e1,p);
        if (det>-1e-8f && det<1e-8f) return false;
        float inv=1f/det; Vector3 tv=o-v0;
        u=Vector3.Dot(tv,p)*inv; if (u<0||u>1) return false;
        Vector3 q=Vector3.Cross(tv,e1);
        v=Vector3.Dot(d,q)*inv; if (v<0||u+v>1) return false;
        dist=Vector3.Dot(e2,q)*inv; return dist>1e-4f;
    }
}
