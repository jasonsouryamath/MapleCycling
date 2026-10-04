using UnityEngine;

public sealed partial class ShuntaNeoTokyo
{
    Material skylineStone, skylineSteel, skylineGlass;
    readonly int[] skylineFamilies = new int[6];
    public int[] SkylineFamilyCounts => (int[])skylineFamilies.Clone();

    void MakeSkylineMaterials()
    {
        skylineStone = Mat("skyline pearl concrete",new Color(.48f,.51f,.55f),.32f);
        skylineSteel = Mat("skyline brushed aluminium",new Color(.40f,.47f,.54f),.62f,.65f);
        skylineGlass = Mat("skyline recessed blue glass",new Color(.09f,.17f,.23f),.88f,.42f);
        // A restrained material floor preserves legibility under the existing fixed night exposure.
        ShuntaLookKit.SetEmissive(skylineStone,new Color(.32f,.39f,.49f),.08f);
        ShuntaLookKit.SetEmissive(skylineSteel,new Color(.30f,.42f,.55f),.06f);
        ShuntaLookKit.SetEmissive(skylineGlass,new Color(.12f,.25f,.36f),.035f);
    }

    void BuildSkylineCluster(int id, ShuntaLookKit.MeshBag mass, ShuntaLookKit.MeshBag detail, Block block)
    {
        float km=(id+.35f)*ChunkKm;
        var zone=route.Course.ZoneAtKm(km);
        if(zone==null || zone.index==4 || zone.index==10 || id%3!=1) return;
        var p=route.PositionAtKm(km);
        var f=route.TangentAtKm(km); f.y=0; f.Normalize();
        var right=new Vector3(f.z,0,-f.x);
        bool coast=zone.index>=11;
        int side=coast?-1:((id/3)%2==0?-1:1);
        var q=Quaternion.LookRotation(right*side,Vector3.up);
        var rng=new System.Random(3917+id*997);
        int count=coast?2:5;
        foreach(var lot in SkylineLots(id))
        {
            BuildSkylineTower(mass,detail,lot.centre,lot.q,lot.width,lot.depth,lot.height,lot.family,rng,block);
            block.families[lot.family]++; skylineFamilies[lot.family]++;
        }
        // Infrastructure districts get one supported elevated link per cluster, never street-level obstruction.
        if((zone.index==8 || zone.index==9 || id==13) && count>=3)
        {
            Vector3 a=p+f*25f+right*side*72f, b=p+f*103f+right*side*112f;
            if(ClearOfRoad(a,km,15f) && ClearOfRoad(b,km,15f))
            {
                mass.Beam(skylineStone,a+Vector3.up*32f,b+Vector3.up*32f,3f,6f);
                mass.Beam(skylineGlass,a+Vector3.up*34.5f,b+Vector3.up*34.5f,2.5f,4.5f);
                foreach(var foot in new[]{a,b})
                    mass.Beam(skylineSteel,foot,foot+Vector3.up*32f,1.7f);
                for(int n=0;n<=9;n++)
                {
                    var c=Vector3.Lerp(a,b,n/9f);
                    mass.Beam(skylineSteel,c+Vector3.up*32f,c+Vector3.up*36f,.18f);
                }
            }
        }
    }

