using UnityEngine;

/// <summary>Minato's existing dressed, animated sidewalk pedestrians, pooled around Shunta's urban streets.</summary>
[DefaultExecutionOrder(40)]
public sealed class ShuntaStreetCrowd : MonoBehaviour
{
    public static ShuntaStreetCrowd Instance{get;private set;}
    public int VisibleCount{get;private set;}
    // 2026-10-04 (user: Shunta should be "alive with people everywhere"): 32 -> 120 walkers spread over +-0.28 km, shown in every street zone
    // (only the expressway zones 5/6/9 stay empty), with varied lateral offsets so they form loose crowds, not a single file.
    const int Count=80;
    GameObject[] people; MinatoCrowdActor[] actors; float[] anchors;
    RideSession session; ShuntaRouteBuilder route; float nextResolve; bool warned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if(Instance!=null)return;
        var go=new GameObject("Shunta reused street crowd"); DontDestroyOnLoad(go);Instance=go.AddComponent<ShuntaStreetCrowd>();
    }
    void Update()
    {
        if(Time.unscaledTime>=nextResolve)
        {
            nextResolve=Time.unscaledTime+1f;
            if(session==null)session=FindFirstObjectByType<RideSession>();
            if(route==null)route=FindFirstObjectByType<ShuntaRouteBuilder>();
        }
        bool active=session!=null && session.courseId==ShuntaRouteProvider.CourseId && route!=null && route.Course!=null;
        VisibleCount=0;
        if(!active){if(people!=null)foreach(var person in people)if(person!=null)person.SetActive(false);return;}
        if(people==null && !Build())return;
        float playerKm=session.DistanceM/session.Course.Length*route.Course.distanceKm;
        for(int i=0;i<Count;i++)
        {
            // Even spread (2026-10-04): the Count walkers tile a fixed 0.52 km window around the rider at equal spacing and wrap round it
            // one by one, so density is constant everywhere. The old code clamped anchors into [0, length]: everyone "behind" the start
            // line piled up at km 0 (the crowd of NPCs at the beginning). Positions outside the course are now simply hidden.
            const float Window=.52f, Half=Window*.5f;
            float spacing=Window/Count;
            if(anchors[i]<-50f||Mathf.Abs(anchors[i]-playerKm)>1f)anchors[i]=playerKm+(i-Count*.5f)*spacing;
            else
            {
                float d=anchors[i]-playerKm;
                if(d<-Half)anchors[i]+=Window; else if(d>Half)anchors[i]-=Window;
            }
            float km=anchors[i]+Mathf.Sin(Time.time*.08f+i)*.004f;
            var zone=(km>.004f && km<route.Course.distanceKm-.004f)?route.Course.ZoneAtKm(km):null;
            bool show=zone!=null && zone.index!=5 && zone.index!=6 && zone.index!=9;
            if(people[i]==null)continue;
            people[i].SetActive(show);if(!show)continue;
            Vector3 forward=route.TangentAtKm(km);forward.y=0f;forward.Normalize();
            Vector3 side=new Vector3(forward.z,0f,-forward.x);
            // Groups of three share a side and a walking direction (friends / families); every 9th person is a crosser in the dense street zones.
            int group=i/3;
            float sideSign=group%2==0?1f:-1f;
            float edge=route.roadWidth*.5f+1.6f+(i%4)*.75f;
            float direction=Mathf.Cos(Time.time*.08f+group*1.7f)>=0f?1f:-1f;
            bool crosser=i%9==4 && (zone.index==1 || zone.index==2 || zone.index==7);
            Vector3 p;Vector3 facing=forward*direction;
            if(crosser)
            {
                float ph=Time.time*.12f+i;
                float lateral=Mathf.Sin(ph)*edge;
                p=route.PositionAtKm(km)+side*lateral+Vector3.up*.14f;
                facing=side*Mathf.Sign(Mathf.Cos(ph));
            }
            else p=route.PositionAtKm(km)+side*sideSign*edge+Vector3.up*.14f;
            // Standers (queues at shopfronts, people waiting at the kerb): fixed on their anchor, turned toward the road (kerb) or the shop (wall).
            bool stander=!crosser && i%9>=7;
            if(stander)
            {
                p=route.PositionAtKm(anchors[i])+side*sideSign*(route.roadWidth*.5f+1.5f+(i%3)*.55f)+Vector3.up*.14f;
                facing=side*sideSign*(i%2==0?-1f:1f);
            }
            // Walkers near the rider glance over: body turns ~40 degrees toward the rider and the pace eases.
            float near=Mathf.Abs(km-playerKm)*1000f;
            float notice=(!crosser && near<14f)?Mathf.Clamp01(1f-near/14f):0f;
            if(notice>0f && !stander)
            {
                Vector3 toRider=route.PositionAtKm(playerKm)-p;toRider.y=0f;
                if(toRider.sqrMagnitude>.01f)facing=Vector3.Slerp(facing,toRider.normalized,.45f*notice);
            }
            people[i].transform.SetPositionAndRotation(p,Quaternion.LookRotation(facing,Vector3.up));
            if(actors[i]!=null)
            {
                float speed=stander?0f:Mathf.Abs(Mathf.Cos(Time.time*.08f+(crosser?i:group*1.7f)))*.72f*(1f-.5f*notice);
                actors[i].externalDrive=true;actors[i].gaitWeight=Mathf.Clamp01(speed/.78f);
                actors[i]._driveHz=Mathf.Max(.05f,speed/MinatoCrowdActor.NominalStridePerCycle);
                actors[i].drivePhase+=Time.deltaTime*actors[i]._driveHz*Mathf.PI*2f;
            }
            VisibleCount++;
        }
    }
    // The baked donors keep LOD0's raw glTF body material (Shared_Material_0, the original Kuro atlas). That atlas does not
    // match the donor mesh's UV layout and renders as red/pink camouflage (same root cause as Nagisa Bay's PeopleOverhaul).
    // Give every such slot a street-clothes PedKit atlas, one shared material per kit.
    static readonly System.Collections.Generic.Dictionary<(Material,Texture),Material> kitMaterials=new System.Collections.Generic.Dictionary<(Material,Texture),Material>();
    static void DressWithStreetKit(GameObject person,int index)
    {
        var library=Resources.Load<PedestrianModelLibrary>("Pedestrians/PedestrianModelLibrary");
        var kits=library!=null && library.kuroRider!=null?library.kuroRider.streetKits:null;
        if(kits==null || kits.Length==0)return;
        var kit=kits[(index*5+index/kits.Length)%kits.Length];
        if(kit==null)return;
        foreach(var renderer in person.GetComponentsInChildren<Renderer>(true))
        {
            if(!(renderer is SkinnedMeshRenderer) || !renderer.name.StartsWith("Mesh_0"))continue;
            var slots=renderer.sharedMaterials;bool changed=false;
            for(int s=0;s<slots.Length;s++)
            {
                var source=slots[s];
                if(source==null || !source.name.StartsWith("Shared_Material_0") || !source.HasProperty("_MainTex"))continue;
                if(!kitMaterials.TryGetValue((source,kit),out var kitted) || kitted==null)
                {
                    kitted=new Material(source){name=source.name+"_"+kit.name+" (Shunta)"};
                    kitted.SetTexture("_MainTex",kit);
                    if(kitted.HasProperty("_Color"))kitted.SetColor("_Color",Color.white);
                    kitMaterials[(source,kit)]=kitted;
                }
                slots[s]=kitted;changed=true;
            }
            if(changed)renderer.sharedMaterials=slots;
        }
    }
    // Every walker has a name, a face card and her/his own Shunta lines (user, 2026-10-04: every NPC must have a name card and dialogue).
    static readonly string[] WalkerLines=
    {
        "Nice night for a ride!","Careful, the crossing's busy.","Love the lights tonight.","Go, go, go!","Is that a race? Good luck!",
        "The ramen stall near here is the best.","Watch the puddles ahead.","Whoa, you're fast!","Odaiba's lovely at this hour.","Ride safe!",
        "I can hear the trains from here.","Shibuya never sleeps.","That bike is gorgeous.","Mind the tram lines!","Evening!",
    };
    static void AddGreeting(GameObject person,int index)
    {
        var names=NpcCyclistInteraction.PortraitNames;
        var g=person.GetComponent<NpcGreeting>();if(g==null)g=person.AddComponent<NpcGreeting>();
        g.enabled=true;g.useFaceCard=true;g.triggerDistance=8f;g.viewAngle=300f;g.visibleSeconds=3.2f;g.rearmSeconds=8f;
        g.bubbleOffset=new Vector3(0f,1.8f,0f);g.smileRendererName="";
        g.riderName=names[(index*13+5)%names.Length];
        int a=(index*3)%WalkerLines.Length;
        g.ownPhrases=new[]{WalkerLines[a],WalkerLines[(a+4)%WalkerLines.Length],WalkerLines[(a+9)%WalkerLines.Length]};
        g.ResolveIdentity();
    }
    bool Build()
    {
        var library=Resources.Load<ShuntaNpcLibrary>(ShuntaNpcLibrary.ResourcePath);
        if(library==null || library.pedestrians.Length==0)
        {
            if(!warned){Debug.LogWarning("[shunta-npcs] street donors not baked yet; run ShuntaNpcLightingValidation.BuildLibrary");warned=true;}return false;
        }
        people=new GameObject[Count];actors=new MinatoCrowdActor[Count];anchors=new float[Count];
        for(int i=0;i<Count;i++)
        {
            var donor=library.pedestrians[i%library.pedestrians.Length];
            if(donor==null)continue;
            people[i]=Instantiate(donor,transform);ShuntaNpcLibrary.Sanitize(people[i],true);
            people[i].name="Shunta Minato pedestrian "+i;
            DressWithStreetKit(people[i],i);
            AddGreeting(people[i],i);
            actors[i]=people[i].GetComponentInChildren<MinatoCrowdActor>(true);
            if(actors[i]!=null){actors[i].motion=MinatoCrowdActor.MotionKind.Walk;actors[i].externalDrive=true;actors[i].gaitWeight=.65f;}
            people[i].SetActive(false);anchors[i]=-100f;
        }
        return true;
    }
}
