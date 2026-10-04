using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ShuntaCyclistTrafficValidation
{
    static string Key=>"ChatGPT.ShuntaCyclists."+Application.dataPath.GetHashCode();
    static RideBootstrap boot; static int frames,lastFrame,shot; static double deadline; static bool failed;
    static RenderTexture target; static Camera camera;
    static ShuntaCyclistTrafficValidation(){EditorApplication.playModeStateChanged+=Mode;}
    static void Require(bool ok,string message)
    {if(!ok)throw new InvalidOperationException("[shunta-cyclists] "+message);Debug.Log("[shunta-cyclists] PASS "+message);}
    public static void Run()
    {
        ShuntaNpcLightingValidation.Run();
        var lib=Resources.Load<ShuntaNpcLibrary>(ShuntaNpcLibrary.ResourcePath);
        Require(lib.origins.Count(s=>s.StartsWith("Minato:"))>=12,"twelve distinct Minato identities");
        Require(lib.origins.Select(s=>System.Text.RegularExpressions.Regex.Replace(s,@"\b\d+\b","")).Distinct().Count()==lib.origins.Length,"no duplicate named pool donors");
        int reverse=0,forward=0;
        for(int i=0;i<ShuntaCyclistTraffic.PoolSize;i++)
        {
            if(ShuntaCyclistTraffic.DirectionFor(i)<0)reverse++;else forward++;
            float lane=ShuntaCyclistTraffic.LaneFor(i,9f);
            Require(Mathf.Abs(lane)>=2.5f && Mathf.Abs(lane)<3.7f,"rider "+i+" stays outside centre and inside road");
            Require(ShuntaCyclistTraffic.SpeedFor(i)>=5f && ShuntaCyclistTraffic.SpeedFor(i)<=14f,"varied plausible speed "+i);
        }
        Require(reverse==ShuntaCyclistTraffic.PoolSize/2 && forward==ShuntaCyclistTraffic.PoolSize/2,"balanced traffic directions");
        Require(ShuntaCyclistTraffic.Advance(5161f,1f,5000f,28400f,1)==4840f,"forward recycle beyond visibility");
        Require(ShuntaCyclistTraffic.Advance(4839f,-1f,5000f,28400f,-1)==5160f,"oncoming recycle beyond visibility");
        Require(ShuntaCyclistTraffic.Advance(5000f,8f,5000f,28400f,1)==5008f,"continuous forward motion");
        Require(ShuntaCyclistTraffic.Advance(5000f,-8f,5000f,28400f,-1)==4992f,"continuous oncoming motion");
        Require(ShuntaCyclistTraffic.Advance(-40f,1f,0f,28400f,1)==-39f,"start arrivals remain separated offstage");
        Require(ShuntaCyclistTraffic.Advance(28440f,-1f,28400f,28400f,-1)==28439f,"finish arrivals remain separated offstage");
        Debug.Log("[shunta-cyclists] ALL PASS");
    }
    public static void Capture()
    {
        Run();EditorSceneManager.OpenScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro));
        SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
    }
    static void Mode(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Key,false))return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {frames=shot=0;lastFrame=-1;boot=null;failed=false;deadline=EditorApplication.timeSinceStartup+480;EditorApplication.update+=Tick;}
        if(mode==PlayModeStateChange.EnteredEditMode)
        {SessionState.SetBool(Key,false);EditorApplication.update-=Tick;Debug.Log("[shunta-cyclists] CAPTURE "+(failed?"FAIL":"PASS"));if(Application.isBatchMode)EditorApplication.Exit(failed?1:0);}
    }
    static void Tick()
    {
        try
        {
            if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("cycling traffic capture");
            if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            if(boot==null)
            {
                boot=UnityEngine.Object.FindFirstObjectByType<RideBootstrap>();if(boot==null)return;
                boot.Resolve();Require(boot.regions.FastTravel(RegionCatalog.ShuntaMetro),"Shunta loads");
                boot.session.ExternalControl=true;boot.devices.HoldZeroPower=true;boot.devices.acceptKeyboardEffort=false;
                boot.session.SeekTo(2600f/28.4f/1000f*boot.session.Course.Length);boot.follower.Apply();
                var go=new GameObject("Shunta cyclist QA camera");camera=go.AddComponent<Camera>();
                camera.CopyFrom(boot.rideCamera);camera.fieldOfView=55f;
                target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
            }
            var traffic=ShuntaCyclistTraffic.Instance;
            var route=UnityEngine.Object.FindFirstObjectByType<ShuntaRouteBuilder>();
            if(traffic==null || route==null || traffic.VisibleCount==0)return;
            float km=2.6f;var f=route.TangentAtKm(km).normalized;var side=new Vector3(f.z,0,-f.x).normalized;
            float direction=shot==0?1f:-1f;var p=route.PositionAtKm(km);
            camera.transform.position=p-f*direction*10f+side*.6f+Vector3.up*1.6f;
            camera.transform.LookAt(p+f*direction*22f+Vector3.up*1f);
            if(++frames<180)return;
            Require(traffic.OncomingCount>=4 && traffic.ForwardCount>=4,"live mixed directions "+traffic.OncomingCount+" / "+traffic.ForwardCount);
            Require(traffic.GetComponentsInChildren<KuroBikeRig>(true).Length==ShuntaCyclistTraffic.PoolSize,"real riding rigs across pool");
            Require(traffic.GetComponentsInChildren<Collider>(true).All(c=>!c.enabled),"no traffic collision blockers");
            string folder=@"C:\Users\jason\OneDrive\Desktop\MapleRide\docs\shunta_captures";Directory.CreateDirectory(folder);
            var previous=RenderTexture.active;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,"cyclists_"+shot+".png"),image.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(image);}
            Debug.Log("[shunta-cyclists] live pool triangles="+traffic.PoolTriangles+" visible="+traffic.VisibleCount);
            shot++;frames=0;if(shot==2)Finish();
        }
        catch(Exception e){failed=true;Debug.LogError(e);Finish();}
    }
    static void Finish()
    {
        EditorApplication.update-=Tick;
        if(camera!=null)UnityEngine.Object.Destroy(camera.gameObject);
        if(target!=null){target.Release();UnityEngine.Object.Destroy(target);target=null;}
        EditorApplication.ExitPlaymode();
    }
}
