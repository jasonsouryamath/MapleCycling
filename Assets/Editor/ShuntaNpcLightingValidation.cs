using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Reuse existing authored NPCs; generate only Shunta resources, never save the donor scene.</summary>
public static class ShuntaNpcLightingValidation
{
    const string Folder="Assets/Resources/ShuntaMetro/Actors";
    static void Require(bool ok,string what){if(!ok)throw new InvalidOperationException("[shunta-npcs] "+what);Debug.Log("[shunta-npcs] PASS "+what);}

    public static void BuildLibrary()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SakuraPass.unity");
        Directory.CreateDirectory(Folder+"/Sources");AssetDatabase.Refresh();
        var riders=new List<GameObject>();var pedestrians=new List<GameObject>();var origins=new List<string>();
        var candidates=new List<GameObject>();
        foreach(var npc in UnityEngine.Object.FindObjectsByType<NPCCyclist>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(npc.GetComponentInChildren<SkinnedMeshRenderer>(true)!=null)candidates.Add(npc.gameObject);
        foreach(var director in UnityEngine.Object.FindObjectsByType<ShiosaiTrafficDirector>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if(director.pool!=null)foreach(var npc in director.pool)
                if(npc!=null && npc.GetComponentInChildren<KuroBikeRig>(true)!=null && !candidates.Contains(npc.gameObject))candidates.Add(npc.gameObject);
        int sakura=0,minato=0,other=0;
        var identities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var donor in candidates.OrderBy(g=>Region(g)).ThenBy(g=>g.name,StringComparer.Ordinal))
        {
            string region=Region(donor);
            // Minato has 30 pool slots but only 12 authored identities. Skip repeated pool
            // names so the library includes all hair/kit designs rather than first-slot clones.
            string identity=System.Text.RegularExpressions.Regex.Replace(donor.name,@"\b\d+\b","").Trim();
            if(!identities.Add(region+":"+identity))continue;
            if(region=="Sakura" && sakura>=12 || region=="Minato" && minato>=12 || region=="Other" && other>=4)continue;
            if(region=="Sakura")sakura++;else if(region=="Minato")minato++;else other++;
            riders.Add(Bake(donor,"Rider_"+riders.Count.ToString("D2"),false));origins.Add(region+": "+donor.name);
        }
        var actors=UnityEngine.Object.FindObjectsByType<MinatoCrowdActor>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Where(a=>a.GetComponentInChildren<SkinnedMeshRenderer>(true)!=null && (a.motion==MinatoCrowdActor.MotionKind.Walk || a.motion==MinatoCrowdActor.MotionKind.Idle))
            .OrderBy(a=>Region(a.gameObject)=="Minato"?0:1).ThenBy(a=>a.name,StringComparer.Ordinal);
        foreach(var donor in actors)
        {
            if(pedestrians.Count>=12)break;
            pedestrians.Add(Bake(donor.gameObject,"Pedestrian_"+pedestrians.Count.ToString("D2"),true));
        }
        Require(riders.Count>0,"real cyclist donors found");Require(pedestrians.Count>0,"real standing/walking pedestrian donors found");
        Require(minato>=12,"all twelve distinct Minato hair/kit/bike identities available");
        string path=Folder+"/NpcLibrary.asset";
        var library=AssetDatabase.LoadAssetAtPath<ShuntaNpcLibrary>(path);
        if(library==null){library=ScriptableObject.CreateInstance<ShuntaNpcLibrary>();AssetDatabase.CreateAsset(library,path);}
        library.riders=riders.ToArray();library.pedestrians=pedestrians.ToArray();library.origins=origins.ToArray();
        EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
        Debug.Log("[shunta-npcs] LIBRARY BUILT "+riders.Count+" cyclists / "+pedestrians.Count+" pedestrians; Sakura="+sakura+" Minato="+minato+" other="+other);
        foreach(string origin in origins)Debug.Log("[shunta-npcs] donor "+origin);
        EditorSceneManager.OpenScene(MapleRideBoot.RegionScenePath(RegionCatalog.ShuntaMetro));
        Run();
    }
    static string Region(GameObject donor)
    {
        for(var t=donor.transform;t!=null;t=t.parent)
        {if(t.name.IndexOf("Minato",StringComparison.OrdinalIgnoreCase)>=0)return "Minato";if(t.name.IndexOf("Sakura",StringComparison.OrdinalIgnoreCase)>=0)return "Sakura";}
        return "Other";
    }
    static GameObject Bake(GameObject donor,string name,bool pedestrian)
    {
        var parking=new GameObject("~Shunta baking");parking.SetActive(false);
        var clone=UnityEngine.Object.Instantiate(donor,parking.transform);
        try
        {
            clone.name=name;clone.transform.localPosition=Vector3.zero;clone.transform.localRotation=Quaternion.identity;clone.transform.localScale=donor.transform.lossyScale;
            ShuntaNpcLibrary.Sanitize(clone,pedestrian);
            foreach(var actor in clone.GetComponentsInChildren<MinatoCrowdActor>(true))
            {actor.motion=MinatoCrowdActor.MotionKind.Walk;actor.pathStart=Vector3.zero;actor.pathEnd=Vector3.forward*10f;}
            int ri=0;
            foreach(var renderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                var slots=renderer.sharedMaterials;
                for(int i=0;i<slots.Length;i++)
                {
                    if(slots[i]==null || AssetDatabase.Contains(slots[i]))continue;
                    var mat=UnityEngine.Object.Instantiate(slots[i]);mat.hideFlags=HideFlags.None;
                    foreach(string textureName in mat.GetTexturePropertyNames())
                    {
                        var texture=mat.GetTexture(textureName);if(texture==null || AssetDatabase.Contains(texture))continue;
                        var copy=UnityEngine.Object.Instantiate(texture);copy.hideFlags=HideFlags.None;
                        string texPath=Folder+"/Sources/"+name+"_"+ri+"_"+i+"_"+textureName+".asset";
                        var old=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(texPath);
                        if(old==null){AssetDatabase.CreateAsset(copy,texPath);mat.SetTexture(textureName,copy);}
                        else{EditorUtility.CopySerialized(copy,old);UnityEngine.Object.DestroyImmediate(copy);mat.SetTexture(textureName,(Texture)old);}
                    }
                    string matPath=Folder+"/Sources/"+name+"_"+ri+"_"+i+".mat";
                    var oldMat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if(oldMat==null){AssetDatabase.CreateAsset(mat,matPath);slots[i]=mat;}
                    else{EditorUtility.CopySerialized(mat,oldMat);UnityEngine.Object.DestroyImmediate(mat);slots[i]=oldMat;}
                }
                renderer.sharedMaterials=slots;ri++;
                var skin=renderer as SkinnedMeshRenderer;var filter=renderer.GetComponent<MeshFilter>();
                Mesh mesh=skin!=null?skin.sharedMesh:filter!=null?filter.sharedMesh:null;
                if(mesh!=null && !AssetDatabase.Contains(mesh))
                {
                    string meshPath=Folder+"/Sources/"+name+"_mesh"+ri+".asset";
                    var persistent=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    var copy=UnityEngine.Object.Instantiate(mesh);copy.hideFlags=HideFlags.None;
                    if(persistent==null){persistent=copy;AssetDatabase.CreateAsset(copy,meshPath);}
                    else{EditorUtility.CopySerialized(copy,persistent);UnityEngine.Object.DestroyImmediate(copy);}
                    if(skin!=null)skin.sharedMesh=persistent;else if(filter!=null)filter.sharedMesh=persistent;
                }
            }
            clone.SetActive(true);
            var prefab=PrefabUtility.SaveAsPrefabAsset(clone,Folder+"/"+name+".prefab");
            Require(prefab!=null,"baked "+name+" from "+Region(donor));return prefab;
        }
        finally{UnityEngine.Object.DestroyImmediate(parking);}
    }
    public static void Run()
    {
        var shader=Resources.Load<Shader>("ShuntaMetro/Actors/ShuntaCharacter");
        Require(shader!=null && shader.isSupported && !ShaderUtil.ShaderHasError(shader),"character exposure shader supported and error-free");
        var library=Resources.Load<ShuntaNpcLibrary>(ShuntaNpcLibrary.ResourcePath);
        Require(library!=null && library.riders.Length>0 && library.pedestrians.Length>0,"persistent reusable NPC library");
        foreach(var prefab in library.riders)
        {
            Require(prefab!=null && prefab.GetComponentInChildren<SkinnedMeshRenderer>(true)!=null,"skinned rider, no primitive substitute");
            Require(prefab.GetComponentsInChildren<Collider>(true).All(c=>!c.enabled),"NPC colliders disabled");
        }
        Debug.Log("[shunta-npcs] ALL PASS");
    }
}
