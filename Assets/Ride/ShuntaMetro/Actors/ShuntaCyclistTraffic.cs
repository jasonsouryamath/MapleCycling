using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Shunta-only local cycling life, independent of the competitive race field.</summary>
[DefaultExecutionOrder(50)]
public sealed class ShuntaCyclistTraffic : MonoBehaviour
{
    public static ShuntaCyclistTraffic Instance { get; private set; }
    public const int PoolSize=24;   // 2026-10-04: 16 -> 36 (user: Shunta should be alive); groups packed tighter so they all fit the 320 m window
    public const float WindowM=320f, VisibleM=130f;
    public int VisibleCount { get; private set; }
    public int OncomingCount { get; private set; }
    public int ForwardCount { get; private set; }
    public int PoolTriangles { get; private set; }
    sealed class Rider
    {
        public GameObject go; public KuroBikeRig rig; public Renderer[] renderers;
        public float distance, speed, lane; public int direction; public bool shown;
    }
    Rider[] riders;
    RideSession session; ShuntaRouteBuilder route;
    float nextResolve, lastPlayer=float.NaN; int runSerial=-1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if(Instance!=null || System.Environment.GetEnvironmentVariable("MR_SHUNTA_CYCLISTS")=="0")return;
        var go=new GameObject("Shunta cycling traffic");DontDestroyOnLoad(go);
        Instance=go.AddComponent<ShuntaCyclistTraffic>();
    }
    public static int DirectionFor(int index)=>index%2==0?-1:1;
    public static float SpeedFor(int index)=>DirectionFor(index)<0 ? 5.5f+((index/6)%6)*.6f : 5.8f+((index/6)%6)*1.3f;
    public static float LaneFor(int index,float width)
    {
        // Keep the centre open. Groups ride single file; both streams stay inside the ribbon.
        float edge=Mathf.Max(.5f,width*.5f-.85f);
        float lane=Mathf.Min(edge,2.7f+((index/6)%3)*.4f);
        return DirectionFor(index)<0?-lane:lane;
    }
    public static float InitialOffset(int index)
    {
        int slot=index/2;
        // Three-rider groups, mixed with solo encounters; 8 m wheel spacing.
        return -135f+(slot/3)*60f+(slot%3)*8f+(index%2)*20f;
    }
    public static float Advance(float distance,float delta,float centre,float length,int direction)
    {
        distance+=delta;
        // Virtual distances beyond the course stay hidden. Clamping this window at
        // the start would stack arriving riders at zero and recycle in full view.
        float low=centre-WindowM*.5f,high=centre+WindowM*.5f;
        if(distance<low || distance>high)distance=direction<0?high:low;
        return distance;
    }
    void Update()
    {
        if(Time.unscaledTime>=nextResolve)
        {
            nextResolve=Time.unscaledTime+1f;
            if(session==null)session=FindFirstObjectByType<RideSession>();
            if(route==null)route=FindFirstObjectByType<ShuntaRouteBuilder>();
        }
        bool active=session!=null && session.Course!=null && session.courseId==ShuntaRouteProvider.CourseId && route!=null && route.Course!=null;
        if(!active){Park();lastPlayer=float.NaN;return;}
        if(riders==null && !Build())return;
        float player=session.DistanceM/session.Course.Length*route.Course.distanceKm*1000f;
        bool reset=float.IsNaN(lastPlayer) || Mathf.Abs(player-lastPlayer)>100f || runSerial!=session.RunSerial;
        float length=route.Course.distanceKm*1000f;
        VisibleCount=OncomingCount=ForwardCount=0;
        for(int i=0;i<riders.Length;i++)
        {
            var r=riders[i];
            if(reset)r.distance=player+InitialOffset(i);
            else r.distance=Advance(r.distance,r.direction*r.speed*Mathf.Min(Time.deltaTime,.1f),player,length,r.direction);
            float gap=Mathf.Abs(r.distance-player);
            // Hide recycled riders at the edge; never teleport in the visible window.
            bool show=gap<VisibleM && r.distance>1f && r.distance<length-1f;
            if(show!=r.shown){r.go.SetActive(show);r.shown=show;}
            if(!show)continue;
            float km=r.distance*.001f;
            Vector3 forward=route.TangentAtKm(km).normalized;
            Vector3 side=new Vector3(forward.z,0f,-forward.x).normalized;
            r.go.transform.SetPositionAndRotation(route.PositionAtKm(km)+side*r.lane+Vector3.up*.05f,
                Quaternion.LookRotation(forward*r.direction,Vector3.up));
            if(r.rig!=null)
            {
                r.rig.previewCadenceRpm=r.speed*6f+18f;
                r.rig.solveEveryNFrames=gap<65f?1:6;r.rig.solvePhase=i%6;
            }
            foreach(var renderer in r.renderers)renderer.shadowCastingMode=gap<65f?ShadowCastingMode.On:ShadowCastingMode.Off;
            VisibleCount++;if(r.direction<0)OncomingCount++;else ForwardCount++;
        }
        lastPlayer=player;runSerial=session.RunSerial;
    }
    bool Build()
    {
        var library=Resources.Load<ShuntaNpcLibrary>(ShuntaNpcLibrary.ResourcePath);
        // Wait for authored donors; cycling life must never become another field of Kuro clones.
        if(library==null || library.riders.Length<12)return false;
        riders=new Rider[PoolSize];
        for(int i=0;i<PoolSize;i++)
        {
            var go=ShuntaNpcLibrary.CloneRider(i,transform);
            if(go==null){Clear();return false;}
            go.name="Shunta traffic "+i+(DirectionFor(i)<0?" oncoming":" forward");
            var r=new Rider{go=go,rig=go.GetComponentInChildren<KuroBikeRig>(true),renderers=go.GetComponentsInChildren<Renderer>(true),
                direction=DirectionFor(i),speed=SpeedFor(i),lane=LaneFor(i,route.roadWidth)};
            riders[i]=r;
            foreach(var renderer in r.renderers)
            {
                Mesh mesh=(renderer as SkinnedMeshRenderer)?.sharedMesh;
                if(mesh==null){var filter=renderer.GetComponent<MeshFilter>();if(filter!=null)mesh=filter.sharedMesh;}
                if(mesh!=null)for(int sub=0;sub<mesh.subMeshCount;sub++)PoolTriangles+=(int)mesh.GetIndexCount(sub)/3;
            }
        }
        Debug.Log("[shunta-cyclists] "+PoolSize+" pooled / "+library.riders.Length+" authored identities / "+PoolTriangles+" triangles; "+PoolSize/2+" each direction");
        return true;
    }
    void Park()
    {
        VisibleCount=OncomingCount=ForwardCount=0;
        if(riders==null)return;
        foreach(var r in riders)if(r!=null && r.go!=null){r.go.SetActive(false);r.shown=false;}
    }
    void Clear()
    {
        if(riders!=null)foreach(var r in riders)if(r!=null && r.go!=null)Destroy(r.go);
        riders=null;PoolTriangles=0;
    }
    void OnDisable()=>Park();
    void OnDestroy(){Clear();if(Instance==this)Instance=null;}
}
