using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Definitive box locator: seat the player, BAKE the skinned Mesh_0 (so we get the actual SEATED
/// geometry), replicate the KuroSeatedInspect waist camera, project every baked vertex to viewport,
/// and report the triangles whose projected centroid lands in the reported box screen rect. For
/// those faces dumps: UV (is it degenerate / pinned to the collapse texel ~0.854,0.778?), whether
/// the tri faces the camera, and seated world Z. Read-only.
/// </summary>
public static class KuroBoxProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    // box screen rect from the waist render (viewport coords, y=0 bottom)
    const float RX0 = 0.40f, RX1 = 0.59f, RY0 = 0.36f, RY1 = 0.44f;

    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        if (player == null) { Debug.LogError("[boxprobe] no player"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }

        SkinnedMeshRenderer body = null;
        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (s.sharedMesh != null && s.sharedMesh.name == "Mesh_0") { body = s; break; }
        if (body == null) { Debug.LogError("[boxprobe] no Mesh_0"); if (Application.isBatchMode) EditorApplication.Exit(1); return; }

        var baked = new Mesh();
        body.BakeMesh(baked, true);                 // seated geometry, local space
        Matrix4x4 l2w = body.transform.localToWorldMatrix;
        Vector3[] bv = baked.vertices;
        Vector2[] uvs = body.sharedMesh.uv;         // original UVs (baking keeps index mapping)
        int[] tris = baked.triangles;

        // waist camera identical to KuroSeatedInspect.Shot("seated_waist",...)
        Vector3 f = player.transform.forward;
        Vector3 hips = player.transform.position;
        Vector3 camPos = hips - f * 1.3f + Vector3.up * 0.72f;
        Vector3 look = hips + Vector3.up * 0.60f;
        var go = new GameObject("~BoxCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = camPos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f;
        cam.aspect = 1f;

        var hitTris = new List<int>();
        for (int t = 0; t < tris.Length; t += 3)
        {
            int a = tris[t], b = tris[t + 1], c = tris[t + 2];
            Vector3 wa = l2w.MultiplyPoint3x4(bv[a]);
            Vector3 wb = l2w.MultiplyPoint3x4(bv[b]);
            Vector3 wc = l2w.MultiplyPoint3x4(bv[c]);
            Vector3 wctr = (wa + wb + wc) / 3f;
            Vector3 vp = cam.WorldToViewportPoint(wctr);
            if (vp.z <= 0) continue;
            if (vp.x < RX0 || vp.x > RX1 || vp.y < RY0 || vp.y > RY1) continue;
            // camera-facing check (strict: this face is what the camera sees here)
            Vector3 n = Vector3.Cross(wb - wa, wc - wa).normalized;
            Vector3 toCam = (camPos - wctr).normalized;
            if (Vector3.Dot(n, toCam) <= 0.35f) continue;
            hitTris.Add(t / 3);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[boxprobe] box-rect front-facing tris={hitTris.Count}");
        int degen = 0; float uMin=1, uMax=0, vMin=1, vMax=0; float zMin=999, zMax=-999;
        int pinned = 0;
        foreach (int fi in hitTris)
        {
            int a = tris[fi*3], b = tris[fi*3+1], c = tris[fi*3+2];
            Vector2 ua = uvs[a], ub = uvs[b], uc = uvs[c];
            float area = Mathf.Abs((ub.x-ua.x)*(uc.y-ua.y) - (uc.x-ua.x)*(ub.y-ua.y)) * 0.5f;
            if (area < 1e-7f) degen++;
            Vector2 uc3 = (ua+ub+uc)/3f;
            if ((uc3 - new Vector2(0.854f,0.778f)).magnitude < 0.02f) pinned++;
            uMin=Mathf.Min(uMin,uc3.x); uMax=Mathf.Max(uMax,uc3.x); vMin=Mathf.Min(vMin,uc3.y); vMax=Mathf.Max(vMax,uc3.y);
            Vector3 wctr = l2w.MultiplyPoint3x4((bv[a]+bv[b]+bv[c])/3f);
            zMin=Mathf.Min(zMin,wctr.y); zMax=Mathf.Max(zMax,wctr.y);
        }
        sb.AppendLine($"[boxprobe] degenerate-UV tris={degen} pinned-to-collapse-texel={pinned}");
        sb.AppendLine($"[boxprobe] face-centroid UV bbox U[{uMin:F3},{uMax:F3}] V[{vMin:F3},{vMax:F3}]  worldY(height)[{zMin:F3},{zMax:F3}]");

        // Sample the body atlas at each hit-face UV centroid to test texture-dark vs shading-dark.
        Texture2D atlas = null;
        var mat = body.sharedMaterial;
        foreach (var pn in new[]{"_BaseColorMap","_BaseMap","_MainTex"})
            if (mat.HasProperty(pn) && mat.GetTexture(pn) is Texture2D tx) { atlas = tx; break; }
        if (atlas != null)
        {
            // make readable copy
            var rt = RenderTexture.GetTemporary(atlas.width, atlas.height);
            Graphics.Blit(atlas, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var rd = new Texture2D(atlas.width, atlas.height, TextureFormat.RGB24, false);
            rd.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0); rd.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            int nbright=0, ndark=0; float meanBr=0;
            foreach (int fi in hitTris)
            {
                int a=tris[fi*3],b=tris[fi*3+1],c=tris[fi*3+2];
                Vector2 u=(uvs[a]+uvs[b]+uvs[c])/3f;
                Color px = rd.GetPixelBilinear(u.x, u.y);
                float br = Mathf.Max(px.r, Mathf.Max(px.g, px.b));
                meanBr += br; if (br < 0.08f) ndark++; else nbright++;
            }
            sb.AppendLine($"[boxprobe] atlas={atlas.name} {atlas.width}x{atlas.height}  hitFace atlas meanBright={meanBr/Mathf.Max(hitTris.Count,1):F3} dark(<0.08)={ndark} lit={nbright}");
            Object.DestroyImmediate(rd);
        }
        else sb.AppendLine("[boxprobe] atlas texture not found on material");
        Debug.Log(sb.ToString());

        // ---- render a RED overlay of the hit tris over the waist view to confirm selection ----
        var omesh = new Mesh();
        var overts = new Vector3[hitTris.Count * 3];
        var otris = new int[hitTris.Count * 3];
        var uvsb = new StringBuilder("[boxprobe] hit-face UV centroids:\n");
        for (int i = 0; i < hitTris.Count; i++)
        {
            int fi = hitTris[i];
            int a = tris[fi*3], b = tris[fi*3+1], c = tris[fi*3+2];
            overts[i*3+0] = l2w.MultiplyPoint3x4(bv[a]);
            overts[i*3+1] = l2w.MultiplyPoint3x4(bv[b]);
            overts[i*3+2] = l2w.MultiplyPoint3x4(bv[c]);
            otris[i*3+0]=i*3+0; otris[i*3+1]=i*3+1; otris[i*3+2]=i*3+2;
            Vector2 u=(uvs[a]+uvs[b]+uvs[c])/3f;
            uvsb.Append($"({u.x:F3},{u.y:F3}) ");
        }
        Debug.Log(uvsb.ToString());
        omesh.vertices = overts; omesh.triangles = otris; omesh.RecalculateNormals();
        var ogo = new GameObject("~BoxOverlay");
        var mf = ogo.AddComponent<MeshFilter>(); mf.sharedMesh = omesh;
        var mr = ogo.AddComponent<MeshRenderer>();
        var redm = new Material(Shader.Find("Unlit/Color")); redm.color = Color.red;
        mr.sharedMaterial = redm;

        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../reference/good_graphics/seated_verify"));
        var cgo = new GameObject("~ConfCam"); var ccam = cgo.AddComponent<Camera>();
        ccam.transform.position = camPos; ccam.transform.LookAt(look, Vector3.up);
        ccam.fieldOfView = 30f; ccam.nearClipPlane = 0.02f; ccam.farClipPlane = 14000f;
        ccam.clearFlags = CameraClearFlags.SolidColor; ccam.backgroundColor = new Color(0.5f,0.55f,0.6f);
        const int W2=1100, H2=1100;
        var rt2 = new RenderTexture(W2,H2,24,RenderTextureFormat.ARGB32){antiAliasing=4};
        ccam.targetTexture = rt2; ccam.Render();
        var prev2 = RenderTexture.active; RenderTexture.active = rt2;
        var img2 = new Texture2D(W2,H2,TextureFormat.RGB24,false);
        img2.ReadPixels(new Rect(0,0,W2,H2),0,0); img2.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir,"box_overlay.png"), img2.EncodeToPNG());
        RenderTexture.active = prev2; ccam.targetTexture=null;
        Object.DestroyImmediate(rt2); Object.DestroyImmediate(cgo); Object.DestroyImmediate(ogo); Object.DestroyImmediate(omesh);
        Debug.Log("[boxprobe] wrote box_overlay.png");
        Object.DestroyImmediate(go); Object.DestroyImmediate(baked);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
