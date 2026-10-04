using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Test making the saddle read as a matte saddle instead of a rim-lit black box.
/// Renders the waist crop for: current tyre material, seatpost carbon material, frame-black
/// material, and a nudged (tucked) position. Read-only.</summary>
public static class KuroSaddleFixTest
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string PlayerName = "Kuro on Sakura Pass";
    public static void Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = GameObject.Find(PlayerName);
        var rig = player.GetComponent<KuroBikeRig>();
        if (rig != null) { rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver); rig.ForceSolveOnce(); }
        Vector3 f = player.transform.forward, hips = player.transform.position;
        Vector3 pos = hips - f * 1.3f + Vector3.up * 0.72f, look = hips + Vector3.up * 0.60f;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/seated_verify"));

        Renderer sRear=null, sNose=null; Transform tRear=null, tNose=null;
        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            if (r.gameObject.name=="SaddleRear"){ sRear=r; tRear=r.transform; }
            if (r.gameObject.name=="SaddleNose"){ sNose=r; tNose=r.transform; }
        }
        // find candidate matte materials in the scene / project
        Material carbon = FindMat("Shared_Colnago_Carbon_Black_CelLit");
        Material frame  = FindMat("KuroBike_Frame_Black");
        var sb = new StringBuilder();
        var tyre = sRear.sharedMaterial;
        sb.AppendLine($"[sfix] tyre mat props: gloss={GetF(tyre,"_Gloss")} spec={GetF(tyre,"_SpecStrength")} rim={GetF(tyre,"_RimStrength")} edgeRim={GetF(tyre,"_EdgeRimStrength")}");
        if (carbon!=null) sb.AppendLine($"[sfix] carbon props: gloss={GetF(carbon,"_Gloss")} rim={GetF(carbon,"_RimStrength")} edgeRim={GetF(carbon,"_EdgeRimStrength")}");
        if (frame!=null)  sb.AppendLine($"[sfix] frame props:  gloss={GetF(frame,"_Gloss")} rim={GetF(frame,"_RimStrength")} edgeRim={GetF(frame,"_EdgeRimStrength")}");
        Debug.Log(sb.ToString());

        Shot(dir,"sfix_tyre",pos,look);
        if (carbon!=null){ sRear.sharedMaterial=carbon; sNose.sharedMaterial=carbon; Shot(dir,"sfix_carbon",pos,look); }
        if (frame!=null){ sRear.sharedMaterial=frame; sNose.sharedMaterial=frame; Shot(dir,"sfix_frame",pos,look); }
        // restore tyre + test a tuck (move saddle forward+down so rider occludes it)
        sRear.sharedMaterial=tyre; sNose.sharedMaterial=tyre;
        Vector3 rp=tRear.localPosition, np=tNose.localPosition;
        Vector3 dz = tRear.parent.InverseTransformVector(f)*0.05f;   // 5cm forward in parent space
        tRear.localPosition = rp + dz + new Vector3(0,-0.02f,0);
        tNose.localPosition = np + dz + new Vector3(0,-0.02f,0);
        Shot(dir,"sfix_tuck",pos,look);
        tRear.localPosition=rp; tNose.localPosition=np;

        Debug.Log("[sfix] wrote sfix_tyre/carbon/frame/tuck");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
    static Material FindMat(string name){
        var guids=AssetDatabase.FindAssets("t:Material "+name);
        foreach(var g in guids){ var p=AssetDatabase.GUIDToAssetPath(g); var m=AssetDatabase.LoadAssetAtPath<Material>(p); if(m&&m.name==name) return m; }
        return null;
    }
    static string GetF(Material m,string p)=> m!=null&&m.HasProperty(p)?m.GetFloat(p).ToString("F2"):"-";
    static void Shot(string dir, string name, Vector3 pos, Vector3 look)
    {
        var go = new GameObject("~SFCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f,0.55f,0.6f);
        const int w=1100,h=1100;
        var rt = new RenderTexture(w,h,24,RenderTextureFormat.ARGB32){antiAliasing=4};
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var img = new Texture2D(w,h,TextureFormat.RGB24,false);
        img.ReadPixels(new Rect(0,0,w,h),0,0); img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev; cam.targetTexture=null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
    }
}
