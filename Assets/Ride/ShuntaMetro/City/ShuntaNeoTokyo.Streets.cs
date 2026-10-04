using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public sealed partial class ShuntaNeoTokyo
{
    void ShopLight(Vector3 position, int side, Block block)
    {
        var go = new GameObject("Neon shop spill") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(block.root.transform, false); go.transform.position = position;
        var light = go.AddComponent<Light>(); light.type = LightType.Point;
        light.range = 10f; light.color = side < 0 ? new Color(1f,.40f,.56f) : new Color(.40f,.72f,1f);
        light.shadows = LightShadows.None;
        var hd = go.GetComponent<HDAdditionalLightData>() ?? go.AddComponent<HDAdditionalLightData>();
        hd.SetIntensity(7000f, LightUnit.Lumen);
        hd.affectsVolumetric = false;
        block.lights.Add(light);
    }
    void StreetFurniture(ShuntaLookKit.MeshBag bag,Vector3 road,Vector3 forward,Vector3 right,int side,Quaternion q,System.Random rng,Block block)
    {
        Vector3 p=road+right*side*(route.roadWidth*.5f+1.8f);
        Vector3 P(float x,float y,float z)=>p+q*new Vector3(x,y,z);
        // Raised paving and curb stay outside the rideable ribbon.
        bag.Box(pavement,P(0,.04f,.5f),q,new Vector3(16f,.12f,2.5f),1.2f,1.2f);
        for(int i=0;i<4;i++) bag.Box(concrete,P(-6f+i*4f,.14f,-.72f),q,new Vector3(3.95f,.24f,.24f));
        // Bollards, cycle hoops and tactile paving give scale without adding colliders.
        for(int i=0;i<3;i++)
        {
            bag.Box(metal,P(1+i*1.5f,.48f,-.8f),q,new Vector3(.12f,.95f,.12f));
            bag.Box(warm,P(1+i*1.5f,.77f,-.8f),q,new Vector3(.13f,.08f,.13f));
        }
        bag.Box(warm,P(0,.115f,-.4f),q,new Vector3(16f,.014f,.23f));
        if(rng.NextDouble()<.5)
        {
            bag.Box(concrete,P(4,.55f,.8f),q,new Vector3(2f,1.1f,.65f));
            bag.Box(canopy,P(4,1.15f,.8f),q,new Vector3(2.1f,.14f,.7f));
            // Leaf clusters are kept behind the frontage line.
            bag.Lathe(foliage,P(4,1.22f,.8f),new[]{.35f,.65f,.3f},new[]{0f,.55f,.95f},7);
        }
        if(rng.NextDouble()<.35)
        {
            // Vending machine with glazed product rows and payment panel.
            bag.Box(concrete,P(-2,1.08f,.8f),q,new Vector3(.95f,2.15f,.7f));
            bag.Box(cool,P(-2,1.35f,.40f),q,new Vector3(.65f,1.15f,.025f));
            for(int y=0;y<3;y++) for(int x=0;x<4;x++)
                bag.Box(y%2==0?warm:neonPink,P(-2.23f+x*.15f,1.02f+y*.3f,.37f),q,new Vector3(.09f,.19f,.03f));
            bag.Box(metal,P(-2,.38f,.36f),q,new Vector3(.65f,.16f,.04f));
            block.details+=15;
        }
        if(rng.NextDouble()<.3)
        {
            // Sheltered metro/bus information panel.
            bag.Box(metal,P(0,2.7f,.4f),q,new Vector3(3.1f,.15f,1.8f));
            bag.Beam(metal,P(-1.4f,0,1f),P(-1.4f,2.7f,1f),.1f);
            bag.Beam(metal,P(1.4f,0,1f),P(1.4f,2.7f,1f),.1f);
            Sign(bag,P(0,2.28f,1f),q,2.4f,.48f,22,block);
            bag.Box(canopy,P(0,.52f,1f),q,new Vector3(2.3f,.14f,.45f));
        }
        block.details+=14;
    }

    void OverheadUtilities(ShuntaLookKit.MeshBag bag,Vector3 road,Vector3 forward,Vector3 right,Block block)
    {
        float lateral=route.roadWidth*.5f+2.4f;
        for(int side=-1;side<=1;side+=2)
        {
            var p=road+right*side*lateral;
            bag.Beam(metal,p,p+Vector3.up*10.3f,.18f);
            bag.Beam(metal,p+Vector3.up*9.4f-right*.65f,p+Vector3.up*9.4f+right*.65f,.10f);
            bag.Box(concrete,p+Vector3.up*8.3f,Quaternion.LookRotation(forward),new Vector3(.65f,.9f,.6f));
            for(int wire=0;wire<3;wire++)
            {
                Vector3 a=p+Vector3.up*(9.4f+wire*.22f)+forward*(wire*.14f);
                for(int i=0;i<8;i++)
                {
                    float t0=i/8f,t1=(i+1)/8f;
                    Vector3 W(float t)=>a+forward*t*66f-Vector3.up*(4f*t*(1f-t)*1.2f);
                    bag.Beam(metal,W(t0),W(t1),.025f);
                }
            }
            block.details+=28;
        }
    }
}