    struct SkylineLot
    {
        public Vector3 centre; public Quaternion q;
        public float width,depth,height; public int family;
    }
    System.Collections.Generic.IEnumerable<SkylineLot> SkylineLots(int id)
    {
        float km=(id+.35f)*ChunkKm;
        var zone=route.Course.ZoneAtKm(km);
        if(zone==null || zone.index==4 || zone.index==10 || id%3!=1) yield break;
        var p=route.PositionAtKm(km); var f=route.TangentAtKm(km); f.y=0; f.Normalize();
        var right=new Vector3(f.z,0,-f.x);
        bool coast=zone.index>=11;
        int side=coast?-1:((id/3)%2==0?-1:1);
        var q=Quaternion.LookRotation(right*side,Vector3.up);
        int count=coast?2:5;
        for(int i=0;i<count;i++)
        {
            int family=i==3?3:i==4?0:(id/3+i*2)%6;
            float width=i==0?30f:i>=3?20f:24f, depth=i==0?27f:i>=3?20f:23f;
            float height=i==0?(zone.index==1 || zone.index==5 || zone.index==9?210f:155f):82f+i*19f;
            if(i>=3) height=i==3?52f:38f;
            if(coast) height*=.72f;
            float along=i==3?-75f:i==4?235f:25f+i*78f;
            float setback=i==3?118f:i==4?95f:72f+i*40f;
            var centre=p+f*along+right*side*setback;
            float radius=Mathf.Sqrt(width*width+depth*depth)*.8f+12f;
            if(!ClearOfRoad(centre,km,radius)) continue;
            // Base follows the road grade at its own frontage, rather than the cluster anchor.
            centre.y=route.PositionAtKm(Mathf.Clamp(km+along/1000f,0f,route.Course.distanceKm)).y;
            if(zone.index==3 || zone.index==5 || zone.index==6 || zone.index==9) centre.y-=12f;
            yield return new SkylineLot { centre=centre,q=q,width=width,depth=depth,height=height,family=family };
        }
    }

    Vector2[] Outline(float w,float d,bool oval=false)
    {
        if(oval)
        {
            var points=new Vector2[16];
            for(int i=0;i<points.Length;i++) { float a=i*Mathf.PI*2/points.Length; points[i]=new Vector2(Mathf.Cos(a)*w*.5f,Mathf.Sin(a)*d*.5f); }
            return points;
        }
        float x=w*.5f,z=d*.5f,c=Mathf.Min(w,d)*.16f;
        return new[]{new Vector2(x-c,-z),new Vector2(x,-z+c),new Vector2(x,z-c),new Vector2(x-c,z),new Vector2(-x+c,z),new Vector2(-x,z-c),new Vector2(-x,-z+c),new Vector2(-x+c,-z)};
    }

    static void Quad(ShuntaLookKit.MeshBag bag,Material mat,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal)
    {
        if(Vector3.Dot(Vector3.Cross(b-a,c-a),normal)<0f) { var swap=b; b=d; d=swap; }
        bag.Tri(mat,a,b,c,normal,normal,normal); bag.Tri(mat,a,c,d,normal,normal,normal);
    }

    void Loft(ShuntaLookKit.MeshBag bag,Material mat,Vector3 origin,Quaternion q,Vector2[] shape,float y0,float y1,float s0=1f,float s1=1f)
    {
        Vector3 P(Vector2 p,float y,float s)=>origin+q*new Vector3(p.x*s,y,p.y*s);
        for(int i=0;i<shape.Length;i++)
        {
            var a=shape[i]; var b=shape[(i+1)%shape.Length];
            var edge=b-a; var n=q*new Vector3(edge.y,0,-edge.x).normalized;
            Quad(bag,mat,P(a,y0,s0),P(a,y1,s1),P(b,y1,s1),P(b,y0,s0),n);
            var centre=origin+Vector3.up*y1;
            var pa=P(a,y1,s1); var pb=P(b,y1,s1);
            bag.Tri(mat,centre,pb,pa,Vector3.up,Vector3.up,Vector3.up);
        }
    }

