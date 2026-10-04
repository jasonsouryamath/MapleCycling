using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>Exposure-independent cel character readability, scoped to Shunta runtime material copies.</summary>
[DefaultExecutionOrder(450)]
public sealed class ShuntaActorLighting : MonoBehaviour
{
    public static ShuntaActorLighting Instance {get;private set;}
    public static bool CorrectionsEnabled=true;
    public int CorrectedMaterials => copies.Count;
    public int CorrectedRenderers => originals.Count;
    readonly Dictionary<Material,Material> copies=new Dictionary<Material,Material>();
    readonly Dictionary<Renderer,Material[]> originals=new Dictionary<Renderer,Material[]>();
    readonly List<Renderer> dead=new List<Renderer>();
    Shader shader; RideBootstrap boot; RideSession session;
    Volume volume; VolumeProfile profile; float nextScan; bool active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if(Instance!=null)return;
        var go=new GameObject("Shunta actor readability"); DontDestroyOnLoad(go);
        Instance=go.AddComponent<ShuntaActorLighting>();
    }
    void Awake()
    {
        shader=Resources.Load<Shader>("ShuntaMetro/Actors/ShuntaCharacter");
        profile=ScriptableObject.CreateInstance<VolumeProfile>(); profile.hideFlags=HideFlags.DontSave;
        var grade=profile.Add<ColorAdjustments>(true);
        grade.saturation.value=6f; grade.contrast.value=2f; grade.postExposure.value=.15f;
        var bloom=profile.Add<Bloom>(true); bloom.intensity.value=.38f; bloom.threshold.value=1f; bloom.scatter.value=.55f;
        volume=gameObject.AddComponent<Volume>(); volume.isGlobal=true; volume.priority=65;volume.sharedProfile=profile;volume.enabled=false;
    }
    void LateUpdate()
    {
        if(Time.unscaledTime<nextScan)return; nextScan=Time.unscaledTime+.5f;
        if(boot==null)boot=FindFirstObjectByType<RideBootstrap>();
        if(session==null)session=FindFirstObjectByType<RideSession>();
        bool want=CorrectionsEnabled && session!=null && session.courseId==ShuntaRouteProvider.CourseId;
        if(!want) {if(active)Restore();active=false;volume.enabled=false;return;}
        active=true;volume.enabled=true;
        if(shader==null)return;
        if(boot!=null && boot.follower!=null && boot.follower.rider!=null) Apply(boot.follower.rider);
        if(ShuntaRaceDirector.Instance!=null)Apply(ShuntaRaceDirector.Instance.transform);
        if(ShuntaStreetCrowd.Instance!=null)Apply(ShuntaStreetCrowd.Instance.transform);
        if(ShuntaCyclistTraffic.Instance!=null)Apply(ShuntaCyclistTraffic.Instance.transform);
        dead.Clear();foreach(var kv in originals)if(kv.Key==null)dead.Add(kv.Key);
        foreach(var renderer in dead)originals.Remove(renderer);
    }
    public void Apply(Transform root)
    {
        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var slots=renderer.sharedMaterials; bool changed=false;
            originals.TryGetValue(renderer,out var original);
            for(int i=0;i<slots.Length;i++)
            {
                var source=slots[i];
                if(source==null || source.shader==null || source.shader.name!="MapleRide/HDRP/CelLit")continue;
                if(original==null || original.Length!=slots.Length)original=(Material[])slots.Clone();
                original[i]=source;
                if(!copies.TryGetValue(source,out var copy))
                {
                    copy=new Material(source){name=source.name+" Shunta actor",hideFlags=HideFlags.DontSave};copy.shader=shader;
                    copy.SetFloat("_ShuntaDisplayComp",1f);copy.SetFloat("_CharacterLight",1f);
                    copy.SetFloat("_CharKeyIntensity",1f);copy.SetFloat("_CharFillIntensity",.50f);copy.SetFloat("_CharAmbient",.35f);
                    copy.SetColor("_CharKeyColor",new Color(1f,.96f,.91f).linear);copy.SetColor("_CharFillColor",new Color(.77f,.87f,1f).linear);
                    copy.SetColor("_MatteFloor",new Color(.13f,.145f,.17f,1f).linear);
                    copy.SetFloat("_RampSmooth",.18f);copy.SetFloat("_RimStrength",.06f);
                    copy.SetFloat("_EdgeRimStrength",.035f);copy.SetColor("_EdgeRimColor",new Color(.45f,.72f,1f).linear);
                    copy.SetFloat("_SpecStrength",.10f);copy.SetFloat("_HighlightRolloff",.8f);
                    copy.SetFloat("_HighlightKnee",.65f);copy.SetFloat("_HighlightWhite",1.15f);
                    copies.Add(source,copy);
                }
                slots[i]=copy;changed=true;
            }
            if(changed){originals[renderer]=original;renderer.sharedMaterials=slots;}
        }
    }
    void Restore()
    {
        foreach(var kv in originals)
        {
            if(kv.Key==null)continue;
            var current=kv.Key.sharedMaterials;
            for(int i=0;i<current.Length && i<kv.Value.Length;i++)
                if(current[i]!=null && current[i].shader==shader)current[i]=kv.Value[i];
            kv.Key.sharedMaterials=current;
        }
        originals.Clear();
    }
    void OnDestroy()
    {
        Restore();foreach(var mat in copies.Values)Destroy(mat);copies.Clear();
        if(profile!=null){foreach(var component in profile.components)if(component!=null)Destroy(component);Destroy(profile);}
        if(Instance==this)Instance=null;
    }
}
