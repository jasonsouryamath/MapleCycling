using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public sealed partial class ShuntaNeoTokyo
{
    Material Mat(string name, Color color, float rough, float metallic = 0f)
    {
        var m = ShuntaLookKit.Lit("NeoTokyo " + name, color, rough, metallic);
        m.enableInstancing = true; materials.Add(m); return m;
    }

    void MakeMaterials()
    {
        concrete = Mat("precast", new Color(.36f,.39f,.43f), .28f);
        brick = Mat("tile facade", Color.white, .34f);
        metal = Mat("steel", new Color(.24f,.28f,.33f), .56f, .65f);
        glass = Mat("shop glass", new Color(.15f,.25f,.31f), .77f, .38f);
        pavement = Mat("paving", new Color(.30f,.32f,.35f), .23f);
        canopy = Mat("awning", new Color(.30f,.13f,.20f), .22f);
        foliage = Mat("planter leaves", new Color(.25f,.38f,.28f), .24f);
        streetLamp = Mat("streetlamp diffuser", Color.white, .4f);
        ShuntaLookKit.SetEmissive(streetLamp, new Color(1f,.86f,.65f), 3.5f);
        warm = Mat("warm interior", new Color(.74f,.64f,.42f), .48f);
        cool = Mat("cool interior", new Color(.31f,.53f,.65f), .56f);
        neonPink = Mat("pink tube", new Color(.35f,.16f,.25f), .5f);
        neonBlue = Mat("blue tube", new Color(.14f,.3f,.4f), .5f);
        sign = Mat("Japanese shop signs", Color.white, .35f);
        tower = Mat("office window grid", new Color(.25f,.31f,.37f), .7f, .35f);
        ShuntaLookKit.SetEmissive(warm, new Color(1f,.7f,.37f), .36f);
        ShuntaLookKit.SetEmissive(cool, new Color(.40f,.73f,1f), .26f);
        ShuntaLookKit.SetEmissive(neonPink, new Color(1f,.17f,.48f), 2.2f);
        ShuntaLookKit.SetEmissive(neonBlue, new Color(.20f,.75f,1f), 1.8f);
        var atlas = Resources.Load<Texture2D>("ShuntaMetro/City/NeoTokyoSigns");
        if (atlas != null)
        {
            ShuntaLookKit.SetBaseMap(sign, atlas, Color.white);
            ShuntaLookKit.SetEmissiveMap(sign, atlas, Color.white, 2.4f);
        }
        else Debug.LogWarning("[neo-tokyo] sign atlas missing");
        var windows = ShuntaLookKit.MakeWindowTextureFine(9301); textures.Add(windows);
        ShuntaLookKit.SetEmissiveMap(tower, windows, Color.white, .55f);
        var tiles = new Texture2D(128,128,TextureFormat.RGBA32,true,false) { name="NeoTokyo ceramic tile", wrapMode=TextureWrapMode.Repeat, hideFlags=HideFlags.DontSave };
        var pixels = new Color32[128*128];
        for (int y=0; y<128; y++) for (int x=0; x<128; x++)
        {
            int offset = (y/16%2)*16;
            bool mortar = y%16<2 || (x+offset)%32<2;
            byte v = (byte)(mortar ? 72 : 124 + ((x*17+y*31)%13));
            pixels[y*128+x] = new Color32(v,(byte)(v*.93f),(byte)(v*.90f),255);
        }
        tiles.SetPixels32(pixels); tiles.Apply(true); textures.Add(tiles);
        ShuntaLookKit.SetBaseMap(brick,tiles,Color.white);
        var normal = new Texture2D(128,128,TextureFormat.RGBA32,true,true) { name="NeoTokyo tile grout normal", wrapMode=TextureWrapMode.Repeat, hideFlags=HideFlags.DontSave };
        float Height(int x,int y)
        {
            x=(x+128)%128; y=(y+128)%128;
            return y%16<2 || (x+(y/16%2)*16)%32<2 ? .75f : 1f;
        }
        for(int y=0;y<128;y++) for(int x=0;x<128;x++)
        {
            Vector3 n=new Vector3(Height(x-1,y)-Height(x+1,y),Height(x,y-1)-Height(x,y+1),1f).normalized;
            pixels[y*128+x]=new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1f);
        }
        normal.SetPixels32(pixels); normal.Apply(true); textures.Add(normal);
        brick.SetTexture("_NormalMap",normal); brick.SetFloat("_NormalScale",.5f);
        brick.EnableKeyword("_NORMALMAP"); brick.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
        ShuntaLookKit.SetEmissive(concrete, new Color(.32f,.37f,.44f), .04f);
        ShuntaLookKit.SetEmissive(brick, new Color(.30f,.28f,.26f), .03f);
        ShuntaLookKit.SetEmissive(metal, new Color(.22f,.29f,.36f), .025f);
        MakeSkylineMaterials();
        MakeInfillMaterials();
        foreach (var material in materials) HDMaterial.ValidateMaterial(material);
    }

    // All architecture uses local X along the frontage and local -Z toward the street.
    void BuildShop(ShuntaLookKit.MeshBag mass, ShuntaLookKit.MeshBag detail, Vector3 origin, Quaternion q,
        float width, float depth, float height, System.Random rng, Block block)
    {
        Vector3 P(float x,float y,float z) => origin + q*new Vector3(x,y,z);
        Material wall = rng.Next(3)==0 ? brick : concrete;
        float baseDrop = .4f;
        mass.Box(wall,P(0,(height-baseDrop)*.5f,0),q,new Vector3(width,height+baseDrop,depth),2f,2f,default,true);
        mass.Box(concrete,P(0,3.3f,-depth*.5f-.15f),q,new Vector3(width+.3f,.38f,.6f));
        mass.Box(concrete,P(0,height+.24f,0),q,new Vector3(width+.4f,.45f,depth+.4f));
        mass.Box(pavement,P(0,-.14f,0),q,new Vector3(width+1f,.28f,depth+1f));
        block.buildings++;
        int bays=Mathf.Max(3,Mathf.FloorToInt(width/2.8f));
        float bayWidth=width/bays;
        for(int bay=0;bay<bays;bay++)
        {
            float x=-width*.5f+(bay+.5f)*bayWidth;
            detail.Box(glass,P(x,1.55f,-depth*.5f-.06f),q,new Vector3(bayWidth-.16f,2.7f,.08f));
            detail.Box(metal,P(x-bayWidth*.5f,1.55f,-depth*.5f-.14f),q,new Vector3(.08f,2.9f,.12f));
            detail.Box(warm,P(x,2.75f,-depth*.5f-.17f),q,new Vector3(bayWidth-.3f,.15f,.08f));
            if(bay%2==0)
            {
                detail.Box(metal,P(x,1.4f,-depth*.5f-.19f),q,new Vector3(.06f,2.4f,.06f));
                detail.Box(metal,P(x+.28f,1.2f,-depth*.5f-.23f),q,new Vector3(.04f,.32f,.04f));
            }
        }
        detail.Box(canopy,P(0,3f,-depth*.5f-.85f),q*Quaternion.Euler(8,0,0),new Vector3(width-.2f,.12f,1.65f));
        detail.Box(neonPink,P(0,2.86f,-depth*.5f-1.65f),q,new Vector3(width-.3f,.07f,.08f));
        Sign(detail,P(0,4.3f,-depth*.5f-.2f),q,Mathf.Min(width-1f,7.2f),1.7f,rng.Next(32),block);
        // Projecting, stacked blade boards offer a recognisable Tokyo street silhouette.
        if(rng.NextDouble()<.8)
        {
            float sx=width*.5f-.7f;
            detail.Box(metal,P(sx,6.3f,-depth*.5f-.8f),q,new Vector3(.12f,5f,1.4f));
            for(int j=0;j<3;j++)
                Sign(detail,P(sx,5.2f+j*1.5f,-depth*.5f-1.35f),q*Quaternion.Euler(0,90,0),1.35f,1.35f,rng.Next(32),block);
        }
        for(float floor=5.7f;floor<height-1f;floor+=3.1f)
        {
            detail.Box(metal,P(0,floor+1.05f,-depth*.5f-.07f),q,new Vector3(width,.12f,.13f));
            for(int bay=0;bay<bays;bay++)
            {
                float x=-width*.5f+(bay+.5f)*bayWidth;
                Material wm=rng.NextDouble()<.43 ? glass : rng.NextDouble()<.6 ? warm : cool;
                detail.Box(wm,P(x,floor,-depth*.5f-.08f),q,new Vector3(bayWidth-.6f,1.8f,.09f));
                detail.Box(metal,P(x,floor-.92f,-depth*.5f-.18f),q,new Vector3(bayWidth-.38f,.12f,.4f));
                if(bay%3==0)
                {
                    detail.Box(concrete,P(x,floor-.15f,-depth*.5f-.52f),q,new Vector3(.75f,.48f,.5f));
                    detail.Box(metal,P(x,floor-.15f,-depth*.5f-.80f),q,new Vector3(.55f,.34f,.04f));
                    block.details+=2;
                }
            }
        }
        // Along the cycling sightline the end walls are prominent: carry real bays around both sides.
        var sideQ=q*Quaternion.Euler(0,90,0);
        int sideBays=Mathf.Max(2,Mathf.FloorToInt(depth/2.6f));
        for(int side=-1;side<=1;side+=2)
            for(float floor=5.7f;floor<height-1f;floor+=3.1f)
            {
                mass.Box(metal,P(side*(width*.5f+.10f),floor+1.05f,0),sideQ,new Vector3(depth,.14f,.18f));
                for(int bay=0;bay<sideBays;bay++)
                {
                    float z=-depth*.5f+(bay+.5f)*depth/sideBays;
                    var window=rng.NextDouble()<.65?skylineGlass:(bay%2==0?warm:cool);
                    mass.Box(window,P(side*(width*.5f+.06f),floor,z),sideQ,new Vector3(depth/sideBays-.45f,1.8f,.08f));
                    detail.Box(metal,P(side*(width*.5f+.14f),floor,z),sideQ,new Vector3(.055f,1.8f,.12f));
                }
            }
        Roof(detail,origin,q,width,depth,height,rng,block);
        // Narrow exterior escape ladder, plumbing and shop-service boxes.
        float lx=-width*.5f+.3f;
        detail.Beam(metal,P(lx,3.7f,-depth*.5f-.3f),P(lx,height,-depth*.5f-.3f),.08f);
        detail.Beam(metal,P(lx+.6f,3.7f,-depth*.5f-.3f),P(lx+.6f,height,-depth*.5f-.3f),.08f);
        for(float y=3.7f;y<height;y+=.4f) detail.Beam(metal,P(lx,y,-depth*.5f-.3f),P(lx+.6f,y,-depth*.5f-.3f),.045f);
        detail.Box(concrete,P(width*.5f-.3f,1.1f,-depth*.5f-.38f),q,new Vector3(.5f,.75f,.38f));
        block.details+=bays*(int)(height/3f)+6;
    }

    void Roof(ShuntaLookKit.MeshBag bag,Vector3 origin,Quaternion q,float width,float depth,float height,System.Random rng,Block b)
    {
        Vector3 P(float x,float y,float z)=>origin+q*new Vector3(x,y,z);
        for(int i=0;i<3;i++)
        {
            float x=R(rng,-width*.3f,width*.3f), z=R(rng,-depth*.3f,depth*.3f);
            bag.Box(metal,P(x,height+.65f,z),q,new Vector3(1.7f,1f,1.1f));
            for(int slat=0;slat<5;slat++) bag.Box(concrete,P(x-.6f+slat*.3f,height+1.18f,z),q,new Vector3(.10f,.08f,.9f));
            b.details+=6;
        }
        bag.Beam(metal,P(0,height,0),P(0,height+4.8f,0),.09f);
        bag.Beam(metal,P(-1.2f,height+4,0),P(1.2f,height+4,0),.06f);
        for(int i=-2;i<=2;i++) bag.Beam(metal,P(i*.4f,height+4,-.5f),P(i*.4f,height+4,.5f),.035f);
        bag.Lathe(metal,P(width*.3f,height+.25f,depth*.22f),new[]{.55f,.55f,.75f,.75f},new[]{0f,.8f,.9f,1.05f},8);
        b.details+=9;
    }

    void Sign(ShuntaLookKit.MeshBag bag,Vector3 p,Quaternion q,float width,float height,int tile,Block block)
    {
        bag.Box(metal,p+q*Vector3.forward*.07f,q,new Vector3(width+.16f,height+.16f,.15f));
        bag.Box(sign,p,q,new Vector3(width,height,.04f),width*8f,height*4f,new Vector2(tile%8/8f,(3-tile/8)/4f));
        block.signs++;
    }
}





