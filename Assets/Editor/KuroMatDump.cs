using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Dump every texture property + value/color property on the seated player's body CelLit
/// material, so we can find a secondary map (mask/AO/smoothness/rim/emission) that carries the
/// rectangular 'box' (albedo is uniform there). Also exports each bound texture's region around
/// the box UV to PNG for inspection. Read-only.</summary>
public static class KuroMatDump
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        SkinnedMeshRenderer body = null;
        foreach (var s in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (s.sharedMesh != null && s.sharedMesh.name == "Mesh_0") { body = s; break; }
        var m = body.sharedMaterial;
        var sh = m.shader;
        var sb = new StringBuilder();
        sb.AppendLine($"[matdump] material={m.name} shader={sh.name} keywords={string.Join(",", m.shaderKeywords)}");
        int n = sh.GetPropertyCount();
        for (int i=0;i<n;i++)
        {
            var pname = sh.GetPropertyName(i);
            var ptype = sh.GetPropertyType(i);
            if (ptype == UnityEngine.Rendering.ShaderPropertyType.Texture)
            {
                var t = m.GetTexture(pname);
                sb.AppendLine($"[matdump] TEX {pname} = {(t? t.name : "null")} {(t? $"{t.width}x{t.height}":"")}");
                if (t is Texture2D t2) DumpRegion(t2, pname);
            }
            else if (ptype == UnityEngine.Rendering.ShaderPropertyType.Color)
                sb.AppendLine($"[matdump] COL {pname} = {m.GetColor(pname)}");
            else if (ptype == UnityEngine.Rendering.ShaderPropertyType.Float || ptype == UnityEngine.Rendering.ShaderPropertyType.Range)
                sb.AppendLine($"[matdump] FLT {pname} = {m.GetFloat(pname)}");
            else if (ptype == UnityEngine.Rendering.ShaderPropertyType.Vector)
                sb.AppendLine($"[matdump] VEC {pname} = {m.GetVector(pname)}");
        }
        Debug.Log(sb.ToString());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
    static void DumpRegion(Texture2D t, string prop)
    {
        var rt = RenderTexture.GetTemporary(t.width, t.height);
        Graphics.Blit(t, rt); var pr = RenderTexture.active; RenderTexture.active = rt;
        var rd = new Texture2D(t.width, t.height, TextureFormat.RGB24, false);
        rd.ReadPixels(new Rect(0,0,t.width,t.height),0,0); rd.Apply();
        RenderTexture.active = pr; RenderTexture.ReleaseTemporary(rt);
        // crop around box UV A (U0.02-0.09 V0.26-0.34)
        int x0=(int)(0.015f*t.width), x1=(int)(0.090f*t.width);
        int y0=(int)((1-0.34f)*t.height), y1=(int)((1-0.26f)*t.height);
        int cw=x1-x0, ch=y1-y0;
        var crop = new Texture2D(cw, ch, TextureFormat.RGB24, false);
        crop.SetPixels(rd.GetPixels(x0,y0,cw,ch)); crop.Apply();
        string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../reference/good_graphics/seated_verify/mat"));
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"map_{prop.Replace('_','x')}.png"), crop.EncodeToPNG());
        Object.DestroyImmediate(rd); Object.DestroyImmediate(crop);
    }
}