    void TowerVolume(ShuntaLookKit.MeshBag mass,ShuntaLookKit.MeshBag detail,Vector3 origin,Quaternion q,
        float w,float d,float bottom,float height,bool oval,int seed,float taper=1f,bool diagonals=false)
    {
        var shape=Outline(w,d,oval);
        Loft(mass,skylineGlass,origin,q,shape,bottom,bottom+height,1f,taper);
        int floors=Mathf.Max(1,Mathf.FloorToInt(height/3.8f));
        for(int floor=0;floor<floors;floor++)
        {
            float y=bottom+floor*3.8f, t=(y-bottom)/height;
            float scale=Mathf.Lerp(1f,taper,t);
            Loft(mass,floor%12==0?skylineStone:skylineSteel,origin,q,shape,y,y+(floor%12==0?.65f:.19f),scale*1.012f,scale*1.012f);
            for(int face=0;face<shape.Length;face++)
            {
                var a=shape[face]*scale; var b=shape[(face+1)%shape.Length]*scale;
                var edge=b-a; float length=edge.magnitude;
                var n=q*new Vector3(edge.y,0,-edge.x).normalized;
                int bays=Mathf.Max(1,Mathf.FloorToInt(length/1.65f));
                for(int bay=0;bay<bays;bay++)
                {
                    // Offices light in contiguous 3-bay / 3-floor occupancies; most glass stays dark.
                    int hash=seed+face*31+(floor/3)*79+(bay/3)*43;
                    if((hash&15)>4) continue;
                    Vector2 left=Vector2.Lerp(a,b,(bay+.13f)/bays),right=Vector2.Lerp(a,b,(bay+.87f)/bays);
                    Vector3 P(Vector2 v,float h) { float hs=Mathf.Lerp(1f,taper,(h-bottom)/height)/scale; return origin+q*new Vector3(v.x*hs,h,v.y*hs)+n*.035f; }
                    Quad(mass,(hash&1)==0?warm:cool,P(left,y+.55f),P(left,y+3.15f),P(right,y+3.15f),P(right,y+.55f),n);
                }
                if(floor==0)
                {
                    var v=shape[face];
                    mass.Beam(skylineSteel,origin+q*new Vector3(v.x,bottom,v.y),origin+q*new Vector3(v.x*taper,bottom+height,v.y*taper),.28f);
                }
                // Fine mullions fade separately; silhouette and lit rooms remain in the mass mesh.
                if(floor%2==0 && length>8f)
                    for(int bay=1;bay<bays;bay++)
                    {
                        var v=Vector2.Lerp(a,b,bay/(float)bays);
                        detail.Beam(skylineSteel,origin+q*new Vector3(v.x,y+.2f,v.y)+n*.05f,origin+q*new Vector3(v.x,y+3.8f,v.y)+n*.05f,.055f);
                    }
            }
        }
        if(diagonals)
            for(int face=0;face<shape.Length;face+=2)
                for(float y=bottom;y<bottom+height-12f;y+=22.8f)
                {
                    // Follow the curved surface; a single chord would disappear inside the glass.
                    for(int step=0;step<3;step++)
                    {
                        float ay=y+step*7.6f, by=Mathf.Min(y+(step+1)*7.6f,bottom+height);
                        float s0=Mathf.Lerp(1f,taper,(ay-bottom)/height), s1=Mathf.Lerp(1f,taper,(by-bottom)/height);
                        var a=shape[(face+step)%shape.Length]*s0*1.035f;
                        var b=shape[(face+step+1)%shape.Length]*s1*1.035f;
                        mass.Beam(skylineStone,origin+q*new Vector3(a.x,ay,a.y),origin+q*new Vector3(b.x,by,b.y),.42f);
                    }
                }
        Loft(mass,skylineStone,origin,q,shape,bottom+height,bottom+height+1.2f,taper*1.025f,taper*1.025f);
    }

