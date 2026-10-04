using UnityEngine;

[CreateAssetMenu(menuName="MapleRide/Shunta NPC Library")]
public sealed class ShuntaNpcLibrary : ScriptableObject
{
    public const string ResourcePath="ShuntaMetro/Actors/NpcLibrary";
    public GameObject[] riders=System.Array.Empty<GameObject>();
    public GameObject[] pedestrians=System.Array.Empty<GameObject>();
    public string[] origins=System.Array.Empty<string>();

    public static GameObject CloneRider(int index,Transform parent)
    {
        var library=Resources.Load<ShuntaNpcLibrary>(ResourcePath);
        GameObject source=null;
        if(library!=null && library.riders.Length>0) source=library.riders[index%library.riders.Length];
        // Only use the authored cast. Never duplicate the playable main character
        // or an arbitrary scene rider when the library hasn't been baked/loaded.
        if(source==null) return null;
        var parking=new GameObject("Shunta NPC clone parking"); parking.SetActive(false);
        var clone=Object.Instantiate(source,parking.transform);
        clone.SetActive(false); Sanitize(clone,false);
        clone.transform.SetParent(parent,false);
        clone.transform.localScale=source.transform.lossyScale;
        Object.Destroy(parking);
        clone.name="Shunta reused rider "+index+" ("+source.name+")";
        return clone;
    }

    public static void Sanitize(GameObject root,bool pedestrian)
    {
        foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.tag="Untagged";
        foreach(var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled=false;
        foreach(var label in root.GetComponentsInChildren<TextMesh>(true)) label.gameObject.SetActive(false);
        foreach(var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.enabled=false;
        foreach(var body in root.GetComponentsInChildren<Rigidbody>(true)) {body.isKinematic=true;body.detectCollisions=false;}
        foreach(var mono in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if(mono==null) continue;
            if(mono is KuroBikeRig rig)
            {
                rig.enabled=!pedestrian; rig.useRideCadenceForCrank=false; rig.rideSession=null;
                rig.previewCadenceRpm=pedestrian?0f:65f; continue;
            }
            string name=mono.GetType().Name;
            bool keep=name.Contains("Lod") || name.Contains("LOD") || name=="RiderBlink";
            if(pedestrian && mono is MinatoCrowdActor) keep=true;
            mono.enabled=keep;
        }
    }
}
