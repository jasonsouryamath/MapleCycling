using System.Collections.Generic;
using UnityEngine;

public sealed partial class ShuntaNeoTokyo
{
    public int InfillBuildingCount { get; private set; }
    public const float InfillPitch = 38f;
    const float InfillWidth = 34f;
    Vector3[] infillRoad, infillSource;
    Matrix4x4 infillMatrix;
    readonly List<SkylineLot> reservedTowers=new List<SkylineLot>();
    Material[] infillFacade;
    bool infillReserved;

    void MakeInfillMaterials()
    {
        infillFacade=new Material[3];
        var texture=ShuntaLookKit.MakeWindowTextureFine(18433); textures.Add(texture);
        var colors=new[]{new Color(.29f,.36f,.42f),new Color(.43f,.40f,.36f),new Color(.37f,.41f,.45f)};
        for(int i=0;i<3;i++)
        {
            var mat=Mat("city infill facade "+i,colors[i],.48f,i==0?.25f:.08f);
            ShuntaLookKit.SetEmissiveMap(mat,texture,Color.white,.24f+i*.025f);
            infillFacade[i]=mat;
        }
    }

    void EnsureInfillRoad()
    {
        EnsureRoadCoordinates();
        if(infillReserved) return;
        for(int id=0;id<Mathf.CeilToInt(route.Course.distanceKm/ChunkKm);id++)
            foreach(var lot in SkylineLots(id)) reservedTowers.Add(lot);
        infillReserved=true;
    }

    // Fixed world parcels have stable ownership at bends and across streamed chunk boundaries.
    // Nearest street sample/segment supplies elevation and district, while footprints share a 4 m alley.
    void BuildCityInfill(int id,ShuntaLookKit.MeshBag mass,ShuntaLookKit.MeshBag fine,Block block)
    {
        EnsureInfillRoad();
        float start=id*ChunkKm,end=Mathf.Min(start+ChunkKm,route.Course.distanceKm);
        var bounds=new Bounds(route.PositionAtKm(start),Vector3.zero);
        for(float km=start;km<=end;km+=.01f) bounds.Encapsulate(route.PositionAtKm(km));
        bounds.Expand(new Vector3(660f,0,660f));
        int x0=Mathf.FloorToInt(bounds.min.x/InfillPitch),x1=Mathf.CeilToInt(bounds.max.x/InfillPitch);
        int z0=Mathf.FloorToInt(bounds.min.z/InfillPitch),z1=Mathf.CeilToInt(bounds.max.z/InfillPitch);
        for(int x=x0;x<=x1;x++) for(int z=z0;z<=z1;z++)
        {
            var centre=new Vector3((x+.5f)*InfillPitch,0,(z+.5f)*InfillPitch);
            float best=float.MaxValue,nearestKm=0; Vector3 street=default,forward=default;
            for(int i=1;i<infillRoad.Length;i++)
            {
                var a=infillRoad[i-1]; var b=infillRoad[i]; float dx=b.x-a.x,dz=b.z-a.z,ls=dx*dx+dz*dz;
                float t=ls>.00001f?Mathf.Clamp01(((centre.x-a.x)*dx+(centre.z-a.z)*dz)/ls):0f;
                float ox=centre.x-a.x-t*dx,oz=centre.z-a.z-t*dz,ds=ox*ox+oz*oz;
                if(ds>=best) continue;
                best=ds; nearestKm=Mathf.Lerp(route.Km[i-1],route.Km[i],t); street=Vector3.Lerp(a,b,t); forward=new Vector3(dx,0,dz).normalized;
            }
            if(Mathf.FloorToInt(nearestKm/ChunkKm)!=id) continue;
            var zone=route.Course.ZoneAtKm(nearestKm);
            if(zone==null || zone.index==4 || zone.index==10) continue;
            var right=new Vector3(forward.z,0,-forward.x);
            int side=Vector3.Dot(centre-street,right)<0?-1:1;
            bool coast=zone.index>=11;
            if(coast && side>0) continue;
            float reach=coast?245f:310f;
            if(best>reach*reach || best<28f*28f) continue;
            // The front row has its own street-facing shops. Reserve that row and all other roads.
            if(!ClearFootprint(centre,Quaternion.identity,InfillWidth,InfillWidth,18f)) continue;
            bool reserved=false;
            foreach(var towerLot in reservedTowers)
                if(ParcelsOverlap(centre,InfillWidth*.5f,towerLot)) { reserved=true; break; }
            if(reserved) continue;
            int seed=unchecked(x*73856093 ^ z*19349663 ^ 7311);
            var rng=new System.Random(seed&int.MaxValue);
            bool streetDistrict=zone.index==1 || zone.index==2 || zone.index==7 || zone.index==8 || coast;
            centre.y=street.y-(streetDistrict?0f:12f);
            float distance=Mathf.Sqrt(best);
            if(distance+InfillWidth*.71f>GroundReach(nearestKm,side)) continue;
            float height=distance<85f?R(rng,18f,37f):distance<180f?R(rng,30f,67f):R(rng,42f,98f);
            if(coast) height*=.78f;
            BuildInfillBuilding(mass,fine,centre,height,rng,block);
            block.infill++; InfillBuildingCount++;
        }
    }