    void BuildSkylineTower(ShuntaLookKit.MeshBag mass,ShuntaLookKit.MeshBag detail,Vector3 o,Quaternion q,
        float w,float d,float h,int family,System.Random rng,Block block)
    {
        Vector3 P(float x,float y,float z)=>o+q*new Vector3(x,y,z);
        Loft(mass,skylineStone,o,q,Outline(w*1.25f,d*1.25f),0f,7f);
        mass.Box(pavement,P(0,-.2f,0),q,new Vector3(w*1.35f,.4f,d*1.35f));
        // Recessed public podium glazing under a projecting canopy.
        for(int i=-3;i<=3;i++)
        {
            mass.Box(glass,P(i*w/8f,3f,-d*.626f),q,new Vector3(w/9f,4.6f,.1f));
            detail.Box(warm,P(i*w/8f,5.6f,-d*.65f),q,new Vector3(w/9f,.10f,.3f));
        }
        mass.Box(skylineSteel,P(0,6.2f,-d*.7f),q,new Vector3(w*1.3f,.45f,d*.24f));
        int seed=rng.Next(10000);
        switch(family)
        {
            case 0: // Slender chamfered corporate tower with a tapered lantern crown.
                TowerVolume(mass,detail,o,q,w*.76f,d*.78f,7f,h,false,seed,.90f);
                Loft(mass,skylineSteel,o,q,Outline(w*.76f,d*.78f),h+8f,h+20f,.90f,.65f);
                break;
            case 1: // Unequal twin towers on a shared podium; the gap remains visible.
                TowerVolume(mass,detail,P(-w*.31f,0,0),q,w*.40f,d*.85f,7f,h,false,seed);
                TowerVolume(mass,detail,P(w*.31f,0,0),q,w*.40f,d*.85f,7f,h*.77f,false,seed+91);
                mass.Box(skylineSteel,P(0,h*.61f,0),q,new Vector3(w*.28f,3f,d*.38f));
                break;
            case 2: // Curved cocoon profile and diagonal exoskeleton.
                TowerVolume(mass,detail,o,q,w*.85f,d*.85f,7f,h*.65f,true,seed,1.10f,true);
                TowerVolume(mass,detail,o,q,w*.935f,d*.935f,7f+h*.65f,h*.35f,true,seed+7,.42f,true);
                break;
            case 3: // Offset concrete setbacks with exposed terraces.
                for(int tier=0;tier<4;tier++)
                    TowerVolume(mass,detail,P(-tier*w*.055f,0,tier*d*.035f),q,w*(1f-tier*.17f),d*(1f-tier*.14f),7f+tier*h/4f,h/4f,false,seed+tier*11);
                break;
            case 4: // Metabolist service core with genuinely cantilevered occupied modules.
                Loft(mass,skylineStone,o,q,Outline(w*.28f,d*.45f),7f,h+18f);
                for(int tier=0;tier<6;tier++)
                {
                    float y=12f+tier*h/6f;
                    float shift=(tier%2==0?-1:1)*w*.2f;
                    TowerVolume(mass,detail,P(shift,0,0),q,w*(tier%2==0?1f:.85f),d*.82f,y,h/6f-3f,false,seed+tier*17);
                    mass.Beam(skylineSteel,P(0,y-4f,0),P(shift,y,-d*.35f),.8f);
                }
                break;
            default: // Infrastructure tower: open service floors and offset equipment crown.
                for(int tier=0;tier<3;tier++)
                {
                    float y=7f+tier*h/3f;
                    TowerVolume(mass,detail,o,q,w,d,y,h/3f-5f,false,seed+tier*23);
                    for(int corner=0;corner<4;corner++)
                    {
                        float x=(corner<2?-1:1)*w*.38f,z=(corner%2==0?-1:1)*d*.38f;
                        mass.Beam(skylineSteel,P(x,y+h/3f-5f,z),P(x,y+h/3f,z),.8f);
                    }
                }
                mass.Box(skylineSteel,P(w*.2f,h+12f,0),q,new Vector3(w*.45f,10f,d*.6f));
                break;
        }
        // One subdued crown accent, instead of luminous outlines on every floor.
        mass.Box(cool,P(0,h+8f,-d*.15f),q,new Vector3(w*.28f,.2f,.3f));
        float roofY=family==0?h+20f:family==4?h+18f:family==5?h+17f:h+8.2f;
        Loft(mass,skylineSteel,o,q,Outline(w*.30f,d*.30f),roofY-.7f,roofY);
        Roof(detail,o,q,w*.28f,d*.28f,roofY,rng,block);
        // A real local facade wash gives the podium and lower structure depth at night.
        var washGo=new GameObject("Shunta architectural wash") { hideFlags=HideFlags.DontSave };
        washGo.transform.SetParent(block.root.transform,false);
        washGo.transform.position=P(0,4f,-d*.5f-10f);
        washGo.transform.rotation=Quaternion.LookRotation(P(0,h*.4f,0)-washGo.transform.position);
        var wash=washGo.AddComponent<Light>(); wash.type=LightType.Spot; wash.range=h+35f;
        wash.spotAngle=42f; wash.color=new Color(.68f,.80f,1f); wash.shadows=LightShadows.None;
        var washHd=washGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
        washHd.SetIntensity(12000000f,UnityEngine.Rendering.LightUnit.Lumen); washHd.affectsVolumetric=false;
        block.lights.Add(wash);
        block.buildings++; block.details+=12;
    }
}







