using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Test saddle-tuck variants from REAR (box gone?) and SIDE (still attached to post?).
/// Read-only.</summary>
public static class KuroSaddleTuckTest
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
        Vector3 f = player.transform.forward, right = player.transform.right, hips = player.transform.position;
        Vector3 rpos = hips - f * 1.3f + Vector3.up * 0.72f, rlook = hips + Vector3.up * 0.60f;
        Vector3 spos = hips + right * 1.6f + Vector3.up * 0.62f, slook = hips + Vector3.up * 0.56f;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../reference/good_graphics/seated_verify"));

        Transform tRear=null,tNose=null;
        foreach (var r in player.GetComponentsInChildren<Transform>(true))
        { if(r.name=="SaddleRear")tRear=r; if(r.name=="SaddleNose")tNose=r; }
        Vector3 rp=tRear.localPosition, np=tNose.localPosition;
        Vector3 fwdL = tRear.parent.InverseTransformVector(f);

        void SetOff(float fwd, float down){
            tRear.localPosition = rp + fwdL*fwd + new Vector3(0,-down,0);
            tNose.localPosition = np + fwdL*fwd + new Vector3(0,-down,0);
        }
        // variant A: forward 3 down 2
        SetOff(0.03f,0.02f); Shot(dir,"tuckA_rear",rpos,rlook); Shot(dir,"tuckA_side",spos,slook);
        // variant B: down 4 only
        SetOff(0.0f,0.04f);  Shot(dir,"tuckB_rear",rpos,rlook); Shot(dir,"tuckB_side",spos,slook);
        // variant C: forward 2 down 3
        SetOff(0.02f,0.03f); Shot(dir,"tuckC_rear",rpos,rlook); Shot(dir,"tuckC_side",spos,slook);
        tRear.localPosition=rp; tNose.localPosition=np;
        // baseline side for comparison
        Shot(dir,"tuck_base_side",spos,slook);
        Debug.Log("[tuck] wrote tuckA/B/C rear+side and base_side");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
    static void Shot(string dir, string name, Vector3 pos, Vector3 look)
    {
        var go = new GameObject("~TCam"); var cam = go.AddComponent<Camera>();
        cam.transform.position = pos; cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = 32f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f,0.55f,0.6f);
        const int w=1000,h=1000;
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