    static bool ParcelsOverlap(Vector3 centre,float half,SkylineLot lot)
    {
        Vector3 d=centre-lot.centre,r=lot.q*Vector3.right,f=lot.q*Vector3.forward;
        float w=lot.width*.72f+.6f,dep=lot.depth*.72f+.6f;
        if(Mathf.Abs(d.x)>=half+Mathf.Abs(r.x)*w+Mathf.Abs(f.x)*dep) return false;
        if(Mathf.Abs(d.z)>=half+Mathf.Abs(r.z)*w+Mathf.Abs(f.z)*dep) return false;
        if(Mathf.Abs(Vector3.Dot(d,r))>=w+half*(Mathf.Abs(r.x)+Mathf.Abs(r.z))) return false;
        return Mathf.Abs(Vector3.Dot(d,f))<dep+half*(Mathf.Abs(f.x)+Mathf.Abs(f.z));
    }

    bool ClearFootprint(Vector3 centre,Quaternion q,float width,float depth,float margin)
    {
        EnsureRoadCoordinates();
        var inverse=Quaternion.Inverse(q);
        float hx=width*.5f+margin,hz=depth*.5f+margin;
        float radius=Mathf.Sqrt(hx*hx+hz*hz);
        for(int i=1;i<infillRoad.Length;i++)
        {
            var a=infillRoad[i-1]; var b=infillRoad[i];
            if(Mathf.Max(a.x,b.x)<centre.x-radius || Mathf.Min(a.x,b.x)>centre.x+radius ||
               Mathf.Max(a.z,b.z)<centre.z-radius || Mathf.Min(a.z,b.z)>centre.z+radius) continue;
            a=inverse*(a-centre); b=inverse*(b-centre);
            float enter=0,exit=1;
            bool Slab(float p,float delta,float half)
            {
                if(Mathf.Abs(delta)<.00001f) return Mathf.Abs(p)<=half;
                float t0=(-half-p)/delta,t1=(half-p)/delta;
                if(t0>t1) { float swap=t0;t0=t1;t1=swap; }
                enter=Mathf.Max(enter,t0); exit=Mathf.Min(exit,t1); return enter<=exit;
            }
            if(Slab(a.x,b.x-a.x,hx) && Slab(a.z,b.z-a.z,hz)) return false;
        }
        return true;
    }

    void EnsureRoadCoordinates()
    {
        if(infillSource==route.Positions && infillMatrix==route.transform.localToWorldMatrix) return;
        infillSource=route.Positions; infillMatrix=route.transform.localToWorldMatrix;
        infillRoad=new Vector3[infillSource.Length];
        for(int i=0;i<infillRoad.Length;i++) infillRoad[i]=infillMatrix.MultiplyPoint3x4(infillSource[i]);
        reservedTowers.Clear(); infillReserved=false;
    }

    void BuildInfillBuilding(ShuntaLookKit.MeshBag mass,ShuntaLookKit.MeshBag fine,Vector3 o,float h,System.Random rng,Block block)
    {
        const float w=InfillWidth;
        var q=Quaternion.identity; var facade=infillFacade[rng.Next(3)];
        Vector3 P(float x,float y,float z)=>o+new Vector3(x,y,z);
        mass.Box(concrete,P(0,-3f,0),q,new Vector3(w,6f,w));
        mass.Box(concrete,P(0,1.3f,0),q,new Vector3(w,2.6f,w));
        mass.Box(facade,P(0,(h+2.6f)*.5f,0),q,new Vector3(w,h-2.6f,w),24f,60.8f,new Vector2(R(rng,0,1),R(rng,0,1)),true);
        for(float y=2.6f;y<h;y+=7.6f)
            mass.Box(skylineStone,P(0,y,0),q,new Vector3(w+.35f,.26f,w+.35f));
        foreach(int sx in new[]{-1,1}) foreach(int sz in new[]{-1,1})
            mass.Box(skylineSteel,P(sx*(w*.5f-.18f),h*.5f,sz*(w*.5f-.18f)),q,new Vector3(.5f,h,.5f));
        mass.Box(skylineStone,P(0,h+.3f,0),q,new Vector3(w+.7f,.6f,w+.7f));
        float crown=R(rng,3f,7f);
        mass.Box(facade,P(R(rng,-3,3),h+crown*.5f+.6f,R(rng,-3,3)),q,new Vector3(w*.72f,crown,w*.72f),24f,60.8f);
        fine.Box(metal,P(7f,h+crown+1f,4f),q,new Vector3(3f,1.2f,2f));
        fine.Box(metal,P(-6f,h+1.3f,-5f),q,new Vector3(2f,1.4f,3f));
        mass.Box(pavement,P(0,-.18f,0),q,new Vector3(w+1,.36f,w+1));
        block.buildings++; block.details+=6;
    }
}


